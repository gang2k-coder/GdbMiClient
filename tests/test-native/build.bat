@echo off
REM Build native test target for WinDbg E2E tests.
REM Requires: clang + Windows SDK (for kernel32.lib)

set SCRIPT_DIR=%~dp0
set TARGET=%SCRIPT_DIR%test_target.exe

echo Building native test target...
clang -g -O0 -nostdlib ^
  -Wl,/entry:mainCRTStartup ^
  -Wl,/subsystem:console ^
  -o "%TARGET%" ^
  "%SCRIPT_DIR%test_target.c" ^
  -lkernel32

if %ERRORLEVEL% NEQ 0 (
    echo ERROR: Build failed
    exit /b 1
)

echo OK: %TARGET%
exit /b 0
