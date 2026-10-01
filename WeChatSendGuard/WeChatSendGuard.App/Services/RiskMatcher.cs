using System.Text.RegularExpressions;
using WeChatSendGuard.Models;

namespace WeChatSendGuard.Services;

/// <summary>
/// 名单匹配器：把 OCR 并集文本与风险名单比对，输出 RiskState。
/// 匹配方式为"识别文本包含名单项"（全串包含）——Spike-05 实测：
/// 仅比对汉字会因共用前缀误报（培立优办公室群 / 培立优杨潇 都命中 培立优BUG）。
/// </summary>
internal static class RiskMatcher
{
    public static (RiskState State, string? Matched) Match(string ocrUnionText, GuardConfig config)
    {
        var flat = Flatten(ocrUnionText);

        // 识别结果为空 → 无法判定。绝不回退为 Normal（不得给出"安全"的错觉）。
        if (flat.Length == 0)
        {
            return (RiskState.Unknown, null);
        }

        if (config.RiskChats.Count == 0)
        {
            return (RiskState.Normal, null);
        }

        foreach (var entry in config.RiskChats)
        {
            var name = Flatten(entry.Name);
            if (name.Length == 0)
            {
                continue;
            }

            var mode = entry.Mode == MatchMode.Default ? config.Matching.DefaultMode : entry.Mode;
            if (IsMatch(flat, name, mode, config.Matching.CaseSensitive))
            {
                return (RiskState.Risk, entry.Name);
            }
        }

        return (RiskState.Normal, null);
    }

    /// <summary>去掉空白：OCR 常在汉字之间插入空格。</summary>
    public static string Flatten(string text) =>
        new(text.Where(character => !char.IsWhiteSpace(character)).ToArray());

    private static bool IsMatch(string flatText, string flatName, MatchMode mode, bool caseSensitive)
    {
        var comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

        switch (mode)
        {
            case MatchMode.Exact:
                return string.Equals(flatText, flatName, comparison);

            case MatchMode.Regex:
                try
                {
                    var options = RegexOptions.CultureInvariant;
                    if (!caseSensitive)
                    {
                        options |= RegexOptions.IgnoreCase;
                    }

                    return Regex.IsMatch(flatText, flatName, options, TimeSpan.FromMilliseconds(200));
                }
                catch (Exception exception) when (exception is ArgumentException or RegexMatchTimeoutException)
                {
                    // 非法/超时正则回退为包含匹配，避免静默失效
                    return flatText.Contains(flatName, comparison);
                }

            default:
                return flatText.Contains(flatName, comparison);
        }
    }
}
