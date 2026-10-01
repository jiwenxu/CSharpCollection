using System.IO;
using WeChatSendGuard.Models;

namespace WeChatSendGuard.Overlays;

/// <summary>
/// GIF 悬浮提醒窗口：默认播放随程序分发的 resource\cat-run.gif（可被 config.json 的 gif.file 覆盖），
/// 文件缺失或留空时回退到内置循环动画。
/// 位置由 GuardEngine 按"相对微信窗口左上角偏移 × dpi/96"计算后传入。
/// </summary>
public partial class GifOverlayWindow : OverlayWindowBase
{
    private GifFramePlayer? _player;
    private string? _loadedFile;
    private int _naturalWidth;
    private int _naturalHeight;

    public GifOverlayWindow()
    {
        InitializeComponent();
    }

    /// <summary>显示尺寸上限内的最大边长（96 DPI 基准 DIP）。</summary>
    public int DisplaySize { get; private set; } = 128;

    /// <summary>是否在使用内置循环动画（诊断用）。</summary>
    public bool IsBuiltIn { get; private set; } = true;

    /// <summary>按配置准备素材；同一文件与上限下重复调用无副作用。</summary>
    public void Prepare(GifOptions options)
    {
        var file = ResolvePath(options.File);
        if (_loadedFile != file)
        {
            _loadedFile = file;
            _player?.Dispose();
            _player = GifFramePlayer.TryLoad(file);

            if (_player is not null)
            {
                IsBuiltIn = false;
                GifImage.Visibility = System.Windows.Visibility.Visible;
                BuiltInAnimation.Visibility = System.Windows.Visibility.Collapsed;
                GifImage.Source = null;
                _player.FrameChanged += frame => GifImage.Source = frame;
                _player.Start();
                _naturalWidth = _player.NaturalWidth;
                _naturalHeight = _player.NaturalHeight;
            }
            else
            {
                IsBuiltIn = true;
                GifImage.Source = null;
                GifImage.Visibility = System.Windows.Visibility.Collapsed;
                BuiltInAnimation.Visibility = System.Windows.Visibility.Visible;
                _naturalWidth = options.MaxSize;
                _naturalHeight = options.MaxSize;
            }
        }
        else if (IsBuiltIn)
        {
            _naturalWidth = options.MaxSize;
            _naturalHeight = options.MaxSize;
        }

        var naturalMax = Math.Max(_naturalWidth, _naturalHeight);
        DisplaySize = naturalMax <= 0 ? options.MaxSize : Math.Min(naturalMax, options.MaxSize);
    }

    /// <summary>
    /// 相对路径按程序目录解析（随程序分发的 resource\*.gif 就是相对路径）；
    /// 绝对路径原样返回；空串原样返回（由 <see cref="GifFramePlayer.TryLoad"/> 判为不可用 → 内置动画）。
    /// </summary>
    private static string ResolvePath(string file)
    {
        if (string.IsNullOrWhiteSpace(file) || Path.IsPathRooted(file))
        {
            return file;
        }

        return Path.Combine(AppContext.BaseDirectory, file);
    }

    /// <summary>计算窗口的物理像素尺寸（等比缩放到 DisplaySize 上限，再按 dpi/96 放大）。</summary>
    public (int Width, int Height) ComputePhysicalSize(double dpiScale)
    {
        var naturalMax = Math.Max(_naturalWidth, _naturalHeight);
        if (naturalMax <= 0)
        {
            var side = (int)Math.Round(DisplaySize * dpiScale);
            return (side, side);
        }

        var ratio = (double)DisplaySize / naturalMax;
        var width = (int)Math.Round(_naturalWidth * ratio * dpiScale);
        var height = (int)Math.Round(_naturalHeight * ratio * dpiScale);
        return (Math.Max(1, width), Math.Max(1, height));
    }
}
