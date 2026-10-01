using System.IO;
using System.Text;

namespace WeChatSendGuard.Services;

/// <summary>
/// 极简日志：追加写入 logs/app-yyyyMMdd.log，同时把最新一行抛给状态窗口显示。
/// </summary>
internal sealed class LogWriter
{
    private readonly object _gate = new();
    private readonly string _path;

    public LogWriter(string directory)
    {
        Directory.CreateDirectory(directory);
        _path = System.IO.Path.Combine(directory, $"app-{DateTime.Now:yyyyMMdd}.log");
    }

    public string Path => _path;

    public event Action<string>? LineWritten;

    public void Write(string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss.fff}] {message}";
        LineWritten?.Invoke(line);

        lock (_gate)
        {
            try
            {
                File.AppendAllText(_path, line + Environment.NewLine, Encoding.UTF8);
            }
            catch
            {
                // 日志失败不得影响主流程
            }
        }
    }
}
