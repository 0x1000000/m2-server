@echo off
setlocal
pushd "%~dp0.." || exit /b 1
set "TRAY_OUTPUT=%~1"
if "%TRAY_OUTPUT%"=="" set "TRAY_OUTPUT=%CD%\out"
for /f "delims=" %%V in ('"%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe" -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath') do set "VS_PATH=%%V"
if not defined VS_PATH goto :fail
call "%VS_PATH%\VC\Auxiliary\Build\vcvars64.bat" >nul
if errorlevel 1 goto :fail
if not exist ".tmp\publish" mkdir ".tmp\publish"
if not exist "%TRAY_OUTPUT%" mkdir "%TRAY_OUTPUT%"
powershell -NoProfile -ExecutionPolicy Bypass -File "build\Prepare-TrayVersion.ps1"
if errorlevel 1 goto :fail
rc.exe /nologo /I ".tmp\publish" /fo ".tmp\publish\tray.res" "M2Server.Tray\tray.rc"
if errorlevel 1 goto :fail
cl.exe /nologo /O1 /MT /W4 /Fo".tmp\publish\tray.obj" "M2Server.Tray\tray.c" ".tmp\publish\tray.res" /link /SUBSYSTEM:WINDOWS /OUT:"%TRAY_OUTPUT%\M2Server.Tray.exe" user32.lib shell32.lib advapi32.lib
if errorlevel 1 goto :fail
popd
exit /b 0
:fail
popd
exit /b 1
