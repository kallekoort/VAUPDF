param(
    [string] $MsiPath = "$PSScriptRoot/Release/VAUPDF_Setup.msi"
)

$ErrorActionPreference = 'Stop'
$installer = New-Object -ComObject WindowsInstaller.Installer
$database = $installer.OpenDatabase((Resolve-Path -LiteralPath $MsiPath).Path, 0)

function Read-MsiRows([string] $Sql) {
    $view = $database.OpenView($Sql)
    try {
        [void]$view.Execute()
        while ($record = $view.Fetch()) {
            $fieldCount = $record.GetType().InvokeMember('FieldCount', 'GetProperty', $null, $record, $null)
            $row = @(for ($index = 1; $index -le $fieldCount; $index++) {
                $record.StringData($index)
            })
            ,$row
            [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($record)
        }
    }
    finally {
        [void]$view.Close()
        [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($view)
    }
}

function Assert-Installer([bool] $Condition, [string] $Message) {
    if (!$Condition) { throw $Message }
}

try {
    $conditions = @(Read-MsiRows 'SELECT `Condition`, `Description` FROM `LaunchCondition`')
    Assert-Installer ($conditions.Count -eq 2) 'Expected exactly two launch conditions, without a .NET prerequisite.'
    foreach ($condition in $conditions) {
        Assert-Installer ($condition[0].StartsWith('Installed OR ')) 'Maintenance/uninstall must bypass prerequisites.'
    }

    $searches = @(Read-MsiRows 'SELECT `Property`, `Signature_` FROM `AppSearch`')
    Assert-Installer ($searches.Count -eq 3) 'Expected Windows build and both WebView2 registry searches.'
    $locators = @(Read-MsiRows 'SELECT `Signature_`, `Root`, `Key`, `Name`, `Type` FROM `RegLocator`')
    $windows = @($locators | Where-Object { $_[0] -eq 'VAUWINDOWSBUILD' })
    Assert-Installer ($windows.Count -eq 1 -and $windows[0][4] -eq '18') 'Windows build must use the 64-bit registry view.'
    foreach ($table in @('InstallUISequence', 'InstallExecuteSequence')) {
        $actions = @(Read-MsiRows "SELECT ``Action``, ``Sequence`` FROM ``$table``")
        $search = $actions | Where-Object { $_[0] -eq 'AppSearch' }
        $launch = $actions | Where-Object { $_[0] -eq 'LaunchConditions' }
        Assert-Installer ($search[1] -eq '50' -and $launch[1] -eq '100') "Incorrect prerequisite order in $table."
    }

    $texts = @(Read-MsiRows 'SELECT `Text` FROM `Control`') | ForEach-Object { $_[0] }
    Assert-Installer ([bool]($texts -match 'Windows 11')) 'Requirements are missing from the installer UI.'
    Assert-Installer ([bool]($texts -match '.NET 10 is included')) 'Bundled .NET information is missing.'
    Assert-Installer ([bool]($texts -match 'setup.exe')) 'WebView2 installation instructions are missing.'

    # A safe MSI session evaluates conditions without installing or changing the computer.
    $session = $installer.OpenPackage((Resolve-Path -LiteralPath $MsiPath).Path, 1)
    try {
        $cases = @(
            @('Windows 11 with machine runtime', '26100', '603', '1', '150.0.0.0', '', '', $true),
            @('Windows 11 with user runtime', '26100', '603', '1', '', '150.0.0.0', '', $true),
            @('Windows 10', '19045', '603', '1', '150.0.0.0', '', '', $false),
            @('Missing runtime', '26100', '603', '1', '', '', '', $false),
            @('Invalid runtime', '26100', '603', '1', '0.0.0.0', '', '', $false),
            @('32-bit Windows', '26100', '', '1', '150.0.0.0', '', '', $false),
            @('Windows Server', '26100', '603', '3', '150.0.0.0', '', '', $false),
            @('Maintenance without prerequisites', '', '', '', '', '', '1', $true)
        )
        $properties = @('VAUWINDOWSBUILD', 'VersionNT64', 'MsiNTProductType',
            'VAUWEBVIEWMACHINE', 'VAUWEBVIEWUSER', 'Installed')
        foreach ($case in $cases) {
            for ($index = 0; $index -lt $properties.Count; $index++) {
                [void]$session.GetType().InvokeMember('Property', 'SetProperty', $null,
                    $session, @($properties[$index], $case[$index + 1]))
            }
            $allowed = $true
            foreach ($condition in $conditions) {
                $result = $session.EvaluateCondition($condition[0])
                Assert-Installer ($result -in 0, 1) 'Invalid MSI condition syntax.'
                $allowed = $allowed -and ($result -eq 1)
            }
            Assert-Installer ($allowed -eq $case[7]) "Failed scenario: $($case[0])"
        }
    }
    finally {
        [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($session)
    }

    $setupPath = Join-Path (Split-Path -Parent $MsiPath) 'setup.exe'
    $setupBytes = [IO.File]::ReadAllBytes($setupPath)
    $setupText = [Text.Encoding]::UTF8.GetString($setupBytes) +
        [Text.Encoding]::Unicode.GetString($setupBytes) +
        [Text.Encoding]::Unicode.GetString($setupBytes, 1, $setupBytes.Length - 1)
    Assert-Installer ($setupText.Contains('Microsoft.EdgeRuntime.en')) 'WebView2 is missing from setup.exe.'
    Assert-Installer ($setupText.Contains('https://go.microsoft.com/fwlink/p/?LinkId=2124703')) 'WebView2 download must use HTTPS.'

    Write-Output 'PASS: 8 prerequisite scenarios, MSI metadata, requirements text and WebView2 bootstrapper.'
}
finally {
    [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($database)
    [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($installer)
}

