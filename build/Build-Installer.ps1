$ErrorActionPreference = 'Stop'
$projectVersion = & (Join-Path $PSScriptRoot 'Get-ProjectVersion.ps1')
& (Join-Path $PSScriptRoot 'Verify-Release.ps1')
$repositoryDirectory = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
Push-Location $repositoryDirectory
try {
    # WiX SDK owns its pinned extensions. The global tool is available for diagnostics and inspection.
    & dotnet build 'M2Server.Installer\M2Server.Installer.wixproj' -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Installer build failed.' }
    $builtInstaller = Join-Path $repositoryDirectory ".tmp\publish\installer\en-us\M2Server-$projectVersion-x64.msi"
    $releaseInstaller = Join-Path $repositoryDirectory "out\M2Server-$projectVersion-x64.msi"
    Copy-Item -LiteralPath $builtInstaller -Destination $releaseInstaller -Force
    & (Join-Path $PSScriptRoot 'Verify-Release.ps1') -IncludeInstaller
} finally { Pop-Location }
