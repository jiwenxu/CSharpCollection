using System;
using System.Collections.Generic;
using System.IO;

namespace ClearCut.Services;

public sealed class TempPathService : ITempPathService
{
    public string GetLegacyAppTempDirectory()
    {
        return Path.Combine(AppContext.BaseDirectory, "temp");
    }

    public string GetSharedTempRoot()
    {
        return Path.Combine(Path.GetTempPath(), "ClearCut");
    }

    public string GetPreviewDirectory()
    {
        return Path.Combine(GetSharedTempRoot(), "preview");
    }

    public IReadOnlyList<string> GetDirectoriesToCleanup()
    {
        return new[]
        {
            GetLegacyAppTempDirectory(),
            GetSharedTempRoot()
        };
    }
}

