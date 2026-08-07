@echo off
setlocal
echo ========================================================
echo   N_m3u8DL_RE_GUI Publisher (Single File Executable)
echo ========================================================

set "SOLUTION_DIR=%~dp0"
set "VERSION=%~1"
if "%VERSION%"=="" set "VERSION=2.1.4"
set "PUBLISH_DIR=%SOLUTION_DIR%Publish\N_m3u8DL_RE_GUI_v%VERSION%"
set "PROJECT_FILE=%SOLUTION_DIR%N_m3u8DL_RE_GUI\N_m3u8DL_RE_GUI.csproj"

echo Code Directory: %SOLUTION_DIR%
echo Target Version: %VERSION%

if exist "%PUBLISH_DIR%" (
    echo Cleaning existing target publish directory...
    rd /s /q "%PUBLISH_DIR%"
)

echo.
echo Building and Publishing...
echo.

:: Publish as single file, self-contained (no .NET runtime needed on target machine)
dotnet publish "%PROJECT_FILE%" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o "%PUBLISH_DIR%"

if %ERRORLEVEL% NEQ 0 (
    echo.
    echo [ERROR] Build failed!
    pause
    exit /b %ERRORLEVEL%
)

echo.
echo Copying external dependencies (if they exist)...

if exist "%SOLUTION_DIR%N_m3u8DL-RE.exe" (
    echo Copying N_m3u8DL-RE.exe...
    copy "%SOLUTION_DIR%N_m3u8DL-RE.exe" "%PUBLISH_DIR%" >nul
) else (
    echo [WARNING] N_m3u8DL-RE.exe not found in root. You must copy it manually.
)

if exist "%SOLUTION_DIR%ffmpeg.exe" (
    echo Copying ffmpeg.exe...
    copy "%SOLUTION_DIR%ffmpeg.exe" "%PUBLISH_DIR%" >nul
)

if exist "%SOLUTION_DIR%m3u8_cf_bypass.py" (
    echo Copying m3u8_cf_bypass.py...
    copy "%SOLUTION_DIR%m3u8_cf_bypass.py" "%PUBLISH_DIR%" >nul
)

echo.
echo ========================================================
echo   Success! Output located at:
echo   %PUBLISH_DIR%
echo ========================================================
echo.

explorer "%PUBLISH_DIR%"
pause
