using System.Threading;
using System.Threading.Tasks;
using ClearCut.Models;

namespace ClearCut.Services;

public interface ICommandService
{
    Task CleanupTempFilesAsync();
}