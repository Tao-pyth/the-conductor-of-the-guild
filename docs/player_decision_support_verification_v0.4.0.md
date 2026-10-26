# v0.4.0-dev 判断材料・防衛判定の検証記録

更新日：2026-10-25。新規防衛の射程判定、成長対象、編成/配置判断材料をローカル実装し、検証した。正式baselineはv0.3.0。今回の契約は0.4.0-devのWindows配布候補までで、Commit/Push/Tag/GitHub Release/外部公開は行わない。

## 自動テストとビルド

- **全192件成功、失敗0、スキップ0**。従来123件と、追加69ケース。
- TRX: `artifacts/decision-support-v0.4.0-dev/test-results/release-validation.trx`。
- Release/ExportReleaseビルド：警告0、エラー0。ログは同ディレクトリの `release-build.log` / `export-release-build.log`。
- tests/gameの `--locked-mode` 復元成功。既存依存の版を更新せず、プロジェクト参照の版だけ0.4.0-devへ整合。
- 最初の190件の試行では、新規旧CommandIdテストの手書きfixtureが公開Windows版のCRLFを再現しておらず1件失敗した。公開b7da687 DLLの実payload/hashを固定した試験へ修正し、再送互換と192件全体を再確認。製品のコマンドhash式を変えて通したものではない。

```powershell
dotnet restore tests/Guild.Domain.Tests/Guild.Domain.Tests.csproj --locked-mode
dotnet test tests/Guild.Domain.Tests/Guild.Domain.Tests.csproj --configuration Release --no-restore
dotnet restore game/Guild.Game.csproj --locked-mode
dotnet build game/Guild.Game.csproj --configuration Release --no-restore
dotnet build game/Guild.Game.csproj --configuration ExportRelease --no-restore
```

## Windows実体の確認

検証した候補ZIPを新規展開し、DOTNET_ROOT系環境変数を外し、PATHをWindows標準だけにしたプロセスで**32シナリオ成功**。終了コード0、ERROR/Exceptionなし、期待マーカー・0.4.0-dev表示・新規PNG/寸法メタデータを確認した。

記録: `artifacts/decision-support-v0.4.0-dev/package-validation/run-f6f33bc2/verification.json`。

| 分類 | 実行した確認 |
| --- | --- |
| 拠点/比較 | Guild、防衛比較、侵攻比較、比較小画面、合計1500/開始1000の合成Rank90編成でも出撃可能 |
| 配置 | 無効マス/候補hover不変、範囲ON/OFF、小画面、詳細スクロールとフォーカス、無料再配置とOFF時無操作 |
| 侵攻 | 準備、戦闘、戦闘保存再開、3区画勝利、敗北/再挑戦、帰還 |
| 成長 | 対象者と別人を閲覧した事前表示、表示対象への種別確定、同じ途中状態の再開 |
| 防衛 | 新規戦闘、再開、勝利、勝利帰還、敗北から再挑戦/帰還 |
| 旧保存 | v0.2.0形式1のbackup移行後の準備/勝利、v0.3.0の旧判定準備/勝利 |
| 名前 | 日本語名のキー入力/保存と別プロセス再開 |

UIの操作は実ボタン・map入力・Godot内キーイベントを通す検証用シナリオ。自然な初見ユーザーの評価/周回時間を測ったものではない。予算超過のデータは外部試験DBのRankだけを合成した境界試験で、高Rankの難易度評価ではない。

通常はwindow/viewport/PNGが1280×720。最小設定はwindow1000×640、論理viewport1280×720、PNG1000×562。実寸を `*.capture.json` へ記録した。主要操作・対象・数値・凡例はスクロール/フォーカスで到達し、新規画像を視認した。旧準備には「この進行中マッチは旧判定。修正は次の新規防衛から適用」と表示する。

Source GUIでも先行25条件と追加7回の確認を行った。凡例の「4.5」が「1.5」に見えるという読取上の疑義は、同じPNGのhash・選択者の実半径・実際のラベル文字列の一致から、実在する不一致とは認定しなかった。最新のメタデータでは選択ユイ/半径4.5/凡例4.5が一致し、検証スクリプトもこの一致を検査する。

## Acceptance Criteria

| ID | 状態 | 証拠 |
| --- | --- | --- |
| V04-AC01 | 確認済 | DefenseRangeV2Testsのx8/x9接線、境界±1/1,000,000。射程不変 |
| V04-AC02 | 確認済 | 同テストの短区間/途中交差/角/複数/除外/気絶/硬直/対象離脱/到達優先 |
| V04-AC03 | 確認済 | v0.2/0.3公開DLL由来golden、DefenseV2SessionTests、既存倍速試験、実体の新旧再開/Retry |
| V04-AC04 | 確認済 | GrowthEventPreviewTestsの混在/未知ルール拒否・元DB不変、既存形式移行回帰、旧実体backup |
| V04-AC05 | 確認済 | Growth/GrowthChooseの対象事前カードと確定後の同一対象 |
| V04-AC06 | 確認済 | EventId/ActorId不一致・古い表示・同一人別イベント・履歴・再送の専用テスト |
| V04-AC07 | 確認済 | DecisionInformationTestsの実出撃比較、HP180、実枠/倍率/固定定義/上限/費用/モード差 |
| V04-AC08 | 確認済 | ComparisonDefense/Invasion、合成Budgetの参考1500と出撃後資源1000 |
| V04-AC09 | 確認済 | Ranges/Hover/RangeOff/Reposition/Small/Detailsと中心・回復円の専用試験。旧準備注意文 |
| V04-AC10 | 確認済 | 次Wave/区画の数/HP/攻撃有無/種類/時限の専用試験と実画面 |
| V04-AC11 | 確認済 | 100回照会・後続抽選・checkpoint/RNG不変、UIのhover/比較/切替fingerprint不変 |
| V04-AC12 | 確認済 | SDK不要32シナリオのログ/PNG/実寸。両モード/旧保存/名前/フォーカス |
| V04-AC13 | 確認済 | 全192成功、Release/ExportRelease警告・エラー0 |
| V04-AC14 | 確認済 | 同梱10文書、notice10原文hash、DB未同梱、Windows数値版0.4.0.0、ZIP/SHA256 |

## 最終配布候補

`artifacts/TheConductorOfTheGuild-0.4.0-dev-windows-x64.zip` と対応する `.sha256` を生成する。上記候補の検証結果を本書へ反映して再包装し、実行関連214ファイルを候補のmanifestと照合したうえで最終実体の起動/表示/保存を再確認する。同一でなければ、最終実体でも32シナリオを実行する。

最終のサイズ/hash/ファイル比較/展開先は `artifacts/decision-support-v0.4.0-dev/final-manifest.json`、実行記録は `final-validation/verification.json`、候補/最終の実行ファイルmanifestは各展開先の `runtime-manifest.json` を正本とする。自己参照を避けるため、ZIP内文書へ自分自身のZIP hashを書き込まない。

```powershell
./scripts/package.ps1 -GodotPath 'C:/tools/Godot/Godot_console.exe' -DotnetPath 'C:/tools/dotnet/dotnet.exe' -Version '0.4.0-dev'
./scripts/verify-decision-support.ps1 -Executable 'C:/validation/TheConductorOfTheGuild/TheConductorOfTheGuild.exe' -PackageDirectory 'C:/validation/TheConductorOfTheGuild'
```

検証スクリプトは試験用の成長/旧保存/Budget DBを使用する。保存元fixtureのhashを確認し、実ユーザーDBは使わない。ログ・試験DBはZIPへ含めない。旧v0.2.0/v0.3.0 goldenの出典/生成方法は `tests/Guild.Domain.Tests/Fixtures/` のREADMEを参照する。

## 残項と公開状態

OS日本語IMEの変換/候補確定、全操作のキーボード完遂/読み上げ、色覚条件、最大負荷の描画60fpsは未検証/非保証。敵性能・報酬・回復経済・成長タイミングや新ステージは変更していない。

正式v0.4.0は公開しない。全requiredの最終実体確認を記録し、ローカルActをcompleted_no_releaseで完了する。正式baselineはv0.3.0を維持する。
