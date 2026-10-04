param([switch]$IncludeInstaller)
$ErrorActionPreference = 'Stop'
$projectVersion = & (Join-Path $PSScriptRoot 'Get-ProjectVersion.ps1')
$releaseDirectory = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\out'))
foreach ($name in 'M2Server.App.exe', 'M2Server.Web.exe', 'M2Server.Tray.exe') {
    $file = Join-Path $releaseDirectory $name
    $metadata = [Diagnostics.FileVersionInfo]::GetVersionInfo($file)
    if ($metadata.FileVersion -ne "$projectVersion.0" -or $metadata.ProductVersion -ne $projectVersion) {
        throw "$name has inconsistent version metadata: $($metadata.FileVersion) / $($metadata.ProductVersion); expected $projectVersion"
    }
    if ($metadata.ProductName -ne 'M2 Server' -or $metadata.CompanyName -ne '0x1000000') {
        throw "$name has inconsistent product or publisher metadata."
    }
}
$frontendVersion = Get-Content -LiteralPath (Join-Path $PSScriptRoot '..\.tmp\web-dist\mobile\browser\project-version.json') -Raw | ConvertFrom-Json
if ($frontendVersion.version -ne $projectVersion) { throw 'Frontend version does not match ProjectVersion.' }
if ($IncludeInstaller) {
    $msiPath = Join-Path $releaseDirectory "M2Server-$projectVersion-x64.msi"
    $installer = New-Object -ComObject WindowsInstaller.Installer
    $database = $installer.OpenDatabase($msiPath, 0)
    $versionParts = [version]$projectVersion
    $expectedProductCode = '{' + ('AF209CD1-78E2-4C1A-A747-0000{0:X2}{1:X2}{2:X4}' -f $versionParts.Major, $versionParts.Minor, $versionParts.Build) + '}'
    foreach ($entry in @{ ProductCode = $expectedProductCode; ProductVersion = $projectVersion; ProductName = 'M2 Server'; Manufacturer = '0x1000000' }.GetEnumerator()) {
        $query = "SELECT ``Value`` FROM ``Property`` WHERE ``Property`` = '$($entry.Key)'"
        $view = $database.OpenView($query)
        $view.Execute()
        $record = $view.Fetch()
        if (!$record -or $record.StringData(1) -ne $entry.Value) { throw "MSI $($entry.Key) does not match project metadata." }
        $view.Close()
    }
    $expectedCleanupCondition = 'Installed AND REMOVE = "ALL" AND NOT UPGRADINGPRODUCTCODE'
    $expectedPurgeCondition = $expectedCleanupCondition + ' AND M2_PRESERVE_CONFIG <> "1"'
    foreach ($action in @{ PrepareUninstall = 3074; PurgeApplicationData = 3586 }.GetEnumerator()) {
        $view = $database.OpenView("SELECT ``Type``, ``Source`` FROM ``CustomAction`` WHERE ``Action`` = '$($action.Key)'")
        $view.Execute()
        $record = $view.Fetch()
        if (!$record -or $record.IntegerData(1) -ne $action.Value -or $record.StringData(2) -ne 'CleanupExecutable') {
            throw "MSI $($action.Key) must be a checked, elevated embedded-executable action with the expected execution phase."
        }
        $view.Close()
        $view = $database.OpenView("SELECT ``Condition`` FROM ``InstallExecuteSequence`` WHERE ``Action`` = '$($action.Key)'")
        $view.Execute()
        $record = $view.Fetch()
        $expectedCondition = if ($action.Key -eq 'PurgeApplicationData') { $expectedPurgeCondition } else { $expectedCleanupCondition }
        if (!$record -or $record.StringData(1) -ne $expectedCondition) {
            throw "MSI $($action.Key) has an incorrect uninstall/configuration-preservation condition."
        }
        $view.Close()
    }
    foreach ($entry in @{ M2_PRESERVE_CONFIG = '1' }.GetEnumerator()) {
        $view = $database.OpenView("SELECT ``Value`` FROM ``Property`` WHERE ``Property`` = '$($entry.Key)'")
        $view.Execute()
        $record = $view.Fetch()
        if (!$record -or $record.StringData(1) -ne $entry.Value) { throw 'MSI must preserve configuration by default.' }
        $view.Close()
    }
    $view = $database.OpenView('SELECT `Value` FROM `Property` WHERE `Property` = ''SecureCustomProperties''')
    $view.Execute()
    $record = $view.Fetch()
    if (!$record -or ($record.StringData(1) -split ';') -notcontains 'M2_PRESERVE_CONFIG') {
        throw 'MSI must pass the configuration choice into elevated execution.'
    }
    $view.Close()
    $view = $database.OpenView('SELECT `Property` FROM `Control` WHERE `Dialog_` = ''UninstallOptionsDlg'' AND `Control` = ''PreserveConfig''')
    $view.Execute()
    $record = $view.Fetch()
    if (!$record -or $record.StringData(1) -ne 'M2_KEEP_CONFIG_CHECKED') { throw 'MSI uninstall checkbox must control configuration preservation.' }
    $view.Close()
    $view = $database.OpenView('SELECT `FileName` FROM `File`')
    $view.Execute()
    $payloadNames = @()
    while ($record = $view.Fetch()) { $payloadNames += ($record.StringData(1) -split '\|')[-1] }
    $view.Close()
    foreach ($name in 'M2Server.App.exe', 'M2Server.Web.exe', 'M2Server.Tray.exe') {
        if ($payloadNames -notcontains $name) { throw "Installer is missing $name." }
    }
    if ($payloadNames | Where-Object { $_ -match '\.(pdb|msi|wixpdb)$' -or $_ -eq 'avalonia-sizes.txt' }) {
        throw 'Installer contains diagnostic or installer output instead of only the application payload.'
    }
}
Write-Host "Verified M2 Server $projectVersion release metadata."
