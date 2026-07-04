@echo off
REM Build Hanoi Tower test program for WinDbg E2E tests.
REM Requires: Visual Studio Build Tools (cl.exe)
REM
REM Usage: build_hanoi.bat
REM   Then: hanoi_win.exe [N]   (default N=5)

call "C:\Program Files (x86)\Microsoft Visual Studio\18\BuildTools\VC\Auxiliary\Build\vcvars64.bat" >nul 2>&1
if %ERRORLEVEL% NEQ 0 (
    echo ERROR: vcvars64.bat not found — is VS Build Tools installed?
    exit /b 1
)

set SCRIPT_DIR=%~dp0

cl /Zi /Od /Fe:"%SCRIPT_DIR%hanoi_win.exe" "%SCRIPT_DIR%hanoi_win.cpp" ^
    kernel32.lib ^
    /link /DEBUG:FULL /ENTRY:mainCRTStartup /SUBSYSTEM:CONSOLE

if %ERRORLEVEL% NEQ 0 (
    echo ERROR: Build failed
    exit /b 1
)

echo.
echo Build OK: %SCRIPT_DIR%hanoi_win.exe
exit /b 0
