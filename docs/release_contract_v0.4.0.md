# v0.4.0-dev ローカル実装・検証契約

作成日：2026-10-25。正式baseline：**v0.3.0**。対象は**0.4.0-dev**、Windows数値版0.4.0.0。ユーザーが実装と動作確認を依頼した、完了済みDecideのV04-01〜05を扱う。今回の完了範囲はローカル実装・検証・Windows ZIPまでで、正式v0.4.0の公開ではない。

## Goal / Scope / Non-Scope

防衛の移動途中の射程接触を修正し、成長する対象、冒険者の能力・技能・費用、準備中の配置範囲、次の敵を確認できる状態にする。[機能仕様](player_decision_support_v0.4.0.md)のV04-AC01〜14と、下記契約を必須とする。

敵性能・報酬・経験値・復帰・帰還回復・侵攻の成長タイミング、新規コンテンツ、現在標的、戦闘中範囲、精算サマリー、戦闘ログ刷新、OS IME一般対応、CI新設は対象外。実行時データ・Excel/ADR回答・候補台帳を変更しない。

## 作業単位

| ID | 責務 | 依存 |
| --- | --- | --- |
| [V04-01 / #2](https://github.com/Tao-pyth/the-conductor-of-the-guild/issues/2) | 新規防衛の射程接触、旧ルールの維持 | なし |
| [V04-02 / #3](https://github.com/Tao-pyth/the-conductor-of-the-guild/issues/3) | 成長対象の事前表示、表示IDの実行照合 | なし |
| [V04-03 / #4](https://github.com/Tao-pyth/the-conductor-of-the-guild/issues/4) | 実能力・実技能・費用とモード区別 | なし |
| [V04-04 / #5](https://github.com/Tao-pyth/the-conductor-of-the-guild/issues/5) | 準備中の範囲表示と次敵予告 | 01/03 |
| [V04-05 / #6](https://github.com/Tao-pyth/the-conductor-of-the-guild/issues/6) | 統合確認、説明、版情報、ローカルZIP | 01〜04 |

[親Issue #1](https://github.com/Tao-pyth/the-conductor-of-the-guild/issues/1)を追跡の入口とする。テストの通過だけで未確認のUI・包装を完了扱いしない。

## Release Contract

| Area | 分類 | 完了に必要な成果・証拠 |
| --- | --- | --- |
| Implementation | required | V04-01〜04。計画外の敵/報酬変更なし |
| Tests | required | 既存123件と追加の境界・互換・対象一致・純粋照会・画面検証。失敗/skip0 |
| Database | required | RuleSetId、操作再送、履歴、一度限り精算の回帰。SQLite schema1維持 |
| Database Migration | not-required | 文書形式2と既存RuleSetIdプロパティを利用。v0.3→0.4の変換なし。既存形式1→2のbackup/失敗復元は回帰確認 |
| Configuration | required | ローカル版・配布構成・0.4文書の選択。戦闘データ値/hash維持 |
| Configuration Migration | not-required | 新しい永続設定なし。範囲表示は画面内の一時状態 |
| API | required | 非永続View/照会DTO、既存BeginEventのActorId/TargetId照合 |
| CLI | not-required | 製品CLI新設なし。既存検証起動引数の最小拡張は検証作業に含む |
| UI | required | 成長事前カード、能力/技能比較、防衛/侵攻の区別、範囲と予告 |
| README | required | dev版の機能とローカル利用、正式公開はv0.3.0であること |
| Architecture Docs | required | defense-v1/v2、純粋照会、固定定義、保存形式据置 |
| User Docs | required | 成長対象、費用、射程/行動条件、旧マッチ、切替操作 |
| CHANGELOG | required | 0.4.0-devの修正/表示追加を過去の公開履歴から分ける |
| Version Metadata | required | .NET/Godot/ZIPは0.4.0-dev、Windows数値版0.4.0.0 |
| Commit | not-required | 今回はローカル実装まで。コミットを行わない |
| Push | not-required | v0.3.0公開時の許可を次版へ流用しない |
| PR / Merge | not-required | ローカル確認まで。公開依頼時に規則を再確認 |
| Tag | not-required | v0.4.0タグを作らない |
| Build | required | Release/ExportRelease成功、警告/エラー0。locked-mode復元、プロジェクト参照lock整合 |
| CI | not-required | 新設せず必要結果をローカルへ保存 |
| Packaging | required | 0.4.0-devのWindows x64 ZIP/SHA256、0.4文書/notice、新規展開先でSDK不要の確認 |
| GitHub Release | not-required | 正式公開は別依頼・追加契約 |
| Artifact Publish | not-required | ローカルZIPまで。外部アップロードなし |
| Published Verification | not-required | 未公開のため対象なし。ローカル実体確認は必須 |
| Release Notes | required | ローカル版の機能/互換/検証/残項を英語と初心者向け日本語で説明 |
| Backward Compatibility | required | 旧null/defense-v1/invasion-v1、文書形式1/2、旧CommandId再送、固定定義/装備/履歴の保持 |

needs-investigationの未決義務は置かない。仕様のrequiredを満たせない場合は失敗・不足証拠を明示し、敵や報酬を変更して成功を取り繕わない。

## 互換と検証

- 新規防衛のみdefense-v2。旧null/defense-v1、侵攻invasion-v1は現在マッチ・再開・再挑戦で保持する。
- Current/MatchStart/WaveStartのcanonicalなルール混在・未知IDを書込前に拒否する。nullとdefense-v1は同じ旧判定。保存を勝手にv2へ置換しない。
- SimulationVersion1、プロフィール/マッチ文書形式2、SQLite schema1を維持する。既存形式1→2のbackup移行はそのまま利用する。
- 参照を繰り返してもprofile/session・revision/履歴・3種RNG・効果残数を変えない。参照有無で次の抽選も一致する。
- 実画面は1280×720と最小設定1000×640で、実client/viewport/PNG寸法、スクロール、フォーカス、重要情報への到達を記録する。
- 新規展開したWindows ZIPで、両モードの準備/勝敗/再挑戦/帰還/再開、成長、表示切替、旧保存を確認する。ユーザーの実セーブは試験しない。

## 完了と記録

成果物は`artifacts/TheConductorOfTheGuild-0.4.0-dev-windows-x64.zip`と対応する`.sha256`。[検証記録](player_decision_support_verification_v0.4.0.md)へ実際のTRX・ログ・PNG・寸法・ZIP/hashを記録する。V04-AC01〜14と全requiredの証拠がそろってから`completed_no_release`とし、正式baselineをv0.3.0に維持する。

以前の[実装契約](implementation_contract.md)、[v0.2.0公開契約](release_contract_v0.2.0.md)、[v0.3.0公開契約](release_contract_v0.3.0.md)は履歴であり、今回の公開許可には転用しない。
