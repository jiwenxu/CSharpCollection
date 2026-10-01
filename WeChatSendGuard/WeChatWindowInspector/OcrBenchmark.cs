using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WeChatWindowInspector;

/// <summary>
/// Phase C：OCR Benchmark —— 自动遍历「Scale × 预处理 × 阈值」的全部组合并对比结果。
///
/// 流程（对应需求方的流程图）：
///   采集图 → 裁剪标题区 → DPI 归一化（缩回 96 DPI 基准尺寸）→ 多路预处理
///   （raw / stretch / otsu / fixed-T / invert / invert+stretch）→ 放大 → OCR → 变体并集
///   → 文本标准化 → 风险名单匹配 → 三态判定（Risk / Unknown / Private）→ 记录结果。
///
/// 与生产口径（WeChatSendGuard.App 的 TitleOcrReader）保持一致的三点：
///   1. 裁剪区按 `dpi/96` 放大，裁剪后先缩回 96 DPI 基准尺寸，最后按整数 Scale 放大；
///   2. 变体判定取并集（Spike-05 实测：单路必漏报）；
///   3. 识别文本为空或过短一律判为 Unknown，绝不判成"安全"（漏报优先于误报）。
///
/// 输出：逐组合对比表（含字符级准确率 / 三态分布 / 漏报 / 误报 / 用时）、按 Scale 的并集汇总（生产口径）、
/// 结论摘要、错字清单（形近字混淆对），以及主 CSV 与错字清单 CSV。
/// </summary>
internal static partial class OcrBenchmark
{
    /// <summary>标准化后长度低于此值即判为 Unknown（无法判定），与生产的"漏报优先"一致</summary>
    private const int MinUsableLength = 2;

    /// <summary>预估 OCR 次数超过此值时给出耗时提醒（不阻止执行）</summary>
    private const int HeavyRunOcrCount = 3000;

    public static int Run(LogWriter log, CliOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.OcrDir))
        {
            log.Write("用法：--ocr-bench --ocr-dir <采集目录> [--ocr-region x,y,w,h] [--bench-scales 3,4,5,6]");
            log.Write("                  [--bench-thresholds 140,180] [--names \"风险名1,风险名2\"] [--bench-csv PATH]");
            log.Write(@"示例：--ocr-bench --ocr-dir logs\spike05 --ocr-region 300,33,580,30 --names ""培立优BUG,徐二狗""");
            log.Write("说明：--ocr-region 视为 96 DPI 基准区，本模式固定按 dpi/96 归一化（与生产一致）；");
            log.Write("      不给 --ocr-region 时用采集时裁好的 *-title.png，同样会按 dpi/96 缩回基准尺寸。");
            return 1;
        }

        var directory = Path.GetFullPath(options.OcrDir);
        if (!Directory.Exists(directory))
        {
            log.Write($"目录不存在：{directory}");
            return 1;
        }

        IOcrEngine engine;
        try
        {
            engine = OcrEngines.Create(options.BenchEngine, options.BenchPaddleModel, options.BenchThreads, options.BenchDevice);
            log.Write($"OCR 引擎：{engine.Describe()}");
        }
        catch (Exception ex)
        {
            log.Write($"OCR 引擎不可用（{options.BenchEngine}）：{ex.Message}");
            return 1;
        }

        using var engineScope = engine;

        var suffix = options.OcrRegion is null ? "-title.png" : "-full.png";
        var samples = LoadSamples(log, directory, suffix);
        if (samples.Count == 0)
        {
            log.Write($"目录内没有可用的已标注样本：需要 *{suffix} 与同名 .json（含 scene 或 label 字段）。");
            log.Write(options.OcrRegion is null
                ? "提示：目录里若有 *-full.png，可用 --ocr-region x,y,w,h 从整图重裁剪后再跑。"
                : "提示：目录里若有 *-title.png，可去掉 --ocr-region 直接跑。");
            return 1;
        }

        var scales = options.BenchScales;
        var variants = BuildVariants(options.BenchThresholds, options.BenchVariants, log);

        // 名单保留原文用于展示，匹配统一用标准化后的形式（与识别文本同一套标准化）
        var namePairs = options.Names
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(raw => (Raw: raw, Normalized: NormalizeText(raw)))
            .Where(pair => pair.Normalized.Length > 0)
            .ToArray();
        var names = namePairs.Select(pair => pair.Normalized).ToArray();

        var ocrCount = samples.Count * variants.Length * scales.Length;
        var dumpDir = options.OcrDumpDir.Length > 0 ? Path.GetFullPath(options.OcrDumpDir) : string.Empty;

        log.Write(string.Empty);
        log.Write("Phase C：OCR Benchmark（Scale × 预处理 × 阈值 自动扫描）");
        log.Write($"  输入目录 : {directory}");
        log.Write($"  样本数   : {samples.Count}（仅统计 sidecar 中有 scene / label 的已标注样本）");
        log.Write($"  Scale 轴 : {string.Join(", ", scales)}");
        log.Write($"  变体轴   : {variants.Length} 路（{string.Join(", ", variants.Select(variant => variant.Name))}）");
        log.Write($"  OCR 次数 : {samples.Count} 样本 × {variants.Length} 变体 × {scales.Length} Scale = {ocrCount} 次");
        log.Write(options.OcrRegion is { } region
            ? $"  裁剪区   : {region}（96 DPI 基准，按 dpi/96 归一化后裁剪，再缩回基准尺寸）"
            : "  裁剪区   : 使用采集时的 *-title.png，按 dpi/96 缩回基准尺寸");
        log.Write($"  风险名单 : {(namePairs.Length > 0 ? string.Join("、", namePairs.Select(pair => pair.Raw)) : "（未提供，仅统计识别命中率与三态分布）")}");
        log.Write($"  识别引擎 : {engine.Name}（{engine.Describe()}）");
        if (dumpDir.Length > 0)
        {
            log.Write($"  变体导出 : {dumpDir}（每样本每变体的基准尺寸预处理图）");
        }

        if (ocrCount > HeavyRunOcrCount)
        {
            log.Write($"  [提醒] OCR 次数较多（{ocrCount} 次），预计需要数分钟；可用 --bench-scales / --bench-thresholds 收窄轴。");
        }

        var cells = NewCellGrid(variants.Length, scales.Length);
        var ensembles = NewCells(scales.Length);
        // 跨 Scale 并集：同一变体在不同 Scale 下的输出一起取并集。
        // 动机：实测"同一个字在不同 Scale 下时对时错"，单 Scale 无法覆盖全部样本。
        var allScaleEnsemble = new Cell();
        var complementary = new int[scales.Length];
        var processed = 0;
        var totalOcr = 0;

        // 错字清单：并集口径下"期望字 → 实际字"的替换计数，按 Scale 分开统计
        var confusions = new Dictionary<(char Expected, char Actual), int>[scales.Length];
        for (var k = 0; k < scales.Length; k++)
        {
            confusions[k] = [];
        }

        log.Write(string.Empty);
        log.Write("逐样本处理：");

        foreach (var sample in samples)
        {
            BitmapSource full;
            try
            {
                full = LoadPng(sample.FilePath);
            }
            catch (Exception ex)
            {
                log.Write($"  样本 {sample.Index} 载入失败：{ex.Message}");
                continue;
            }

            // 裁剪区按 dpi/96 放大；无裁剪区时 input 即采集时裁好的标题区（同为物理像素）
            var factor = sample.Dpi > 0 ? sample.Dpi / 96.0 : 1.0;
            BitmapSource titleArea = full;
            var cropText = "未裁剪（用 -title.png）";
            if (options.OcrRegion is { } baseRegion)
            {
                var cropRegion = new RegionSpec(
                    (int)Math.Round(baseRegion.X * factor),
                    (int)Math.Round(baseRegion.Y * factor),
                    (int)Math.Round(baseRegion.Width * factor),
                    (int)Math.Round(baseRegion.Height * factor));

                try
                {
                    titleArea = ScreenCapture.Crop(full, cropRegion);
                }
                catch (Exception ex)
                {
                    log.Write($"  样本 {sample.Index} 裁剪失败：{ex.Message}");
                    continue;
                }

                cropText = $"裁剪 {cropRegion}";
            }

            // 先缩回 96 DPI 基准尺寸，使后续"整数 Scale 放大"在任何 DPI 下完全一致
            var baseline = ResampleToBaseline(titleArea, factor);

            // 预处理与 Scale 无关，每个变体只算一次，供所有 Scale 复用
            var preprocessed = new BitmapSource[variants.Length];
            for (var v = 0; v < variants.Length; v++)
            {
                preprocessed[v] = Preprocess(baseline, variants[v]);
                if (dumpDir.Length > 0)
                {
                    Dump(preprocessed[v], dumpDir, sample.Index, variants[v].Name);
                }
            }

            var sampleWatch = Stopwatch.StartNew();
            var allScaleTexts = new List<string>();
            long allScaleMs = 0;
            for (var k = 0; k < scales.Length; k++)
            {
                var texts = new string[variants.Length];
                long sumMs = 0;

                for (var v = 0; v < variants.Length; v++)
                {
                    var watch = Stopwatch.StartNew();
                    string text;
                    try
                    {
                        text = engine.Recognize(ScaleUp(preprocessed[v], scales[k], variants[v].Scaling));
                    }
                    catch (Exception ex)
                    {
                        text = string.Empty;
                        log.Write($"  样本 {sample.Index} 变体 {variants[v].Name}@{scales[k]}x OCR 失败：{ex.Message}");
                    }

                    watch.Stop();
                    texts[v] = text;
                    sumMs += watch.ElapsedMilliseconds;
                    totalOcr++;
                    Accumulate(cells[v, k], text, sample, names, watch.ElapsedMilliseconds);
                }

                var unionText = string.Join(' ', texts);
                var unionNormalized = NormalizeText(unionText);
                Accumulate(ensembles[k], unionText, sample, names, sumMs);

                allScaleTexts.AddRange(texts);
                allScaleMs += sumMs;

                // 错字清单：被读出来但与 ground truth 不对齐的字符（只记替换，丢字/多字只计入编辑距离）
                // 仅当"名字基本被读出"（错误率 ≤ 1/3）时才记录，否则对齐结果只是噪声，不构成错字证据
                var diff = CharDiff(unionNormalized, sample.Scene);
                if (diff.Distance * 3 <= sample.Scene.Length)
                {
                    foreach (var pair in diff.Pairs)
                    {
                        confusions[k][pair] = confusions[k].GetValueOrDefault(pair) + 1;
                    }
                }

                // 互补性：单路全漏而并集命中 —— 记录"并集是必要的"的直接证据
                if (!texts.Any(text => NormalizeText(text).Contains(sample.Scene, StringComparison.Ordinal)) &&
                    unionNormalized.Contains(sample.Scene, StringComparison.Ordinal))
                {
                    complementary[k]++;
                }
            }

            // 跨 Scale 并集：该样本在全部 Scale × 全部变体下的识别文本合并成一条
            Accumulate(allScaleEnsemble, string.Join(' ', allScaleTexts), sample, names, allScaleMs);

            sampleWatch.Stop();
            processed++;
            log.Write($"  {sample.Index,-3} {Display(sample.Display, 16),-16} {DescribeDpi(sample.Dpi, sample.ScalePercent),-9} " +
                      $"{cropText,-24} → 基准 {baseline.PixelWidth}x{baseline.PixelHeight}，" +
                      $"已 OCR {variants.Length * scales.Length} 次 / {sampleWatch.ElapsedMilliseconds}ms");
        }

        if (processed == 0)
        {
            log.Write("没有样本处理成功，无法给出对比结果。");
            return 1;
        }

        log.Write(string.Empty);
        log.Write($"有效样本：{processed}（OCR 实际执行 {totalOcr} 次）");

        PrintVariantComparison(log, variants, scales, cells, names.Length > 0);
        var bestScaleIndex = PrintScaleEnsemble(log, variants, scales, cells, ensembles, allScaleEnsemble, complementary, names.Length > 0);
        PrintConfusions(log, scales[bestScaleIndex], confusions[bestScaleIndex], ensembles[bestScaleIndex]);

        var csvPath = WriteCsv(directory, options, variants, scales, cells, ensembles, allScaleEnsemble);
        if (csvPath.Length > 0)
        {
            log.Write(string.Empty);
            log.Write($"CSV 已写入：{csvPath}");

            var confusionPath = WriteConfusionCsv(csvPath, scales[bestScaleIndex], confusions[bestScaleIndex]);
            if (confusionPath.Length > 0)
            {
                log.Write($"错字清单 CSV 已写入：{confusionPath}");
            }
        }

        return 0;
    }

    /// <summary>逐组合对比表：每一路变体在每个 Scale 上单独判定（不含并集）</summary>
    private static void PrintVariantComparison(
        LogWriter log,
        Variant[] variants,
        int[] scales,
        Cell[,] cells,
        bool withRisk)
    {
        var rows = new List<(string Label, Cell Cell)>();
        for (var v = 0; v < variants.Length; v++)
        {
            for (var k = 0; k < scales.Length; k++)
            {
                rows.Add(($"{variants[v].Name}@{scales[k]}x", cells[v, k]));
            }
        }

        log.Write(string.Empty);
        log.Write("逐组合结果（按命中率排序，变体单独判定，不含并集）");
        log.Write($"  {"#",-3}{"组合",-16}{"样本",-2}  {"命中率",-3}  {"字准",-4}{"Risk",-5}{"Unknown",-8}{"Private",-8}" +
                  (withRisk ? $"{"漏报",-4}{"误报",-4}" : string.Empty) + $"{"平均ms",-4}");
        log.Write($"  {new string('-', 3 + 18 + 4 + 2 + 6 + 2 + 6 + 2 + 5 + 8 + 8 + (withRisk ? 12 : 0) + 6)}");

        var rank = 0;
        foreach (var (label, cell) in rows.OrderByDescending(row => row.Cell.HitRate).ThenBy(row => row.Cell.AvgMs))
        {
            rank++;
            log.Write($"  {rank,-3}{label,-18}{cell.Samples,-4}  {cell.HitRate,5:F1}%  {cell.CharAccuracy,5:F1}%  " +
                      $"{cell.Risk,-5}{cell.Unknown,-8}{cell.Private,-8}" +
                      (withRisk ? $"{cell.Missed,-6}{cell.FalseAlarm,-6}" : string.Empty) + $"{cell.AvgMs,6:F0}");
        }

        var best = rows.OrderByDescending(row => row.Cell.HitRate).ThenBy(row => row.Cell.AvgMs).First();
        log.Write($"  最佳单路：{best.Label}（命中率 {best.Cell.HitRate:F1}%，平均 {best.Cell.AvgMs:F0} ms）");
    }

    /// <summary>按 Scale 汇总：全部变体取并集，即生产实际使用的判定口径。返回最佳 Scale 的下标</summary>
    private static int PrintScaleEnsemble(
        LogWriter log,
        Variant[] variants,
        int[] scales,
        Cell[,] cells,
        Cell[] ensembles,
        Cell allScaleEnsemble,
        int[] complementary,
        bool withRisk)
    {
        log.Write(string.Empty);
        log.Write("按 Scale 汇总（全部变体并集 —— 生产判定口径）");
        log.Write($"  {"Scale",-6}{"样本",-2}  {"命中率",-3}  {"字准",-4}{"Risk",-5}{"Unknown",-8}{"Private",-8}" +
                  (withRisk ? $"{"漏报",-4}{"误报",-4}" : string.Empty) + $"{"平均ms",-4}  {"互补命中",-4}");
        log.Write($"  {new string('-', 6 + 4 + 2 + 6 + 2 + 6 + 2 + 5 + 8 + 8 + (withRisk ? 12 : 0) + 6 + 2 + 8)}");

        var bestIndex = 0;
        for (var k = 0; k < scales.Length; k++)
        {
            var cell = ensembles[k];
            log.Write($"  {scales[k] + "x",-6}{cell.Samples,-4}  {cell.HitRate,5:F1}%  {cell.CharAccuracy,5:F1}%  " +
                      $"{cell.Risk,-5}{cell.Unknown,-8}{cell.Private,-8}" +
                      (withRisk ? $"{cell.Missed,-6}{cell.FalseAlarm,-6}" : string.Empty) +
                      $"{cell.AvgMs,6:F0}  {complementary[k],-8}");

            if (cell.HitRate > ensembles[bestIndex].HitRate ||
                (cell.HitRate == ensembles[bestIndex].HitRate && cell.AvgMs < ensembles[bestIndex].AvgMs))
            {
                bestIndex = k;
            }
        }

        // 跨 Scale 并集：把各 Scale 的输出一起并入（单 Scale 无法覆盖全部样本时的兜底口径）
        if (scales.Length > 1)
        {
            var cell = allScaleEnsemble;
            log.Write($"  {"全Scale",-6}{cell.Samples,-4}  {cell.HitRate,5:F1}%  {cell.CharAccuracy,5:F1}%  " +
                      $"{cell.Risk,-5}{cell.Unknown,-8}{cell.Private,-8}" +
                      (withRisk ? $"{cell.Missed,-6}{cell.FalseAlarm,-6}" : string.Empty) +
                      $"{cell.AvgMs,6:F0}  {"-",-8}");
        }

        log.Write(string.Empty);
        log.Write("结论：");

        var bestCell = ensembles[bestIndex];
        log.Write($"  1. 最佳 Scale（并集口径）：{scales[bestIndex]}x —— 命中率 {bestCell.HitRate:F1}%，" +
                  $"字符级准确率 {bestCell.CharAccuracy:F1}%，" +
                  $"平均 {bestCell.AvgMs:F0} ms/样本（含 {bestCell.Samples} 个样本的全部变体）。");

        // 与"该 Scale 下最强单路变体"对比，量化并集的收益
        var bestVariantIndex = 0;
        for (var v = 1; v < variants.Length; v++)
        {
            var current = cells[v, bestIndex];
            var best = cells[bestVariantIndex, bestIndex];
            if (current.HitRate > best.HitRate || (current.HitRate == best.HitRate && current.AvgMs < best.AvgMs))
            {
                bestVariantIndex = v;
            }
        }

        var bestVariantCell = cells[bestVariantIndex, bestIndex];
        log.Write($"  2. 变体互补性：并集 {bestCell.HitRate:F1}% 比该 Scale 下最佳单路" +
                  $"（{variants[bestVariantIndex].Name}，{bestVariantCell.HitRate:F1}%）高 " +
                  $"{bestCell.HitRate - bestVariantCell.HitRate:F1} 个百分点；" +
                  $"其中 {complementary[bestIndex]} 处为「单路全部漏检、仅并集命中」。" +
                  (complementary[bestIndex] > 0
                      ? "说明多路并集是必要的（与 Spike-05 结论一致）。"
                      : "本批样本未出现必须靠并集兜底的情况。"));

        if (withRisk)
        {
            log.Write($"  3. 风险判定的安全性：漏报 {bestCell.Missed} 例、误报 {bestCell.FalseAlarm} 例、" +
                      $"Unknown {bestCell.Unknown} 例（Unknown 不显示提醒，属漏报侧，符合「绝不显示错误安全感」的设计）。");
            if (bestCell.Missed > 0)
            {
                log.Write("     注意：仍存在漏报，该组合不宜直接作为生产参数；需继续调整裁剪区 / 预处理 / Scale。");
            }
        }
        else
        {
            log.Write("  3. 未提供 --names，本次只评估识别命中率与三态分布，未评估漏报 / 误报。");
        }

        var unknownIndex = 0;
        for (var k = 1; k < scales.Length; k++)
        {
            if (ensembles[k].Unknown > ensembles[unknownIndex].Unknown)
            {
                unknownIndex = k;
            }
        }

        log.Write(ensembles[unknownIndex].Unknown > 0
            ? $"  4. Unknown 最多：{scales[unknownIndex]}x（{ensembles[unknownIndex].Unknown} 例）—— " +
              "Unknown 表示识别文本为空或过短，生产口径下按「不提醒」处理。"
            : "  4. 各 Scale 的并集均无 Unknown（识别文本均非空）。");

        if (scales.Length > 1)
        {
            var delta = allScaleEnsemble.HitRate - bestCell.HitRate;
            log.Write($"  5. 跨 Scale 并集（{string.Join(" + ", scales.Select(value => value + "x"))} × 全部变体）：" +
                      $"命中率 {allScaleEnsemble.HitRate:F1}%（较最佳单 Scale {scales[bestIndex]}x 的 {bestCell.HitRate:F1}% " +
                      $"{(delta >= 0 ? "高" : "低")} {Math.Abs(delta):F1} 个百分点），" +
                      $"字符级准确率 {allScaleEnsemble.CharAccuracy:F1}%，" +
                      $"平均 {allScaleEnsemble.AvgMs:F0} ms/样本（含全部 Scale 的扫描成本）。");
        }

        return bestIndex;
    }

    /// <summary>错字清单：并集口径下与 ground truth 不对齐的字符（形近字混淆的直接证据）</summary>
    private static void PrintConfusions(
        LogWriter log,
        int scale,
        Dictionary<(char Expected, char Actual), int> confusions,
        Cell ensemble)
    {
        log.Write(string.Empty);
        log.Write($"错字清单（并集口径 @{scale}x：字符级正确 {ensemble.Chars - ensemble.CharErrors}/{ensemble.Chars} 字，" +
                  $"准确率 {ensemble.CharAccuracy:F1}%）");

        if (ensemble.CharErrors == 0)
        {
            log.Write("  无：所有样本的会话名均被完整读出。");
            return;
        }

        if (confusions.Count == 0)
        {
            log.Write("  无可列出的字符替换，但存在丢字 / 多字（编辑距离非零，全部由插入删除造成）。");
            return;
        }

        var rank = 0;
        foreach (var (pair, count) in confusions
                     .OrderByDescending(entry => entry.Value)
                     .ThenBy(entry => entry.Key.Expected))
        {
            rank++;
            log.Write($"  {rank,2}. {pair.Expected} → {pair.Actual}   出现 {count} 次");
            if (rank >= 20)
            {
                log.Write($"  （仅列出前 20 项，共 {confusions.Count} 种替换；完整清单见 CSV）");
                break;
            }
        }
    }

    private static string WriteCsv(
        string directory,
        CliOptions options,
        Variant[] variants,
        int[] scales,
        Cell[,] cells,
        Cell[] ensembles,
        Cell allScaleEnsemble)
    {
        var path = options.BenchCsv.Length > 0
            ? Path.GetFullPath(options.BenchCsv)
            : Path.Combine(directory, $"ocr-benchmark-{DateTime.Now:yyyyMMdd-HHmmss}.csv");

        var builder = new StringBuilder();
        builder.AppendLine("type,scale,variant,samples,hits,hitRate,charAccuracy,chars,charErrors,risk,unknown,private,expectedRisk,missed,falseAlarm,avgMs");

        for (var v = 0; v < variants.Length; v++)
        {
            for (var k = 0; k < scales.Length; k++)
            {
                AppendCsvRow(builder, "variant", scales[k], variants[v].Name, cells[v, k]);
            }
        }

        for (var k = 0; k < scales.Length; k++)
        {
            AppendCsvRow(builder, "ensemble", scales[k], "union", ensembles[k]);
        }

        // 跨 Scale 并集：scale 记 0（表示"全部 Scale"），便于下游按 type 区分
        if (scales.Length > 1)
        {
            AppendCsvRow(builder, "ensemble-all", 0, "union", allScaleEnsemble);
        }

        try
        {
            var fullPath = Path.GetFullPath(path);
            var parent = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(parent))
            {
                Directory.CreateDirectory(parent);
            }

            // UTF-8 with BOM：Excel 直接双击打开不会乱码
            File.WriteAllText(fullPath, builder.ToString(), new UTF8Encoding(true));
            return fullPath;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[警告] CSV 写入失败 {path}：{ex.Message}");
            return string.Empty;
        }
    }

    private static void AppendCsvRow(StringBuilder builder, string type, int scale, string variant, Cell cell)
    {
        builder.Append(type).Append(',')
            .Append(scale).Append(',')
            .Append(variant).Append(',')
            .Append(cell.Samples).Append(',')
            .Append(cell.Hits).Append(',')
            .Append(cell.HitRate.ToString("F1")).Append(',')
            .Append(cell.CharAccuracy.ToString("F1")).Append(',')
            .Append(cell.Chars).Append(',')
            .Append(cell.CharErrors).Append(',')
            .Append(cell.Risk).Append(',')
            .Append(cell.Unknown).Append(',')
            .Append(cell.Private).Append(',')
            .Append(cell.ExpectedRisk).Append(',')
            .Append(cell.Missed).Append(',')
            .Append(cell.FalseAlarm).Append(',')
            .Append(cell.AvgMs.ToString("F0"))
            .AppendLine();
    }

    /// <summary>错字清单单独落一份 CSV（与主 CSV 同名前缀），便于逐字核对与后续做容错映射</summary>
    private static string WriteConfusionCsv(
        string benchmarkCsvPath,
        int scale,
        Dictionary<(char Expected, char Actual), int> confusions)
    {
        if (confusions.Count == 0)
        {
            return string.Empty;
        }

        var directory = Path.GetDirectoryName(benchmarkCsvPath);
        if (string.IsNullOrEmpty(directory))
        {
            return string.Empty;
        }

        var path = Path.Combine(
            directory,
            Path.GetFileNameWithoutExtension(benchmarkCsvPath) + "-confusions.csv");

        var builder = new StringBuilder();
        builder.AppendLine("scale,expected,actual,count");
        foreach (var (pair, count) in confusions
                     .OrderByDescending(entry => entry.Value)
                     .ThenBy(entry => entry.Key.Expected))
        {
            builder.Append(scale).Append(',')
                .Append(pair.Expected).Append(',')
                .Append(pair.Actual).Append(',')
                .Append(count)
                .AppendLine();
        }

        try
        {
            File.WriteAllText(path, builder.ToString(), new UTF8Encoding(true));
            return path;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[警告] 错字清单 CSV 写入失败 {path}：{ex.Message}");
            return string.Empty;
        }
    }

    private static Cell[,] NewCellGrid(int variants, int scales)
    {
        var cells = new Cell[variants, scales];
        for (var v = 0; v < variants; v++)
        {
            for (var k = 0; k < scales; k++)
            {
                cells[v, k] = new Cell();
            }
        }

        return cells;
    }

    private static Cell[] NewCells(int count)
    {
        var cells = new Cell[count];
        for (var i = 0; i < count; i++)
        {
            cells[i] = new Cell();
        }

        return cells;
    }

    /// <summary>把一个组合的识别结果计入统计（文本标准化 → 命中判定 → 三态判定 → 漏报/误报）</summary>
    private static void Accumulate(Cell cell, string text, Sample sample, string[] names, long milliseconds)
    {
        cell.Samples++;
        cell.TotalMs += milliseconds;

        var normalized = NormalizeText(text);
        if (normalized.Contains(sample.Scene, StringComparison.Ordinal))
        {
            cell.Hits++;
        }

        // 字符级指标：整串命中直接记 0 错，否则取与 ground truth 最匹配片段的编辑距离
        cell.Chars += sample.Scene.Length;
        cell.CharErrors += CharDiff(normalized, sample.Scene).Distance;

        var verdict = normalized.Length < MinUsableLength
            ? Verdict.Unknown
            : names.Any(name => normalized.Contains(name, StringComparison.Ordinal))
                ? Verdict.Risk
                : Verdict.Private;

        switch (verdict)
        {
            case Verdict.Risk:
                cell.Risk++;
                break;

            case Verdict.Unknown:
                cell.Unknown++;
                break;

            default:
                cell.Private++;
                break;
        }

        if (names.Length == 0)
        {
            return;
        }

        // 期望判定由 ground truth 决定：会话名命中名单 → 期望 Risk，否则期望 Private
        if (names.Any(name => sample.Scene.Contains(name, StringComparison.Ordinal)))
        {
            cell.ExpectedRisk++;
            if (verdict != Verdict.Risk)
            {
                cell.Missed++;
            }
        }
        else if (verdict == Verdict.Risk)
        {
            cell.FalseAlarm++;
        }
    }

    /// <summary>
    /// 变体轴（与目标生产流程一一对应）：
    ///   raw / stretch / otsu 为免阈值的三路基准；
    ///   fixed-T 由阈值轴展开（T 取自 --bench-thresholds），针对浅色主题；
    ///   invert / invert+stretch 为深色主题路径（浅字深底 → 反相后变深字浅底）。
    /// <paramref name="requested"/> 非空时只保留指定变体（用于消融对比），未匹配到任何变体则退回全部。
    /// </summary>
    private static Variant[] BuildVariants(int[] thresholds, string requested, LogWriter log)
    {
        var variants = new List<Variant>
        {
            new("raw", VariantTone.Color, 0, BitmapScalingMode.HighQuality),
            new("stretch", VariantTone.Stretch, 0, BitmapScalingMode.HighQuality),
            new("otsu", VariantTone.Otsu, 0, BitmapScalingMode.NearestNeighbor),
        };

        foreach (var threshold in thresholds)
        {
            variants.Add(new Variant($"fixed-{threshold}", VariantTone.Fixed, threshold, BitmapScalingMode.NearestNeighbor));
        }

        variants.Add(new Variant("invert", VariantTone.Invert, 0, BitmapScalingMode.HighQuality));
        variants.Add(new Variant("invert+stretch", VariantTone.InvertStretch, 0, BitmapScalingMode.HighQuality));

        var names = requested
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (names.Length == 0)
        {
            return variants.ToArray();
        }

        var unknown = names
            .Where(name => !variants.Any(variant => string.Equals(variant.Name, name, StringComparison.OrdinalIgnoreCase)))
            .ToArray();
        if (unknown.Length > 0)
        {
            log.Write($"  [警告] --bench-variants 中这些变体名不存在，已忽略：{string.Join(", ", unknown)}");
        }

        var selected = variants
            .Where(variant => names.Contains(variant.Name, StringComparer.OrdinalIgnoreCase))
            .ToArray();
        if (selected.Length == 0)
        {
            log.Write("  [警告] --bench-variants 未匹配到任何变体，改为使用全部变体。");
            return variants.ToArray();
        }

        return selected;
    }

    private static List<Sample> LoadSamples(LogWriter log, string directory, string suffix)
    {
        var samples = new List<Sample>();
        var skipped = 0;

        var files = Directory.GetFiles(directory, $"*{suffix}")
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase);

        foreach (var file in files)
        {
            // sidecar 命名规则：<prefix>-title.png / <prefix>-full.png 对应 <prefix>.json
            var sidecarPath = file[..^suffix.Length] + ".json";
            var (scene, kind, dpi, scalePercent, label) = ReadSidecar(sidecarPath);

            // scene 缺失时回退用 label 的"/"前部分（采集时 --label 形如 "会话名/类型"）
            var expected = scene.Length > 0 ? scene : SplitLabelName(label);
            var normalized = NormalizeText(expected);
            if (normalized.Length == 0)
            {
                skipped++;
                continue;
            }

            samples.Add(new Sample(file, Index(file), normalized, expected, kind, dpi, scalePercent));
        }

        if (skipped > 0)
        {
            log.Write($"  跳过未标注样本 {skipped} 个（sidecar 缺少 scene / label，无法作为 ground truth）");
        }

        return samples;
    }

    /// <summary>把采集图按 1/factor 缩回 96 DPI 基准尺寸（factor 为 1 时原样返回）</summary>
    private static BitmapSource ResampleToBaseline(BitmapSource source, double factor)
    {
        var width = Math.Max(1, (int)Math.Round(source.PixelWidth / factor));
        var height = Math.Max(1, (int)Math.Round(source.PixelHeight / factor));
        if (width == source.PixelWidth && height == source.PixelHeight)
        {
            return source;
        }

        return RenderToBgra32(source, width, height, BitmapScalingMode.HighQuality);
    }

    /// <summary>按指定变体预处理（与 Scale 无关，可在多个 Scale 间复用）</summary>
    private static BitmapSource Preprocess(BitmapSource source, Variant variant)
    {
        if (variant.Tone == VariantTone.Color)
        {
            return source;
        }

        var (gray, width, height) = ReadGray(source);
        return variant.Tone switch
        {
            // 浅色主题（深字浅底）
            VariantTone.Stretch => CreateGray(Stretch(gray), width, height),
            VariantTone.Otsu => Binarize(gray, width, height, OtsuThreshold(gray)),
            VariantTone.Fixed => Binarize(gray, width, height, variant.Threshold),

            // 深色主题（浅字深底）：反相把字变深、底变浅，再交给 OCR 更擅长的"深字浅底"
            VariantTone.Invert => CreateGray(Invert(gray), width, height),
            _ => CreateGray(Stretch(Invert(gray)), width, height),
        };
    }

    /// <summary>把预处理图按整数 Scale 放大并铺白底，输出 OCR 可直接消费的 Bgra32 位图</summary>
    private static BitmapSource ScaleUp(BitmapSource source, int scale, BitmapScalingMode scaling)
    {
        var width = Math.Max(1, source.PixelWidth * scale);
        var height = Math.Max(1, source.PixelHeight * scale);
        return RenderToBgra32(source, width, height, scaling);
    }

    /// <summary>统一渲染路径：负责铺白底、按插值方式缩放、并转换成 Bgra32（OCR 只接受 4 字节像素）</summary>
    private static BitmapSource RenderToBgra32(BitmapSource source, int width, int height, BitmapScalingMode scaling)
    {
        var visual = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(visual, scaling);
        using (var context = visual.RenderOpen())
        {
            context.DrawRectangle(Brushes.White, null, new Rect(0, 0, width, height));
            context.DrawImage(source, new Rect(0, 0, width, height));
        }

        var target = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        target.Render(visual);
        target.Freeze();

        var converted = new FormatConvertedBitmap(target, PixelFormats.Bgra32, null, 0);
        converted.Freeze();
        return converted;
    }

    private static void Dump(BitmapSource image, string directory, string index, string variant)
    {
        try
        {
            Directory.CreateDirectory(directory);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(image));
            using var stream = File.Create(Path.Combine(directory, $"{index}-{variant}.png"));
            encoder.Save(stream);
        }
        catch
        {
            // 变体导出失败不影响 Benchmark
        }
    }

    private static (byte[] Pixels, int Width, int Height) ReadGray(BitmapSource source)
    {
        var gray = new FormatConvertedBitmap(source, PixelFormats.Gray8, null, 0);
        gray.Freeze();
        var width = gray.PixelWidth;
        var height = gray.PixelHeight;
        var pixels = new byte[width * height];
        gray.CopyPixels(pixels, width, 0);
        return (pixels, width, height);
    }

    private static BitmapSource CreateGray(byte[] gray, int width, int height)
    {
        var image = BitmapSource.Create(width, height, 96, 96, PixelFormats.Gray8, null, gray, width);
        image.Freeze();
        return image;
    }

    /// <summary>线性对比拉伸到 0~255：标题栏底色浅、字色深，拉伸后字形边缘更锐利</summary>
    private static byte[] Stretch(byte[] gray)
    {
        byte min = 255;
        byte max = 0;
        foreach (var value in gray)
        {
            if (value < min)
            {
                min = value;
            }

            if (value > max)
            {
                max = value;
            }
        }

        if (max <= min)
        {
            return gray;
        }

        var span = max - min;
        var stretched = new byte[gray.Length];
        for (var i = 0; i < gray.Length; i++)
        {
            stretched[i] = (byte)((gray[i] - min) * 255 / span);
        }

        return stretched;
    }

    /// <summary>灰度反相：深色主题下把"浅字深底"翻成 OCR 更擅长的"深字浅底"</summary>
    private static byte[] Invert(byte[] gray)
    {
        var inverted = new byte[gray.Length];
        for (var i = 0; i < gray.Length; i++)
        {
            inverted[i] = (byte)(255 - gray[i]);
        }

        return inverted;
    }

    /// <summary>大津法自动阈值：逐图自适应，避免固定阈值在不同底色/主题下失效</summary>
    private static int OtsuThreshold(byte[] gray)
    {
        var histogram = new int[256];
        foreach (var value in gray)
        {
            histogram[value]++;
        }

        long total = gray.Length;
        long sum = 0;
        for (var i = 0; i < 256; i++)
        {
            sum += (long)i * histogram[i];
        }

        long sumBackground = 0;
        var weightBackground = 0;
        double best = 0;
        var threshold = 128;
        for (var t = 0; t < 256; t++)
        {
            weightBackground += histogram[t];
            if (weightBackground == 0)
            {
                continue;
            }

            var weightForeground = (int)(total - weightBackground);
            if (weightForeground == 0)
            {
                break;
            }

            sumBackground += (long)t * histogram[t];
            var meanBackground = (double)sumBackground / weightBackground;
            var meanForeground = (double)(sum - sumBackground) / weightForeground;
            var between = (double)weightBackground * weightForeground * (meanBackground - meanForeground) * (meanBackground - meanForeground);
            if (between > best)
            {
                best = between;
                threshold = t;
            }
        }

        return threshold;
    }

    /// <summary>按阈值二值化：小于阈值取黑（字），否则取白（底）</summary>
    private static BitmapSource Binarize(byte[] gray, int width, int height, int threshold)
    {
        var pixels = new byte[width * height * 4];
        for (var i = 0; i < gray.Length; i++)
        {
            var value = gray[i] < threshold ? (byte)0 : (byte)255;
            var offset = i * 4;
            pixels[offset] = value;
            pixels[offset + 1] = value;
            pixels[offset + 2] = value;
            pixels[offset + 3] = 255;
        }

        var image = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
        image.Freeze();
        return image;
    }

    private static BitmapSource LoadPng(string path)
    {
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.UriSource = new Uri(path);
        image.EndInit();
        image.Freeze();
        return image;
    }

    private static (string Scene, string Kind, int Dpi, int ScalePercent, string Label) ReadSidecar(string jsonPath)
    {
        if (!File.Exists(jsonPath))
        {
            return (string.Empty, string.Empty, 0, 0, string.Empty);
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(jsonPath, Encoding.UTF8));
            var root = document.RootElement;
            return (
                GetString(root, "scene"),
                GetString(root, "kind"),
                root.TryGetProperty("dpi", out var dpi) && dpi.ValueKind == JsonValueKind.Number ? dpi.GetInt32() : 0,
                root.TryGetProperty("scalePercent", out var scale) && scale.ValueKind == JsonValueKind.Number ? scale.GetInt32() : 0,
                GetString(root, "label"));
        }
        catch
        {
            return (string.Empty, string.Empty, 0, 0, string.Empty);
        }
    }

    private static string GetString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var element) && element.ValueKind == JsonValueKind.String
            ? element.GetString() ?? string.Empty
            : string.Empty;

    /// <summary>取 label 的"/"前部分作为会话名（label 形如 "技术交流群/群"）</summary>
    private static string SplitLabelName(string label)
    {
        if (string.IsNullOrWhiteSpace(label))
        {
            return string.Empty;
        }

        var parts = label.Split('/', 2, StringSplitOptions.TrimEntries);
        return parts[0];
    }

    /// <summary>
    /// 文本标准化（对应流程图"文本标准化"节点）：
    /// 1. 全角字符转半角（含全角空格）；
    /// 2. 去除空白、标点与符号，只保留字母 / 数字 / 汉字（OCR 常在汉字间插入空格或假标点）；
    /// 3. 拉丁字母统一小写（大小写差异不应影响命中判定）。
    /// 风险名单与识别文本走同一套标准化，保证判定对称。
    /// </summary>
    private static string NormalizeText(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var original in text)
        {
            var character = original;
            if (character == '\u3000')
            {
                continue;
            }

            // 全角 ASCII（！~ ～）映射回半角，便于与名单中的半角写法对齐
            if (character >= '\uFF01' && character <= '\uFF5E')
            {
                character = (char)(character - 0xFEE0);
            }

            if (char.IsWhiteSpace(character) || char.IsPunctuation(character) || char.IsSymbol(character))
            {
                continue;
            }

            builder.Append(char.ToLowerInvariant(character));
        }

        return builder.ToString();
    }

    /// <summary>
    /// 字符级差异：在识别文本中找与 ground truth 最匹配的片段（长度 ±1 滑动），
    /// 返回该片段的最小编辑距离与"替换"明细（错字清单的来源）。
    /// 目的：OCR 文本常含会话名之外的噪声，直接对整串算编辑距离会把噪声误记为错字。
    /// </summary>
    private static (int Distance, List<(char Expected, char Actual)> Pairs) CharDiff(string actual, string expected)
    {
        if (expected.Length == 0)
        {
            return (0, []);
        }

        if (actual.Length == 0)
        {
            return (expected.Length, []);
        }

        // 整串包含 → 会话名被完整读出，字级零错误
        if (actual.Contains(expected, StringComparison.Ordinal))
        {
            return (0, []);
        }

        var bestDistance = int.MaxValue;
        var bestPairs = new List<(char Expected, char Actual)>();
        var shortest = Math.Max(1, expected.Length - 1);
        var longest = expected.Length + 1;

        for (var length = shortest; length <= longest; length++)
        {
            if (length > actual.Length)
            {
                continue;
            }

            for (var start = 0; start + length <= actual.Length; start++)
            {
                var (distance, pairs) = Levenshtein(actual.Substring(start, length), expected);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestPairs = pairs;
                }
            }
        }

        // 识别文本比会话名还短（大量丢字）时，窗口扫不到 → 退化为整串比较
        if (bestDistance == int.MaxValue)
        {
            return Levenshtein(actual, expected);
        }

        return (bestDistance, bestPairs);
    }

    /// <summary>编辑距离 + 回溯出"替换"明细（插入 / 删除只计入距离，不计入错字清单）</summary>
    private static (int Distance, List<(char Expected, char Actual)> Pairs) Levenshtein(string actual, string expected)
    {
        var rows = actual.Length + 1;
        var columns = expected.Length + 1;
        var matrix = new int[rows, columns];

        for (var i = 0; i < rows; i++)
        {
            matrix[i, 0] = i;
        }

        for (var j = 0; j < columns; j++)
        {
            matrix[0, j] = j;
        }

        for (var i = 1; i < rows; i++)
        {
            for (var j = 1; j < columns; j++)
            {
                var cost = actual[i - 1] == expected[j - 1] ? 0 : 1;
                matrix[i, j] = Math.Min(
                    Math.Min(matrix[i - 1, j] + 1, matrix[i, j - 1] + 1),
                    matrix[i - 1, j - 1] + cost);
            }
        }

        var pairs = new List<(char Expected, char Actual)>();
        var x = actual.Length;
        var y = expected.Length;
        while (x > 0 && y > 0)
        {
            var cost = actual[x - 1] == expected[y - 1] ? 0 : 1;
            if (matrix[x, y] == matrix[x - 1, y - 1] + cost)
            {
                if (cost == 1)
                {
                    pairs.Add((expected[y - 1], actual[x - 1]));
                }

                x--;
                y--;
            }
            else if (matrix[x, y] == matrix[x - 1, y] + 1)
            {
                x--;
            }
            else
            {
                y--;
            }
        }

        return (matrix[actual.Length, expected.Length], pairs);
    }

    /// <summary>取文件名前缀序号，如 "01-培立优BUG_群-title.png" → "01"</summary>
    private static string Index(string path)
    {
        var name = Path.GetFileName(path);
        var dash = name.IndexOf('-');
        return dash > 0 ? name[..dash] : name;
    }

    /// <summary>控制台展示用：压成单行并截断，避免表格错位</summary>
    private static string Display(string text, int max)
    {
        var flat = text.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return flat.Length <= max ? flat : flat[..max] + "…";
    }

    private static string DescribeDpi(int dpi, int scalePercent) =>
        dpi > 0 ? $"{dpi}({scalePercent}%)" : "—";

    /// <summary>一个「变体 × Scale」组合（或一个 Scale 的并集）的累计统计</summary>
    private sealed class Cell
    {
        /// <summary>已统计样本数</summary>
        public int Samples { get; set; }

        /// <summary>识别文本（标准化后）包含 ground truth 会话名的样本数</summary>
        public int Hits { get; set; }

        public long TotalMs { get; set; }

        public int Risk { get; set; }

        public int Unknown { get; set; }

        public int Private { get; set; }

        /// <summary>ground truth 命中风险名单的样本数（即"本该提醒"的样本）</summary>
        public int ExpectedRisk { get; set; }

        /// <summary>本该提醒却未判为 Risk（漏报，最关键的安全指标）</summary>
        public int Missed { get; set; }

        /// <summary>不该提醒却判为 Risk（误报）</summary>
        public int FalseAlarm { get; set; }

        /// <summary>ground truth 字符总数（字符级准确率的分母）</summary>
        public int Chars { get; set; }

        /// <summary>字符级编辑距离累计（字符级准确率的分子）；整串命中时记 0</summary>
        public int CharErrors { get; set; }

        public double HitRate => Samples == 0 ? 0 : Hits * 100.0 / Samples;

        public double AvgMs => Samples == 0 ? 0 : TotalMs / (double)Samples;

        /// <summary>字符级准确率：1 - 累计编辑距离 / 累计字符数。整串命中不算错，个别字混淆会被计入</summary>
        public double CharAccuracy => Chars == 0 ? 0 : Math.Max(0, 1 - CharErrors / (double)Chars) * 100;
    }

    private sealed record Sample(
        string FilePath,
        string Index,
        string Scene,
        string Display,
        string Kind,
        int Dpi,
        int ScalePercent);

    private enum VariantTone
    {
        /// <summary>原色，不做灰度化</summary>
        Color,

        /// <summary>灰度化 + 对比拉伸（浅色主题）</summary>
        Stretch,

        /// <summary>灰度化 + 大津法自适应阈值二值化</summary>
        Otsu,

        /// <summary>灰度化 + 固定阈值二值化（浅色主题）</summary>
        Fixed,

        /// <summary>灰度化 + 反相（深色主题）</summary>
        Invert,

        /// <summary>灰度化 + 反相 + 对比拉伸（深色主题）</summary>
        InvertStretch,
    }

    private sealed record Variant(string Name, VariantTone Tone, int Threshold, BitmapScalingMode Scaling);

    /// <summary>三态判定：与生产一致，"无法确认"绝不等于"安全"</summary>
    private enum Verdict
    {
        /// <summary>识别文本为空或过短 → 无法判定，按"不提醒"处理</summary>
        Unknown,

        /// <summary>命中风险名单 → 应显示提醒</summary>
        Risk,

        /// <summary>识别成功但未命中名单 → 不提醒</summary>
        Private,
    }
}
