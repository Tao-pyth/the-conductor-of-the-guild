# 公的ギルドUI・フルHD表示の検証記録

更新日：2026-10-25。対象はローカル0.4.0-dev、正式baselineはv0.3.0。完了条件は[UI改修契約](ui_layout_contract_v0.4.0-dev.md)。全192テスト、最終コードのWindows実体49条件、視覚QAに成功した。実行関連ファイルを維持して検証結果の文書を最終ZIPへ反映し、照合結果を外部manifestへ記録する。

## 実装

- 白〜生成りの紙、淡木枠、青緑の紋章、濃色本文、朱印の共通テーマ。画像は動的な値を含まない個別素材として実装した。
- 準備は名簿/地図/配置記録の3列。HPバー、実武器種、費用、XP購入、復帰可否、標的優先、朱印を固定し、長い能力/技能説明は中央の地図と切り替える。
- 朱印は既存StartWaveを即時に1回実行する。短い承認印の表示は開始を遅らせず、追加の承認操作を設けない。
- 論理viewport1920×1080、FHD/HDで同じ構成、従来最小1000×640を保持。ボタン/F11で全画面切替、Escでウィンドウへ戻り、直前のサイズ・位置を復元する。設定を永続化しない。
- 地図の描画・click・hover・試験入力を同じ平方マス計算へ集約した。配置可能域、実際の射程/警戒/行動/技能の半径と中心は変更していない。
- WeaponTypeは表示専用DTOへ実ユニットから供給する。ドメイン・保存形式・戦闘/報酬/成長の値を変更しない。

## 自動テスト・ビルド

- 全192テスト成功、失敗0、スキップ0。今回のTRX：`artifacts/guild-ui-v0.4.0-dev/test-results/ui-regression.trx`。
- Debug/Release/ExportReleaseビルドは警告0・エラー0。Release/ExportReleaseのログは同フォルダー直下の`release-build.log`と`export-release-build.log`。
- Godot SDK4.7.2、.NET SDK10.0.402、Windows/Compatibility描画。
- Release構成を指定したlocked-mode復元に成功。Godot SDKはDebug構成だけGodotSharpEditorを追加するため、最初の構成未指定locked復元はNU1004で失敗した。依存版を変更せず、`dotnet restore game/Guild.Game.csproj -p:Configuration=Release --locked-mode`で既存配布用lockと一致した。初回試行は`locked-restore-debug-attempt.log`、成功記録は`locked-restore-game.log`。
- Godotプロジェクトの残っていた0.3.0版指定を現在の0.4.0-devへ整合。新しい正式版は作っていない。

## ソース版の実ゲーム確認

`scripts/verify-civic-ui.ps1`をGodot .NET実行ファイルへ接続し、明示した試験保存と分離APPDATA/LOCALAPPDATAで実行した。

- 統合17条件すべて成功：`artifacts/guild-ui-v0.4.0-dev/ui-source/run-2e7e19f1/verification.json`。
- 最終の地図素材/文字調整後の重点5条件も成功：`artifacts/guild-ui-v0.4.0-dev/ui-visual-final/run-31a03a2e/verification.json`。
- 最大化から全画面へ切り替えた場合のMode復元も追加し、通常ウィンドウ・最大化・全画面の往復に成功：`artifacts/guild-ui-v0.4.0-dev/maximized-source/run-a49de538/verification.json`。最大化からは最大化状態へ戻し、通常ウィンドウへ戻した際にも元サイズ/位置を復元した。
- FHD/HD/最小の両モード準備、能力詳細の開閉・フォーカスと末尾スクロール、初配置、無料再配置、標的優先、範囲切替、XP購入と強化、朱印による戦闘開始、長い日本語名、50名候補を確認。
- 50名は隔離DBの資源だけを100000へ試験注入し、枠拡張と募集は正規コマンドを使用した。境界レイアウトの確認であり、難易度/経済の証拠ではない。
- ボタン→全画面→Esc、F11往復で元サイズ/位置を復元。Viewに加えてSQLiteの全文/Revision/RNGを含むhashが表示操作の前後で一致。

| 条件 | 実window | 論理viewport | PNG |
| --- | --- | --- | --- |
| フルHD | 1920×1080 | 1920×1080 | 1920×1080 |
| HD | 1280×720 | 1920×1080 | 1280×720 |
| 従来最小 | 1000×640 | 1920×1080 | 1000×562 |
| 試験環境の全画面 | 3440×1440 / ExclusiveFullscreen | 1920×1080 | 2560×1440 |

16:9以外では比率を維持する。Godotのviewport保存はウィンドウ外周の余白を含まないため、最小/全画面のwindowとPNG寸法は異なる。PNGの大きさをウィンドウ寸法の証拠として代用していない。

## 画像と視覚確認

採用画像は`guild-fullhd-ui/guild-preparation-master.png`（このチャットのvisualizations配下）。実画面は上記`ui-visual-final/run-31a03a2e`のDefenseFhd/DefenseHd/DefenseMinimum/InvasionFhd/FullscreenHd.png。

初回実画面にあった名簿の状態省略、HPバー不足、押印の弱さを調整し、見出しへ明朝体、名簿へHPバーと独立した状態表示、朱印の文字強調、拠点の実素材を反映した。

生成画像の各部寸法をそのまま数値の正本にはしない。ゲーム実データに合わせてミナは槌、技能名/能力/HPは実値を表示する。厳密な画素コピーではなく、採用した視覚・情報階層をGodotへ実装する。[視覚QA](../design-qa.md)へ比較と残る軽微な差を記録する。

## Windows配布実体

初回候補を新規`candidate-62f14da0`へ展開し、SDK用PATH/DOTNET_ROOTを除外した実プロセスで49条件に成功した。

- 新UI17条件：`artifacts/guild-ui-v0.4.0-dev/package-civic/run-6451fab3/verification.json`。
- 既存32回帰：`artifacts/guild-ui-v0.4.0-dev/package-regression/run-2ddb8986/verification.json`。拠点/成長/両モード/再挑戦/帰還/再開/旧保存/長い名前を含む。
- 全49条件はsource=false/sdkRootAbsent=true、終了コード0・実行エラーなし・試験DBのみ。
- 214実行関連ファイル、DB未同梱、Windows版0.4.0.0、管理Game DLLは0.4.0-devを確認。

初回候補後に最大化Modeの復元と、詳細を開いて人物を切り替えた場合のトグル文言を調整した。朱印の有効条件は既存StartWaveに合わせ、防衛は配置済みの戦闘不能者だけでもWave開始できる従来ルールを維持し、侵攻だけ生存配置者を要求する。ゲームの開始条件をUI独自に厳しくしない。

これらを含む最終コードを`final-code-c06b9b19`へ新規展開し、**もう一度49条件すべてに成功**した。

- 新UI17条件：`artifacts/guild-ui-v0.4.0-dev/final-package-civic/run-1135a63e/verification.json`。
- 既存32回帰：`artifacts/guild-ui-v0.4.0-dev/final-package-regression/run-30842ee9/verification.json`。
- 全49条件で終了コード0、実行エラーなし、source=false/sdkRootAbsent=true、試験DBのみ。fixtureの入力hashも保護した。
- DisplayHd/FullscreenHdは通常の切替と最大化復帰の両方がtrueで、全画面/最大化復帰のPNG・JSONを保存した。最大化の実windowは3440×1369、viewportPNG2433×1369、論理表示1920×1080。
- 1920×1080の最終Windows実画面を`artifacts/guild-ui-v0.4.0-dev/ui-preview-fhd.png`、HDを`ui-preview-hd.png`へ保存した。

保存先は`artifacts/guild-ui-v0.4.0-dev/TheConductorOfTheGuild-0.4.0-dev-windows-x64.zip`と対応する`.sha256`。ZIP内へこの結果を反映する際は実行関連214ファイルを変更しない。最終のサイズ/hash、新規展開先、214件の照合、文書反映後の起動確認は、自己参照hashを避けて同ディレクトリの`final-manifest.json`と`final-startup/verification.json`を正本とする。

旧0.4判断支援ZIPはSHA256 ede5b9db55aa888f58705c2dc219e122588b782ddac9c59cf1ff455ab501157bのまま保持。正式公開、Commit/Push/PR/Tagは行わない。

## Acceptance Criteriaの対応

| AC | 確認結果・証拠 |
| --- | --- |
| UI-AC01 | 紙/木/紋章/朱印と3領域を最終Windowsの実画面で確認。単なる色替えにせず名簿・操作・詳細を再構成 |
| UI-AC02 | 最終CivicActionsDefense/Invasionで初配置/再配置/標的/範囲/XP購入を確認。復帰の可否と既存コマンドへの接続を保持 |
| UI-AC03 | Growth/GrowthChooseと旧32回帰、192テスト。恒久Rank/今回Lv・資源・参考/実値・実技能枠/非搭載枠・未知合成を保持 |
| UI-AC04 | StampDefense/Invasionで一操作・revision増分1の戦闘開始。配置0名では無効。防衛/侵攻の既存条件に合わせる |
| UI-AC05 | 最終CivicのFHD/HD/最小、長名、50名、主要UIの境界・フォーカス・詳細末尾を確認 |
| UI-AC06 | 最終DisplayHd/FullscreenHdでボタン/F11/Esc、サイズ/位置/最大化Mode復元、保存全文とRNG不変 |
| UI-AC07 | 同じgeometryによる描画/クリック/hover、16×10平方マス、道上/盤外無操作、既存範囲/旧判定を確認 |
| UI-AC08 | 192成功/失敗skip0、source17+重点5+最大化1、最終Windows49成功 |
| UI-AC09 | 参照と実画面を比較し初回P2を修正。独立した視覚レビューを含めP0/P1/P2未解消なし |
| UI-AC10 | ガイド・契約・QAと実測結果を同梱。ZIP/SHA256・SDK不要実体・214ファイルの最終照合をmanifestへ記録 |

## 残る検証限界

OS IME変換、読み上げ、すべてのキーボード操作の完遂、全モニター/Windows表示倍率、最大負荷での60fpsは保証していない。実ユーザーDBを試験せず、既存の隔離fixtureを使う。

## 画像素材の出典

`game/assets/guild-ui/assets-manifest.json`に6素材の生成元・プロンプト・実寸・SHA256を記録。OpenAI built-in image_genで生成した紙/木、紋章、挿絵、印鑑、武器アトラス、防衛門を使用。武器アトラスの領域選択は描画用AtlasTextureで行い、ゲーム値を画像から推測しない。
