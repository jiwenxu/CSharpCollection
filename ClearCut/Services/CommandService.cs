
using System;
using System.IO;
using System.Threading.Tasks;

namespace ClearCut.Services;

public sealed class CommandService : ICommandService
{
    public Task CleanupTempFilesAsync()
    {
        try
        {
            var tempDir = Path.Combine(AppContext.BaseDirectory, "temp");
            if (Directory.Exists(tempDir))
            {
                // 删除 temp 目录下所有文件和子目录
                foreach (var file in Directory.GetFiles(tempDir, "*", SearchOption.AllDirectories))
                {
                    File.SetAttributes(file, FileAttributes.Normal); // 移除只读属性
                    File.Delete(file);
                }

                foreach (var dir in Directory.GetDirectories(tempDir, "*", SearchOption.AllDirectories))
                {
                    Directory.Delete(dir, true);
                }

                // 可选：也删除 temp 根目录本身
                // Directory.Delete(tempDir);
            }
            else
            {
                Directory.CreateDirectory(tempDir);
            }
        }
        catch
        {
            // 记录日志（MVP 阶段可忽略，或写入本地 log.txt）
            // 重要：不要 throw，避免阻止程序退出
            //System.Diagnostics.Debug.WriteLine($"Cleanup failed: {ex.Message}");
        }
        return Task.CompletedTask;
    }
}