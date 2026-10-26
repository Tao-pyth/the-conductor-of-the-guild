# 0.4.0-dev UIフィードバック7点・追加ローカル契約

2026-10-25。baselineはv0.3.0、targetは現在の0.4.0-dev（Windows 0.4.0.0）。ユーザーの実画面指摘①〜⑦を修正し、実行できるWindows ZIPまで用意する。既存UI契約を維持し、今回の結果見込みと履歴投影は追加依頼として対象に含める。

## Goal / Scope / Approach

出撃・育成の判断に必要な情報を、比較できるカード、見出し付き領域、重要な結果に絞った履歴で示す。表示文言は日本語JSONカタログへ集約してApplicationとGodotで共有し、計算値・戦闘定義は文言から独立させる。

## Work Units / Handoff

| ID | 概要・背景と作業 | AC / 検証 | 依存 |
| --- | --- | --- | --- |
| FB-01 | 分散した日本語表示文言をJSONへ集約し共有読取APIを実装。味方の青と出撃名称を整合 | AC01/02、カタログ読込・欠落検査、配布版で利用 | なし |
| FB-02 | HOME/詳細/戦闘のプロフィール・能力・Skill・加護・装備をラベル付き領域化。Lv横3択と説明、加護の所持6枠/候補比較 | AC03〜05、FHD/HD/最小画面、操作の回帰 | FB-01 |
| FB-03 | 帰還精算と共有する純粋な結果照会、5列結果カード、最終出撃結果と重要選択の成長履歴 | AC06〜08、結果/精算一致・保存/再送・旧記録回帰 | FB-01 |
| FB-04 | 実画面QA、既存回帰、文書、別保存先へのWindows ZIPとSDK不要検証 | AC09/10、全テスト/Build/配布実体 | FB-01〜03 |

作業単位を本書/Obsidianで追跡する。GitHub登録は必須とせず、未許可のIssue/PR操作を追加しない。検出したopen trackingは0件。

## Acceptance Criteria / Test Requirements

- AC01：使用する日本語表示文言・テンプレートを`data`配下のJSONへ集約し、文言の場所と数値プレースホルダーを説明する。配布で欠落せず、文言変更はゲーム値やRNGを変えない。
- AC02：冒険者トークンは明確な青で凡例と一致。画面の行為名を出撃準備/出撃中/出撃完了へそろえ、防衛/侵攻はモード識別として併記。防衛対象/耐久というルールの意味は保持。
- AC03：HOMEを含む対象全画面で情報を見出し付き領域へ区分。SkillはSkillと表示された領域内に置く。長文の単純な直列表示で完了しない。
- AC04：Lv強化は固定3候補を横一列の3カードで表示。対象冒険者、今回マッチ限り、正しい数値効果、使いどころを示す。1つの選択で既存コマンドを1回実行。
- AC05：加護取得では対象冒険者の現在の所持数/6枠・名前・Rank・日本語効果を表示。候補ごとに新規取得/進化・現在→取得後Rankを区別。満枠では置換対象を明確にし、既存抽選/取消不可/固定定義/未発見合成秘匿を維持。
- AC06：作戦報告書は横5名、所持人数に応じた行数（6名=2行、50名=10行）のカードで全員を一覧化し縦スクロール可能。名前/出撃状態・Rank上昇見込み・終了HP/最大HPと損耗・帰還後HPを区別して示す。
- AC07：結果照会のRank/HP/衰弱/資源は実際の帰還精算と一致。閲覧でprofile/session/revision/history/RNGを変えず、帰還は従来通り一度だけ。防衛/侵攻の勝利/敗北・非出撃・HP0・Rank複数上昇を確認する。
- AC08：成長履歴は各マッチ最終帰還につき1件（全突破または敗走Wave/区画を明示）。開始・準備・Wave単位・Lv一時強化・購入/配置等を成長履歴に表示しない。Rankイベント種類、加護名/Rank/置換、合成/装備/恒久変化を具体的に記録。監査履歴と旧記録を破壊せず、再読込・再挑戦・同CommandId再送で重複しない。
- AC09：全既存テスト＋必要な新規結果/履歴/加護/文言テストが失敗/skip0。Release/ExportReleaseで警告/エラー0。実画面FHD/HD/最小、長名・50名で到達可能性・重なり/欠落を確認し修正。
- AC10：説明/検証/リリース記録を更新。`artifacts/guild-feedback-v0.4.0-dev`へ新しいWindows ZIP/SHA256を作り、新規展開したSDK不要実体で新UIと既存回帰を確認。旧ZIPは保持。

## Non-Scope / Rejected Alternatives

ゲーム性能・敵・報酬・経験値・費用・抽選・台帳/ADRの変更、新ステージ、無関係なリファクタ、実ユーザーDBの試験、過去ログ削除、既存未コミット変更の巻戻し、外部公開/Commit/Push/PR/Tagは対象外。結果を見ただけで帰還精算する方式、表示のための乱数消費、旧曖昧ログからの選択内容捏造は採用しない。

## Risks

幅の制約には折返し・縦スクロールを使用。精算予測は実精算と計算を共有する。旧監査情報にない内容は補造しない。数値のルールを文言カタログへ移さない。DSQ-001はKeep。

## Release Contract

| Area | 分類 | 必要な成果・証拠 |
| --- | --- | --- |
| Implementation | required | FB-01〜04 / AC01〜10 |
| Tests | required | 既存192件と意味のある新規検証、実画面・配布実体 |
| Database | required | 監査原記録と再送の保持、結果閲覧不変、履歴表示投影 |
| Database Migration | not-required | SQLite/文書形式据置。既存履歴を読取拡張で利用 |
| Configuration | required | JSON文言を欠落なくビルド/配布 |
| Configuration Migration | not-required | 永続設定の追加なし |
| API | required | 表示用結果/所持加護/履歴DTOと純粋照会 |
| CLI | not-required | 製品CLIなし。試験シナリオはTestsの範囲 |
| UI | required | 7点の実画面と操作 |
| README | required | 新UI/文言/成果物案内 |
| Architecture Docs | required | カタログ・結果照会・監査と成長履歴の区別 |
| User Docs | required | 強化・加護・結果・履歴の読み方 |
| CHANGELOG | required | 0.4.0-devに追記 |
| Version Metadata | required | 既存0.4.0-dev/0.4.0.0を保持 |
| Commit | not-required | ローカル修正まで |
| Push | not-required | 同上 |
| PR / Merge | not-required | 同上 |
| Tag | not-required | 同上 |
| Build | required | Release/ExportRelease、警告/エラー0 |
| CI | not-required | 新設なし |
| Packaging | required | 別保存先Windows x64 ZIP/SHA256、SDK不要実体確認 |
| GitHub Release | not-required | baseline v0.3.0維持 |
| Artifact Publish | not-required | ローカル成果物まで |
| Published Verification | not-required | 未公開。ローカル実体の検証はrequired |
| Release Notes | required | 今回の成果/検証/限界を記載 |
| Backward Compatibility | required | 旧防衛/侵攻/成長/保存/履歴/再開・一度限り精算 |

requiredを証拠付きで満たしてからcompleted_no_releaseとする。
