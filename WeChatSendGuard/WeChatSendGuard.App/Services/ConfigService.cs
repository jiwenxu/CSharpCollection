using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using WeChatSendGuard.Models;

namespace WeChatSendGuard.Services;

/// <summary>config.json 的读写与校验（本阶段由用户手工编辑）。</summary>
internal static class ConfigService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static GuardConfig Load(string path, LogWriter log)
    {
        if (!File.Exists(path))
        {
            var created = CreateDefault();
            Save(created, path);
            log.Write($"未找到配置文件，已生成默认配置：{path}");
            return created;
        }

        try
        {
            var config = JsonSerializer.Deserialize<GuardConfig>(File.ReadAllText(path, Encoding.UTF8), JsonOptions)
                         ?? CreateDefault();
            Validate(config, log);
            log.Write($"配置已加载：{path}（名单 {config.RiskChats.Count} 条）");
            return config;
        }
        catch (Exception exception)
        {
            log.Write($"配置读取失败，暂用内存默认值（原文件未改动）：{exception.Message}");
            return CreateDefault();
        }
    }

    public static void Save(GuardConfig config, string path)
    {
        var directory = System.IO.Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, JsonSerializer.Serialize(config, JsonOptions), Encoding.UTF8);
    }

    /// <summary>深拷贝一份配置，供设置窗口在副本上编辑（取消时可直接丢弃）。</summary>
    public static GuardConfig Clone(GuardConfig config) =>
        JsonSerializer.Deserialize<GuardConfig>(JsonSerializer.Serialize(config, JsonOptions), JsonOptions)
        ?? new GuardConfig();

    private static GuardConfig CreateDefault() => new()
    {
        RiskChats =
        [
            new RiskChatEntry
            {
                Name = "培立优BUG",
                Mode = MatchMode.Contains,
                Note = "示例条目，请替换为你自己的高风险聊天名",
            },
        ],
    };

    /// <summary>把明显不合理的值夹回可用范围，并校验正则；只警告不阻断。</summary>
    private static void Validate(GuardConfig config, LogWriter log)
    {
        if (config.WindowPollMs < 50)
        {
            log.Write($"配置修正：windowPollMs={config.WindowPollMs} 过小，已改为 50");
            config.WindowPollMs = 50;
        }

        if (config.OcrIntervalMs < 500)
        {
            log.Write($"配置修正：ocrIntervalMs={config.OcrIntervalMs} 过小，已改为 500");
            config.OcrIntervalMs = 500;
        }

        if (config.DebounceCount < 1)
        {
            config.DebounceCount = 1;
        }

        if (config.Ocr.BaseScale < 1)
        {
            config.Ocr.BaseScale = 1;
        }

        if (config.Ocr.BaseRegion.Width <= 0 || config.Ocr.BaseRegion.Height <= 0)
        {
            log.Write("配置修正：ocr.baseRegion 尺寸无效，已改回默认 300,33,580x30");
            config.Ocr.BaseRegion = new RegionSpec(300, 33, 580, 30);
        }

        if (config.Gif.MaxSize is < 16 or > 512)
        {
            log.Write($"配置修正：gif.maxSize={config.Gif.MaxSize} 超出 16~512，已夹回");
            config.Gif.MaxSize = Math.Clamp(config.Gif.MaxSize, 16, 512);
        }

        if (config.Border.Thickness is < 1 or > 40)
        {
            config.Border.Thickness = Math.Clamp(config.Border.Thickness, 1, 40);
        }

        foreach (var entry in config.RiskChats)
        {
            var mode = entry.Mode == MatchMode.Default ? config.Matching.DefaultMode : entry.Mode;
            if (mode != MatchMode.Regex)
            {
                continue;
            }

            try
            {
                _ = new Regex(entry.Name);
            }
            catch (Exception exception) when (exception is ArgumentException)
            {
                log.Write($"配置警告：名单项 \"{entry.Name}\" 的正则非法（{exception.Message}），运行时会回退为包含匹配");
            }
        }
    }
}
