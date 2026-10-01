namespace WeChatSendGuard.Models;

/// <summary>矩形区域（相对渲染子窗口客户区的物理像素偏移）。</summary>
public sealed record RegionSpec(int X, int Y, int Width, int Height)
{
    public override string ToString() => $"{X},{Y},{Width}x{Height}";

    /// <summary>按 dpi/96 缩放，用于 DPI 归一化。</summary>
    public RegionSpec ScaleBy(double factor) => new(
        (int)Math.Round(X * factor),
        (int)Math.Round(Y * factor),
        (int)Math.Round(Width * factor),
        (int)Math.Round(Height * factor));
}
