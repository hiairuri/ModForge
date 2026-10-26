@echo off
rem ===================================================================
rem  ModForge launcher
rem
rem  Double-click this file to start the editor. It builds the project
rem  (incrementally, so this is quick after the first time), starts the
rem  editor, and opens your browser.
rem
rem  Options are passed straight through, for example:
rem     ModForge.bat --game "D:\Games\Slay the Spire 2"
rem     ModForge.bat --no-browser
rem
rem  NOTE: this file must keep CRLF line endings. cmd.exe mis-parses a batch
rem  file that uses bare LF, and the failure is confusing rather than obvious:
rem  comment lines get executed as commands, and later lines are skipped, so
rem  the build "succeeds" but the paths come out empty. If you edit this file
rem  with a tool that writes LF, convert it back (tools/battest checks this).
rem ===================================================================

setlocal
cd /d "%~dp0"

set "PROJECT=src\ModForge.App\ModForge.App.csproj"
rem 目标框架是 net9.0-windows（文件夹选择对话框需要 Windows 桌面 API），
rem 所以输出目录带 -windows 后缀。
set "EXE=src\ModForge.App\bin\Release\net9.0-windows\ModForge.exe"

rem --- Find the .NET SDK -------------------------------------------
where dotnet >nul 2>nul
if errorlevel 1 (
    echo.
    echo   .NET SDK not found.
    echo.
    echo   ModForge needs the .NET 9 SDK to build. Install it from:
    echo     https://dotnet.microsoft.com/download
    echo.
    echo   After installing, run this file again.
    echo.
    pause
    exit /b 1
)

rem --- Already running? then just open it ---------------------------
rem Starting a second copy only conflicts over the port and the build output
rem (the running one holds its own files open, which makes the build fail).
rem Opening the existing editor is what you actually want here.
rem
rem The test is the PORT, not the process name. "Something is already running"
rem has exactly one hard fact: somebody is listening on 17890. Looking for a
rem process name missed the `dotnet run` case (child is ModForge.exe, host is
rem dotnet.exe), so this script would start a second copy; that copy finds the
rem port taken, moves to 17891, and the browser stays on 17890 - which is where
rem "cannot reach the editor service" comes from.
rem
rem netstat prints LISTENING in English regardless of system language, so it is
rem safe to match on.
netstat -ano -p tcp | findstr ":17890 " | findstr "LISTENING" >nul 2>nul
if not errorlevel 1 (
    echo.
    echo   ModForge is already running - opening it in your browser.
    echo   ^(Close that window first if you want to restart it.^)
    echo.
    start "" "http://127.0.0.1:17890/"
    exit /b 0
)

rem --- Build -------------------------------------------------------
rem Always build: this is incremental, so an unchanged project finishes
rem in about a second, and it avoids any guesswork about what changed.
echo.
echo   Preparing ModForge...
echo.
dotnet build "%PROJECT%" -c Release -v quiet --nologo
if errorlevel 1 (
    echo.
    echo   Build failed. The errors are shown above.
    echo.
    pause
    exit /b 1
)

if not exist "%EXE%" (
    echo   Build reported success but %EXE% is missing.
    pause
    exit /b 1
)

rem --- Run ---------------------------------------------------------
rem The editor keeps running in this window, so the address, the log and
rem any errors stay visible. Closing the window stops it.
"%EXE%" %*

echo.
echo   ModForge has stopped.
pause
