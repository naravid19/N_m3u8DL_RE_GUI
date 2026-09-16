using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace N_m3u8DL_RE_GUI.Core.Services
{
    /// <summary>
    /// One bundled engine tool's state: the version parsed from the local binary,
    /// the latest release tag on GitHub, and whether an update exists.
    /// </summary>
    public record EngineToolStatus(
        string Name,
        string LocalVersion,
        string LatestVersion,
        bool HasUpdate,
        string ReleaseUrl,
        bool Detectable
    );

    /// <summary>
    /// Checks the local N_m3u8DL-RE / ffmpeg binaries against their GitHub releases.
    /// Version probes run the exe with a version flag and parse the first output line;
    /// latest versions come from the releases/latest redirect (no API rate limit).
    /// ffmpeg "updates" mean a newer essentials build from GyanD (the same source the
    /// bundled binary came from); the GUI links to the release page rather than
    /// replacing a running binary in place.
    /// </summary>
    public class EngineUpdateCheckService
    {
        private static readonly HttpClient _httpClient = new(new HttpClientHandler
        {
            AllowAutoRedirect = false
        })
        {
            Timeout = TimeSpan.FromSeconds(8)
        };

        // "0.6.0+df70f0b3da..." / "N_m3u8DL-RE (Beta version) 20260628" — grab the first x.y[.z] token.
        private static readonly Regex ReVersionPattern = new(@"(\d+\.\d+(?:\.\d+)?)", RegexOptions.Compiled);

        // "ffmpeg version 2026-07-30-git-2ae2413488-essentials_build-www.gyan.dev" — git-master builds
        // carry a date, not a semver. GyanD release tags are plain versions ("9.0.1"), so a
        // date-stamped local build is reported as "up to date?" only via the release link.
        private static readonly Regex FfmpegVersionPattern = new(
            @"ffmpeg version (\S+)", RegexOptions.Compiled);

        private readonly string _engineDir;

        public EngineUpdateCheckService(string? engineDir = null)
        {
            _engineDir = engineDir ?? AppContext.BaseDirectory;
        }

        public async Task<EngineToolStatus> CheckNReAsync()
        {
            var exe = Path.Combine(_engineDir, "N_m3u8DL-RE.exe");
            return await CheckAsync(
                "N_m3u8DL-RE", exe, "--version", ReVersionPattern,
                "nilaoda", "N_m3u8DL-RE",
                latest => latest.TrimStart('v', 'V'));
        }

        public async Task<EngineToolStatus> CheckFfmpegAsync()
        {
            var exe = Path.Combine(_engineDir, "ffmpeg.exe");
            return await CheckAsync(
                "FFmpeg", exe, "-version", FfmpegVersionPattern,
                "GyanD", "codexffmpeg",
                latest => latest);
        }

        private static async Task<EngineToolStatus> CheckAsync(
            string name, string exePath, string versionArg, Regex localPattern,
            string owner, string repo, Func<string, string> normalizeLatest)
        {
            var localVersion = await ProbeLocalVersionAsync(exePath, versionArg, localPattern);
            var (latest, releaseUrl) = await FetchLatestReleaseAsync(owner, repo);

            var hasUpdate = false;
            if (localVersion != null && latest != null)
            {
                // Compare semver prefixes when both sides parse; otherwise (git-master
                // builds vs dated tags) only report that a new release exists.
                var localMatch = ReVersionPattern.Match(localVersion);
                var latestMatch = ReVersionPattern.Match(normalizeLatest(latest));
                if (localMatch.Success && latestMatch.Success
                    && Version.TryParse(latestMatch.Groups[1].Value, out var latestVer)
                    && Version.TryParse(localMatch.Groups[1].Value, out var localVer))
                {
                    hasUpdate = latestVer > localVer;
                }
            }

            return new EngineToolStatus(
                Name: name,
                LocalVersion: localVersion ?? "not found",
                LatestVersion: latest ?? "unknown",
                HasUpdate: hasUpdate,
                ReleaseUrl: releaseUrl ?? $"https://github.com/{owner}/{repo}/releases",
                Detectable: localVersion != null);
        }

        private static async Task<string?> ProbeLocalVersionAsync(string exePath, string arg, Regex pattern)
        {
            if (!File.Exists(exePath))
                return null;

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = exePath,
                    Arguments = arg,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                using var process = Process.Start(psi);
                if (process == null)
                    return null;

                var stdout = await process.StandardOutput.ReadToEndAsync();
                var stderr = await process.StandardError.ReadToEndAsync();
                process.WaitForExit(5000);

                // ffmpeg writes its banner to stderr, N_m3u8DL-RE to stdout.
                var m = pattern.Match(stdout);
                if (!m.Success)
                    m = pattern.Match(stderr);
                return m.Success ? m.Groups[1].Value.Trim() : null;
            }
            catch
            {
                return null;
            }
        }

        private static async Task<(string? Tag, string? Url)> FetchLatestReleaseAsync(string owner, string repo)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, $"https://github.com/{owner}/{repo}/releases/latest");
                request.Headers.UserAgent.ParseAdd("N_m3u8DL-RE-GUI-UpdateChecker/2.1");

                using var response = await _httpClient.SendAsync(request);
                if (response.StatusCode == System.Net.HttpStatusCode.Redirect ||
                    response.StatusCode == System.Net.HttpStatusCode.Found ||
                    response.StatusCode == System.Net.HttpStatusCode.MovedPermanently)
                {
                    var location = response.Headers.Location?.AbsoluteUri;
                    if (!string.IsNullOrEmpty(location))
                    {
                        var match = Regex.Match(location, @"/tag/(v?[^/]+)$");
                        if (match.Success)
                            return (Uri.UnescapeDataString(match.Groups[1].Value), location);
                    }
                }
            }
            catch
            {
                // Silent failure on network offline / timeout
            }

            return (null, null);
        }
    }
}
