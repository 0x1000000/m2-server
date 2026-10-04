$ErrorActionPreference = 'Stop'
$projectVersion = & (Join-Path $PSScriptRoot 'Get-ProjectVersion.ps1')
[xml]$metadata = Get-Content -LiteralPath (Join-Path $PSScriptRoot '..\Version.props') -Raw
$generatedDirectory = Join-Path $PSScriptRoot '..\.tmp\publish'
New-Item -ItemType Directory -Force -Path $generatedDirectory | Out-Null
$numericVersion = $projectVersion.Replace('.', ',') + ',0'
@"
#define M2_FILE_VERSION $numericVersion
#define M2_VERSION_STRING "$projectVersion.0"
#define M2_PRODUCT_VERSION_STRING "$projectVersion"
#define M2_PRODUCT_NAME "$($metadata.Project.PropertyGroup.Product)"
#define M2_PUBLISHER_NAME "$($metadata.Project.PropertyGroup.Company)"
"@ | Set-Content -LiteralPath (Join-Path $generatedDirectory 'tray-version.h') -Encoding ascii
