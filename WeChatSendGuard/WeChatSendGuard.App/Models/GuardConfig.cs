namespace WeChatSendGuard.Models;

/// <summary>名单匹配模式。</summary>
public enum MatchMode
{
    /// <summary>继承全局默认模式。</summary>
    Default = 0,

    /// <summary>完全相等。</summary>
    Exact = 1,

    /// <summary>包含子串（默认）。</summary>
    Contains = 2,

    /// <summary>正则匹配。</summary>
    Regex = 3,
}

/// <summary>风险名单条目。</summary>
public sealed class RiskChatEntry
{
    public string Name { get; set; } = string.Empty;

    public MatchMode Mode { get; set; } = MatchMode.Default;

    /// <summary>可选备注，仅用于日志可读性。</summary>
    public string? Note { get; set; }
}

/// <summary>OCR 参数（96 DPI 基准）。</summary>
public sealed class OcrOptions
{
    /// <summary>标题区裁剪区（相对渲染子窗口客户区，96 DPI 基准物理像素）。</summary>
    public RegionSpec BaseRegion { get; set; } = new(300, 33, 580, 30);

    /// <summary>96 DPI 基准放大倍数；物理裁剪图先缩回基准尺寸再按此倍数放大，故任意 DPI 下等效放大恒定。</summary>
    public int BaseScale { get; set; } = 2;

    /// <summary>是否按 dpi/96 归一化裁剪区（实测必需，勿关）。</summary>
    public bool NormalizeByDpi { get; set; } = true;

    /// <summary>裁剪出来的标题小图落盘目录（调试用，留空则不落盘）。</summary>
    public string DumpDirectory { get; set; } = string.Empty;
}

public sealed class MatchingOptions
{
    public MatchMode DefaultMode { get; set; } = MatchMode.Contains;

    public bool CaseSensitive { get; set; } = false;
}

public sealed class BorderOptions
{
    public bool Enabled { get; set; } = true;

    /// <summary>边框颜色（#RRGGBB 或 #AARRGGBB）。</summary>
    public string Color { get; set; } = "#E53935";

    /// <summary>边框粗细（DIP）。</summary>
    public double Thickness { get; set; } = 4;

    /// <summary>是否显示文字标签（默认关）。</summary>
    public bool ShowLabel { get; set; } = false;

    /// <summary>标签格式，{0} 为命中的名单项。</summary>
    public string LabelFormat { get; set; } = "⚠ 风险聊天：{0}";
}

public sealed class GifOptions
{
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// 自定义 GIF 文件路径；相对路径按程序目录解析。默认用随程序分发的 resource\cat-run.gif，
    /// 留空则退回内置循环动画（纯 XAML 绘制）。
    /// </summary>
    public string File { get; set; } = @"resource\cat-run.gif";

    /// <summary>显示尺寸上限（96 DPI 基准 DIP，实际按 dpi/96 缩放）。</summary>
    public int MaxSize { get; set; } = 128;

    /// <summary>相对微信窗口左上角的偏移（96 DPI 基准，按 dpi/96 归一化）。</summary>
    public int OffsetX { get; set; } = 750;

    public int OffsetY { get; set; } = 450;
}

/// <summary>应用配置，落盘为 config.json（手工编辑）。</summary>
public sealed class GuardConfig
{
    /// <summary>总开关。</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>窗口位置轮询间隔（毫秒）。</summary>
    public int WindowPollMs { get; set; } = 100;

    /// <summary>
    /// OCR 识别间隔（毫秒），仅在微信前台时执行。
    /// PaddleOCR 单次识别约 600 ms，间隔小于该值只会被忙碌标志跳过，故默认 800。
    /// </summary>
    public int OcrIntervalMs { get; set; } = 800;

    /// <summary>去抖：连续 N 次识别结果一致才提交状态切换。</summary>
    public int DebounceCount { get; set; } = 2;

    public OcrOptions Ocr { get; set; } = new();

    public MatchingOptions Matching { get; set; } = new();

    public List<RiskChatEntry> RiskChats { get; set; } = new();

    public BorderOptions Border { get; set; } = new();

    public GifOptions Gif { get; set; } = new();
}
