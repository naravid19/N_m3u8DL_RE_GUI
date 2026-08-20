# Download Lifecycle Fixes Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make automatic update checks, direct downloads, Cloudflare downloads, and batch downloads responsive and safely cancellable.

**Architecture:** Keep process ownership in `IDownloadService`. The window stays responsible for UI state, while `DownloadService` owns the process and cancellation-token lifetime. Cloudflare preparation remains in the window but becomes asynchronous and receives a cancellation token from the Stop action.

**Tech Stack:** .NET 9, WPF, xUnit.

## Global Constraints

- Do not add packages or a new process-management abstraction beyond one private helper inside `DownloadService`.
- Preserve the visible console window for direct and Cloudflare downloads.
- A Stop click must be safe when no process exists, during Cloudflare preparation, and while a process is exiting.
- Keep all UI-control access on the WPF dispatcher thread.

---

### Task 1: Keep startup update checks on the UI thread

**Files:**
- Modify: `N_m3u8DL_RE_GUI/MainWindow.xaml.cs:559-571`
- Test: manual WPF smoke check

**Interfaces:**
- Consumes: `CheckGuiUpdateAsync(bool isManual)`.
- Produces: an auto-update request that starts on the UI dispatcher and updates `Button_CheckUpdate`, `TextBlock_UpdateStatus`, and `Button_UpdateBadge` safely.

- [ ] **Step 1: Replace the `Task.Run` wrapper with a dispatcher-owned call**

```csharp
if (CheckBox_AutoCheckGuiUpdate?.IsChecked == true)
    _ = CheckGuiUpdateAsync(isManual: false);
```

- [ ] **Step 2: Keep error handling inside `CheckGuiUpdateAsync` only where the UI can report or safely ignore it**

The existing `GitHubUpdateCheckService` already handles network failures. Do not catch cross-thread exceptions; this change must prevent them.

- [ ] **Step 3: Manual verification**

Run the application with automatic update checks enabled. Confirm that the UI remains interactive and no `InvalidOperationException` is written for dispatcher access.

- [ ] **Step 4: Build and commit**

Run: `dotnet build N_m3u8DL_RE_GUI.sln /warnaserror`

Commit: `fix(update): run startup update check on UI dispatcher`

### Task 2: Make `DownloadService` cancellation ownership race-free

**Files:**
- Modify: `N_m3u8DL_RE_GUI/Services/DownloadService.cs:12-215`
- Test: `N_m3u8DL_RE_GUI.Tests/Unit/Services/DownloadServiceTests.cs`

**Interfaces:**
- Preserves: `Task<bool> StartDownloadAsync(...)`, `Task<bool> StartProcessAsync(...)`, and `void StopDownload()`.
- Produces: safe stopping during start, execution, and exit for both service entry points.

- [ ] **Step 1: Add a failing cancellation regression test using `cmd.exe`**

```csharp
[Fact]
public async Task StartProcessAsync_WhenStopped_ReturnsFalseWithoutThrowing()
{
    var service = new DownloadService();
    var task = service.StartProcessAsync("cmd.exe", "/c ping -n 30 127.0.0.1 > nul");
    await WaitUntilAsync(() => service.IsDownloading);

    service.StopDownload();

    Assert.False(await task);
    Assert.False(service.IsDownloading);
}
```

`WaitUntilAsync` must time out deterministically rather than sleeping for a fixed duration.

- [ ] **Step 2: Extract the duplicate lifecycle into one private method**

Both public start methods construct a `ProcessStartInfo` and delegate to a private `StartTrackedProcessAsync`. The helper owns its local `Process` and `CancellationTokenSource`; it clears shared references only when they still reference those same instances.

```csharp
private async Task<bool> StartTrackedProcessAsync(
    ProcessStartInfo startInfo,
    Action<string>? logCallback,
    CancellationToken cancellationToken)
```

- [ ] **Step 3: Synchronize stop and disposal under the same lock**

Capture and cancel the current process/token while holding `_lockObject`, then let the owner dispose them only after it has removed matching shared references. `StopDownload()` must treat `ObjectDisposedException` as a completed operation.

- [ ] **Step 4: Run focused regression tests**

Run: `dotnet test N_m3u8DL_RE_GUI.Tests/N_m3u8DL_RE_GUI.Tests.csproj --filter FullyQualifiedName~DownloadServiceTests`

Expected: all `DownloadServiceTests` pass, including the new stop test.

- [ ] **Step 5: Commit**

Commit: `fix(download): synchronize process cancellation lifecycle`

### Task 3: Track batch downloads and keep Stop usable

**Files:**
- Modify: `N_m3u8DL_RE_GUI/MainWindow.xaml.cs:629-655`
- Test: manual WPF batch-download smoke check

**Interfaces:**
- Consumes: `IBatchScriptService.BuildScriptAsync`, `SaveScript`, and `IDownloadService.StartProcessAsync`.
- Produces: a generated batch process that is tracked by the same service as direct and Cloudflare downloads.

- [ ] **Step 1: Preserve the disabled UI only while generating the script**

Keep `this.IsEnabled = false` while `BuildScriptAsync` gathers titles and writes the batch file, so each generated command is derived from one stable UI state.

- [ ] **Step 2: Re-enable the window before launching the finished batch file**

After `SaveScript`, re-enable the window, disable only `Button_GO`, show `Button_Stop`, and start the script through the service:

```csharp
this.IsEnabled = true;
Button_GO.IsEnabled = false;
Button_Stop.Visibility = Visibility.Visible;
await _downloadService.StartProcessAsync(result.FilePath, string.Empty);
```

- [ ] **Step 3: Restore button state in one `finally` block**

Regardless of build, start, or cancellation outcome, re-enable `Button_GO`, collapse `Button_Stop`, and ensure `this.IsEnabled` is true.

- [ ] **Step 4: Manual verification**

Use a batch input containing at least two entries. Confirm that Stop remains clickable after launch, stops the active command process tree, and returns the main window to its ready state.

- [ ] **Step 5: Commit**

Commit: `fix(batch): track generated batch downloads for cancellation`

### Task 4: Make Cloudflare preparation asynchronous and cancellable

**Files:**
- Modify: `N_m3u8DL_RE_GUI/MainWindow.xaml.cs:60-62, 690-694, 833-919, 950-996`
- Test: manual WPF Cloudflare smoke check

**Interfaces:**
- Produces: `Task<string?> FindPythonWithCurlCffiAsync(CancellationToken)`.
- Consumes: a window-owned preparation `CancellationTokenSource`, cancelled by `Button_Stop_Click`.

- [ ] **Step 1: Add a window-owned preparation cancellation source**

Create the source immediately before Cloudflare preparation and clear/dispose it in `finally`. In the Stop handler, cancel this source before calling `_downloadService.StopDownload()`.

- [ ] **Step 2: Convert interpreter probing to asynchronous process waits**

Replace `ReadToEnd` plus `WaitForExit(10000)` with asynchronous stream reads and `WaitForExitAsync(cancellationToken)`. Retain the ten-second per-candidate timeout by linking a timeout token to the supplied cancellation token.

```csharp
using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
timeout.CancelAfter(TimeSpan.FromSeconds(10));
await process.WaitForExitAsync(timeout.Token);
```

- [ ] **Step 3: Await preparation before generating the batch file**

`StartCloudflareDownloadAsync` awaits the new lookup. If the user stopped or lookup is cancelled, return without writing or launching `cf_dl_*.bat`.

- [ ] **Step 4: Verify the responsive stop path manually**

Temporarily make Python discovery slow or use an unavailable interpreter. Start Cloudflare mode, press Stop during lookup, and confirm the UI remains responsive, no batch file is launched, and no exception reaches the UI thread.

- [ ] **Step 5: Run final verification and commit**

Run:

```powershell
dotnet build N_m3u8DL_RE_GUI.sln /warnaserror
dotnet test N_m3u8DL_RE_GUI.Tests/N_m3u8DL_RE_GUI.Tests.csproj --no-build
```

Commit: `fix(cf): cancel asynchronous Python discovery safely`

## Out Of Scope

- Reintroducing in-app progress capture while preserving an interactive visible console.
- Removing the unused `StartExecutableWithArguments` helper.
- Consolidating `BuildArgsRE` and `BuildDownloadOptions`.
- Adding an Alt+S input binding.

## Plan Self-Review

- All four approved defects map to one independently testable task.
- The plan adds no dependencies and preserves the current visible-console behavior.
- Download-process lifetime has an automated regression test; WPF-specific behavior has explicit manual smoke checks because the existing test suite has no STA UI harness.
