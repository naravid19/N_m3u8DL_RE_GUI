using System;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace N_m3u8DL_RE_GUI.Core.Services
{
    public class GitHubUpdateCheckService : IUpdateCheckService
    {
        private static readonly HttpClient _httpClient = new(new HttpClientHandler
        {
            AllowAutoRedirect = false
        })
        {
            Timeout = TimeSpan.FromSeconds(4)
        };

        public async Task<UpdateCheckResult> CheckForUpdateAsync(string owner, string repo, Version currentVersion)
        {
            if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(repo) || currentVersion == null)
                return new UpdateCheckResult(false, currentVersion?.ToString() ?? "", "", "");

            try
            {
                string requestUrl = $"https://github.com/{owner}/{repo}/releases/latest";
                using var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);
                request.Headers.UserAgent.ParseAdd($"N_m3u8DL-RE-GUI-UpdateChecker/{currentVersion?.ToString(3) ?? "2.1.4"}");

                using var response = await _httpClient.SendAsync(request);

                if (response.StatusCode == System.Net.HttpStatusCode.Redirect ||
                    response.StatusCode == System.Net.HttpStatusCode.Found ||
                    response.StatusCode == System.Net.HttpStatusCode.MovedPermanently)
                {
                    var location = response.Headers.Location?.AbsoluteUri;
                    if (!string.IsNullOrEmpty(location))
                    {
                        var match = Regex.Match(location, @"/tag/v?([0-9]+\.[0-9]+\.[0-9]+)");
                        if (match.Success && Version.TryParse(match.Groups[1].Value, out var latestVer))
                        {
                            var currentVerClean = new Version(currentVersion.Major, currentVersion.Minor, Math.Max(0, currentVersion.Build));
                            bool isNewer = latestVer > currentVerClean;

                            return new UpdateCheckResult(
                                HasUpdate: isNewer,
                                CurrentVersion: $"v{currentVerClean.Major}.{currentVerClean.Minor}.{currentVerClean.Build}",
                                LatestVersion: $"v{latestVer.Major}.{latestVer.Minor}.{latestVer.Build}",
                                ReleaseUrl: location
                            );
                        }
                    }
                }
            }
            catch
            {
                // Silent failure on network offline / timeout
            }

            return new UpdateCheckResult(
                HasUpdate: false,
                CurrentVersion: $"v{currentVersion.Major}.{currentVersion.Minor}.{Math.Max(0, currentVersion.Build)}",
                LatestVersion: "",
                ReleaseUrl: ""
            );
        }
    }
}
