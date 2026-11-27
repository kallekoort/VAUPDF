param(
    [Parameter(Mandatory = $true)]
    [string] $MsiPath
)

$ErrorActionPreference = 'Stop'
$installer = New-Object -ComObject WindowsInstaller.Installer
$database = $installer.OpenDatabase((Resolve-Path -LiteralPath $MsiPath).Path, 1)

function Invoke-MsiSql([string] $Sql) {
    $view = $database.OpenView($Sql)
    try {
        $view.Execute()
    }
    finally {
        $view.Close()
        [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($view)
    }
}

function Ensure-MsiTable([string] $Name, [string] $Definition) {
    $view = $database.OpenView("SELECT ``Name`` FROM ``_Tables`` WHERE ``Name`` = '$Name'")
    try {
        $view.Execute()
        $record = $view.Fetch()
        if ($null -eq $record) {
            Invoke-MsiSql $Definition
        }
        else {
            [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($record)
        }
    }
    finally {
        $view.Close()
        [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($view)
    }
}

try {
    Ensure-MsiTable 'AppSearch' @'
CREATE TABLE `AppSearch` (`Property` CHAR(72) NOT NULL, `Signature_` CHAR(72) NOT NULL PRIMARY KEY `Property`, `Signature_`)
'@
    Ensure-MsiTable 'RegLocator' @'
CREATE TABLE `RegLocator` (`Signature_` CHAR(72) NOT NULL, `Root` SHORT NOT NULL, `Key` CHAR(255) NOT NULL, `Name` CHAR(255), `Type` SHORT PRIMARY KEY `Signature_`)
'@
    Ensure-MsiTable 'LaunchCondition' @'
CREATE TABLE `LaunchCondition` (`Condition` CHAR(255) NOT NULL, `Description` CHAR(255) LOCALIZABLE PRIMARY KEY `Condition`)
'@

    # RegLocator type 2 reads a value in the 32-bit registry view; 18 uses the 64-bit view.
    $searches = @(
        @('VAUWINDOWSBUILD', 2, 'SOFTWARE\Microsoft\Windows NT\CurrentVersion', 'CurrentBuildNumber', 18),
        @('VAUWEBVIEWMACHINE', 2, 'SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}', 'pv', 2),
        @('VAUWEBVIEWUSER', 1, 'Software\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}', 'pv', 2)
    )
    foreach ($search in $searches) {
        $property, $root, $key, $value, $type = $search
        Invoke-MsiSql "DELETE FROM ``AppSearch`` WHERE ``Property`` = '$property'"
        Invoke-MsiSql "DELETE FROM ``RegLocator`` WHERE ``Signature_`` = '$property'"
        Invoke-MsiSql "INSERT INTO ``AppSearch`` (``Property``, ``Signature_``) VALUES ('$property', '$property')"
        Invoke-MsiSql "INSERT INTO ``RegLocator`` (``Signature_``, ``Root``, ``Key``, ``Name``, ``Type``) VALUES ('$property', $root, '$key', '$value', $type)"
    }

    # Maintenance/uninstall must remain possible after a dependency is removed.
    $conditions = @(
        @('Installed OR (VersionNT64 AND MsiNTProductType = 1 AND VAUWINDOWSBUILD >= 22000)',
          'VAUPDF requires Windows 11 (x64). Windows 10 and 32-bit Windows are not supported by this package.'),
        @('Installed OR ((VAUWEBVIEWMACHINE AND VAUWEBVIEWMACHINE <> "0.0.0.0") OR (VAUWEBVIEWUSER AND VAUWEBVIEWUSER <> "0.0.0.0"))',
          'Microsoft Edge WebView2 Runtime is required. Run setup.exe to install it, or install the Evergreen Runtime from https://developer.microsoft.com/microsoft-edge/webview2/ and retry.')
    )
    foreach ($condition in $conditions) {
        $expression, $description = $condition
        Invoke-MsiSql "DELETE FROM ``LaunchCondition`` WHERE ``Condition`` = '$expression'"
        Invoke-MsiSql "INSERT INTO ``LaunchCondition`` (``Condition``, ``Description``) VALUES ('$expression', '$description')"
    }

    # Check prerequisites for both interactive and silent MSI installations.
    foreach ($table in @('InstallUISequence', 'InstallExecuteSequence')) {
        Invoke-MsiSql "DELETE FROM ``$table`` WHERE ``Action`` = 'AppSearch'"
        Invoke-MsiSql "INSERT INTO ``$table`` (``Action``, ``Sequence``) VALUES ('AppSearch', 50)"
        Invoke-MsiSql "DELETE FROM ``$table`` WHERE ``Action`` = 'LaunchConditions'"
        Invoke-MsiSql "INSERT INTO ``$table`` (``Action``, ``Sequence``) VALUES ('LaunchConditions', 100)"
    }

    $welcome = "VAUPDF system requirements:`r`n`r`nWindows 11 (x64).`r`nMicrosoft Edge WebView2 Runtime.`r`n.NET 10 is included; no separate .NET installation is needed."
    $details = "Administrator approval is required.`r`nUse setup.exe to install a missing WebView2 Runtime (internet required). Local PDF viewing works offline.`r`nAllow extra disk space for WebView2 and its cache."
    foreach ($dialog in @('WelcomeForm', 'AdminWelcomeForm')) {
        Invoke-MsiSql "UPDATE ``Control`` SET ``Text`` = '$welcome' WHERE ``Dialog_`` = '$dialog' AND ``Control`` = 'WelcomeText'"
        Invoke-MsiSql "UPDATE ``Control`` SET ``Text`` = '$details' WHERE ``Dialog_`` = '$dialog' AND ``Control`` = 'CopyrightWarningText'"
    }

    $database.Commit()
    Write-Output 'VAUPDF MSI: Windows 11 x64 and WebView2 requirements configured.'
}
finally {
    [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($database)
    [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($installer)
}

$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
$visualStudio = & $vswhere -latest -products '*' -requires Microsoft.Component.MSBuild -property installationPath
if (!$visualStudio) { throw 'Visual Studio MSBuild was not found.' }
$msbuild = Join-Path $visualStudio 'MSBuild/Current/Bin/MSBuild.exe'
& $msbuild "$PSScriptRoot/Bootstrapper.proj" /nologo /verbosity:minimal "/p:MsiPath=$((Resolve-Path -LiteralPath $MsiPath).Path)"
if ($LASTEXITCODE -ne 0) { throw 'Building the WebView2 bootstrapper failed.' }
