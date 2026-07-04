@echo off
call "C:\Program Files (x86)\Microsoft Visual Studio\18\BuildTools\VC\Auxiliary\Build\vcvars64.bat"
cl /Zi /Od /Fe:test_pdb.exe test_pdb.c kernel32.lib /link /DEBUG:FULL /ENTRY:mainCRTStartup /SUBSYSTEM:CONSOLE
