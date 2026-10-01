namespace WeChatSendGuard.Models;

/// <summary>屏幕物理像素矩形。</summary>
public readonly record struct PixelRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;

    public int Height => Bottom - Top;

    public bool IsEmpty => Width <= 0 || Height <= 0;

    public override string ToString() => $"({Left},{Top},{Right},{Bottom}) {Width}x{Height}";
}

/// <summary>聊天对象识别状态：只回答"是否拿到了名称"。</summary>
public enum ChatIdentityState
{
    /// <summary>无法判定（截图为空、识别失败、切换中）。</summary>
    Unknown,

    /// <summary>已取得可用名称。</summary>
    Known,
}

/// <summary>风险匹配结果：只回答"是否命中风险"。</summary>
public enum RiskState
{
    /// <summary>无法判定 —— 不得显示"安全"。</summary>
    Unknown,

    /// <summary>明确未命中名单。</summary>
    Normal,

    /// <summary>明确命中名单。</summary>
    Risk,
}

/// <summary>覆盖层状态（对应需求文档 4.3 状态机）。</summary>
public enum OverlayState
{
    None,
    WeChatNotFound,
    WeChatMinimized,
    WeChatInactive,
    ChatUnknown,
    NormalChat,
    RiskChat,
}

/// <summary>
/// 聊天上下文：覆盖层与风险匹配共同消费的唯一状态来源。
/// 注意：不提供 IsGroup —— 视觉通道无法可靠区分群/私（Spike-05 G3 不通过）。
/// </summary>
public sealed record ChatContext
{
    public IntPtr Hwnd { get; init; }

    /// <summary>渲染子窗口客户区屏幕矩形（物理像素）。</summary>
    public PixelRect Rect { get; init; }

    /// <summary>窗口所在显示器 DPI。</summary>
    public int Dpi { get; init; }

    /// <summary>视觉识别得到的当前会话名；无法确定时为 null。</summary>
    public string? ChatName { get; init; }

    public ChatIdentityState Identity { get; init; }

    /// <summary>命中的名单项（仅 RiskState.Risk 时非空）。</summary>
    public string? MatchedEntry { get; init; }

    public RiskState Risk { get; init; }

    /// <summary>原始 OCR 并集文本（诊断用）。</summary>
    public string OcrText { get; init; } = string.Empty;

    public long OcrMilliseconds { get; init; }
}
