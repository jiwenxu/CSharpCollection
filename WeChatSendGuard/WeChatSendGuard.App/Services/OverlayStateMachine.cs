using WeChatSendGuard.Models;

namespace WeChatSendGuard.Services;

/// <summary>覆盖层状态机（纯逻辑，对应需求文档 4.3）。</summary>
internal static class OverlayStateMachine
{
    public static OverlayState Compute(WindowSnapshot snapshot, ChatContext context, bool enabled)
    {
        if (!enabled)
        {
            return OverlayState.None;
        }

        if (!snapshot.Found)
        {
            return OverlayState.WeChatNotFound;
        }

        if (snapshot.Minimized)
        {
            return OverlayState.WeChatMinimized;
        }

        if (!snapshot.IsForeground)
        {
            return OverlayState.WeChatInactive;
        }

        if (context.Identity == ChatIdentityState.Unknown)
        {
            return OverlayState.ChatUnknown;
        }

        return context.Risk == RiskState.Risk ? OverlayState.RiskChat : OverlayState.NormalChat;
    }

    /// <summary>覆盖层是否应可见（只有明确命中风险才显示；不确定一律隐藏）。</summary>
    public static bool ShouldShow(OverlayState state) => state == OverlayState.RiskChat;

    public static string Describe(OverlayState state) => state switch
    {
        OverlayState.None => "未启用",
        OverlayState.WeChatNotFound => "未找到微信窗口",
        OverlayState.WeChatMinimized => "微信已最小化",
        OverlayState.WeChatInactive => "微信非前台",
        OverlayState.ChatUnknown => "无法识别当前会话",
        OverlayState.NormalChat => "已识别，未命中名单",
        OverlayState.RiskChat => "命中风险名单",
        _ => state.ToString(),
    };
}
