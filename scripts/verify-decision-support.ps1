[CmdletBinding()]
param(
 [Parameter(Mandatory)][string]$Executable,[string]$ProjectPath,[string]$PackageDirectory,
 [string]$OutputRoot,[string[]]$Only,[string]$GrowthFixture,[string]$LegacyV020Fixture,[string]$LegacyV030Fixture,[string]$BudgetFixture,[string]$ExpectedVersion='0.5.0-dev',
 [ValidateRange(1,10)][double]$SmokeSeconds=2,[ValidateRange(10,60)][int]$TimeoutSeconds=45
)
$ErrorActionPreference='Stop'
$projectRoot=Split-Path -Parent $PSScriptRoot
if(-not $OutputRoot){$OutputRoot=Join-Path $projectRoot 'artifacts/decision-support-v0.4.0-dev/ui-validation'}
$runRoot=Join-Path ([IO.Path]::GetFullPath($OutputRoot)) ('run-'+[Guid]::NewGuid().ToString('N').Substring(0,8))
New-Item -ItemType Directory -Path $runRoot -Force | Out-Null
if(-not $GrowthFixture){$GrowthFixture=Join-Path $projectRoot 'artifacts/decision-support-v0.4.0-dev/fixtures/growth.sqlite3'}
if(-not $LegacyV020Fixture){$LegacyV020Fixture=Join-Path $projectRoot 'artifacts/invasion-v0.3.0-dev/legacy/battle.sqlite3'}
if(-not $LegacyV030Fixture){$LegacyV030Fixture=Join-Path $projectRoot 'artifacts/decision-support-v0.4.0-dev/fixtures/legacy-v030.sqlite3'}
if(-not $BudgetFixture){$BudgetFixture=Join-Path $projectRoot 'artifacts/decision-support-v0.4.0-dev/fixtures/budget.sqlite3'}
$inputHashes=@(@($GrowthFixture,$LegacyV020Fixture,$LegacyV030Fixture,$BudgetFixture)|Where-Object{Test-Path -LiteralPath $_}|ForEach-Object{Get-FileHash -LiteralPath $_ -Algorithm SHA256|Select-Object Path,Hash})
$cases=@(
 @{name='Guild';args=@();marker='GUILD_SCENE_READY screen=Guild'},
 @{name='ComparisonDefense';args=@('--decision-scenario','ComparisonDefense');marker='GUILD_DECISION_COMPARISON_OK mode=defense'},
 @{name='ComparisonInvasion';args=@('--decision-scenario','ComparisonInvasion');marker='GUILD_DECISION_COMPARISON_OK mode=invasion'},
 @{name='ComparisonSmall';args=@('--decision-scenario','ComparisonInvasion');marker='GUILD_DECISION_COMPARISON_OK mode=invasion';small=$true},
 @{name='Budget';args=@('--decision-scenario','Budget');marker='GUILD_DECISION_BUDGET_OK';fixture='budget'},
 @{name='Hover';args=@('--decision-scenario','Hover');marker='GUILD_DECISION_HOVER_PURE_OK'},
 @{name='Ranges';args=@('--decision-scenario','Ranges');marker='GUILD_DECISION_RANGE_TOGGLE_PURE_OK'},
 @{name='RangesSmall';args=@('--decision-scenario','Ranges');marker='GUILD_DECISION_RANGE_TOGGLE_PURE_OK';small=$true},
 @{name='RangesDetails';args=@('--decision-scenario','RangesDetails');marker='GUILD_DECISION_DETAILS_FOCUS_OK'},
 @{name='RangeOff';args=@('--decision-scenario','RangeOff');marker='GUILD_DECISION_RANGE_TOGGLE_PURE_OK'},
 @{name='Reposition';args=@('--decision-scenario','Reposition');marker='GUILD_DECISION_REPOSITION_OK'},
 @{name='InvasionPreparation';args=@('--invasion-scenario','Preparation');marker='GUILD_INVASION_UI_DEPLOY_OK'},
 @{name='InvasionBattle';args=@('--invasion-scenario','Battle');marker='GUILD_INVASION_SCENARIO_READY scenario=Battle screen=Battle'},
 @{name='InvasionReopen';args=@('--invasion-scenario','Reopen');marker='GUILD_INVASION_REOPEN_OK';reuse='InvasionBattle'},
 @{name='InvasionVictory';args=@('--invasion-scenario','Victory');marker='GUILD_INVASION_VICTORY_OK'},
 @{name='InvasionRetry';args=@('--invasion-scenario','DefeatRetry');marker='GUILD_INVASION_RETRY_OK'},
 @{name='InvasionReturn';args=@('--invasion-scenario','ReturnGuild');marker='GUILD_INVASION_RETURN_OK'},
 @{name='Growth';args=@('--decision-scenario','Growth');marker='GUILD_DECISION_GROWTH_PREVIEW_OK';fixture='growth'},
 @{name='GrowthChoose';args=@('--decision-scenario','GrowthChoose');marker='GUILD_DECISION_GROWTH_BOUND_OK';fixture='growth'},
 @{name='GrowthReopen';args=@();marker='GUILD_SCENE_READY screen=RankEvent';reuse='GrowthChoose'},
 @{name='DefenseBattle';args=@('--smoke-battle');marker='GUILD_BATTLE_SMOKE_READY screen=Battle'},
 @{name='DefenseReopen';args=@();marker='GUILD_SCENE_READY screen=Battle';reuse='DefenseBattle'},
 @{name='DefenseVictory';args=@('--smoke-screen','Victory');marker='GUILD_SCREEN_SCENARIO_READY screen=Victory'},
 @{name='DefenseVictoryReturn';args=@('--smoke-screen','Guild');marker='GUILD_SCREEN_SCENARIO_READY screen=Guild'},
 @{name='DefenseDefeatRetry';args=@('--decision-scenario','DefenseDefeatRetry');marker='GUILD_DECISION_DEFENSE_RETRY_OK'},
 @{name='DefenseDefeatReturn';args=@('--decision-scenario','DefenseDefeatReturn');marker='GUILD_DECISION_DEFENSE_RETURN_OK'},
 @{name='LegacyV020Victory';args=@('--smoke-screen','Victory');marker='GUILD_SCREEN_SCENARIO_READY screen=Victory';fixture='v020'},
 @{name='LegacyV020Preparation';args=@('--smoke-screen','Preparation');marker='GUILD_SCREEN_SCENARIO_READY screen=Preparation';fixture='v020'},
 @{name='LegacyV030Victory';args=@('--smoke-screen','Victory');marker='GUILD_SCREEN_SCENARIO_READY screen=Victory';fixture='v030'},
 @{name='LegacyV030Preparation';args=@('--smoke-screen','Preparation');marker='GUILD_SCREEN_SCENARIO_READY screen=Preparation';fixture='v030'},
 @{name='LongName';args=@('--smoke-rename','アルフォンス・フォン・シュタインベルク');marker='GUILD_RENAME_UI_OK'},
 @{name='LongNameReopen';args=@('--smoke-rename','アルフォンス・フォン・シュタインベルク');marker='GUILD_RENAME_REOPEN_OK';reuse='LongName'}
)
if($Only){$cases=@($cases|Where-Object{$_.name -in $Only})}
if($cases.Count -eq 0){throw 'No matching verification scenarios.'}
if($Only){foreach($name in $Only){if($name -notin $cases.name){throw ('Unknown verification scenario: '+$name)}}}
$results=@()
$savedPath=$env:PATH;$savedAppData=$env:APPDATA;$savedLocal=$env:LOCALAPPDATA
$savedRoots=@(Get-ChildItem Env:|Where-Object Name -Like 'DOTNET_ROOT*')
$source=-not [string]::IsNullOrEmpty($ProjectPath)
if(-not $source -and [string]::IsNullOrEmpty($PackageDirectory)){throw 'PackageDirectory is required for executable verification.'}
$recordPath=Join-Path $runRoot 'verification.json'
function Save-Results{
 $report=[ordered]@{runRoot=$runRoot;source=$source;executable=$Executable;packageDirectory=$PackageDirectory;expectedVersion=$ExpectedVersion;usesOnlyTestDatabases=$true;expectedCases=$cases.Count;completed=($results.Count -eq $cases.Count);cases=$results;allPassed=($results.Count -eq $cases.Count -and @($results|Where-Object{-not $_.passed}).Count -eq 0);fixtureHashes=$inputHashes;visualReview='Pending inspection of this run PNGs'}
 $report|ConvertTo-Json -Depth 9|Set-Content -LiteralPath $recordPath -Encoding utf8
 $report|ConvertTo-Json -Depth 9|Set-Content -LiteralPath (Join-Path $OutputRoot 'verification.json') -Encoding utf8
}
try{
 if(-not $source){
  foreach($entry in $savedRoots){Remove-Item -LiteralPath ('Env:'+$entry.Name)}
  $env:PATH='C:\Windows\System32;C:\Windows;C:\Windows\System32\Wbem'
 }
 $env:APPDATA=Join-Path $runRoot 'isolated-appdata';$env:LOCALAPPDATA=Join-Path $runRoot 'isolated-localappdata'
 foreach($case in $cases){
  $prefix=Join-Path $runRoot $case.name
  $db=if($case.reuse){Join-Path $runRoot ($case.reuse+'.sqlite3')}else{$prefix+'.sqlite3'}
  if($case.fixture){
   $fixture=switch($case.fixture){'growth'{$GrowthFixture};'v020'{$LegacyV020Fixture};'v030'{$LegacyV030Fixture};'budget'{$BudgetFixture}}
   Copy-Item -LiteralPath $fixture -Destination $db
  }
  $arguments=@()
  if($source){$arguments+=@('--path',$ProjectPath)}
  if($case.small){$arguments+=@('--resolution','1000x640')}
  $arguments+=@('--','--smoke-test',$SmokeSeconds.ToString([Globalization.CultureInfo]::InvariantCulture),'--save-path',$db,'--capture',($prefix+'.png'))+$case.args
  $workingDirectory=if($source){$ProjectPath}else{$PackageDirectory}
  $process=Start-Process -FilePath $Executable -ArgumentList $arguments -WorkingDirectory $workingDirectory -WindowStyle Hidden -RedirectStandardOutput ($prefix+'.stdout.log') -RedirectStandardError ($prefix+'.stderr.log') -PassThru
  $completed=$process.WaitForExit($TimeoutSeconds*1000)
  if(-not $completed){Stop-Process -Id $process.Id;$code=-1}else{$process.Refresh();$code=$process.ExitCode}
  $stdout=[string](Get-Content -LiteralPath ($prefix+'.stdout.log') -Raw)
  $stderr=[string](Get-Content -LiteralPath ($prefix+'.stderr.log') -Raw)
  $metadata=if(Test-Path -LiteralPath ($prefix+'.capture.json')){Get-Content -LiteralPath ($prefix+'.capture.json') -Raw|ConvertFrom-Json}else{$null}
  $errors=($stdout+[Environment]::NewLine+$stderr) -match 'ERROR:|Exception|SCENARIO_FAILED'
  $backups=@(Get-ChildItem -LiteralPath $runRoot -Filter ($case.name+'.sqlite3.before-documents-v*-*.backup') -File|Select-Object -ExpandProperty FullName)
  $pass=$completed -and $code -eq 0 -and -not $errors -and $stdout.Contains($case.marker) -and $metadata.version -eq $ExpectedVersion -and (Test-Path -LiteralPath ($prefix+'.png'))
  if($metadata.attackRadius -and $metadata.rangeLegend){
   $radius=([double]$metadata.attackRadius).ToString('0.##',[Globalization.CultureInfo]::InvariantCulture)
   $pass=$pass -and $metadata.rangeLegend.Contains('通常射程'+$radius)
  }
  if($case.name -like 'Legacy*Preparation'){$pass=$pass -and $metadata.legacyDefense -eq $true -and $metadata.rangeLegend.Contains('旧判定')}
  if($case.name -like 'LegacyV020*'){$pass=$pass -and $backups.Count -gt 0}
  $results+=[pscustomobject]@{name=$case.name;passed=[bool]$pass;exitCode=$code;hasError=[bool]$errors;expectedMarker=$case.marker;markerPresent=$stdout.Contains($case.marker);metadata=$metadata;capture=$prefix+'.png';database=$db;stdout=$prefix+'.stdout.log';stderr=$prefix+'.stderr.log';sdkRootAbsent=(@(Get-ChildItem Env:|Where-Object Name -Like 'DOTNET_ROOT*').Count -eq 0);migrationBackups=$backups}
  Save-Results
  Write-Output ($results[-1]|Select-Object name,passed,exitCode,markerPresent|ConvertTo-Json -Compress)
 }
}finally{
 $env:PATH=$savedPath;$env:APPDATA=$savedAppData;$env:LOCALAPPDATA=$savedLocal
 foreach($entry in $savedRoots){Set-Item -LiteralPath ('Env:'+$entry.Name) -Value $entry.Value}
}
foreach($fixture in $inputHashes){if((Get-FileHash -LiteralPath $fixture.Path -Algorithm SHA256).Hash -ne $fixture.Hash){throw 'Input fixture was changed.'}}
Save-Results
Write-Output ('VERIFICATION_REPORT='+$recordPath)
if(@($results|Where-Object{-not $_.passed}).Count -gt 0){throw 'One or more native application scenarios failed.'}
