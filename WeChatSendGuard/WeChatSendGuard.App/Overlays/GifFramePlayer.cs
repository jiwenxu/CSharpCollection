using System.IO;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace WeChatSendGuard.Overlays;

/// <summary>
/// 逐帧播放 GIF。
/// WPF 的 Image 不直接播放动图，这里用 GifBitmapDecoder 解出帧与延迟自行播放，
/// 保持"零外部依赖"（不引入 WpfAnimatedGif 等第三方包）。
/// </summary>
internal sealed class GifFramePlayer : IDisposable
{
    private readonly BitmapFrame[] _frames;
    private readonly int[] _delaysMs;
    private readonly DispatcherTimer _timer;
    private int _index;

    private GifFramePlayer(BitmapFrame[] frames, int[] delaysMs)
    {
        _frames = frames;
        _delaysMs = delaysMs;
        _timer = new DispatcherTimer(DispatcherPriority.Render);
        _timer.Tick += OnTick;
    }

    public event Action<BitmapSource>? FrameChanged;

    public int NaturalWidth => _frames[0].PixelWidth;

    public int NaturalHeight => _frames[0].PixelHeight;

    /// <summary>尝试加载 GIF；文件不存在或解码失败时返回 null（调用方回退到内置动画）。</summary>
    public static GifFramePlayer? TryLoad(string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return null;
            }

            var decoder = new GifBitmapDecoder(
                new Uri(System.IO.Path.GetFullPath(path)),
                BitmapCreateOptions.PreservePixelFormat,
                BitmapCacheOption.OnLoad);

            var frames = decoder.Frames.ToArray();
            if (frames.Length == 0)
            {
                return null;
            }

            var delays = frames.Select(ReadDelayMs).ToArray();
            return new GifFramePlayer(frames, delays);
        }
        catch
        {
            return null;
        }
    }

    public void Start()
    {
        _index = 0;
        FrameChanged?.Invoke(_frames[0]);
        ScheduleNext();
    }

    public void Dispose()
    {
        _timer.Stop();
        _timer.Tick -= OnTick;
    }

    private void OnTick(object? sender, EventArgs e)
    {
        _timer.Stop();
        _index = (_index + 1) % _frames.Length;
        FrameChanged?.Invoke(_frames[_index]);
        ScheduleNext();
    }

    private void ScheduleNext()
    {
        // 少于 2 帧时无需循环
        if (_frames.Length < 2)
        {
            return;
        }

        _timer.Interval = TimeSpan.FromMilliseconds(_delaysMs[_index]);
        _timer.Start();
    }

    /// <summary>GIF 帧延迟以 1/100 秒存储；缺失或异常时取 100ms。</summary>
    private static int ReadDelayMs(BitmapFrame frame)
    {
        try
        {
            if (frame.Metadata is BitmapMetadata metadata
                && metadata.GetQuery("/grctlext/Delay") is ushort delay
                && delay > 0)
            {
                return Math.Clamp(delay * 10, 20, 1000);
            }
        }
        catch
        {
            // 元数据缺失，走默认值
        }

        return 100;
    }
}
