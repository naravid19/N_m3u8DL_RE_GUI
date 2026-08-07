using System;
using System.Threading.Tasks;

namespace N_m3u8DL_RE_GUI.Core.Services
{
    public record UpdateCheckResult(
        bool HasUpdate,
        string CurrentVersion,
        string LatestVersion,
        string ReleaseUrl
    );

    public interface IUpdateCheckService
    {
        Task<UpdateCheckResult> CheckForUpdateAsync(string owner, string repo, Version currentVersion);
    }
}
