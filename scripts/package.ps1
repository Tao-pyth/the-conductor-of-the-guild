[CmdletBinding()]
param(
    [string]$GodotPath = 'godot',
    [string]$DotnetPath = 'dotnet',
    [string]$Version = '0.2.0',
    [string]$OutputDirectory,
    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'
if ($Version -notmatch '^[0-9A-Za-z._-]+$') { throw 'Versionには英数字・ピリオド・ハイフン・アンダースコアを使用してください。' }
$resolvedDotnet = (Get-Command $DotnetPath -ErrorAction Stop).Source
if ($resolvedDotnet) {
    $sdkDirectory = Split-Path -Parent $resolvedDotnet
    $env:DOTNET_ROOT = $sdkDirectory
    $env:PATH = $sdkDirectory + [System.IO.Path]::PathSeparator + $env:PATH
}
$projectRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$gameDirectory = Join-Path $projectRoot 'game'
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $projectRoot 'artifacts' }
$outputRoot = [System.IO.Path]::GetFullPath($OutputDirectory)
$stagingDirectory = Join-Path $outputRoot ('staging\' + [Guid]::NewGuid().ToString('N'))
$packageDirectory = Join-Path $stagingDirectory 'TheConductorOfTheGuild'
$archivePath = Join-Path $outputRoot ('TheConductorOfTheGuild-' + $Version + '-windows-x64.zip')
New-Item -ItemType Directory -Path $packageDirectory -Force | Out-Null

$projectSettings = Get-Content -LiteralPath (Join-Path $gameDirectory 'project.godot') -Raw
if ($projectSettings -notmatch ('(?m)^config/version="' + [regex]::Escape($Version) + '"\r?$')) {
    throw 'パッケージ名の版と project.godot の版が一致しません。版情報を更新してから実行してください。'
}
$releaseDocuments = @(
    'user_guide.md', 'technical_specification.md', 'verification.md',
    'release_contract_v0.2.0.md', 'implementation_contract.md', 'release_notes_v0.2.0.md'
)
foreach ($releaseDocument in $releaseDocuments) {
    if (-not (Test-Path -LiteralPath (Join-Path (Join-Path $projectRoot 'docs') $releaseDocument))) {
        throw ('配布用文書がありません: docs/' + $releaseDocument)
    }
}
$thirdPartySource = Join-Path $projectRoot 'third_party'
$requiredNotices = @(
    'README.md', 'sources.json', 'godot/LICENSE.txt', 'godot/COPYRIGHT.txt',
    'dotnet/LICENSE.TXT', 'dotnet/THIRD-PARTY-NOTICES.TXT',
    'microsoft-data-sqlite/LICENSE.txt', 'sqlitepclraw/LICENSE.TXT',
    'sqlitepclraw/NOTICE.TXT', 'sqlitepclraw/PACKAGE-NOTICES.txt',
    'sqlite/LICENSE.md', 'pcg/LICENSE.txt', 'pcg/NOTICE.txt'
)
foreach ($requiredNotice in $requiredNotices) {
    if (-not (Test-Path -LiteralPath (Join-Path $thirdPartySource $requiredNotice))) {
        throw ('配布用の出典・ライセンス文書がありません: third_party/' + $requiredNotice)
    }
}
$noticeManifest = Get-Content -LiteralPath (Join-Path $thirdPartySource 'sources.json') -Raw | ConvertFrom-Json
foreach ($noticeSource in $noticeManifest.sources) {
    if ((Get-FileHash -LiteralPath (Join-Path $thirdPartySource $noticeSource.file) -Algorithm SHA256).Hash -ne $noticeSource.sha256) {
        throw ('提供元のライセンス原文ハッシュが一致しません: ' + $noticeSource.file)
    }
}

$contentSource = Join-Path $projectRoot 'data\tech-content.json'
if (-not (Test-Path -LiteralPath $contentSource)) { throw '技術検証データ data/tech-content.json がありません。' }
New-Item -ItemType Directory -Path (Join-Path $gameDirectory 'content') -Force | Out-Null
Copy-Item -LiteralPath $contentSource -Destination (Join-Path $gameDirectory 'content\tech-content.json') -Force

if (-not $SkipBuild) {
    & $DotnetPath build (Join-Path $gameDirectory 'Guild.Game.csproj') --configuration Release
    if ($LASTEXITCODE -ne 0) { throw 'ゲームのビルドに失敗しました。' }
}

$executablePath = Join-Path $packageDirectory 'TheConductorOfTheGuild.exe'
$exportOutput = @(& $GodotPath --headless --path $gameDirectory --export-release 'Windows Desktop' $executablePath 2>&1)
$exportExitCode = $LASTEXITCODE
$exportText = @($exportOutput | ForEach-Object { $_.ToString() })
$exportText | Set-Content -LiteralPath (Join-Path $stagingDirectory 'export.log') -Encoding utf8
Write-Output $exportText
if ($exportExitCode -ne 0 -or ($exportText -match 'ERROR:') -or -not (Test-Path -LiteralPath $executablePath)) {
    throw 'Windowsへの書き出しに失敗しました。Godot 4.7.2 .NET版と対応するExport Templatesを確認してください。'
}

$readmePath = Join-Path $projectRoot 'README.md'
Copy-Item -LiteralPath $readmePath -Destination (Join-Path $packageDirectory 'README.md')
Copy-Item -LiteralPath (Join-Path $projectRoot 'CHANGELOG.md') -Destination (Join-Path $packageDirectory 'CHANGELOG.md')
$packageDocs = Join-Path $packageDirectory 'docs'
New-Item -ItemType Directory -Path $packageDocs -Force | Out-Null
foreach ($releaseDocument in $releaseDocuments) {
    Copy-Item -LiteralPath (Join-Path (Join-Path $projectRoot 'docs') $releaseDocument) -Destination (Join-Path $packageDocs $releaseDocument)
}
# Candidate catalogues and the design workbook remain in the repository. Their
# reference from the small technical guide points to the versioned source page.
$packagedTechnicalSpec = Join-Path $packageDocs 'technical_specification.md'
$packagedSpecText = Get-Content -LiteralPath $packagedTechnicalSpec -Raw
$packagedSpecText.Replace('](specification_status_v0.1.0.md)', '](https://github.com/Tao-pyth/the-conductor-of-the-guild/blob/v0.2.0/docs/specification_status_v0.1.0.md)') |
    Set-Content -LiteralPath $packagedTechnicalSpec -Encoding utf8 -NoNewline
Copy-Item -LiteralPath $thirdPartySource -Destination (Join-Path $packageDirectory 'third_party') -Recurse
Compress-Archive -LiteralPath $packageDirectory -DestinationPath $archivePath -Force
Write-Output ('PACKAGE_DIRECTORY=' + $packageDirectory)
Write-Output ('PACKAGE_ZIP=' + $archivePath)
