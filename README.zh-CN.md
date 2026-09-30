<!-- 提升“返回顶部”链接的兼容性：参见 https://github.com/othneildrew/Best-README-Template/pull/73 -->

<a id="readme-top"></a>

[English](README.md) | **简体中文**

<!-- 项目徽章 -->

[![Version][version-shield]][version-url]
[![.NET][dotnet-shield]][dotnet-url]
[![WPF][wpf-shield]][wpf-url]
[![C#][csharp-shield]][csharp-url]
[![License][license-shield]][license-url]

<!-- 项目标志 -->
<br />
<div align="center">
  <a href="https://github.com/naravid19/N_m3u8DL_RE_GUI">
    <img src="images/logo.ico" alt="Logo" width="80" height="80">
  </a>

  <h3 align="center">N_m3u8DL-RE GUI</h3>

  <p align="center">
    为强大的 N_m3u8DL-RE 命令行工具打造的现代化、易用的 Windows 图形界面。
    <br />
    <a href="https://github.com/nilaoda/N_m3u8DL-RE"><strong>查看原版命令行工具</strong></a>
    <br />
    <br />
    <a href="#getting-started">快速开始</a>
    ·
    <a href="https://github.com/naravid19/N_m3u8DL_RE_GUI/issues/new?labels=bug">报告问题</a>
    ·
    <a href="https://github.com/naravid19/N_m3u8DL_RE_GUI/issues/new?labels=enhancement">功能建议</a>
  </p>
</div>

<!-- 关于本项目 -->

## 关于本项目

<div align="center">
  <img src="images/screenshot.png" alt="产品截图" width="80%">
</div>

**N_m3u8DL-RE GUI** 为 [N_m3u8DL-RE](https://github.com/nilaoda/N_m3u8DL-RE) 命令行工具提供图形界面，让下载 DASH、HLS 和 MSS 流媒体变得非常简单——再也不用记忆复杂的命令行参数！

### 主要优势：

- 🚀 **无需记忆命令行** - 常用选项都可以通过简单的界面控件完成。
- ⏯️ **断点续传** - 自动发现已停止或崩溃、磁盘上仍留有分片的下载。由于带签名的链接很快就会过期，你可以直接换上一条新的流链接，继续下载而不丢失已下载的分片。
- 🎬 **原生支持 Abyss 与 Hydrax** - 无需外部工具，直接对 `abysscdn.com`、`playhydrax.com`、`zplayer.io` 和 `short.ink` 进行 AES-CTR 分片解密与合并；视频提供多种画质时会弹出选择窗口。
- 🌏 **说你的语言** - 整个界面（包括状态提示）可在 English、简体中文、繁體中文之间即时切换。
- 📦 **批量处理** - 一键从文本文件或文件夹中批量下载多个流。
- 🔒 **隐私优先** - 你的设置和请求头会在会话之间自动保存，并使用 Windows DPAPI 加密。
- 🛡️ **绕过 Cloudflare WAF** - 内置 TLS 指纹模拟，顺畅绕过 Cloudflare 的安全防护。

<p align="right">(<a href="#readme-top">返回顶部</a>)</p>

---

<!-- 快速开始 -->

<a id="getting-started"></a>

## 快速开始（安装）

我们刻意让安装过程尽可能简单：没有安装程序，也没有繁琐的配置。

### 1. 下载

从 [GitHub Releases](https://github.com/naravid19/N_m3u8DL_RE_GUI/releases) 页面下载最新版本（`N_m3u8DL_RE_GUI_v2.1.6.zip`）。

### 2. 解压

将 `.zip` 文件解压到电脑上的任意位置。文件夹内包含 4 个核心文件，以及可选的配套浏览器扩展：

```text
N_m3u8DL_RE_GUI_v2.1.6/
├── N_m3u8DL_RE_GUI.exe    <-- 主程序（双击运行！）
├── N_m3u8DL-RE.exe        <-- 核心下载引擎
├── ffmpeg.exe             <-- 音视频合并引擎
├── m3u8_cf_bypass.py      <-- Cloudflare TLS 绕过脚本
└── extension/             <-- 可选的浏览器配套扩展（见下文）
```

### 3. 运行

双击 `N_m3u8DL_RE_GUI.exe` 即可启动应用。

> [!NOTE]
> **Python 要求：** 如果你打算使用 **Cloudflare 绕过** 功能，请确保 Windows 上已安装 Python，并在命令提示符中运行 `pip install curl_cffi`。

<p align="right">(<a href="#readme-top">返回顶部</a>)</p>

---

<!-- 使用方法 -->

## 使用指南

### 快速上手

1. **输入 URL** - 在顶部 URL 栏粘贴你的 `.m3u8`、`.mpd` 或其他流媒体地址。
2. **配置选项** - 在侧边栏标签页中选择需要的选项（例如仅音频、仅字幕）。
3. **点击 GO** - 应用会自动生成命令行并开始下载。

### 支持的输入方式

| 方式 | 使用方法 |
| ----------- | ------------------------------------- |
| ⬇ 一键下载 | 在浏览器扩展中点击 **⬇ Download**，通过 Native Messaging 自动把下载任务排入 GUI 并开始下载。 |
| 📋 从浏览器粘贴 | 在浏览器开发者工具（F12）中把请求复制为 cURL，或在浏览器扩展中点击 **Copy as cURL**，然后点击 **📋 Paste from browser**。 |
| 🗂️ 拖入 HAR 抓包 | 把 `.har` 网络抓包文件拖到 GUI 上。如果发现多个流，会弹出交互式选择窗口，让你挑选主流。 |
| 🎬 Abyss / Hydrax | 直接粘贴 `abysscdn.com/?v=...`、`playhydrax.com/?v=...`、`zplayer.io/?v=...` 或 `short.ink/...`。GUI 会获取可用画质并原生下载分片。提供多种画质时由你选择（体积最大的排在最前）；由浏览器扩展发起的下载会自动选择最大的。 |
| 直接输入 URL | 在顶部栏直接粘贴标准的 `.m3u8`、`.mpd` 或 `.mp4` 流地址。 |
| 拖放文件 | 把 `.m3u8`、`.mpd` 或 `.txt` 文件直接拖入窗口。 |
| 批量文件 | 拖入包含多个 URL 的 `.txt` 文件（每行一个）。 |
| 文件夹 | 拖入包含流文件的文件夹，批量处理其中所有文件。 |

### N-RE Stream Bridge 浏览器扩展（v1.4.5）

使用 `extension/` 中的配套浏览器扩展 **N-RE Stream Bridge**，即可在 Chrome、Edge 和 Brave 中一键抓取流、选择画质并建立多 URL 批量队列：
1. 打开 `chrome://extensions` 并启用**开发者模式**。
2. 点击**加载已解压的扩展程序**，选择 `extension/` 文件夹。
3. 在浏览器中播放任意视频或音频 → 点击扩展图标。
4. **单个流：** 点击 **`▸ Qualities`** 选择分辨率（1080p、720p 等）→ **`⬇ Download`**（或 **`📋 Copy as cURL`**）。
5. **批量流：** 勾选多个流 → 点击 **`📋 Copy as list`**。
6. 在 GUI 中点击 **`📋 Paste from browser`**（或按 Ctrl+V）→ 所有流的 URL、请求头和画质选择器都会立即填好！

> [!TIP]
> **一键下载：** 只要 GUI 至少运行过一次，弹窗里就会在 `📋 Copy as cURL` 旁边出现 **`⬇ Download`** 按钮。它会把流（URL、请求头、合并后的 Cookie 和所选画质）直接交给 GUI 并开始下载，无需复制粘贴，也无需切换窗口。如果下载正在进行中点击它，这个流会自动排在当前下载之后。

> [!NOTE]
> **流覆盖范围与隐私：** 支持 **HLS**（`.m3u8`）、**DASH**（`.mpd`）、**Smooth Streaming**（`.ism`/`/Manifest`）、**Abyss/Hydrax**、独立音频（`.m4a`、`.opus`、`.flac`、`.wav`、`.aac`、`.mp3`）以及渐进式格式（`.mp4`、`.m4v`、`.webm`、`.mkv` 等）。扩展会自动抑制大量分片请求以保持清单可见，显示实时文件大小和置信度徽章，仅在需要时才探测各画质，并使用内存中的 `chrome.storage.session`，因此敏感 Cookie 绝不会以明文写入磁盘。

### 如何使用 Cloudflare 绕过

如果网站用 Cloudflare 拦截了你，请打开 **网络标签页（🌐）**，找到 **⚡ Cloudflare Bypass (curl_cffi)** 区域：
1. 勾选 **Enable Cloudflare Bypass**。
2. 选择绕过**引擎**：
   - **Auto（推荐）** - 普通 HLS 通过多线程 `curl_cffi` 直接下载；DASH/MSS/CENC 流则启动本地代理服务器。如果直接下载 HLS 时遇到无法自行合并的内容（fMP4 初始化分片、独立音轨、按字节范围切分的分片），会自动把任务交给代理引擎。
   - **CF Direct（仅 HLS）** - 用并发工作线程直接下载并合并普通 HLS 流。遇到 fMP4 / 独立音轨 / 字节范围的播放列表时会直接拒绝，而不是写出一个损坏的文件；这类流请使用 *Auto* 或 *Proxy*。
   - **CF Proxy + RE（DASH / MSS / DRM）** - 启动本地 `--serve` 临时代理服务器（`127.0.0.1:0`），带令牌认证和 SSRF 防护，用来改写清单并把请求转发给 N_m3u8DL-RE。此路径不会应用自定义代理或系统代理，因为 N_m3u8DL-RE 只与本地代理通信。
3. 选择浏览器指纹（例如 `chrome131`、`chrome120`、`edge101`、`safari17_0`）。
4. 按需填写 `Referer` 或 `CF Cookie`——或者直接点击 **📋 Paste from browser**，从扩展自动填入。
5. 点击 **▶ GO**。

> [!TIP]
> **凭据隐私：** 参数预览栏显示的是真实命令（包含凭据），这样 **📋 Copy** 得到的内容可以原样粘贴到终端运行。因此你粘贴的 Cookie 会显示在屏幕上——分享截图或录屏之前请留意。Cloudflare 绕过下载一旦开始，预览栏在剩余过程中会被该命令的脱敏快照替换（`--cookie "...(masked, 412 chars)"`），而实际启动的进程仍会收到完整的值。

> [!TIP]
> **恢复已停止的下载：** 如果 Cloudflare 绕过下载中途停止或失败，只需粘贴同一个 URL 和保存名称，再次点击 **▶ GO**——磁盘上已保存的分片会被识别并跳过，下载会接着进行，而不是从 0% 重新开始。

<p align="right">(<a href="#readme-top">返回顶部</a>)</p>

---

<!-- 功能 -->

## 功能详解

### 核心功能
- **通用流抓取** - 通过 Native Messaging 一键 **⬇ Download**、直接粘贴浏览器的 cURL 命令、拖入 `.har` 抓包（自动对流排序并供你选择），或使用 **N-RE Stream Bridge** 浏览器扩展。每次抓取都从干净状态开始：解密密钥、Cloudflare 绕过和 TLS 指纹会被重置，不会沿用上一个流的设置；你自己的代理设置则会保留。
- **断点续传** - 自动生成确定的临时目录（`<saveDir>/.nre-tmp/<saveName>`）并保存当前任务的元数据。启动时会检测留有分片的未完成下载，可一键换上新链接继续，也可以干净地丢弃。
- **原生 Abyss / Hydrax 下载器** - 内置零依赖的 C# 加密引擎，可对 `abysscdn.com`、`playhydrax.com`、`zplayer.io` 和 `short.ink` 的分片进行解密并重新组装。会校验磁盘上的分片大小，并从精确的字节偏移处恢复已停止或中断的下载，而不是重新开始；取消状态如实报告，实时进度准确。视频提供多种画质时会打开画质选择窗口（体积最大的排在最前）。
- **强化的 Cloudflare WAF 绕过** - 多线程分片下载器（`curl_cffi`）、浏览器指纹模拟、面向 DASH/MSS/DRM 流的清单改写与代理模式（`--serve`）、如实的退出码处理，以及基于每个下载任务固定目录的自动分片续传（用同一个 URL 和保存名称重新运行会跳过磁盘上已有的分片）。
- **三区现代化 UX/UI 架构** - 布局清晰：顶部为 URL 主栏，侧边为 6 个标签页（`📦 Download`、`🌐 Network`、`🔒 Security`、`🎬 Media`、`📡 Live`、`⚙️ Advanced`），底部为固定的命令预览栏。
- **GUI 自动更新引擎** - 不受速率限制的 HTTP 更新检查。发布新版本后，顶部会出现绿色胶囊徽章（`🎉 vX.X.X Available!`）。
- **多语言界面** - 支持 English、简体中文和繁體中文，可在顶部即时切换，无需重启。标签、对话框、状态提示、续传横幅和更新状态都已翻译；技术日志保持英文。首次启动时语言会跟随 Windows 的显示语言。
- **完整支持 RE** - 兼容 N_m3u8DL-RE 的所有主要命令行参数。
- **批量下载** - 从文本文件批量处理多个 URL，或直接拖入整个流文件夹。
- **配置持久化** - 设置会在会话之间自动保存。

### 安全与稳定性
- **Windows DPAPI 机密保护** - 你的自定义请求头、代理、解密密钥和 IV 会通过 Windows DPAPI 安全地加密保存在 `config.json` 中。不存在明文机密！
- **续传时的凭据隐私** - 续传任务记录有意只保存来源主机名（绝不保存原始流 URL 或带签名的访问令牌）。运行中的 Cloudflare 绕过下载还会把参数预览换成隐去 Cookie 的命令快照；实时预览本身保持原样，因此 **📋 Copy** 依然可以复制即运行。
- **线程安全的取消** - 响应迅速的进程取消，令牌生命周期管理清晰，能安全地终止子进程树。
- **窗口内实时反馈与进度** - 实时进度条、带颜色提示的状态消息（绿色成功、琥珀色警告/部分完成、红色错误）、可折叠的诊断日志，以及完成后的“打开文件夹”按钮。
- **无障碍与键盘友好** - 高对比度的焦点指示、快捷键（`Alt+G` 开始，`Alt+S` / `Escape` 停止），以及完整的 UI 自动化属性。
- **自动化测试套件（1,336 个测试）** - 由 1,336 个单元、集成、一致性和无障碍测试保障稳定性，覆盖 .NET（892 个测试，另有 1 个联网实测被有意跳过）、Node.js（361 个测试）和 Python（83 个测试）三套测试。

### 下载选项
- **并发下载** - 同时下载多个流。
- **音频/字幕选择** - 轻松只下载音频或只下载字幕。
- **流选择（正则）** - 用标准正则选择或丢弃视频/音频/字幕流。
- **时间范围** - 只下载流中的某一段（例如 `00:05:00-00:10:00`）。
- **限速** - 设置最大下载速度以避免被限流。
- **自定义代理** - 支持 HTTP 和 SOCKS5 代理。

### 合并与输出
- **下载后合并** - 使用 `ffmpeg` 自动把视频和音频合并为 `.mp4` 或 `.mkv`。
- **合并时导入** - 合并时导入外部媒体文件。
- **字幕格式** - 可选 SRT 或 VTT 输出格式。

### 直播录制
- **按 VOD 处理** - 把直播流当作 VOD 处理，可完整下载并暂停。
- **实时合并** - 无需等待结束，实时合并分片。
- **管道合并** - 直接管道传给合并器，节省磁盘 I/O。
- **录制时长限制** - 设置最长录制时长。

### 解密
- **引擎选择** - 可在 MP4DECRYPT、SHAKA_PACKAGER 或 FFMPEG 之间选择，用于实时 MP4 分片解密。
- **HLS 方法覆盖** - 设置自定义的 HLS 解密方法。
- **密钥文本文件** - 直接从文件中加载海量解密密钥。

### 高级控制
- **自定义请求头** - 添加 HTTP 请求头（Cookie、User-Agent、Origin 等）。
- **线程控制** - 自定义线程数、重试次数和超时参数。
- **自动字幕修复** - 自动修复字幕同步问题。与 *下载后删除临时文件* 一样，它在 N_m3u8DL-RE 中默认开启，取消勾选现在会真正把它关闭。
- **保存命名模式** - 为下载的文件自定义命名模式。
- **日志级别** - 控制输出详细程度（OFF/ERROR/WARN/INFO/DEBUG）。

### 构建发布包

要生成发布文件夹和 zip，请在仓库根目录运行 `publish.bat`（从其他脚本调用时加上 `/nopause`）。它会从 `Directory.Build.props` 读取版本号，把应用发布为自包含的单文件，加入 `N_m3u8DL-RE.exe`、`ffmpeg.exe`、`m3u8_cf_bypass.py` 和 `extension/` 文件夹（不含其测试），检查结果，然后生成 `Publish\N_m3u8DL_RE_GUI_v<版本号>\` 以及对应的 `N_m3u8DL_RE_GUI_v<版本号>.zip`。`N_m3u8DL-RE.exe`、`ffmpeg.exe` 和 `m3u8_cf_bypass.py` 必须放在仓库根目录；缺少任何一个，脚本会在开始构建之前就停止。所有内容先在 `Publish\.staging` 中组装，全部检查通过后才移入正式位置，因此失败的运行绝不会留下构建到一半的发布包。

完整的发布压缩包必须包含：`N_m3u8DL_RE_GUI.exe`、`N_m3u8DL-RE.exe`、`ffmpeg.exe`、`m3u8_cf_bypass.py` 和 `extension/` 文件夹。版本号就是 `Directory.Build.props` 中的 `AppVersion`，也是唯一需要手写版本号的地方；`publish.bat` 会检查构建出的 exe 报告的版本与之相同，因此文件夹名、zip 名和二进制文件不会出现版本不一致。

<p align="right">(<a href="#readme-top">返回顶部</a>)</p>

---

<!-- 路线图 -->

## 路线图

- [x] 完整支持 N_m3u8DL-RE 参数
- [x] 从文本文件批量下载
- [x] 多语言界面（English、简体中文、繁體中文），支持即时热切换
- [x] 深色主题，带 Zone D 状态条和可折叠日志面板
- [x] 用正则选择流
- [x] 安全的配置解析器与 Windows DPAPI 机密保护
- [x] GUI 自动更新检查系统
- [x] 下载进度与实时状态可视化
- [x] 键盘快捷键、可见的焦点框，以及每个控件的屏幕阅读器名称
- [x] 完全符合 WCAG 2.1 AA 对比度，并显式提示选项冲突依赖
- [ ] 可折叠的选项分组与面向任务的分组
- [ ] 队列管理

<p align="right">(<a href="#readme-top">返回顶部</a>)</p>

---

<!-- 许可证与免责声明 -->

## 免责声明

本应用**仅是图形界面封装**。所有下载和处理工作都由 [N_m3u8DL-RE](https://github.com/nilaoda/N_m3u8DL-RE) 和 [FFmpeg](https://ffmpeg.org/) 完成。如遇下载或媒体处理失败的问题，请前往它们各自的仓库反馈。

## 许可证

基于 MIT 许可证发布。详见 `LICENSE`。

<!-- MARKDOWN 链接与图片 -->
[version-shield]: https://img.shields.io/badge/version-2.1.6-blue?style=for-the-badge
[version-url]: CHANGELOG.md
[dotnet-shield]: https://img.shields.io/badge/.NET-9.0-512BD4?style=for-the-badge&logo=dotnet&logoColor=white
[dotnet-url]: https://dotnet.microsoft.com/
[wpf-shield]: https://img.shields.io/badge/WPF-Windows-0078D6?style=for-the-badge&logo=windows&logoColor=white
[wpf-url]: https://docs.microsoft.com/en-us/dotnet/desktop/wpf/
[csharp-shield]: https://img.shields.io/badge/C%23-13.0-239120?style=for-the-badge&logo=csharp&logoColor=white
[csharp-url]: https://docs.microsoft.com/en-us/dotnet/csharp/
[license-shield]: https://img.shields.io/badge/License-MIT-green?style=for-the-badge
[license-url]: LICENSE
[product-screenshot]: images/screenshot.png
