@echo off
rem ===================================================================
rem  ModForge stop script
rem
rem  Double-click this file to stop an editor started by ModForge.bat.
rem
rem  Normally you just close the window ModForge.bat left open - that stops
rem  it. Use this when the window is out of reach (hidden behind others,
rem  minimized, on another desktop, or the editor was started detached and
rem  has no window at all) while the process still holds port 17890, so
rem  ModForge.bat reports "already running" or fails to start.
rem
rem     StopModForge.bat        asks first, then waits so you can read it
rem     StopModForge.bat -y     stops without asking (no pause; for scripts)
rem
rem  NOTE: this file must keep CRLF line endings. cmd.exe mis-parses a batch
rem  file that uses bare LF, and the failure is confusing rather than obvious
rem  (comment lines get executed as commands). tools/battest checks this.
rem ===================================================================

setlocal
cd /d "%~dp0"

set "EXE_NAME=ModForge.exe"

rem --- Was -y passed? -----------------------------------------------
rem Also decides whether to pause at the end: a double-clicked .bat closes
rem its window the moment it finishes, so without a pause the whole thing
rem looks like a flash and any message is unreadable. -y means "no pause",
rem which is what a script or a shortcut wants.
set "PAUSE_AT_END=1"
if /i "%~1"=="-y"    set "PAUSE_AT_END="
if /i "%~1"=="/y"    set "PAUSE_AT_END="
if /i "%~1"=="--yes" set "PAUSE_AT_END="

rem --- Is it running? ------------------------------------------------
rem Uses tasklist's own output rather than piping into find: a GNU find.exe
rem earlier on PATH would swallow the arguments and break the check. Same
rem technique as the launcher, so both files behave identically here.
set "TASKLIST_OUT=%TEMP%\modforge-stop-tasklist.txt"
tasklist /fi "IMAGENAME eq %EXE_NAME%" /nh > "%TASKLIST_OUT%" 2>nul

set "RUNNING="
set "RUNNING_PID="
for /f "usebackq tokens=1,2" %%A in ("%TASKLIST_OUT%") do (
    if /i "%%A"=="%EXE_NAME%" (
        set "RUNNING=1"
        set "RUNNING_PID=%%B"
    )
)

if not defined RUNNING (
    del "%TASKLIST_OUT%" >nul 2>nul
    echo.
    echo   ModForge is not running - nothing to stop.
    echo.
    set "RC=0"
    goto :done
)

rem --- Confirm -------------------------------------------------------
rem The editor keeps unsaved work in the browser page, not on disk: it only
rem writes the project when you press Save. So stopping cannot save anything
rem on your behalf, and killing the process discards whatever was not saved.
rem That is worth one question.
if defined PAUSE_AT_END (
    echo.
    echo   ModForge is running ^(PID %RUNNING_PID%^).
    echo.
    echo   Anything you have not saved yet lives in the browser page and
    echo   will be lost - save there first if you need it.
    echo.
    choice /c yn /n /m "  Stop it now? [Y/N] "
    if errorlevel 2 (
        del "%TASKLIST_OUT%" >nul 2>nul
        echo.
        echo   Cancelled - ModForge is still running.
        echo.
        set "RC=1"
        goto :done
    )
    echo.
)

rem --- Stop ----------------------------------------------------------
rem /F is required, not optional: this is a console application, so it has no
rem top-level window of its own for a polite close request to reach. Without
rem /F, taskkill reports the process "can only be terminated forcefully".
taskkill /f /im "%EXE_NAME%" >nul 2>nul

rem --- Verify --------------------------------------------------------
rem Report what actually happened rather than assuming the kill worked:
rem a permission problem, or another copy, would otherwise go unnoticed
rem until the next start silently fails on the busy port.
rem
rem Poll instead of checking once: taskkill returns as soon as termination is
rem requested, while the process is still being torn down. A single immediate
rem check therefore sees the dying process and reports a failure that is not
rem real (measured: it stopped, but the message said it had not).
set /a WAITED=0

:verify
tasklist /fi "IMAGENAME eq %EXE_NAME%" /nh > "%TASKLIST_OUT%" 2>nul
set "STILL_RUNNING="
for /f "usebackq tokens=1" %%A in ("%TASKLIST_OUT%") do (
    if /i "%%A"=="%EXE_NAME%" set "STILL_RUNNING=1"
)

if not defined STILL_RUNNING goto :stopped

rem Roughly a second between attempts. ping is used rather than timeout
rem because timeout refuses to run when input is redirected, which would
rem turn the wait into a busy loop.
set /a WAITED+=1
if %WAITED% geq 20 goto :failed
ping -n 2 127.0.0.1 >nul 2>nul
goto :verify

:failed
del "%TASKLIST_OUT%" >nul 2>nul
echo   ModForge is still running after %WAITED% seconds - it could not be stopped.
echo.
echo   Try running this file as administrator, or end %EXE_NAME%
echo   manually in Task Manager.
echo.
set "RC=1"
goto :done

:stopped
del "%TASKLIST_OUT%" >nul 2>nul
echo   ModForge has stopped.
echo.
echo   The browser tab can be closed; it will show connection errors
echo   until you start the editor again.
echo.
set "RC=0"

:done
if defined PAUSE_AT_END pause
exit /b %RC%
