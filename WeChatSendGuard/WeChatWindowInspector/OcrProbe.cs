using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Security.Cryptography;

namespace WeChatWindowInspector;

/// <summary>
/// Phase B OCR 探针：对已采集的标题区截图做本地 OCR（Windows.Media.Ocr，零外部依赖），
/// 与 sidecar 中的人工标注比对，统计命中率，并演示"包含即提醒"的匹配方式。
/// 对应《Spike-05验证设计.md》第 4 节 Phase B 与判据 G2/G3。
///
/// 第二轮改进：对同一张裁剪图生成多路预处理变体（原色/灰度/对比拉伸/二值化/Otsu/白边 ×
/// 高质量/最近邻插值），逐路识别后取"并集"判定。目标是压住小字号拉丁字母的误识（BUG→BIJG），
/// 因为漏报（该提醒没提醒）是不可接受的方向。
/// </summary>
internal static class OcrProbe
{
    /// <summary>优先使用的识别语言（微信界面为简体中文）</summary>
    private const string PreferredLanguage = "zh-Hans-CN";

    /// <summary>
    /// 实测有效且互补的三路变体（其余候选已淘汰：nn/gray 与 raw 结果完全一致，bin128 命中 0，
    /// otsuFat 相对 otsu 无增益，otsuPad 只引入边缘噪字）：
    ///   raw     原色 + 高质量插值，长于整体字形（徐二狗、培立优办公室群）
    ///   stretch 灰度对比拉伸 + 高质量插值，长于细笔画（杨潇）
    ///   otsu    大津二值化 + 最近邻插值，长于拉丁字母（BUG，原色下会误识为 BIJG）
    /// </summary>
    private static readonly Variant[] Variants =
    [
        new("raw", VariantTone.Color, BitmapScalingMode.HighQuality),
        new("stretch", VariantTone.Stretch, BitmapScalingMode.HighQuality),
        new("otsu", VariantTone.Binary, BitmapScalingMode.NearestNeighbor),
    ];

    public static int Run(LogWriter log, CliOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.OcrDir))
        {
            log.Write("用法：--ocr --ocr-dir <采集目录> [--ocr-scale 5] [--ocr-region x,y,w,h] [--names \"风险名1,风险名2\"]");
            log.Write(@"示例：--ocr --ocr-dir logs\spike05");
            log.Write(@"示例：--ocr --ocr-dir logs\spike05 --ocr-region 375,41,725,38（从 -full.png 重裁剪，用于验证 DPI 缩放）");
            return 1;
        }

        var directory = Path.GetFullPath(options.OcrDir);
        if (!Directory.Exists(directory))
        {
            log.Write($"目录不存在：{directory}");
            return 1;
        }

        // 指定 --ocr-region 时从整图重裁剪，否则直接用采集时裁好的标题区小图
        var suffix = options.OcrRegion is null ? "-title.png" : "-full.png";
        var files = Directory.GetFiles(directory, $"*{suffix}")
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (files.Length == 0)
        {
            log.Write($"目录内没有 *{suffix} 截图：{directory}");
            return 1;
        }

        log.Write(string.Empty);
        log.Write("Phase B：标题区 OCR 探针（引擎 Windows.Media.Ocr，零外部依赖）");
        log.Write($"  输入目录 : {directory}");
        log.Write($"  样本数   : {files.Length}");
        log.Write($"  放大倍数 : {options.OcrScale}x");
        log.Write($"  预处理   : {Variants.Length} 路变体（{string.Join(", ", Variants.Select(variant => variant.Name))}），判定取并集");
        if (options.OcrDumpDir.Length > 0)
        {
            log.Write($"  变体导出 : {Path.GetFullPath(options.OcrDumpDir)}");
        }

        if (options.OcrRegion is { } ocrRegion)
        {
            log.Write($"  裁剪区   : {ocrRegion}（物理像素，从 -full.png 重裁剪）");
        }

        string engineLanguage;
        try
        {
            engineLanguage = DescribeEngine();
        }
        catch (Exception ex)
        {
            log.Write($"OCR 引擎不可用：{ex.Message}");
            return 1;
        }

        log.Write($"  识别语言 : {engineLanguage}");
        if (options.Names.Length > 0)
        {
            log.Write($"  风险名单 : {options.Names}（匹配方式：识别文本包含名单项即提醒）");
        }

        var names = options.Names
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        log.Write(string.Empty);
        log.Write("  #   期望会话名           类型  DPI(缩放)  原样  仅汉字  变体命中  用时");

        var variantHits = new int[Variants.Length];
        var variantCjkHits = new int[Variants.Length];
        var unionRawHits = 0;
        var unionCjkHits = 0;
        var verdictHits = 0;
        var total = 0;
        long totalMs = 0;

        foreach (var file in files)
        {
            // sidecar 命名规则：<prefix>-title.png / <prefix>-full.png 对应 <prefix>.json
            var jsonPath = file[..^suffix.Length] + ".json";
            var (scene, kind, dpi, scalePercent) = ReadSidecar(jsonPath);
            var expected = Normalize(scene);
            var expectedCjk = NormalizeCjk(scene);
            var index = Index(file);
            total++;

            // 裁剪区与放大倍数归一化：--ocr-region-dpi96 时把给定区域视为 96 DPI 基准。
            // 区域按 dpi/96 放大、放大倍数按 dpi/96 缩小，使"等效放大 = scale × dpi/96"恒定。
            var region = options.OcrRegion;
            var scale = options.OcrScale;
            if (region is { } baseRegion && options.OcrRegionScaleByDpi && dpi > 0)
            {
                var factor = dpi / 96.0;
                region = new RegionSpec(
                    (int)Math.Round(baseRegion.X * factor),
                    (int)Math.Round(baseRegion.Y * factor),
                    (int)Math.Round(baseRegion.Width * factor),
                    (int)Math.Round(baseRegion.Height * factor));
                scale = Math.Max(1, (int)Math.Round(options.OcrScale / factor));
            }

            BitmapSource image;
            try
            {
                image = LoadPng(file);
                if (region is { } cropRegion)
                {
                    image = ScreenCapture.Crop(image, cropRegion);
                }
            }
            catch (Exception ex)
            {
                log.Write($"  {index,-3} 载入失败 —— {ex.Message}");
                continue;
            }

            var watch = Stopwatch.StartNew();
            var texts = new string[Variants.Length];
            for (var i = 0; i < Variants.Length; i++)
            {
                try
                {
                    var rendered = RenderVariant(image, Variants[i], scale);
                    if (options.OcrDumpDir.Length > 0)
                    {
                        Dump(rendered, Path.GetFullPath(options.OcrDumpDir), index, Variants[i].Name);
                    }

                    texts[i] = Recognize(rendered);
                }
                catch (Exception ex)
                {
                    texts[i] = string.Empty;
                    log.Write($"  {index,-3} 变体 {Variants[i].Name} OCR 失败 —— {ex.Message}");
                }
            }

            watch.Stop();
            totalMs += watch.ElapsedMilliseconds;

            var hits = new bool[Variants.Length];
            var cjkHits = new bool[Variants.Length];
            for (var i = 0; i < Variants.Length; i++)
            {
                hits[i] = expected.Length > 0 && Normalize(texts[i]).Contains(expected, StringComparison.Ordinal);
                cjkHits[i] = expectedCjk.Length > 0 && NormalizeCjk(texts[i]).Contains(expectedCjk, StringComparison.Ordinal);
            }

            for (var i = 0; i < Variants.Length; i++)
            {
                if (hits[i])
                {
                    variantHits[i]++;
                }

                if (cjkHits[i])
                {
                    variantCjkHits[i]++;
                }
            }

            var unionRaw = hits.Any(hit => hit);
            var unionCjk = cjkHits.Any(hit => hit);
            if (unionRaw)
            {
                unionRawHits++;
            }

            if (unionCjk)
            {
                unionCjkHits++;
            }

            log.Write($"  {index,-3} {Display(scene),-20} {(kind.Length > 0 ? kind : "—"),-4} {DescribeDpi(dpi, scalePercent),-9} " +
                      $"{(unionRaw ? "是" : "否")}    {(unionCjk ? "是" : "否")}     {hits.Count(hit => hit)}/{Variants.Length}     {watch.ElapsedMilliseconds}ms");

            if (region is not null && options.OcrRegionScaleByDpi)
            {
                log.Write($"      └ 实际裁剪区 {region.X},{region.Y},{region.Width},{region.Height}，实际放大 {scale}x" +
                          $"（按 dpi/96 = {dpi / 96.0:F2} 归一化，等效 96 DPI 下 {options.OcrScale}x）");
            }

            WriteTextGroups(log, texts);

            if (names.Length > 0)
            {
                // 按用户口径判定：识别文本包含配置的风险名即提醒
                var matched = names
                    .Where(name => texts.Any(text => Normalize(text).Contains(Normalize(name), StringComparison.Ordinal)))
                    .ToArray();
                var matchedCjk = names
                    .Where(name =>
                    {
                        var target = NormalizeCjk(name);
                        return target.Length > 0 && texts.Any(text => NormalizeCjk(text).Contains(target, StringComparison.Ordinal));
                    })
                    .ToArray();

                if (matched.Length > 0)
                {
                    verdictHits++;
                }

                log.Write($"      └ 原样判定：{(matched.Length > 0 ? $"提醒（{string.Join("、", matched)}）" : "不提醒")}" +
                          $"    仅汉字判定：{(matchedCjk.Length > 0 ? $"提醒（{string.Join("、", matchedCjk)}）" : "不提醒")}");
            }
        }

        var rawRate = total == 0 ? 0 : unionRawHits * 100.0 / total;
        var cjkRate = total == 0 ? 0 : unionCjkHits * 100.0 / total;
        var verdictRate = total == 0 ? 0 : verdictHits * 100.0 / total;

        log.Write(string.Empty);
        log.Write("各变体命中率（原样包含 / 仅汉字包含）：");
        for (var i = 0; i < Variants.Length; i++)
        {
            log.Write($"  {Variants[i].Name,-10} {variantHits[i],2}/{total} = {Rate(variantHits[i], total),5:F1}%   " +
                      $"{variantCjkHits[i],2}/{total} = {Rate(variantCjkHits[i], total),5:F1}%");
        }

        log.Write(string.Empty);
        log.Write($"并集原样包含命中（严格对齐 sidecar 的 scene 字段）：{unionRawHits}/{total} = {rawRate:F1}%");
        log.Write($"并集仅汉字包含命中：{unionCjkHits}/{total} = {cjkRate:F1}%（过滤拉丁字母误识，但会丢弃数字与字母）");
        if (names.Length > 0)
        {
            log.Write($"判定口径命中（识别文本包含配置风险名即提醒）：{verdictHits}/{total} = {verdictRate:F1}%");
        }

        if (total > 0)
        {
            log.Write($"平均 OCR 用时：{totalMs / (double)total:F0} ms/次（含 {Variants.Length} 路变体）");
        }

        // 有风险名单时以判定口径为准；否则只能报严格口径
        var strictMetricNote = names.Length > 0
            ? "判定口径（识别文本包含配置风险名即提醒）"
            : "严格口径（识别文本包含 sidecar 的 scene 字段）";
        var pass = names.Length > 0 ? verdictRate >= 95 : rawRate >= 95;
        var rate = names.Length > 0 ? verdictRate : rawRate;

        log.Write(pass
            ? $"结论：G2 通过（{strictMetricNote} {rate:F1}%）——标题区文本可被本地 OCR 稳定还原，视觉识别路线可行。"
            : $"结论：G2 未通过（{strictMetricNote} {rate:F1}%，判据要求 ≥95%）——需继续改进预处理/裁剪，或改走视觉指纹方案。");
        if (names.Length > 0 && rawRate < 95)
        {
            log.Write("      注：严格口径偏低时请先核对 sidecar 的 scene 字段是否混入了场景描述前缀（如\"最大化\"\"小窗\"\"DPI125\"）。");
        }

        return 0;
    }

    /// <summary>按识别文本分组打印变体名，便于看清哪一路起了作用</summary>
    private static void WriteTextGroups(LogWriter log, string[] texts)
    {
        var groups = new List<(string Text, List<string> Variants)>();
        for (var i = 0; i < texts.Length; i++)
        {
            var text = Flatten(texts[i]);
            var existing = groups.FindIndex(group => group.Text == text);
            if (existing >= 0)
            {
                groups[existing].Variants.Add(Variants[i].Name);
            }
            else
            {
                groups.Add((text, [Variants[i].Name]));
            }
        }

        foreach (var (text, variants) in groups)
        {
            log.Write($"      └ {string.Join("/", variants)} → {(text.Length == 0 ? "（空）" : Display(text, 48))}");
        }
    }

    private static double Rate(int hits, int total) => total == 0 ? 0 : hits * 100.0 / total;

    /// <summary>取文件名前缀序号，如 "01-培立优BUG_群-title.png" → "01"</summary>
    private static string Index(string path)
    {
        var name = Path.GetFileName(path);
        var dash = name.IndexOf('-');
        return dash > 0 ? name[..dash] : name;
    }

    /// <summary>控制台展示用：压成单行并截断，避免表格错位</summary>
    private static string Display(string text, int max = 18)
    {
        var flat = Flatten(text);
        return flat.Length <= max ? flat : flat[..max] + "…";
    }

    /// <summary>把裁剪图按指定变体预处理并放大，输出 OCR 可直接消费的 Bgra32 位图</summary>
    private static BitmapSource RenderVariant(BitmapSource source, Variant variant, int scale)
    {
        var processed = Preprocess(source, variant);
        var width = processed.PixelWidth * scale;
        var height = processed.PixelHeight * scale;

        var visual = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(visual, variant.Scaling);
        using (var context = visual.RenderOpen())
        {
            context.DrawRectangle(Brushes.White, null, new Rect(0, 0, width, height));
            context.DrawImage(processed, new Rect(0, 0, width, height));
        }

        var target = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        target.Render(visual);
        target.Freeze();

        var converted = new FormatConvertedBitmap(target, PixelFormats.Bgra32, null, 0);
        converted.Freeze();
        return converted;
    }

    private static BitmapSource Preprocess(BitmapSource source, Variant variant)
    {
        if (variant.Tone == VariantTone.Color)
        {
            return source;
        }

        var (gray, width, height) = ReadGray(source);
        if (variant.Tone == VariantTone.Stretch)
        {
            return CreateGray(Stretch(gray), width, height);
        }

        return Binarize(gray, width, height, OtsuThreshold(gray));
    }

    private static void Dump(BitmapSource image, string directory, string index, string variant)
    {
        Directory.CreateDirectory(directory);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(image));
        using var stream = File.Create(Path.Combine(directory, $"{index}-{variant}.png"));
        encoder.Save(stream);
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

    private static OcrEngine? _engine;

    private static string Recognize(BitmapSource prepared)
    {
        var width = prepared.PixelWidth;
        var height = prepared.PixelHeight;
        var stride = width * 4;
        var pixels = new byte[stride * height];
        prepared.CopyPixels(pixels, stride, 0);

        // WinRT 异步调用放到线程池（MTA）执行，避免在 STA 主线程上阻塞等待
        return Task.Run(() =>
        {
            var buffer = CryptographicBuffer.CreateFromByteArray(pixels);
            using var bitmap = SoftwareBitmap.CreateCopyFromBuffer(
                buffer,
                BitmapPixelFormat.Bgra8,
                width,
                height,
                BitmapAlphaMode.Ignore);

            var engine = _engine ??= OcrEngine.TryCreateFromLanguage(new Windows.Globalization.Language(PreferredLanguage))
                                     ?? OcrEngine.TryCreateFromUserProfileLanguages()
                                     ?? throw new InvalidOperationException("系统未安装可用的 OCR 识别语言包");

            var result = engine.RecognizeAsync(bitmap).AsTask().GetAwaiter().GetResult();
            return result.Text ?? string.Empty;
        }).GetAwaiter().GetResult();
    }

    private static string DescribeEngine()
    {
        var languages = OcrEngine.AvailableRecognizerLanguages
            .Select(language => language.LanguageTag)
            .ToArray();

        if (languages.Length == 0)
        {
            throw new InvalidOperationException("系统未安装任何 OCR 识别语言包");
        }

        return languages.Contains(PreferredLanguage)
            ? PreferredLanguage
            : $"用户默认（可用：{string.Join(", ", languages)}）";
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

    /// <summary>
    /// 从同名 sidecar 读取人工标注与采集时的 DPI。
    /// 缩放因子必须用 `dpi / 96`（如 dpi=120 → 1.25），**不能**用 `scalePercent / 96`。
    /// </summary>
    private static (string Scene, string Kind, int Dpi, int ScalePercent) ReadSidecar(string jsonPath)
    {
        if (!File.Exists(jsonPath))
        {
            return (string.Empty, string.Empty, 0, 0);
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(jsonPath, Encoding.UTF8));
            var root = document.RootElement;
            var scene = root.TryGetProperty("scene", out var sceneElement) ? sceneElement.GetString() ?? string.Empty : string.Empty;
            var kind = root.TryGetProperty("kind", out var kindElement) ? kindElement.GetString() ?? string.Empty : string.Empty;
            var dpi = root.TryGetProperty("dpi", out var dpiElement) ? dpiElement.GetInt32() : 0;
            var scalePercent = root.TryGetProperty("scalePercent", out var scaleElement) ? scaleElement.GetInt32() : 0;
            return (scene, kind, dpi, scalePercent);
        }
        catch
        {
            return (string.Empty, string.Empty, 0, 0);
        }
    }

    private static string DescribeDpi(int dpi, int scalePercent) =>
        dpi > 0 ? $"{dpi}({scalePercent}%)" : "—";

    /// <summary>去空白用于包含判定（OCR 可能在汉字间插入空格）</summary>
    private static string Normalize(string text) =>
        new(text.Where(character => !char.IsWhiteSpace(character)).ToArray());

    /// <summary>只保留中日韩字符：绕开 OCR 对拉丁字母的误识（如 BUG→BIJG、优→Vt）</summary>
    private static string NormalizeCjk(string text) =>
        new(text.Where(IsCjk).ToArray());

    private static bool IsCjk(char character) =>
        (character >= 0x3400 && character <= 0x4DBF) ||   // 扩展 A
        (character >= 0x4E00 && character <= 0x9FFF) ||   // 基本区
        (character >= 0xF900 && character <= 0xFAFF);     // 兼容表意

    /// <summary>把识别文本压成单行便于表格展示</summary>
    private static string Flatten(string text) =>
        text.Replace('\r', ' ').Replace('\n', ' ').Trim();

    private enum VariantTone
    {
        /// <summary>原色，不做灰度化</summary>
        Color,

        /// <summary>灰度化 + 对比拉伸</summary>
        Stretch,

        /// <summary>灰度化 + 大津法二值化</summary>
        Binary,
    }

    private sealed record Variant(string Name, VariantTone Tone, BitmapScalingMode Scaling);
}
