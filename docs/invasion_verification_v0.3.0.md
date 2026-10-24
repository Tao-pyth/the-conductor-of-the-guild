# v0.3.0 侵攻モード・正式公開検証記録

## v0.3.0 正式版の検証状況 — 2026-10-25

ユーザーのコミット・正式公開依頼を受け、[正式公開契約](release_contract_v0.3.0.md)に従って0.3.0の再検証・公開を行う。下記のdev完了済み記録とは別の証拠を残す。

| 対象 | 状態 | 正式版の証拠予定 |
| --- | --- | --- |
| 正式Release全123テスト | 成功123・失敗0・スキップ0 | `artifacts/release-v0.3.0/test-results/release-validation.trx` |
| Release/ExportReleaseビルド | 警告・エラー0 | 正式版のビルド/出力ログと今回のAct記録 |
| Windows正式ZIPの展開実体 | 候補ZIPの7シナリオ合格。公開コミットSHA入りの最終ZIPも再検証を必須とする | 最新結果 `artifacts/release-v0.3.0/package-validation/verification.json`、候補履歴 `package-validation/run-95b71470/verification.json` |
| Commit/Push、v0.3.0タグ、正式latest Release、ZIP/SHA256添付 | 公開時の必須照合 | 今回のAct記録に実際のcommit/Release/資産識別子と照合結果を記録 |
| 公開資産の再取得・照合・SDK不要起動/保存/再開 | 公開後の必須確認 | 実際の結果を `artifacts/release-v0.3.0/published-validation/verification.json` と今回のAct記録へ保存 |

正式版の対象は、防衛1ステージ10Wave、侵攻1ステージ3区画、5武器種、5技能、3加護、1合成レシピ。INV-AC01〜18を維持し、公開番号のために機能・数値を変更しない。正式ZIP名は `TheConductorOfTheGuild-0.3.0-windows-x64.zip`、hashの正本は同梱外の対応する `.sha256` ファイルとする。公開後の検証が終わるまで正式公開完了とは扱わない。

配布DLLのソース識別子を公開コミットへ合わせるため、コミット後の同じソースから再ビルド・再包装し、SDK不要の起動・保存等を再確認する。その最終資産と公開後の実結果はAct記録とartifact JSONへ保存し、リリースタグ確定後に検証結果だけの未コミット差分を残す方式は採らない。

正式候補ZIPの7シナリオは Battle / Victory / DefeatRetry / ReturnGuild / Reopen / DefenseVictory / LegacyDefenseVictory。全件exit=0、ERRORなし、期待マーカー・画像を確認した。初期6名で侵攻3区画勝利後に拠点資源300・Rankイベント6件へ到達した。Windows基本PATHのみ・SDK設定なし、旧保存原本不変と移行前backup、0.3.0ガイド同梱、第三者notice10件の原文hash一致、検証DB未同梱を確認した。

## v0.3.0-dev ローカル検証の完了記録

検証日：2026-10-24〜25。基準版：v0.2.0。対象：ローカル技術検証版v0.3.0-dev。正式公開・Commit/Push/Tagは契約対象外。

## 検証の入力

- [侵攻仕様](invasion_mode_spec_v0.3.0_draft.md)のINV-AC01〜18とローカルRelease Contract。
- SDK 10.0.402、Godot 4.7.2 .NET、Windows x64。
- 公開済みv0.2.0のWindows EXEで検証専用のv1プロフィール/進行中防衛を生成。
- 公開済みGuild.Domain.dllで、旧開始snapshotから4008更新・10Wave・80160msを実行したgolden state。定義hash `ad265a687145776a58cde75ac0d1db26f7062dc2b26111f4ce06b05356dffeac`。

## 自動検証

**123件成功、失敗0、スキップ0。** ビルド警告・エラー0。既存72件、侵攻Domain22件、Application/移行20件、境界/倍速等7件、公開旧版比較2件。

```powershell
dotnet test tests/Guild.Domain.Tests/Guild.Domain.Tests.csproj --configuration Release --no-restore --logger 'trx;LogFileName=invasion-validation.trx' --results-directory artifacts/invasion-v0.3.0-dev/test-results
dotnet build game/Guild.Game.csproj --configuration Release
```

結果は `artifacts/invasion-v0.3.0-dev/test-results/invasion-validation.trx`。1倍/120fpsと2倍/30fpsで同じ侵攻状態に到達し、一時停止で時計・戦況が止まることを確認した。旧版の10Waveは、公開DLLが出力した全既存フィールドと比較して一致した。

保存移行は実v1定義を含むfixtureで、バックアップ、一括移行、二重移行防止、途中失敗のrollback、確定後応答消失、将来形式・不正所有/ロスター/定義hashの変更前拒否を検証した。

## Windows実体確認

Godot実画面の9シナリオ（編成、準備、戦闘、Lv強化、勝利、敗北、再挑戦、帰還、再開）がすべて終了0・ERRORなし。初期6名を改造せず、通常の配置・Lv購入・復帰・強化のボタン経路で3区画を攻略できた。

- source記録：`artifacts/invasion-v0.3.0-dev/ui-source/verification.json`。
- 7画面を画像で確認。日本語・主要ボタン・費用・説明が1280×720に収まる。帰還直後の画面保存タイミングを修正し、資源300・Rank2・イベント6件のGuild画面を再撮影した。
- Windows配布物を別フォルダーへ展開し、DOTNET_ROOT類なし・PATHをWindows基本ディレクトリだけにして7シナリオを実行。侵攻開始、3区画勝利、敗北再挑戦、帰還、同DB再開、新規防衛10Wave勝利、公開旧版の進行中防衛の移行/10Wave勝利がすべて終了0・ERRORなし。
- 配布実体の記録：`artifacts/invasion-v0.3.0-dev/package-validation/verification.json` と `run-f02bb8b7/`。勝利・帰還・旧防衛の3画像も視認した。
- 旧DBは検証用コピーを使用し、元DB不変と移行前backup生成を確認した。ユーザーの実セーブは操作していない。

## Acceptance Criteria

| AC | 結果と主な証拠 |
| --- | --- |
| INV-AC01 | 合格：モード選択/編成UI、Applicationの育成共有、侵攻帰還後の防衛出撃 |
| INV-AC02 | 合格：0/1/10/11名、10出撃マス、対象外/重複/費用/戦闘中操作拒否 |
| INV-AC03 | 合格：視認だけの停止回避、初期合流/経路帰還、速度差、射程での交戦、3区画完走 |
| INV-AC04 | 合格：4優先・HP/距離/生成順、対象外設備、回復の射程境界とHP0/範囲外除外、共通投射物回帰 |
| INV-AC05 | 合格：全敵/門/司令核を条件とする3区画突破、初期6名のGUI勝利 |
| INV-AC06 | 合格：控えを残した全滅、相打ち敗北、全撃破と時間切れの同更新優先 |
| INV-AC07 | 合格：固定更新、区画時刻、1倍/2倍・描画delta差・一時停止の再現性 |
| INV-AC08 | 合格：自然150XPでLv2/残25、参加者/HP0/控え、突破報酬の重複防止 |
| INV-AC09 | 合格：不足XP費用ぴったり/1不足/Lv10、HP0購入不可、復帰HP/候補破棄、再挑戦の復帰番号 |
| INV-AC10 | 合格：区画間の位置/通算時刻・CD・異常保持。残り2秒の異常が次区画でも2秒 |
| INV-AC11 | 合格：資源300/武器1/RankXP120・敗北0/10/20、未参加者、通算初回、再送とTX共通障害回帰 |
| INV-AC12 | 合格：開始checkpoint、準備途中、再挑戦/結果再開、GrowthRng・復帰回数、配布EXEで同DB再開 |
| INV-AC13 | 合格：公開v0.2.0の移行・旧定義hash/装備保持・10Wave全状態比較、失敗/将来版/破損拒否 |
| INV-AC14 | 合格：不正経路/区画/時間/敵ID/到達不能設備/整数化後HP0の定義拒否 |
| INV-AC15 | 合格：構造物の直接攻撃と状態免疫、守備兵だけの撃破資源、共通会心計算 |
| INV-AC16 | 合格：日本語9シナリオ、主要7画面の画像確認、費用・控え・敗因・目標HPの表示 |
| INV-AC17 | 合格：既存72件を含む全123件、防衛10Waveの公開goldenと新規/移行後のEXE勝利 |
| INV-AC18 | 合格：Windows dev ZIP、SDK不要の7シナリオ、同梱文書/notice、SHA256 |

## 成果物・残項

配布物は `artifacts/TheConductorOfTheGuild-0.3.0-dev-windows-x64.zip`、照合用は同名 `.sha256`。検証後に説明文書を仕上げ、同じ検証済み実行ファイル群のままZIPへ反映する。最終ZIPの再展開・実行ファイル群の214ファイルhash比較・起動結果は `artifacts/invasion-v0.3.0-dev/final-package-validation.json` を参照する。ZIP自身のhashを自身の中へ記録する循環を避け、正本を同梱外のSHA256ファイルに置く。

個別性能・300秒・報酬は承認された試作用の初期値であり、正式バランスや所要時間当たりの防衛との同効率を保証しない。OS日本語IMEによる変換/候補確定、最大負荷描画60fpsは従来の未確認/非保証範囲を維持する。

このローカル検証時点では仕様のrequired全項目を確認し、Act結果を `completed_no_release` とした。当時はCommit/Push/PR/Merge/Tag/GitHub Release/外部公開が契約上not-requiredで実施せず、正式baselineをv0.2.0に維持した。今回の正式公開は本書冒頭と[v0.3.0公開契約](release_contract_v0.3.0.md)に従う。
