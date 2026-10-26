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
if (-not $OutputRoot) { $OutputRoot = Join-Path $projectRoot 'artifacts/guild-feedback-v0.4.0-dev/feedback-validation' }
$runRoot = Join-Path ([IO.Path]::GetFullPath($OutputRoot)) ('run-' + [Guid]::NewGuid().ToString('N').Substring(0,8))
New-Item -ItemType Directory -Path $runRoot -Force | Out-Null
$source = -not [string]::IsNullOrWhiteSpace($ProjectPath)
if (-not $source -and [string]::IsNullOrWhiteSpace($PackageDirectory)) { throw 'PackageDirectory is required for executable verification.' }
$workingDirectory = if ($source) { [IO.Path]::GetFullPath($ProjectPath) } else { [IO.Path]::GetFullPath($PackageDirectory) }
$executablePath = (Get-Command $Executable -ErrorAction Stop).Source
$cases = @(
    @{name='HomeFhd'; scenario='FeedbackHome'; resolution='1920x1080'},
    @{name='HomeHd'; scenario='FeedbackHome'; resolution='1280x720'},
    @{name='HomeMinimum'; scenario='FeedbackHome'; resolution='1000x640'},
    @{name='EquipmentFhd'; scenario='FeedbackEquipment'; resolution='1920x1080'},
    @{name='HomeManyMinimum'; scenario='FeedbackHomeMany'; resolution='1000x640'},
    @{name='LevelFhd'; scenario='FeedbackLevel'; resolution='1920x1080'},
    @{name='LevelHd'; scenario='FeedbackLevel'; resolution='1280x720'},
    @{name='LevelMinimum'; scenario='FeedbackLevel'; resolution='1000x640'},
    @{name='BlessingEmptyFhd'; scenario='FeedbackBlessing'; resolution='1920x1080'},
    @{name='BlessingOwnedFhd'; scenario='FeedbackBlessingOwned'; resolution='1920x1080'},
    @{name='BlessingFullFhd'; scenario='FeedbackBlessingFull'; resolution='1920x1080'},
    @{name='BlessingEmptyMinimum'; scenario='FeedbackBlessing'; resolution='1000x640'},
    @{name='BlessingFullMinimum'; scenario='FeedbackBlessingFull'; resolution='1000x640'},
    @{name='BlessingAcquireFhd'; scenario='FeedbackBlessingAcquire'; resolution='1920x1080'},
    @{name='BlessingUpgradeFhd'; scenario='FeedbackBlessingUpgrade'; resolution='1920x1080'},
    @{name='BlessingReplaceFhd'; scenario='FeedbackBlessingReplace'; resolution='1920x1080'},
    @{name='ResultDefenseFhd'; scenario='FeedbackResultDefense'; resolution='1920x1080'},
    @{name='ResultDefenseHd'; scenario='FeedbackResultDefense'; resolution='1280x720'},
    @{name='ResultDefenseMinimum'; scenario='FeedbackResultDefense'; resolution='1000x640'},
    @{name='ResultInvasionFhd'; scenario='FeedbackResultInvasion'; resolution='1920x1080'},
    @{name='ResultManyFhd'; scenario='FeedbackResultMany'; resolution='1920x1080'},
    @{name='ResultManyMinimum'; scenario='FeedbackResultMany'; resolution='1000x640'},
    @{name='HistoryFhd'; scenario='FeedbackHistory'; resolution='1920x1080'},
    @{name='HistoryMinimum'; scenario='FeedbackHistory'; resolution='1000x640'}
)
if ($Only) {
    $unknown = @($Only | Where-Object { $_ -notin $cases.name })
    if ($unknown.Count) { throw ('Unknown feedback case: ' + ($unknown -join ', ')) }
    $cases = @($cases | Where-Object { $_.name -in $Only })
}
$results = @()
$recordPath = Join-Path $runRoot 'verification.json'
$savedPath = $env:PATH
$savedAppData = $env:APPDATA
$savedLocalAppData = $env:LOCALAPPDATA
$savedRoots = @(Get-ChildItem Env: | Where-Object Name -Like 'DOTNET_ROOT*')
function Save-FeedbackResults {
    [ordered]@{runRoot=$runRoot; source=$source; executable=$executablePath; onlyIsolatedTestSaves=$true;
        expectedCases=$cases.Count; completed=($results.Count -eq $cases.Count);
        allPassed=($results.Count -eq $cases.Count -and @($results | Where-Object { -not $_.passed }).Count -eq 0);
        cases=$results} | ConvertTo-Json -Depth 9 | Set-Content -LiteralPath $recordPath -Encoding utf8
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
        $arguments = [Collections.Generic.List[string]]::new()
        if ($source) { $arguments.Add('--path'); $arguments.Add($workingDirectory) }
        foreach ($argument in @('--resolution', $case.resolution, '--', '--smoke-test',
            $SmokeSeconds.ToString([Globalization.CultureInfo]::InvariantCulture), '--save-path', ($prefix+'.sqlite3'),
            '--capture', ($prefix+'.png'), '--decision-scenario', $case.scenario)) { $arguments.Add([string]$argument) }
        $startInfo = [Diagnostics.ProcessStartInfo]::new()
        $startInfo.FileName = $executablePath; $startInfo.WorkingDirectory = $workingDirectory
        $startInfo.UseShellExecute = $false; $startInfo.CreateNoWindow = $true
        $startInfo.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
        $startInfo.RedirectStandardOutput = $true; $startInfo.RedirectStandardError = $true
        foreach ($argument in $arguments) { $startInfo.ArgumentList.Add($argument) }
        $process = [Diagnostics.Process]::new(); $process.StartInfo = $startInfo
        if (-not $process.Start()) { throw 'Could not start feedback UI check.' }
        $stdoutTask = $process.StandardOutput.ReadToEndAsync(); $stderrTask = $process.StandardError.ReadToEndAsync()
        $completed = $process.WaitForExit($TimeoutSeconds * 1000)
        if (-not $completed) { $process.Kill($true); $process.WaitForExit() }
        $code = if ($completed) { $process.ExitCode } else { -1 }
        $stdout = $stdoutTask.GetAwaiter().GetResult(); $stderr = $stderrTask.GetAwaiter().GetResult(); $process.Dispose()
        $stdout | Set-Content -LiteralPath ($prefix+'.stdout.log') -Encoding utf8
        $stderr | Set-Content -LiteralPath ($prefix+'.stderr.log') -Encoding utf8
        $metadata = if (Test-Path -LiteralPath ($prefix+'.capture.json')) { Get-Content -LiteralPath ($prefix+'.capture.json') -Raw | ConvertFrom-Json } else { $null }
        $marker = 'GUILD_FEEDBACK_OK scenario='+$case.scenario
        $expected = $case.resolution.Split('x')
        $passed = $completed -and $code -eq 0 -and ($stdout+$stderr) -notmatch 'ERROR:|Exception|SCENARIO_FAILED' -and
            $stdout.Contains($marker) -and (Test-Path -LiteralPath ($prefix+'.png')) -and $metadata.version -eq $ExpectedVersion -and
            $metadata.windowWidth -eq [int]$expected[0] -and $metadata.windowHeight -eq [int]$expected[1]
        $results += [pscustomobject]@{name=$case.name;scenario=$case.scenario;passed=[bool]$passed;exitCode=$code;
            markerPresent=$stdout.Contains($marker);capture=($prefix+'.png');metadata=$metadata;
            sdkRootAbsent=(@(Get-ChildItem Env: | Where-Object Name -Like 'DOTNET_ROOT*').Count -eq 0);
            stdout=($prefix+'.stdout.log');stderr=($prefix+'.stderr.log')}
        Save-FeedbackResults
        Write-Output ($results[-1] | Select-Object name,passed,exitCode,markerPresent | ConvertTo-Json -Compress)
    }
} finally {
    $env:PATH=$savedPath; $env:APPDATA=$savedAppData; $env:LOCALAPPDATA=$savedLocalAppData
    foreach ($entry in $savedRoots) { Set-Item -LiteralPath ('Env:'+$entry.Name) -Value $entry.Value }
}
Save-FeedbackResults
Write-Output ('FEEDBACK_VERIFICATION_REPORT='+$recordPath)
if (@($results | Where-Object { -not $_.passed }).Count) { throw 'One or more feedback UI checks failed.' }
