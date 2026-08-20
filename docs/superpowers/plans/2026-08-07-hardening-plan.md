# N_m3u8DL_RE_GUI Hardening Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** ปรับปรุงความปลอดภัย ความทนทาน และ usability ของ GUI โดยไม่เปลี่ยน workflow หลักหรือเพิ่ม dependency ใหม่

**Architecture:** แก้ที่ shared core/services ก่อน แล้วค่อยปรับ WPF surface ให้ใช้ผลลัพธ์เดียวกัน การ validate URL จะอยู่ใน `InputValidation`; การ resolve title จะรับ cancellation token; config จะคง migration เดิมแต่ไม่เขียน secret ลง legacy/plaintext โดยไม่จำเป็น

**Tech Stack:** .NET 9, WPF, CommunityToolkit.Mvvm, xUnit, NSubstitute, Windows DPAPI ผ่าน native platform API โดยไม่เพิ่ม package ถ้า dependency ยังไม่มีในโปรเจกต์

## Global Constraints

- ห้ามเพิ่ม package สำหรับ validation, logging, encryption หรือ UI icon
- คง command-line output ของ `ArgsBuilder` และ compatibility กับ `config.txt` เท่าที่ไม่ทำให้ secret รั่ว
- เปลี่ยนเฉพาะไฟล์ที่ระบุในแต่ละ task
- ทุก behavioral change ต้องมี unit test ที่ล้มเหลวก่อนแก้และผ่านหลังแก้
- อย่าเปลี่ยน visual identity ทั้งหน้าจอ; ปรับเฉพาะ accessibility, feedback และ layout ที่จำเป็น

---

### Task 1: ทำ URL validation และ title lookup ให้ fail-fast

**Files:**
- Modify: `N_m3u8DL_RE_GUI.Core/InputValidation.cs`
- Modify: `N_m3u8DL_RE_GUI/Services/UtilityService.cs`
- Modify: `N_m3u8DL_RE_GUI/Services/IUtilityService.cs`
- Modify: `N_m3u8DL_RE_GUI/MainWindow.xaml.cs`
- Test: `N_m3u8DL_RE_GUI.Tests/Unit/Core/InputValidationTests.cs`
- Test: `N_m3u8DL_RE_GUI.Tests/Unit/Services/UtilityServiceTests.cs`

**Interfaces:**
- `InputValidation.IsHttpUrl(string?)` ยังคงชื่อและ return type เดิม แต่ต้องใช้ `Uri.TryCreate` และรับเฉพาะ `http`/`https` ที่มี host
- เปลี่ยน `IUtilityService.GetTitleFromUrlAsync` เป็น `Task<string> GetTitleFromUrlAsync(string url, CancellationToken cancellationToken = default)`
- ทุก caller ใน `MainWindow.xaml.cs` ส่ง token ที่เหมาะสม หรือใช้ `default` สำหรับ action ที่ไม่มี lifetime token

- [ ] **Step 1: เพิ่ม failing tests สำหรับ URL ที่ไม่ valid**

```csharp
[Theory]
[InlineData("http://")]
[InlineData("https:///")]
[InlineData("ftp://example.com/video.m3u8")]
[InlineData("https://?query-only")]
public void IsHttpUrl_rejects_invalid_absolute_urls(string value)
{
    Assert.False(InputValidation.IsHttpUrl(value));
}
```

- [ ] **Step 2: รัน test เฉพาะชุดนี้และยืนยันว่า fail**

Run: `dotnet test N_m3u8DL_RE_GUI.Tests\N_m3u8DL_RE_GUI.Tests.csproj --no-build --filter FullyQualifiedName~InputValidationTests`

Expected: FAIL อย่างน้อยหนึ่ง case เพราะ implementation ปัจจุบันตรวจแค่ prefix

- [ ] **Step 3: แก้ `IsHttpUrl` ด้วย BCL เท่านั้น**

```csharp
public static bool IsHttpUrl(string? input)
{
    return Uri.TryCreate(input, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
        && !string.IsNullOrWhiteSpace(uri.Host);
}
```

- [ ] **Step 4: เพิ่ม cancellation และ timeout ที่ `UtilityService`**

กำหนด `HttpClient.Timeout` เป็น 15 วินาที ส่ง `cancellationToken` ให้ทุก `GetAsync/GetStringAsync` ที่เกี่ยวข้อง และรักษา behavior เดิมคือคืน `string.Empty` เมื่อ lookup ล้มเหลว แต่ต้อง rethrow `OperationCanceledException` เพื่อให้ caller ยกเลิกได้จริง

- [ ] **Step 5: ส่ง cancellation จาก batch/title actions**

เพิ่ม `CancellationTokenSource` เฉพาะงาน title lookup ใน `MainWindow.xaml.cs`, cancel ใน Stop/เมื่อ action จบ และส่ง token เข้า `BuildScriptAsync`/`GetTitleFromUrlAsync` โดยไม่สร้าง service abstraction ใหม่

- [ ] **Step 6: รัน tests ให้ผ่าน**

Run: `dotnet test N_m3u8DL_RE_GUI.Tests\N_m3u8DL_RE_GUI.Tests.csproj --no-build --filter FullyQualifiedName!~Integration`

Expected: PASS และไม่มี network call ใน unit tests

- [ ] **Step 7: Commit**

```bash
git add N_m3u8DL_RE_GUI.Core/InputValidation.cs N_m3u8DL_RE_GUI/Services/UtilityService.cs N_m3u8DL_RE_GUI/Services/IUtilityService.cs N_m3u8DL_RE_GUI/MainWindow.xaml.cs N_m3u8DL_RE_GUI.Tests/Unit/Core/InputValidationTests.cs N_m3u8DL_RE_GUI.Tests/Unit/Services/UtilityServiceTests.cs
git commit -m "fix: validate URLs and cancel title lookups"
```

### Task 2: หยุดเก็บ key/header ที่เป็น secret แบบ plaintext

**Files:**
- Modify: `N_m3u8DL_RE_GUI/Services/JsonConfigService.cs`
- Modify: `N_m3u8DL_RE_GUI/Services/ConfigService.cs`
- Modify: `N_m3u8DL_RE_GUI/Services/MainWindowConfigMapper.cs`
- Modify: `N_m3u8DL_RE_GUI/Services/AppConfigState.cs`
- Test: `N_m3u8DL_RE_GUI.Tests/Unit/Services/JsonConfigServiceTests.cs`
- Test: `N_m3u8DL_RE_GUI.Tests/Unit/Services/ConfigServiceTests.cs`

**Interfaces:**
- คง `IConfigService` เดิม
- ใช้ Windows DPAPI ผ่าน native platform API เฉพาะค่าที่เป็น secret; ห้ามเพิ่ม package ใหม่เพียงเพื่อ encryption
- ค่า non-secret ยังอยู่ใน `config.json` ตามเดิม

- [ ] **Step 1: เพิ่ม failing test ว่าค่า secret ไม่ปรากฏตรง ๆ ในไฟล์**

สร้าง temporary config path, ใส่ `CustomHLSKey = "secret-key"` และ `Headers = "Cookie: secret"`, เรียก `Save`, แล้ว assert ว่าไฟล์ที่เขียนไม่มีข้อความสองค่านี้ตรง ๆ

- [ ] **Step 2: เพิ่ม helper ภายใน `JsonConfigService` สำหรับ protect/unprotect**

ใช้ UTF-8 bytes + Windows `CryptProtectData`/`CryptUnprotectData` ผ่าน P/Invoke กับ current user scope, serialize เป็น Base64 และใช้ prefix เดียว เช่น `dpapi:` เพื่อแยกค่าที่เข้ารหัสออกจาก legacy value; ถ้า runtime/platform ไม่ใช่ Windows ให้ fail closed และไม่เขียน secret ใหม่ลง disk

- [ ] **Step 3: protect เฉพาะ secret keys**

กำหนด static `HashSet<string>` ใน `JsonConfigService` สำหรับ `Headers`, `Proxy`, `CustomHLSKey`, `CustomHLSIv`, `Key` เฉพาะค่าที่เป็น secret จริง; ห้ามเข้ารหัส `KeyTextFile` หรือ path ทั่วไป

- [ ] **Step 4: รักษา legacy migration แบบปลอดภัย**

อ่านค่า legacy เดิมได้เพื่อ migration แต่เมื่อ save ให้ JSON เป็นแหล่งหลัก และไม่เขียน secret กลับลง `config.txt`; ถ้า compatibility เก่าจำเป็นต้องเก็บ ให้เขียน marker ที่อ่านได้เฉพาะเวอร์ชันใหม่และ document limitation ใน code comment สั้น ๆ

- [ ] **Step 5: เพิ่ม tests สำหรับ round-trip และเครื่อง/ผู้ใช้ต่างกัน**

ตรวจว่า user เดิม load ได้ค่าเดิม, malformed `dpapi:` ไม่ทำให้แอป crash และ user อื่นไม่สามารถถอดค่าได้ใน scope เดียวกัน

- [ ] **Step 6: รัน unit tests และตรวจไฟล์จริงใน temp directory**

Run: `dotnet test N_m3u8DL_RE_GUI.Tests\N_m3u8DL_RE_GUI.Tests.csproj --no-build --filter FullyQualifiedName~JsonConfigServiceTests`

Expected: PASS และไม่มี secret plaintext ใน JSON output

- [ ] **Step 7: Commit**

```bash
git add N_m3u8DL_RE_GUI/Services/JsonConfigService.cs N_m3u8DL_RE_GUI/Services/ConfigService.cs N_m3u8DL_RE_GUI/Services/MainWindowConfigMapper.cs N_m3u8DL_RE_GUI/Services/AppConfigState.cs N_m3u8DL_RE_GUI.Tests/Unit/Services/JsonConfigServiceTests.cs N_m3u8DL_RE_GUI.Tests/Unit/Services/ConfigServiceTests.cs
git commit -m "security: protect persisted download secrets"
```

### Task 3: จัดการ batch script lifecycle และ error feedback

**Files:**
- Modify: `N_m3u8DL_RE_GUI/Services/BatchScriptService.cs`
- Modify: `N_m3u8DL_RE_GUI/Services/BatchScriptBuildResult.cs`
- Modify: `N_m3u8DL_RE_GUI/MainWindow.xaml.cs`
- Test: `N_m3u8DL_RE_GUI.Tests/Unit/Services/BatchScriptServiceTests.cs`

**Interfaces:**
- `BatchScriptBuildResult.FilePath` ยังคงเป็น path ที่เรียกใช้งานได้
- เพิ่ม cleanup หลัง process จบ โดยลบเฉพาะไฟล์ batch ที่ service สร้างและอยู่ใน temp/work directory ที่ระบุ

- [ ] **Step 1: เพิ่ม failing test สำหรับ filename collision และ cleanup contract**

ให้ build สองครั้งภายในวินาทีเดียวกันแล้ว assert ว่า path ไม่ซ้ำ หรือใช้ unique temp name; เพิ่ม test ว่า `FilePath` เป็นไฟล์ `.bat` ที่สร้างจริงและลบได้หลัง execution

- [ ] **Step 2: เปลี่ยนชื่อไฟล์จาก timestamp-only เป็น `Path.GetTempFileName`/GUID ของ BCL**

คงนามสกุล `.bat`, ไม่เพิ่ม custom ID generator และไม่เขียนไฟล์ไว้ใน project directory โดย default

- [ ] **Step 3: เพิ่ม cleanup ใน `MainWindow.xaml.cs` ด้วย `try/finally`**

เก็บ `result.FilePath`, เรียก `StartProcessAsync`, แล้ว `File.Delete` ใน finally โดย catch เฉพาะ `IOException`/`UnauthorizedAccessException` และแสดงข้อความสั้น ๆ ผ่าน log เท่านั้น

- [ ] **Step 4: เพิ่ม test ว่า title count ใช้จำนวนรายการที่ parse ได้**

กรณี input text มีบรรทัดว่าง/บรรทัด invalid ต้องได้ `TITLE "[1/1]..."` ไม่ใช่ denominator จาก `rawLines.Count`

- [ ] **Step 5: รัน batch tests**

Run: `dotnet test N_m3u8DL_RE_GUI.Tests\N_m3u8DL_RE_GUI.Tests.csproj --no-build --filter FullyQualifiedName~BatchScriptServiceTests`

Expected: PASS และไม่มี `.bat` ค้างจาก test

- [ ] **Step 6: Commit**

```bash
git add N_m3u8DL_RE_GUI/Services/BatchScriptService.cs N_m3u8DL_RE_GUI/Services/BatchScriptBuildResult.cs N_m3u8DL_RE_GUI/MainWindow.xaml.cs N_m3u8DL_RE_GUI.Tests/Unit/Services/BatchScriptServiceTests.cs
git commit -m "fix: isolate and clean up generated batch scripts"
```

### Task 4: ปรับ WPF UX/accessibility แบบ surgical

**Files:**
- Modify: `N_m3u8DL_RE_GUI/MainWindow.xaml`
- Modify: `N_m3u8DL_RE_GUI/MainWindow.xaml.cs`
- Test: `N_m3u8DL_RE_GUI.Tests/Unit/ViewModels/MainViewModelTests.cs` เฉพาะ state/command behavior ที่แก้

**Interfaces:**
- ไม่เปลี่ยน layout หลัก, สีหลัก หรือ tab information architecture
- ใช้ native WPF accessibility properties และ existing styles เท่านั้น

- [ ] **Step 1: เพิ่ม accessible names ให้ controls ที่มี label แบบ TextBlock**

เพิ่ม `AutomationProperties.Name` ให้ URL, Save Dir, Save Name, EXE path, proxy, headers, key, IV, command preview และปุ่ม icon/emoji ที่ไม่มี labelชัดเจน โดยใช้ข้อความเดียวกับ label/tooltip ปัจจุบัน

- [ ] **Step 2: แยก validation error จากสีอย่างเดียว**

ให้ field invalid มี tooltip/status text ที่อธิบายวิธีแก้ และรักษา border color เดิมไว้เป็น secondary signal; ห้ามเพิ่ม validation framework

- [ ] **Step 3: ปรับ loading state ของ title lookup/download**

ใช้ข้อความสถานะที่มีอยู่หรือเพิ่ม TextBlock เดียวสำหรับ `Resolving title…`, `Downloading…`, `Cancelled`; ปุ่มต้อง disabled เฉพาะ operation ที่กำลังทำ และ Stop ต้องยังใช้งานได้

- [ ] **Step 4: แทน emoji ที่ทำหน้าที่เป็น structural icon เฉพาะจุดที่จำเป็น**

ใช้ text label เดิม เช่น `Download`, `Stop`, `Check Now` เป็น accessible fallback; ไม่เพิ่ม icon package และไม่ redesign ทั้งชุด

- [ ] **Step 5: ตรวจด้วย keyboard-only pass**

ตรวจ Tab order, Enter ที่ปุ่ม Download, Escape/Stop ระหว่าง process, focus border และอ่าน field ผ่าน Narrator/Accessibility Insights ถ้ามีในเครื่อง

- [ ] **Step 6: รัน unit tests และ build**

Run: `dotnet test N_m3u8DL_RE_GUI.Tests\N_m3u8DL_RE_GUI.Tests.csproj --no-build --filter FullyQualifiedName!~Integration`

Run: `dotnet build N_m3u8DL_RE_GUI.sln /warnaserror`

Expected: unit tests ผ่านทั้งหมด และ build จบโดยไม่มี warning

- [ ] **Step 7: Commit**

```bash
git add N_m3u8DL_RE_GUI/MainWindow.xaml N_m3u8DL_RE_GUI/MainWindow.xaml.cs N_m3u8DL_RE_GUI.Tests/Unit/ViewModels/MainViewModelTests.cs
git commit -m "ux: improve desktop accessibility and operation feedback"
```

### Task 5: Final verification gate

**Files:**
- Modify: ไม่มี source change โดย default
- Check: `README.md`, `CHANGELOG.md`, `docs/PROJECT_STRUCTURE.md`

- [ ] **Step 1: รัน unit tests ทั้งชุด**

Run: `dotnet test N_m3u8DL_RE_GUI.Tests\N_m3u8DL_RE_GUI.Tests.csproj --no-build --filter FullyQualifiedName!~Integration`

Expected: จำนวน test ต้องไม่น้อยกว่า baseline 152 และทุก test pass

- [ ] **Step 2: รัน integration tests แยกจาก unit**

Run: `dotnet test N_m3u8DL_RE_GUI.Tests\N_m3u8DL_RE_GUI.Tests.csproj --no-build --filter FullyQualifiedName~Integration`

Expected: ถ้า network fixture ใช้งานไม่ได้ ให้รายงานเป็น environment failure ไม่เปลี่ยน production code เพื่อทำให้ test ผ่านแบบหลอก ๆ

- [ ] **Step 3: ตรวจ git diff และไฟล์ลับ**

Run: `git diff --check` และตรวจว่า `config.json`, `config.txt`, generated `.bat`, secrets และ build output ไม่ถูก stage

- [ ] **Step 4: ตรวจ manual acceptance**

ทดสอบ URL ปกติ, URL malformed, batch input, cancel title lookup, cancel download, restart app แล้วตรวจ config round-trip, และใช้ keyboard-only flow ตั้งแต่กรอก URL ถึง Download

- [ ] **Step 5: อัปเดตเอกสารเฉพาะเมื่อ behavior เปลี่ยนจริง**

เพิ่ม CHANGELOG entry เรื่อง protected config, URL validation และ batch cleanup; ไม่สร้าง architecture document ใหม่

## Scope intentionally skipped

- ไม่ทำ MVVM rewrite ทั้ง `MainWindow.xaml.cs`
- ไม่เพิ่ม DI/container abstraction สำหรับ service ที่มี implementation เดียว
- ไม่ทำ redesign ธีมทั้งหมด
- ไม่รันหรือเพิ่ม graph visualization เพราะไม่มี callable `graphify` skill
- ไม่ค้น memory history เพราะไม่มี callable `claude-mem` skill

## Self-review

- ทุก finding จาก review เดิมมี task รองรับ: plaintext secrets (Task 2), URL validation/title timeout (Task 1), generated batch files (Task 3), accessibility/feedback (Task 4)
- ไม่มี task ที่ต้องใช้ dependency ใหม่
- ทุก task มีไฟล์เป้าหมาย, failing test หรือ verification command และ commit boundary
- Integration network failure ถูกแยกจาก unit-test gate ไม่ถูกใช้เป็นเหตุผลแก้ production code แบบ speculative
