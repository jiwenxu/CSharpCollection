using System.IO;
using System.Text;

namespace WeChatWindowInspector;

/// <summary>同时输出到控制台与日志文件的简单写入器（UTF-8 with BOM，便于直接复制粘贴）</summary>
internal sealed class LogWriter : IDisposable
{
    private readonly StreamWriter? _file;

    public LogWriter(string path)
    {
        try
        {
            var fullPath = Path.GetFullPath(path);
            var directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            _file = new StreamWriter(fullPath, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true))
            {
                AutoFlush = true,
            };

            FilePath = fullPath;
        }
        catch (Exception ex)
        {
            _file = null;
            Console.WriteLine($"[警告] 无法写入日志文件 {path}：{ex.Message}");
        }
    }

    public string? FilePath { get; }

    public void Write(string text)
    {
        Console.WriteLine(text);
        _file?.WriteLine(text);
    }

    public void Dispose() => _file?.Dispose();
}
