using System.Collections.Generic;

namespace ClearCut.Services;

public interface ITempPathService
{
    string GetLegacyAppTempDirectory();

    string GetSharedTempRoot();

    string GetPreviewDirectory();

    IReadOnlyList<string> GetDirectoriesToCleanup();
}

