namespace WeChatWindowInspector;

/// <summary>
/// 单个窗口的瞬时快照信息。
/// 对应需求文档 v1.1 第 7.1 节要求输出的字段集合。
/// </summary>
internal sealed record WindowInfo
{
    public IntPtr Hwnd { get; init; }

    /// <summary>窗口类名（v1.1 中 "WeChatMainWndForPC" / "ChatWnd" 均为待验证假设）</summary>
    public string ClassName { get; init; } = string.Empty;

    /// <summary>窗口标题</summary>
    public string Text { get; init; } = string.Empty;

    /// <summary>父窗口（GA_PARENT）。顶层窗口的父为桌面，此处输出 0x00000000 表示无父窗口</summary>
    public IntPtr Parent { get; init; }

    /// <summary>所有者窗口（GW_OWNER）</summary>
    public IntPtr Owner { get; init; }

    /// <summary>所属顶层窗口（GA_ROOT）</summary>
    public IntPtr Root { get; init; }

    /// <summary>层级深度：0 为顶层窗口，1 及以上为子窗口</summary>
    public int Depth { get; init; }

    public int ProcessId { get; init; }

    public string ProcessName { get; init; } = string.Empty;

    public int Left { get; init; }
    public int Top { get; init; }
    public int Right { get; init; }
    public int Bottom { get; init; }

    public bool Visible { get; init; }
    public bool Enabled { get; init; }
    public bool Minimized { get; init; }
    public bool Maximized { get; init; }

    /// <summary>是否为当前前台窗口</summary>
    public bool IsForeground { get; init; }

    /// <summary>窗口所在显示器的 DPI（0 表示无法获取）</summary>
    public uint Dpi { get; init; }

    public int Style { get; init; }
    public int ExStyle { get; init; }

    public int Width => Right - Left;

    public int Height => Bottom - Top;

    public string HwndText => $"0x{Hwnd.ToInt64():X8}";

    public string RectText => $"({Left},{Top},{Right},{Bottom}) Size={Width}x{Height}";

    public string DpiText => Dpi == 0 ? "未知" : $"{Dpi} ({Dpi * 100 / 96}%)";

    public string LevelText => Depth == 0 ? "顶层窗口" : $"子窗口 depth={Depth}";

    public string Describe() =>
        $"{HwndText} {LevelText} [{ClassName}] \"{Text}\" Rect={RectText} Visible={Visible}";

    /// <summary>比较两个快照之间的关键字段差异，返回可读的变更描述</summary>
    public IReadOnlyList<string> DiffFrom(WindowInfo old)
    {
        var changes = new List<string>();

        if (!string.Equals(Text, old.Text, StringComparison.Ordinal))
        {
            changes.Add($"WindowText: \"{old.Text}\" -> \"{Text}\"");
        }

        if (RectText != old.RectText)
        {
            changes.Add($"Rect: {old.RectText} -> {RectText}");
        }

        if (Visible != old.Visible)
        {
            changes.Add($"Visible: {old.Visible} -> {Visible}");
        }

        if (Enabled != old.Enabled)
        {
            changes.Add($"Enabled: {old.Enabled} -> {Enabled}");
        }

        if (Minimized != old.Minimized)
        {
            changes.Add($"Minimized: {old.Minimized} -> {Minimized}");
        }

        if (Maximized != old.Maximized)
        {
            changes.Add($"Maximized: {old.Maximized} -> {Maximized}");
        }

        if (!string.Equals(ClassName, old.ClassName, StringComparison.Ordinal))
        {
            changes.Add($"ClassName: {old.ClassName} -> {ClassName}");
        }

        if (Parent != old.Parent)
        {
            changes.Add($"Parent: 0x{old.Parent.ToInt64():X8} -> 0x{Parent.ToInt64():X8}");
        }

        if (Owner != old.Owner)
        {
            changes.Add($"Owner: 0x{old.Owner.ToInt64():X8} -> 0x{Owner.ToInt64():X8}");
        }

        if (Dpi != old.Dpi)
        {
            changes.Add($"DPI: {old.DpiText} -> {DpiText}");
        }

        if (Style != old.Style)
        {
            changes.Add($"Style: 0x{old.Style:X8} -> 0x{Style:X8}");
        }

        if (ExStyle != old.ExStyle)
        {
            changes.Add($"ExStyle: 0x{old.ExStyle:X8} -> 0x{ExStyle:X8}");
        }

        return changes;
    }
}
