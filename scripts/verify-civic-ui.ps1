[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Executable,
    [string]$ProjectPath,
    [string]$PackageDirectory,
    [string]$OutputRoot,
    [string[]]$Only,
    [string]$ExpectedVersion = '0.5.0-dev',
    [ValidateRange(1,10)][double]$SmokeSeconds = 2,
    [ValidateRange(10,90)][int]$TimeoutSeconds = 60
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
if (-not $OutputRoot) { $OutputRoot = Join-Path $projectRoot 'artifacts/guild-ui-v0.4.0-dev/ui-validation' }
$outputDirectory = [IO.Path]::GetFullPath($OutputRoot)
$runRoot = Join-Path $outputDirectory ('run-' + [Guid]::NewGuid().ToString('N').Substring(0,8))
New-Item -ItemType Directory -Path $runRoot -Force | Out-Null
$source = -not [string]::IsNullOrWhiteSpace($ProjectPath)
if (-not $source -and [string]::IsNullOrWhiteSpace($PackageDirectory)) { throw 'PackageDirectory is required for executable verification.' }
$workingDirectory = if ($source) { [IO.Path]::GetFullPath($ProjectPath) } else { [IO.Path]::GetFullPath($PackageDirectory) }
$executablePath = (Get-Command $Executable -ErrorAction Stop).Source

$cases = @(
    @{ name='DefenseFhd'; scenario='CivicDefense'; resolution='1920x1080' },
    @{ name='DefenseHd'; scenario='CivicDefense'; resolution='1280x720' },
    @{ name='DefenseMinimum'; scenario='CivicDefense'; resolution='1000x640' },
    @{ name='InvasionFhd'; scenario='CivicInvasion'; resolution='1920x1080' },
    @{ name='InvasionHd'; scenario='CivicInvasion'; resolution='1280x720' },
    @{ name='InvasionMinimum'; scenario='CivicInvasion'; resolution='1000x640' },
    @{ name='DetailsHd'; scenario='CivicDetails'; resolution='1280x720' },
    @{ name='DisplayHd'; scenario='CivicDisplay'; resolution='1280x720'; display=$true },
    @{ name='FullscreenHd'; scenario='CivicFullscreen'; resolution='1280x720'; display=$true; fullscreen=$true },
    @{ name='StampDefense'; scenario='CivicStampDefense'; resolution='1280x720'; stamp=$true },
    @{ name='StampInvasion'; scenario='CivicStampInvasion'; resolution='1280x720'; stamp=$true },
    @{ name='ActionsDefense'; scenario='CivicActionsDefense'; resolution='1280x720' },
    @{ name='ActionsInvasion'; scenario='CivicActionsInvasion'; resolution='1280x720' },
    @{ name='LongNameFhd'; scenario='CivicLongName'; resolution='1920x1080' },
    @{ name='LongNameMinimum'; scenario='CivicLongName'; resolution='1000x640' },
    @{ name='ManyFhd'; scenario='CivicMany'; resolution='1920x1080'; many=$true },
    @{ name='ManyMinimum'; scenario='CivicMany'; resolution='1000x640'; many=$true }
)
if ($Only) {
    $unknown = @($Only | Where-Object { $_ -notin $cases.name })
    if ($unknown.Count) { throw ('Unknown civic verification scenario: ' + ($unknown -join ', ')) }
    $cases = @($cases | Where-Object { $_.name -in $Only })
}
if (-not $cases.Count) { throw 'No matching civic UI scenarios.' }

$results = @()
$recordPath = Join-Path $runRoot 'verification.json'
$savedPath = $env:PATH
$savedAppData = $env:APPDATA
$savedLocalAppData = $env:LOCALAPPDATA
$savedRoots = @(Get-ChildItem Env: | Where-Object Name -Like 'DOTNET_ROOT*')
function Save-CivicResults {
    $report = [ordered]@{
        runRoot=$runRoot; source=$source; executable=$executablePath
        packageDirectory=$PackageDirectory; expectedVersion=$ExpectedVersion
        usesOnlyTestDatabases=$true; expectedCases=$cases.Count
        completed=($results.Count -eq $cases.Count)
        allPassed=($results.Count -eq $cases.Count -and @($results | Where-Object { -not $_.passed }).Count -eq 0)
        cases=$results
        syntheticFixturePolicy='CivicMany seeds only isolated test funding, then uses real ExpandRoster/Recruit commands to reach 50. It is a layout boundary, not balance evidence.'
        visualReview='Pending human/agent inspection of this run PNGs; successful input and bounds checks alone do not prove visual fidelity.'
    }
    $json = $report | ConvertTo-Json -Depth 20
    $json | Set-Content -LiteralPath $recordPath -Encoding utf8
    $json | Set-Content -LiteralPath (Join-Path $outputDirectory 'verification.json') -Encoding utf8
}

try {
    if (-not $source) {
        foreach ($entry in $savedRoots) { Remove-Item -LiteralPath ('Env:' + $entry.Name) }
        $env:PATH = 'C:\Windows\System32;C:\Windows;C:\Windows\System32\Wbem'
    }
    $env:APPDATA = Join-Path $runRoot 'isolated-appdata'
    $env:LOCALAPPDATA = Join-Path $runRoot 'isolated-localappdata'
    foreach ($case in $cases) {
        $prefix = Join-Path $runRoot $case.name
        $database = $prefix + '.sqlite3'
        if (Test-Path -LiteralPath $database) { throw 'Civic scenarios require a new isolated database.' }
        $arguments = [Collections.Generic.List[string]]::new()
        if ($source) { $arguments.Add('--path'); $arguments.Add($workingDirectory) }
        foreach ($argument in @('--resolution', $case.resolution, '--', '--smoke-test',
            $SmokeSeconds.ToString([Globalization.CultureInfo]::InvariantCulture), '--save-path', $database,
            '--capture', ($prefix + '.png'), '--decision-scenario', $case.scenario)) {
            $arguments.Add([string]$argument)
        }
        # ArgumentList keeps arbitrary Windows paths as individual arguments.
        $startInfo = [Diagnostics.ProcessStartInfo]::new()
        $startInfo.FileName = $executablePath
        $startInfo.WorkingDirectory = $workingDirectory
        $startInfo.UseShellExecute = $false
        $startInfo.CreateNoWindow = $true
        $startInfo.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
        $startInfo.RedirectStandardOutput = $true
        $startInfo.RedirectStandardError = $true
        foreach ($argument in $arguments) { $startInfo.ArgumentList.Add($argument) }
        $process = [Diagnostics.Process]::new()
        $process.StartInfo = $startInfo
        if (-not $process.Start()) { throw 'Could not start civic UI verification process.' }
        $stdoutTask = $process.StandardOutput.ReadToEndAsync()
        $stderrTask = $process.StandardError.ReadToEndAsync()
        $completed = $process.WaitForExit($TimeoutSeconds * 1000)
        if (-not $completed) { $process.Kill($true); $process.WaitForExit() }
        $exitCode = if ($completed) { $process.ExitCode } else { -1 }
        $stdout = $stdoutTask.GetAwaiter().GetResult()
        $stderr = $stderrTask.GetAwaiter().GetResult()
        $process.Dispose()
        $stdout | Set-Content -LiteralPath ($prefix + '.stdout.log') -Encoding utf8
        $stderr | Set-Content -LiteralPath ($prefix + '.stderr.log') -Encoding utf8
        $metadata = if (Test-Path -LiteralPath ($prefix + '.capture.json')) {
            Get-Content -LiteralPath ($prefix + '.capture.json') -Raw | ConvertFrom-Json
        } else { $null }
        $marker = 'GUILD_CIVIC_LAYOUT_OK scenario=' + $case.scenario
        $failureReasons = [Collections.Generic.List[string]]::new()
        if (-not $completed -or $exitCode -ne 0) { $failureReasons.Add('Process did not complete successfully.') }
        if (($stdout + "`n" + $stderr) -match 'ERROR:|Exception|SCENARIO_FAILED') { $failureReasons.Add('Runtime error in log.') }
        if (-not $stdout.Contains($marker)) { $failureReasons.Add('Expected civic scenario marker missing.') }
        if (-not (Test-Path -LiteralPath ($prefix + '.png'))) { $failureReasons.Add('Main screenshot missing.') }
        if (-not $metadata -or $metadata.version -ne $ExpectedVersion) { $failureReasons.Add('Wrong/missing capture version.') }
        if ($metadata.viewportWidth -ne 1920 -or $metadata.viewportHeight -ne 1080) { $failureReasons.Add('Logical viewport is not Full HD.') }
        if (-not $metadata.civic.detailsVerified -or -not $metadata.civic.geometryVerified) { $failureReasons.Add('Details/focus/geometry checks not verified.') }
        $expected = $case.resolution.Split('x')
        if (-not $case.fullscreen -and ($metadata.windowWidth -ne [int]$expected[0] -or $metadata.windowHeight -ne [int]$expected[1])) {
            $failureReasons.Add('Window size does not match requested test size.')
        }
        $fullscreenEvidence = @()
        if ($case.display) {
            if (-not $metadata.civic.displayVerified -or -not $metadata.civic.maximizedRestoreVerified) { $failureReasons.Add('Button/F11/Esc or maximized-restoration state-preserving display check missing.') }
            foreach ($extension in @('.fullscreen.png', '.fullscreen.json', '.maximized-restored.png', '.maximized-restored.json')) {
                $path = $prefix + $extension
                if (-not (Test-Path -LiteralPath $path)) { $failureReasons.Add('Fullscreen evidence missing: ' + $extension) }
                else { $fullscreenEvidence += $path }
            }
            if (Test-Path -LiteralPath ($prefix + '.fullscreen.json')) {
                $full = Get-Content -LiteralPath ($prefix + '.fullscreen.json') -Raw | ConvertFrom-Json
                if ($full.civic.windowMode -notin @('Fullscreen', 'ExclusiveFullscreen')) { $failureReasons.Add('Evidence is not a fullscreen game window.') }
            }
            if (Test-Path -LiteralPath ($prefix + '.maximized-restored.json')) {
                $maximized = Get-Content -LiteralPath ($prefix + '.maximized-restored.json') -Raw | ConvertFrom-Json
                if ($maximized.civic.windowMode -ne 'Maximized') { $failureReasons.Add('Maximized game-window restoration evidence has the wrong mode.') }
            }
        }
        if ($case.fullscreen -and $metadata.civic.windowMode -notin @('Fullscreen', 'ExclusiveFullscreen')) { $failureReasons.Add('Main capture was not taken in fullscreen.') }
        if ($case.stamp -and (-not $metadata.civic.stampVerified -or $metadata.screen -ne 'Battle')) { $failureReasons.Add('Single stamp start not verified.') }
        if ($case.many -and ($metadata.civic.actors -ne 50 -or [string]::IsNullOrWhiteSpace($metadata.civic.fixture))) { $failureReasons.Add('50-member boundary/fixture provenance missing.') }
        $results += [pscustomobject]@{
            name=$case.name; scenario=$case.scenario; requestedResolution=$case.resolution
            passed=($failureReasons.Count -eq 0); failureReasons=$failureReasons.ToArray()
            exitCode=$exitCode; markerPresent=$stdout.Contains($marker); metadata=$metadata
            capture=($prefix + '.png'); database=$database; fullscreenEvidence=$fullscreenEvidence
            stdout=($prefix + '.stdout.log'); stderr=($prefix + '.stderr.log')
            sdkRootAbsent=(@(Get-ChildItem Env: | Where-Object Name -Like 'DOTNET_ROOT*').Count -eq 0)
        }
        Save-CivicResults
        Write-Output ($results[-1] | Select-Object name,passed,exitCode,failureReasons | ConvertTo-Json -Compress)
    }
} finally {
    $env:PATH = $savedPath
    $env:APPDATA = $savedAppData
    $env:LOCALAPPDATA = $savedLocalAppData
    foreach ($entry in $savedRoots) { Set-Item -LiteralPath ('Env:' + $entry.Name) -Value $entry.Value }
}
Save-CivicResults
Write-Output ('CIVIC_VERIFICATION_REPORT=' + $recordPath)
if (@($results | Where-Object { -not $_.passed }).Count) { throw 'One or more civic UI scenarios failed.' }
