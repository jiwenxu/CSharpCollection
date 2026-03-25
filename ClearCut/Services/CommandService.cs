using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace ClearCut.Services;

public sealed class CommandService : ICommandService
{
    private readonly ITempPathService _tempPathService;

    public CommandService()
        : this(new TempPathService())
    {
    }

    public CommandService(ITempPathService tempPathService)
    {
        _tempPathService = tempPathService ?? throw new ArgumentNullException(nameof(tempPathService));
    }

    public Task CleanupTempFilesAsync()
    {
        try
        {
            foreach (var tempDir in _tempPathService.GetDirectoriesToCleanup())
            {
                if (!Directory.Exists(tempDir))
                {
                    continue;
                }

                foreach (var file in Directory.GetFiles(tempDir, "*", SearchOption.AllDirectories))
                {
                    File.SetAttributes(file, FileAttributes.Normal);
                }

                Directory.Delete(tempDir, recursive: true);
            }

            // 保持既有行为：确保应用本地 temp 目录存在
            Directory.CreateDirectory(_tempPathService.GetLegacyAppTempDirectory());
        }
        catch (Exception ex)
        {
            // 清理失败不应阻止应用退出
            Debug.WriteLine($"Cleanup temp files failed: {ex.Message}");
        }
        return Task.CompletedTask;
    }
}