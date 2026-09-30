#nullable enable
using System;
using System.Threading.Tasks;

namespace N_m3u8DL_RE_GUI.Core.Services
{
    public enum UpdateCheckStatus
    {
        /// <summary>The check completed and this build is current.</summary>
        UpToDate,

        /// <summary>The check completed and a newer release exists.</summary>
        UpdateAvailable,

        /// <summary>The check did not complete. Says nothing about whether an update exists.</summary>
        CheckFailed
    }

    public record UpdateCheckResult(
        UpdateCheckStatus Status,
        string CurrentVersion,
        string LatestVersion,
        string ReleaseUrl
    )
    {
        /// <summary>Kept so existing callers compile. Note this is false for both
        /// UpToDate and CheckFailed — branch on <see cref="Status"/> when the
        /// difference matters, which it does anywhere a message is shown.</summary>
        public bool HasUpdate => Status == UpdateCheckStatus.UpdateAvailable;
    }

    public interface IUpdateCheckService
    {
        Task<UpdateCheckResult> CheckForUpdateAsync(string owner, string repo, Version currentVersion);
    }
}
