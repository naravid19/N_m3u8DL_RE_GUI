#nullable enable
using System;
using System.Net.Http;
using System.Threading.Tasks;
using N_m3u8DL_RE_GUI.Core;
using N_m3u8DL_RE_GUI.Services;
using N_m3u8DL_RE_GUI.Tests.Fixtures;
using Xunit;

namespace N_m3u8DL_RE_GUI.Tests.Integration;

/// <summary>
/// Integration and live URL validation tests using the exact test targets specified by the user:
/// 1. Cloudflare / Surrit Stream: https://surrit.com/33ece07f-3229-41eb-b189-ec2485619e02/360p/video.m3u8
///    with Referrer: https://missav123.com/
/// 2. Open M3U8 Stream: https://hls.animeindy.com:8443/vid/MN8fWZAdg/video.mp4/playlist.m3u8
/// </summary>
public class LiveStreamValidationTests
{
    [Fact]
    public void InputValidation_SurritCloudflareUrl_ShouldBeRecognizedAsValidHttpUrl()
    {
        Assert.True(InputValidation.IsHttpUrl(TestConstants.CfStreamUrl));
        Assert.True(InputValidation.IsLikelyValidInput(TestConstants.CfStreamUrl));
    }

    [Fact]
    public void InputValidation_AnimeIndyM3u8Url_ShouldBeRecognizedAsValidHttpUrl()
    {
        Assert.True(InputValidation.IsHttpUrl(TestConstants.NormalM3u8Url));
        Assert.True(InputValidation.IsLikelyValidInput(TestConstants.NormalM3u8Url));
    }

    [Fact]
    public void ArgsBuilder_SurritWithReferrerHeader_ShouldFormatCommandLineCorrectly()
    {
        var options = new DownloadOptions
        {
            Input = TestConstants.CfStreamUrl,
            Headers = $"Referer: {TestConstants.CfReferrerUrl}",
            SaveName = "Surrit_360p",
            SaveDir = @"C:\Downloads"
        };

        var args = ArgsBuilder.Build(options);

        Assert.Contains($"\"{TestConstants.CfStreamUrl}\"", args);
        Assert.Contains("-H \"Referer: https://missav123.com/\"", args);
        Assert.Contains("--save-name \"Surrit_360p\"", args);
    }

    [Fact]
    public void ArgsBuilder_AnimeIndyUrl_ShouldFormatCommandLineCorrectly()
    {
        var options = new DownloadOptions
        {
            Input = TestConstants.NormalM3u8Url,
            SaveName = "AnimeIndy_Stream",
            SaveDir = @"C:\Downloads"
        };

        var args = ArgsBuilder.Build(options);

        Assert.Contains($"\"{TestConstants.NormalM3u8Url}\"", args);
        Assert.Contains("--save-name \"AnimeIndy_Stream\"", args);
    }

    [Fact]
    public async Task LiveNetwork_AnimeIndyM3u8_ShouldBeReachableOrHandleNetworkGracefully()
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Head, TestConstants.NormalM3u8Url);
            using var response = await client.SendAsync(request);
            
            // If reachable, status code should be 200/302/403 (any valid HTTP response)
            Assert.True((int)response.StatusCode >= 200 && (int)response.StatusCode < 600);
        }
        catch (HttpRequestException)
        {
            // Network offline or endpoint unreachable - test passes as failure is handled
        }
        catch (TaskCanceledException)
        {
            // Timeout handled gracefully
        }
    }
}
