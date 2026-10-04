Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
[xml]$versionDocument = Get-Content -LiteralPath (Join-Path $PSScriptRoot '..\Version.props') -Raw
$projectVersion = [string]$versionDocument.Project.PropertyGroup.ProjectVersion
if ($projectVersion -notmatch '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$') {
    throw 'ProjectVersion must be canonical Major.Minor.Patch.'
}
$parsedVersion = [version]$projectVersion
if ($parsedVersion.Major -gt 255 -or $parsedVersion.Minor -gt 255 -or $parsedVersion.Build -gt 65535) {
    throw 'ProjectVersion exceeds MSI limits (255.255.65535).'
}
$projectVersion
