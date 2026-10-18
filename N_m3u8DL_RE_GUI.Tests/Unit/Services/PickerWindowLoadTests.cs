#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows;
using N_m3u8DL_RE_GUI.Core.Abyss;
using N_m3u8DL_RE_GUI.Core.Capture;
using N_m3u8DL_RE_GUI.Views;
using Xunit;

namespace N_m3u8DL_RE_GUI.Tests.Unit.Services;

/// <summary>
/// A window's XAML is only parsed when it is constructed, so a typo in a resource path or a
/// style key surfaces as a crash the first time a user opens the dialog. This constructs both
/// pickers for real and fails here instead. One test, one STA thread: WPF allows a single
/// Application per process and ties every window to the thread that created it.
/// </summary>
public class PickerWindowLoadTests
{
    [Fact]
    public void BothPickerWindowsLoadWithTheSharedStyles()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                // pack://application URIs (the shared PickerStyles dictionary) need an Application.
                _ = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };

                var abyss = new AbyssQualityWindow(AbyssSourceChoice.Ranked(new[]
                {
                    new AbyssSource { Label = "720p", Size = 200, Codec = "h264" },
                    new AbyssSource { Label = "1080p", Size = 400, Codec = "h264" },
                }));
                Assert.Equal(2, abyss.List_Qualities.Items.Count);
                Assert.Equal(0, abyss.List_Qualities.SelectedIndex);
                Assert.Null(abyss.Selected);

                var stream = new StreamPickerWindow(new List<CapturedRequest>
                {
                    new("https://a.example/master.m3u8", new List<CapturedHeader>(), CapturedStreamKind.Hls),
                });
                Assert.Single(stream.List_Candidates.Items);
                Assert.Equal(0, stream.List_Candidates.SelectedIndex);
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();

        Assert.True(thread.Join(TimeSpan.FromSeconds(60)), "constructing the picker windows did not finish in 60s");
        if (failure is not null)
            throw new Xunit.Sdk.XunitException("Window failed to load: " + failure);
    }
}
