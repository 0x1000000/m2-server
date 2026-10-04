@echo off
setlocal

rem Always run relative to this script, even when invoked from another directory.
pushd "%~dp0" || exit /b 1

set "OUTPUT_DIR=%CD%\out"

echo [1/4] Building the web application for production...
if not exist "web\node_modules\.bin\vite.cmd" (
    echo Web dependencies are missing. Installing them with npm ci...
    pushd "web" || goto :fail
    call npm ci
    if errorlevel 1 goto :fail_nested
    popd
)

pushd "web" || goto :fail
call npm run build
if errorlevel 1 goto :fail_nested
popd

echo [2/4] Preparing the output directory...
powershell -NoProfile -Command "$root = [IO.Path]::GetFullPath('%CD%'); $target = [IO.Path]::GetFullPath('%OUTPUT_DIR%'); if ($target -ne [IO.Path]::Combine($root, 'out')) { exit 2 }; if (Test-Path -LiteralPath $target) { Remove-Item -LiteralPath $target -Recurse -Force }"
if errorlevel 1 goto :fail
if exist "%OUTPUT_DIR%" goto :fail
mkdir "%OUTPUT_DIR%"
if errorlevel 1 goto :fail

echo [3/4] Publishing the two optimized self-contained .NET applications...
dotnet publish "M2Server.App\M2Server.App.csproj" ^
    --configuration Release ^
    --runtime win-x64 ^
    --output "%OUTPUT_DIR%" ^
    -p:PublishAot=true ^
    -p:OptimizationPreference=Size ^
    -p:DebugType=None ^
    -p:DebugSymbols=false
if errorlevel 1 goto :fail

dotnet publish "M2Server.Web\M2Server.Web.csproj" ^
    --configuration Release ^
    --runtime win-x64 ^
    --output "%OUTPUT_DIR%" ^
    -p:PublishAot=true ^
    -p:OptimizationPreference=Size ^
    -p:DebugType=None ^
    -p:DebugSymbols=false
if errorlevel 1 goto :fail

echo [4/4] Compiling the Win32 tray...
call "M2Server.Tray\build.cmd" "%OUTPUT_DIR%"
if errorlevel 1 goto :fail

powershell -NoProfile -ExecutionPolicy Bypass -Command "$root = [IO.Path]::GetFullPath('%OUTPUT_DIR%'); $symbols = [IO.Path]::GetFullPath('%CD%\.tmp\publish\symbols'); New-Item -ItemType Directory -Force -Path $symbols | Out-Null; Get-ChildItem -LiteralPath $root -Filter '*.pdb' -Recurse -File | Move-Item -Destination $symbols -Force; $files = Get-ChildItem -LiteralPath $root -Recurse -File; $total = ($files | Measure-Object -Property Length -Sum).Sum; $lines = @('Publish directory: ' + $root, 'Total bytes: ' + $total, 'Total MiB: ' + [math]::Round($total / 1MB, 2), '', ('Bytes' + [char]9 + 'File')); $lines += $files | ForEach-Object { $_.Length.ToString() + [char]9 + $_.FullName.Substring($root.Length).TrimStart([IO.Path]::DirectorySeparatorChar) }; Set-Content -LiteralPath (Join-Path $root 'avalonia-sizes.txt') -Value $lines; Write-Host ('Payload: ' + [math]::Round($total / 1MB, 2) + ' MiB'); Write-Host ('Size report: ' + (Join-Path $root 'avalonia-sizes.txt'))"
if errorlevel 1 goto :fail

echo.
echo Building the M2 Server installer...
powershell -NoProfile -ExecutionPolicy Bypass -File "build\Build-Installer.ps1"
if errorlevel 1 goto :fail
echo Publish completed successfully.
echo Run: "%OUTPUT_DIR%\M2Server.Tray.exe"
popd
exit /b 0

:fail_nested
popd
:fail
echo.
echo ERROR: Publish failed.
popd
exit /b 1
