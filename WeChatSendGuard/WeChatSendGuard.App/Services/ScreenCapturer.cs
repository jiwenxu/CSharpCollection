using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using WeChatSendGuard.Models;
using WeChatSendGuard.Native;

namespace WeChatSendGuard.Services;

/// <summary>
/// 只读截图与裁剪。
/// 使用 <c>PrintWindow(PW_RENDERFULLCONTENT)</c>：Spike-05 已证明其可离屏抓到
/// Qt 自绘内容（微信 4.x 的界面全部自绘于 MMUIRenderSubWindowHW）。
/// </summary>
internal static class ScreenCapturer
{
    /// <summary>抓取整个窗口。返回的位图尺寸等于传入矩形尺寸。</summary>
    public static BitmapSource CaptureWindow(IntPtr hwnd, PixelRect rect)
    {
        if (rect.IsEmpty)
        {
            throw new InvalidOperationException($"目标矩形无效：{rect}");
        }

        var sourceDc = NativeMethods.GetWindowDC(hwnd);
        if (sourceDc == IntPtr.Zero)
        {
            throw new InvalidOperationException("无法获取窗口 DC");
        }

        var memDc = NativeMethods.CreateCompatibleDC(sourceDc);
        var bitmap = NativeMethods.CreateCompatibleBitmap(sourceDc, rect.Width, rect.Height);
        var previous = NativeMethods.SelectObject(memDc, bitmap);

        try
        {
            if (!NativeMethods.PrintWindow(hwnd, memDc, NativeMethods.PW_RENDERFULLCONTENT))
            {
                throw new InvalidOperationException("PrintWindow 调用失败");
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
            NativeMethods.ReleaseDC(hwnd, sourceDc);
        }
    }

    /// <summary>按偏移裁剪；越界部分自动截断。</summary>
    public static BitmapSource Crop(BitmapSource source, RegionSpec region)
    {
        var x = Math.Clamp(region.X, 0, Math.Max(0, source.PixelWidth - 1));
        var y = Math.Clamp(region.Y, 0, Math.Max(0, source.PixelHeight - 1));
        var width = Math.Min(region.Width, source.PixelWidth - x);
        var height = Math.Min(region.Height, source.PixelHeight - y);

        if (width <= 0 || height <= 0)
        {
            throw new InvalidOperationException(
                $"裁剪区超出截图范围（截图 {source.PixelWidth}x{source.PixelHeight}，请求 {region}）");
        }

        var cropped = new CroppedBitmap(source, new Int32Rect(x, y, width, height));
        cropped.Freeze();
        return cropped;
    }
}
