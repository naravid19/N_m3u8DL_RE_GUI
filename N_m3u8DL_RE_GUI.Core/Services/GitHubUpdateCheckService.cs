#nullable enable
using System;
using System.Diagnostics;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace N_m3u8DL_RE_GUI.Core.Services
{
    public class GitHubUpdateCheckService : IUpdateCheckService
    {
        private static readonly HttpClient SharedClient = new(new HttpClientHandler
        {
            AllowAutoRedirect = false
        })
        {
            Timeout = TimeSpan.FromSeconds(4)
        };

        private readonly HttpClient _client;

        /// <summary>Production path: shares one static client across the app.</summary>
        public GitHubUpdateCheckService() => _client = SharedClient;

        /// <summary>Test seam. A constructor overload rather than a container —
        /// the only thing that needs substituting is the transport.</summary>
        public GitHubUpdateCheckService(HttpMessageHandler handler) =>
            _client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(4) };

        public async Task<UpdateCheckResult> CheckForUpdateAsync(string owner, string repo, Version currentVersion)
        {
            if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(repo) || currentVersion == null)
            {
                return new UpdateCheckResult(
                    UpdateCheckStatus.CheckFailed,
                    currentVersion != null ? $"v{currentVersion.Major}.{currentVersion.Minor}.{Math.Max(0, currentVersion.Build)}" : "",
                    "",
                    "");
            }

            var currentVerClean = new Version(currentVersion.Major, currentVersion.Minor, Math.Max(0, currentVersion.Build));
            var currentVerString = $"v{currentVerClean.Major}.{currentVerClean.Minor}.{currentVerClean.Build}";

            try
            {
                string requestUrl = $"https://github.com/{owner}/{repo}/releases/latest";
                using var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);
                request.Headers.UserAgent.ParseAdd($"N_m3u8DL-RE-GUI-UpdateChecker/{currentVersion.ToString(3)}");

                using var response = await _client.SendAsync(request);

                int statusCode = (int)response.StatusCode;
                if (statusCode is 301 or 302 or 307 or 308)
                {
                    var location = response.Headers.Location?.AbsoluteUri;
                    if (!string.IsNullOrEmpty(location))
                    {
                        var match = Regex.Match(location, @"/tag/v?([0-9]+\.[0-9]+\.[0-9]+)");
                        if (match.Success && Version.TryParse(match.Groups[1].Value, out var latestVer))
                        {
                            bool isNewer = latestVer > currentVerClean;

                            return new UpdateCheckResult(
                                Status: isNewer ? UpdateCheckStatus.UpdateAvailable : UpdateCheckStatus.UpToDate,
                                CurrentVersion: currentVerString,
                                LatestVersion: $"v{latestVer.Major}.{latestVer.Minor}.{latestVer.Build}",
                                ReleaseUrl: location
                            );
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // Silent to the user on an automatic check, but never silent to a
                // developer — a permanent breakage here is otherwise indistinguishable
                // from "no update available", for every user, forever.
                Debug.WriteLine($"[UpdateCheck] {ex.GetType().Name}: {ex.Message}");
            }

            return new UpdateCheckResult(
                Status: UpdateCheckStatus.CheckFailed,
                CurrentVersion: currentVerString,
                LatestVersion: "",
                ReleaseUrl: ""
            );
        }
    }
}
