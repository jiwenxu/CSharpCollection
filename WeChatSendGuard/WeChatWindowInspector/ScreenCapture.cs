using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WeChatWindowInspector;

/// <summary>
/// 截图与图像处理：把窗口 / 屏幕内容抓成 BitmapSource，并支持坐标网格标注、区域裁剪与 PNG 落盘。
/// 对应《Spike-05验证设计.md》第 5 节工具改造需求；只读抓取，不修改目标窗口。
/// </summary>
internal static class ScreenCapture
{
    public const string MethodPrintWindow = "PrintWindow";

    public const string MethodScreenBitBlt = "ScreenBitBlt";

    /// <summary>网格线间距（物理像素）</summary>
    private const int GridStep = 50;

    /// <summary>
    /// 抓取目标矩形区域。
    /// useScreen=false 走 <c>PrintWindow(PW_RENDERFULLCONTENT)</c>（可离屏抓取自绘内容，不保证成功）；
    /// useScreen=true 走屏幕 BitBlt（所见即所得，但要求窗口可见且未被遮挡）。
    /// </summary>
    public static BitmapSource Capture(IntPtr hwnd, NativeMethods.RECT rect, bool useScreen)
    {
        var width = rect.Right - rect.Left;
        var height = rect.Bottom - rect.Top;
        if (width <= 0 || height <= 0)
        {
            throw new InvalidOperationException($"目标矩形无效：{width}x{height}");
        }

        var sourceDc = useScreen ? NativeMethods.GetDC(IntPtr.Zero) : NativeMethods.GetWindowDC(hwnd);
        if (sourceDc == IntPtr.Zero)
        {
            throw new InvalidOperationException("无法获取源 DC");
        }

        var memDc = NativeMethods.CreateCompatibleDC(sourceDc);
        var bitmap = NativeMethods.CreateCompatibleBitmap(sourceDc, width, height);
        var previous = NativeMethods.SelectObject(memDc, bitmap);

        try
        {
            var ok = useScreen
                ? NativeMethods.BitBlt(memDc, 0, 0, width, height, sourceDc, rect.Left, rect.Top, NativeMethods.SRCCOPY)
                : NativeMethods.PrintWindow(hwnd, memDc, NativeMethods.PW_RENDERFULLCONTENT);

            if (!ok)
            {
                throw new InvalidOperationException(useScreen ? "BitBlt 调用失败" : "PrintWindow 调用失败");
            }

            var image = Imaging.CreateBitmapSourceFromHBitmap(
                bitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            image.Freeze();
            return image;
        }
        finally
        {
            NativeMethods.SelectObject(memDc, previous);
            NativeMethods.DeleteObject(bitmap);
            NativeMethods.DeleteDC(memDc);
            NativeMethods.ReleaseDC(useScreen ? IntPtr.Zero : hwnd, sourceDc);
        }
    }

    /// <summary>在整图上叠加坐标刻度网格：每 50px 细线，每 100px 加粗线并标注坐标，便于目视换算标题区偏移。</summary>
    public static BitmapSource DrawGrid(BitmapSource source)
    {
        var width = source.PixelWidth;
        var height = source.PixelHeight;

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawImage(source, new Rect(0, 0, width, height));

            var thin = new Pen(new SolidColorBrush(Color.FromArgb(110, 0, 255, 0)), 1);
            var thick = new Pen(new SolidColorBrush(Color.FromArgb(200, 255, 0, 255)), 1);
            thin.Freeze();
            thick.Freeze();

            for (var x = 0; x <= width; x += GridStep)
            {
                dc.DrawLine(x % (GridStep * 2) == 0 ? thick : thin, new Point(x + 0.5, 0), new Point(x + 0.5, height));
            }

            for (var y = 0; y <= height; y += GridStep)
            {
                dc.DrawLine(y % (GridStep * 2) == 0 ? thick : thin, new Point(0, y + 0.5), new Point(width, y + 0.5));
            }

            var textBrush = new SolidColorBrush(Color.FromArgb(230, 255, 255, 0));
            textBrush.Freeze();
            for (var x = 0; x <= width; x += GridStep * 2)
            {
                for (var y = 0; y <= height; y += GridStep * 2)
                {
                    dc.DrawText(CreateText($"{x},{y}", textBrush), new Point(x + 3, y + 3));
                }
            }
        }

        var target = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        target.Render(visual);
        target.Freeze();
        return target;
    }

    /// <summary>按相对渲染子窗口的偏移裁剪标题区域；越界部分自动截断。</summary>
    public static BitmapSource Crop(BitmapSource source, RegionSpec region)
    {
        var x = Math.Clamp(region.X, 0, Math.Max(0, source.PixelWidth - 1));
        var y = Math.Clamp(region.Y, 0, Math.Max(0, source.PixelHeight - 1));
        var width = Math.Min(region.Width, source.PixelWidth - x);
        var height = Math.Min(region.Height, source.PixelHeight - y);

        if (width <= 0 || height <= 0)
        {
            throw new InvalidOperationException(
                $"裁剪区域超出截图范围（截图 {source.PixelWidth}x{source.PixelHeight}，请求 {region}）");
        }

        var cropped = new CroppedBitmap(source, new Int32Rect(x, y, width, height));
        cropped.Freeze();
        return cropped;
    }

    public static void SavePng(BitmapSource source, string path)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));

        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    private static FormattedText CreateText(string value, Brush brush) =>
        new(value,
            System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface("Consolas"),
            11,
            brush,
            96);
}
