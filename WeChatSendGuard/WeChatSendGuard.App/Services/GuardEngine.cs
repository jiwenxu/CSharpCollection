using System.Windows.Threading;
using WeChatSendGuard.Models;
using WeChatSendGuard.Overlays;

namespace WeChatSendGuard.Services;

/// <summary>
/// 调度引擎：窗口跟踪（100ms）+ 识别（500ms，仅微信前台）+ 匹配 + 状态机 + 覆盖层驱动。
///
/// 线程约定：
/// - 两个计时器都在 UI 线程触发；
/// - 截图与 OCR 这类耗时工作丢到线程池，完成后回 UI 线程提交状态；
/// - 覆盖层只在 UI 线程操作。
/// </summary>
internal sealed class GuardEngine : IDisposable
{
    private readonly Dispatcher _dispatcher;
    private readonly LogWriter _log;
    private readonly WeChatWindowLocator _locator = new();
    private readonly TitleOcrReader _ocrReader = new();
    private readonly BorderOverlayWindow _borderOverlay;
    private readonly GifOverlayWindow _gifOverlay;
    private readonly DispatcherTimer _windowTimer;
    private readonly DispatcherTimer _ocrTimer;

    private GuardConfig _config;

    private WindowSnapshot _snapshot = new();
    private ChatContext _context = new();
    private OverlayState _state = OverlayState.None;

    private string? _pendingText;
    private int _pendingCount;
    private bool _ocrBusy;
    private bool _disposed;
    private long _lastOcrMs;
    private string _lastParameterSignature = string.Empty;
    private string _lastSignature = string.Empty;

    // 命中会话切换（A→B 都命中名单）时的"先隐藏再显示"：让新提醒有明显的"消失→出现"分界，
    // 否则两个会话共用同一个 GIF 悬浮窗、动画连续播放，看起来像"上一个提醒还没消失、下一个已出现"。
    private string? _shownRiskKey;
    private DateTime _riskSwitchGapUntil = DateTime.MinValue;
    private static readonly TimeSpan RiskSwitchGap = TimeSpan.FromMilliseconds(300);

    // 场景切换留痕用的上一次采样（微信出现/消失、最小化/恢复）
    private bool _lastFound;
    private bool _lastMinimized;

    public GuardEngine(GuardConfig config, LogWriter log, Dispatcher dispatcher)
    {
        _config = config;
        _log = log;
        _dispatcher = dispatcher;

        _borderOverlay = new BorderOverlayWindow();
        _gifOverlay = new GifOverlayWindow();

        _windowTimer = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TimeSpan.FromMilliseconds(config.WindowPollMs) };
        _windowTimer.Tick += OnWindowTick;

        _ocrTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(config.OcrIntervalMs) };
        _ocrTimer.Tick += OnOcrTick;
    }

    /// <summary>状态刷新（仅当关键信息变化时触发）。</summary>
    public event Action<GuardStatus>? StatusChanged;

    public GuardConfig Config => _config;

    public void Start()
    {
        _log.Write($"引擎启动：窗口轮询 {_config.WindowPollMs}ms，识别间隔 {_config.OcrIntervalMs}ms，"
                   + $"名单 {_config.RiskChats.Count} 条，红框 {( _config.Border.Enabled ? "开" : "关")}，"
                   + $"GIF {(_config.Gif.Enabled ? $"开（{(_config.Gif.File.Length > 0 ? _config.Gif.File : "内置循环动画")}）" : "关")}");

        try
        {
            _log.Write($"OCR 引擎：{TitleOcrReader.DescribeEngine()}");
            // PaddleOCR 冷启动约 1.5s（模型加载 + 首帧）：挪到后台，避免拖慢第一次真识别
            Task.Run(() =>
            {
                try
                {
                    _ocrReader.Warmup();
                    _log.Write("OCR 引擎预热完成");
                }
                catch (Exception exception)
                {
                    _log.Write($"OCR 引擎预热失败（每次识别都会失败）：{exception.Message}");
                }
            });
        }
        catch (Exception exception)
        {
            _log.Write($"OCR 引擎不可用：{exception.Message}");
        }

        _windowTimer.Start();
        _ocrTimer.Start();
    }

    /// <summary>热重载配置（本阶段由状态窗口的"重载配置"触发）。</summary>
    public void Reload(GuardConfig config)
    {
        _config = config;
        _pendingText = null;
        _pendingCount = 0;
        _windowTimer.Interval = TimeSpan.FromMilliseconds(config.WindowPollMs);
        _ocrTimer.Interval = TimeSpan.FromMilliseconds(config.OcrIntervalMs);
        _lastSignature = string.Empty;
        _lastParameterSignature = string.Empty;
        _log.Write($"配置已重载：名单 {config.RiskChats.Count} 条，识别间隔 {config.OcrIntervalMs}ms，"
                   + $"总开关 {(config.Enabled ? "开" : "关")}");
    }

    /// <summary>托盘总开关：只改内存态，不写回 config.json（下次启动以文件为准）。</summary>
    public void SetEnabled(bool enabled)
    {
        if (_config.Enabled == enabled)
        {
            return;
        }

        _config.Enabled = enabled;
        _pendingText = null;
        _pendingCount = 0;
        _lastSignature = string.Empty;
        _log.Write($"总开关已{(enabled ? "启用" : "停用")}（托盘操作，仅本次运行有效）");
    }

    public void Dispose()
    {
        _disposed = true;
        _windowTimer.Stop();
        _ocrTimer.Stop();
        _borderOverlay.Close();
        _gifOverlay.Close();
        _ocrReader.Dispose();
    }

    private bool CanRecognize =>
        _config.Enabled
        && _snapshot.Found
        && _snapshot.Visible
        && !_snapshot.Minimized
        && _snapshot.IsForeground;

    // ---------- 窗口跟踪 ----------

    private void OnWindowTick(object? sender, EventArgs e)
    {
        _snapshot = _locator.Locate();
        LogScenarioTransitions();

        if (!CanRecognize)
        {
            // 不具备识别条件（未找到 / 最小化 / 非前台 / 已停用）：清空上下文，避免残留提醒
            ResetContext();
            _pendingText = null;
            _pendingCount = 0;
        }

        _state = OverlayStateMachine.Compute(_snapshot, _context, _config.Enabled);
        UpdateOverlays();
        PublishStatus();
    }

    /// <summary>
    /// 场景切换留痕：微信窗口出现/消失（关闭或崩溃）、最小化/恢复。
    /// 只在状态翻转时各写一行，避免 100ms 轮询刷屏。
    /// </summary>
    private void LogScenarioTransitions()
    {
        if (_snapshot.Found != _lastFound)
        {
            _lastFound = _snapshot.Found;
            _log.Write(_snapshot.Found
                ? $"已找到微信窗口（渲染区 {_snapshot.RenderRect}，dpi={_snapshot.Dpi}）"
                : "微信窗口已丢失（已关闭或崩溃），暂停识别并隐藏提醒");
        }

        if (_snapshot.Found && _snapshot.Minimized != _lastMinimized)
        {
            _lastMinimized = _snapshot.Minimized;
            _log.Write(_snapshot.Minimized
                ? "微信已最小化，暂停识别并隐藏提醒"
                : "微信已从最小化恢复，继续识别");
        }
    }

    private void ResetContext()
    {
        if (_context.Identity == ChatIdentityState.Unknown && _context.Risk == RiskState.Unknown)
        {
            return;
        }

        _context = _context with
        {
            ChatName = null,
            Identity = ChatIdentityState.Unknown,
            MatchedEntry = null,
            Risk = RiskState.Unknown,
            OcrText = string.Empty,
        };
    }

    // ---------- 识别 ----------

    private void OnOcrTick(object? sender, EventArgs e)
    {
        if (_ocrBusy || !CanRecognize)
        {
            return;
        }

        _ocrBusy = true;
        var hwnd = _snapshot.RenderWindow;
        var rect = _snapshot.RenderRect;
        var dpi = _snapshot.Dpi;
        var ocrOptions = _config.Ocr;

        Task.Run(() =>
        {
            try
            {
                var image = ScreenCapturer.CaptureWindow(hwnd, rect);
                return _ocrReader.Read(image, dpi, ocrOptions);
            }
            catch (Exception exception)
            {
                _log.Write($"识别失败：{exception.Message}");
                return null;
            }
        }).ContinueWith(
            task =>
            {
                // 退出过程中调度器可能已停止；此处静默跳过，避免关闭瞬间抛异常
                if (_disposed || _dispatcher.HasShutdownStarted)
                {
                    return;
                }

                try
                {
                    _dispatcher.Invoke(() => CommitOcr(task.Result));
                }
                catch (TaskCanceledException)
                {
                    // 调度器在 Invoke 期间停止，忽略
                }
            },
            TaskScheduler.Default);
    }

    private void CommitOcr(OcrReadResult? result)
    {
        _ocrBusy = false;

        if (result is null)
        {
            ResetContext();
            return;
        }

        _lastOcrMs = result.Milliseconds;

        // 识别参数变化时留痕：DPI 归一化是否生效，靠这行日志判断
        var parameterSignature = $"{result.Dpi}|{result.Region}|{result.RenderScale}";
        if (parameterSignature != _lastParameterSignature)
        {
            _lastParameterSignature = parameterSignature;
            _log.Write($"识别参数：dpi={result.Dpi}（{result.Dpi * 100 / 96}%），"
                       + $"裁剪区={result.Region}（基准 {_config.Ocr.BaseRegion}），"
                       + $"缩回基准 {result.BaselineRegion.Width}x{result.BaselineRegion.Height}，"
                       + $"放大={result.RenderScale:0.###}x（基准 {_config.Ocr.BaseScale}x）");
        }

        var flat = RiskMatcher.Flatten(result.Text);
        if (flat.Length == 0)
        {
            // 识别为空：不得回退为"正常"，保持 Unknown（漏报优先于误报）
            _pendingText = null;
            _pendingCount = 0;
            ResetContext();
            return;
        }

        // 去抖：连续 DebounceCount 次一致才提交，避免切换瞬间闪烁
        if (flat == _pendingText)
        {
            _pendingCount++;
        }
        else
        {
            _pendingText = flat;
            _pendingCount = 1;
        }

        if (_pendingCount < Math.Max(1, _config.DebounceCount))
        {
            return;
        }

        var (risk, matched) = RiskMatcher.Match(result.Text, _config);
        var previousRisk = _context.Risk;
        var previousMatched = _context.MatchedEntry;

        _context = new ChatContext
        {
            Hwnd = _snapshot.RenderWindow,
            Rect = _snapshot.RenderRect,
            Dpi = _snapshot.Dpi,
            ChatName = flat,
            Identity = ChatIdentityState.Known,
            MatchedEntry = matched,
            Risk = risk,
            OcrText = result.Text,
            OcrMilliseconds = result.Milliseconds,
        };

        if (risk != previousRisk || matched != previousMatched)
        {
            _log.Write(risk == RiskState.Risk
                ? $"命中风险：\"{matched}\"（识别文本：{result.Text}）"
                : $"未命中：识别文本 {result.Text}");
        }
    }

    // ---------- 覆盖层 ----------

    private void UpdateOverlays()
    {
        var showRisk = OverlayStateMachine.ShouldShow(_state) && _snapshot.Found && !_snapshot.RenderRect.IsEmpty;

        if (showRisk)
        {
            // 以当前识别到的会话名为键：命中会话改变（A→B）时先隐藏一小段时间再显示，
            // 形成"消失→出现"的分界；否则同一悬浮窗的 GIF 会连续播放，看不出切换。
            var riskKey = _context.ChatName ?? _context.MatchedEntry ?? "<risk>";
            if (_shownRiskKey is null)
            {
                _shownRiskKey = riskKey;
            }
            else if (!string.Equals(_shownRiskKey, riskKey, StringComparison.Ordinal))
            {
                _shownRiskKey = riskKey;
                _riskSwitchGapUntil = DateTime.UtcNow + RiskSwitchGap;
            }

            if (DateTime.UtcNow < _riskSwitchGapUntil)
            {
                showRisk = false;
            }
        }
        else
        {
            _shownRiskKey = null;
            _riskSwitchGapUntil = DateTime.MinValue;
        }

        var scale = _snapshot.Dpi > 0 ? _snapshot.Dpi / 96.0 : 1.0;

        if (_config.Border.Enabled)
        {
            _borderOverlay.Configure(_config.Border, _context.MatchedEntry);
            _borderOverlay.Apply(_snapshot.RenderRect, showRisk);
        }
        else
        {
            _borderOverlay.HideOverlay();
        }

        if (_config.Gif.Enabled)
        {
            _gifOverlay.Prepare(_config.Gif);
            var (width, height) = _gifOverlay.ComputePhysicalSize(scale);
            var left = _snapshot.RenderRect.Left + (int)Math.Round(_config.Gif.OffsetX * scale);
            var top = _snapshot.RenderRect.Top + (int)Math.Round(_config.Gif.OffsetY * scale);
            _gifOverlay.Apply(new PixelRect(left, top, left + width, top + height), showRisk);
        }
        else
        {
            _gifOverlay.HideOverlay();
        }
    }

    // ---------- 状态上报 ----------

    private void PublishStatus()
    {
        var status = new GuardStatus(
            _snapshot,
            _context,
            _state,
            _lastOcrMs,
            _config.Ocr,
            _gifOverlay.IsBuiltIn,
            _gifOverlay.DisplaySize);

        var signature = string.Join('|',
            status.WindowStateText,
            status.RenderRect,
            status.DpiText,
            status.ChatName ?? "<null>",
            status.RiskText,
            status.OverlayStateText,
            status.OcrMilliseconds);

        if (signature == _lastSignature)
        {
            return;
        }

        _lastSignature = signature;
        StatusChanged?.Invoke(status);
    }
}

/// <summary>状态窗口消费的快照。</summary>
internal sealed record GuardStatus(
    WindowSnapshot Snapshot,
    ChatContext Context,
    OverlayState State,
    long LastOcrMilliseconds,
    OcrOptions Ocr,
    bool GifIsBuiltIn,
    int GifDisplaySize)
{
    public string WindowStateText => !Snapshot.Found
        ? "未找到微信窗口"
        : Snapshot.Minimized
            ? "微信已最小化"
            : !Snapshot.IsForeground
                ? "微信非前台"
                : "微信前台";

    public string RenderRect => Snapshot.Found ? Snapshot.RenderRect.ToString() : "—";

    public string DpiText => Snapshot.Dpi > 0 ? $"{Snapshot.Dpi} ({Snapshot.Dpi * 100 / 96}%)" : "—";

    public string? ChatName => Context.ChatName;

    public string RiskText => Context.Risk switch
    {
        RiskState.Risk => $"命中：{Context.MatchedEntry}",
        RiskState.Normal => "未命中",
        _ => "无法判定",
    };

    public string OverlayStateText => OverlayStateMachine.Describe(State);

    public long OcrMilliseconds => LastOcrMilliseconds;
}
