# 技術仕様実装契約

- 基準: v0.1.0。ローカル検証版: 0.2.0-dev。
- 根拠: 2026-10-24に承認された「実装技術仕様案」とその実装依頼。
- 方針: Godot 4.7.2 .NET / C# / .NET 10 / Microsoft.Data.Sqlite。端末内保存、Windows x64。

## 実装範囲

1. 独立したC#数値・定義・乱数基盤と自動テスト。
2. 恒久状態、マッチ開始点、Wave開始点、操作途中、変更履歴のSQLite保存。
3. 固定20ms更新の戦闘、配置、移動、攻撃、投射物、異常、技能、Wave、勝敗、再挑戦。
4. 日本語UI、募集、装備、成長選択、加護・合成、履歴から次出撃までの技術検証サイクル。
5. Windows向けビルド・ZIP・起動確認・検証結果の記録。

最初のデータは `tech-*` IDによる技術検証専用セットとする。既存150技能・453加護・300レシピを正式収録したと扱わず、候補台帳・Excelを変更しない。個性表やUNIQUEの性能基準など未確定の製品要件も確定済みと扱わない。

## Acceptance Criteria

- AC01: 画面に依存せず分数計算、型付きパラメータ、PCG32を検証できる。
- AC02: 巨大整数と分数を精度損失なく保存・復元できる。表示だけ小数第2位に数学的ceil。
- AC03: 不可逆操作の状態・消費・履歴・処理済みIDを同一TXで保存し、再要求で重複しない。
- AC04: 再挑戦・強制終了再開・Wave復帰を別処理として検証できる。
- AC05: 同じ入力と乱数で、描画速度・倍速によらず同じ戦闘結果になる。
- AC06: 同一技能2枠のCD独立、進行率保持、成分重複防止、派生再誘発防止を検証できる。
- AC07: 開始時の定義実体を固定し、旧マッチ再開と次出撃での定義更新を区別する。
- AC08: 初期6名・10Waveの検証セットで出撃、勝敗、成長、履歴、募集・装備、再出撃へ到達できる。
- AC09: 日本語表示・名前変更入力とWindows配布物の起動を確認する。IMEの実機操作を自動確認できない場合は未確認として残す。
- AC10: 冒険者10・敵200・投射物400を性能測定用負荷として計測する。60fpsは目標であり未測定の保証としない。

## 保存・互換性

- 現在状態+snapshot+監査履歴。履歴再生を現在状態の復元方式にしない。
- 生成済み武器の基礎値とSkillIDは保持し、技能効果は次出撃から更新。進行中マッチは定義実体を保持。
- 保存形式・定義・処理版は別管理。新形式は拒否し、移行前にSQLite Backupで保護する。
- 候補・生成結果・用途別乱数状態を保存。Wave間の確定操作とRankイベント途中は直ちに保存。

## Release Contract

required: Implementation, Tests, Database, Database migration safeguards, Configuration, Internal API, UI, README, Architecture Docs, User Docs, Version Metadata, Build, Packaging, Backward Compatibility evidence。

not-required: Configuration Migration (初期構成), Public API, CLI product, CHANGELOG release history, CI service setup, Commit, Push, PR/Merge, Tag, GitHub Release, Artifact Publish, Published Verification, Release Notes, Issue/Milestone registration/closure。

正式公開を行わず現行baselineはv0.1.0を維持する。必須検証が未達の場合、ローカル試作の完成やOODA Act完了を宣言しない。

## 正式公開の追加依頼

上記は0.2.0-devローカル実装時の契約。2026-10-24のユーザーによる正式公開・コミット依頼以降の操作には、[v0.2.0正式公開契約](release_contract_v0.2.0.md)を使用する。
