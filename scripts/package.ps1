[CmdletBinding()]
param(
    [string]$GodotPath = 'godot',
    [string]$DotnetPath = 'dotnet',
    [string]$Version = '0.5.0-dev',
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
$releaseDocuments = @('user_guide.md', 'technical_specification.md', 'verification.md', 'implementation_contract.md')
if ($Version -like '0.5.0*') {
    $releaseDocuments += @('guild_economy_v0.5.0-dev.md', 'guild_economy_contract_v0.5.0-dev.md', 'guild_economy_verification_v0.5.0-dev.md', 'release_notes_v0.5.0-dev.md', 'display_text_catalog.md')
    $releaseDocuments += @('player_decision_support_v0.4.0.md', 'player_decision_support_verification_v0.4.0.md', 'release_contract_v0.4.0.md', 'release_notes_v0.4.0.md', 'ui_layout_contract_v0.4.0-dev.md', 'ui_layout_verification_v0.4.0-dev.md', 'ui_feedback_contract_v0.4.0-dev.md', 'ui_feedback_verification_v0.4.0-dev.md')
} elseif ($Version -like '0.4.0*') {
    $releaseDocuments += @('player_decision_support_v0.4.0.md', 'player_decision_support_verification_v0.4.0.md', 'release_contract_v0.4.0.md', 'release_notes_v0.4.0.md')
    $releaseDocuments += @('ui_layout_contract_v0.4.0-dev.md', 'ui_layout_verification_v0.4.0-dev.md')
    $releaseDocuments += @('ui_feedback_contract_v0.4.0-dev.md', 'ui_feedback_verification_v0.4.0-dev.md', 'display_text_catalog.md')
} elseif ($Version -like '0.3.0*') {
    $releaseDocuments += @('invasion_mode_spec_v0.3.0_draft.md', 'invasion_verification_v0.3.0.md')
    if ($Version -notlike '*dev*') {
        $releaseDocuments += @('release_contract_v0.3.0.md', 'release_notes_v0.3.0.md')
    }
} else {
    $releaseDocuments += @('release_contract_v0.2.0.md', 'release_notes_v0.2.0.md')
}
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
if ($Version -like '0.5.0*') {
    $economyVerification = Join-Path $projectRoot 'docs\guild_economy_verification_v0.5.0-dev.md'
    if ((Get-Content -LiteralPath $economyVerification -Raw) -notmatch '(?m)^visual result: passed\r?$') {
        throw '派遣・納品の視覚検証がpassedではありません。今回の検証記録を確認してください。'
    }
} elseif ($Version -like '0.4.0*') {
    $qaPath = Join-Path $projectRoot 'design-qa.md'
    if (-not (Test-Path -LiteralPath $qaPath) -or (Get-Content -LiteralPath $qaPath -Raw) -notmatch '(?m)^final result: passed\r?$') {
        throw 'UI改修の視覚QAがpassedではありません。design-qa.mdを確認してください。'
    }
    Copy-Item -LiteralPath $qaPath -Destination (Join-Path $packageDirectory 'design-qa.md')
}
$packageDocs = Join-Path $packageDirectory 'docs'
New-Item -ItemType Directory -Path $packageDocs -Force | Out-Null
foreach ($releaseDocument in $releaseDocuments) {
    Copy-Item -LiteralPath (Join-Path (Join-Path $projectRoot 'docs') $releaseDocument) -Destination (Join-Path $packageDocs $releaseDocument)
}
if ($Version -like '0.4.0*' -or $Version -like '0.5.0*') {
    $textCatalog = Join-Path $projectRoot 'data\text\ja'
    $packageText = Join-Path $packageDirectory 'data\text'
    New-Item -ItemType Directory -Path $packageText -Force | Out-Null
    Copy-Item -LiteralPath $textCatalog -Destination (Join-Path $packageText 'ja') -Recurse
}
if ($Version -like '0.5.0*') {
    Copy-Item -LiteralPath (Join-Path $projectRoot 'data\guild-economy.json') -Destination (Join-Path $packageDirectory 'data\guild-economy.json')
}
# Keep links to the small set of packaged guides local. References to design
# catalogues that remain in the repository point to this release's source tag.
$sourceTag = if ($Version -like '*dev*') { 'main' } else { 'v' + $Version }
$packagedGuides = @((Get-Item -LiteralPath (Join-Path $packageDirectory 'README.md'))) + @(Get-ChildItem -LiteralPath $packageDocs -File -Filter '*.md')
foreach ($guide in $packagedGuides) {
    $sourceDocument = Join-Path $projectRoot ([IO.Path]::GetRelativePath($packageDirectory, $guide.FullName))
    $guideText = Get-Content -LiteralPath $guide.FullName -Raw
    $resolvedGuide = [regex]::Replace($guideText, '\]\(([^)\r\n]+)\)', {
        param($link)
        $target = $link.Groups[1].Value
        if ($target -match '^([a-zA-Z][a-zA-Z0-9+.-]*:|#|/)') { return $link.Value }
        $parts = $target -split '#', 2
        $packagedTarget = [IO.Path]::GetFullPath((Join-Path $guide.DirectoryName $parts[0]))
        if (Test-Path -LiteralPath $packagedTarget) { return $link.Value }
        $sourceTarget = [IO.Path]::GetFullPath((Join-Path (Split-Path -Parent $sourceDocument) $parts[0]))
        if (-not $sourceTarget.StartsWith($projectRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or
            -not (Test-Path -LiteralPath $sourceTarget)) { return $link.Value }
        $repositoryPath = [IO.Path]::GetRelativePath($projectRoot, $sourceTarget)
        $urlPath = (($repositoryPath -split '[/\\]' | ForEach-Object { [Uri]::EscapeDataString($_) }) -join '/')
        $fragment = if ($parts.Count -gt 1) { '#' + $parts[1] } else { '' }
        return "](https://github.com/Tao-pyth/the-conductor-of-the-guild/blob/$sourceTag/$urlPath$fragment)"
    })
    Set-Content -LiteralPath $guide.FullName -Value $resolvedGuide -Encoding utf8 -NoNewline
}
Copy-Item -LiteralPath $thirdPartySource -Destination (Join-Path $packageDirectory 'third_party') -Recurse
Compress-Archive -LiteralPath $packageDirectory -DestinationPath $archivePath -Force
$archiveHash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
($archiveHash + '  ' + [System.IO.Path]::GetFileName($archivePath)) |
    Set-Content -LiteralPath ($archivePath + '.sha256') -Encoding ascii
Write-Output ('PACKAGE_DIRECTORY=' + $packageDirectory)
Write-Output ('PACKAGE_ZIP=' + $archivePath)
Write-Output ('PACKAGE_SHA256=' + $archiveHash)
