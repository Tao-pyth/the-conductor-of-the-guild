# v0.3.0 正式公開契約

- 現行正式baseline：**v0.2.0**。公開対象：**v0.3.0**。
- 根拠：2026-10-25、ユーザーが侵攻モードを含む変更のコミットとv0.3.0の正式公開を明示的に依頼した。
- 対象：完成済みv0.3.0-devの侵攻実装と関連成果をレビューし、機能を追加せず正式番号へそろえて公開する。
- [侵攻仕様](invasion_mode_spec_v0.3.0_draft.md)§12とDecideのローカル契約、[v0.2.0公開契約](release_contract_v0.2.0.md)は当時の履歴として保持する。本書は今回の公開操作に関するnot-required分類を、今回の明示依頼に基づいて更新する。

## Scope

版情報・README・操作/技術説明・CHANGELOG・英語/日本語リリース説明を0.3.0へそろえる。侵攻実装、検証、保存互換、関連する仕様・確認資料のレビュー済み差分をコミットする。全123テストと侵攻INV-AC01〜18の条件を保持し、正式Windows ZIPを再生成・確認する。mainへpushし、v0.3.0タグ、正式latest Release、ZIPとSHA256を公開する。公開資産を再取得して内容・hash・起動/保存/再開を確認する。

ユーザーの実データ、キャッシュ、検証DB、実行ログ、一時出力、開発用ファイルロックを公開へ混入させない。候補台帳の未確定値や第4確認票の未回答を承認済みへ変更しない。元のユーザー変更はレビュー対象を特定して保持し、無関係な変更を破棄しない。

## Non-Scope

新しい機能・技能・加護・ステージの追加、数値や報酬の再設計、既存台帳全件の収録、リポジトリ可視性の変更、CI新設、Issue/Milestone新設。OS日本語IMEの変換/候補確定の未確認と、最大負荷60fps非保証は従来の残項として説明へ保持する。

## Release Contract

| Area | 分類 | 必要な成果・証拠 |
| --- | --- | --- |
| Implementation | required | 完了済みINV-01〜06のレビュー。公開昇格のために機能範囲を変えない |
| Tests | required | Release構成の全123件成功、失敗0・スキップ0。INV-AC01〜18の証拠を維持 |
| Database | required | 一度限り精算、履歴、操作ID、保存された侵攻/防衛の保持 |
| Database Migration | required | 形式1→2のbackup、一括移行、失敗復元、将来形式/破損拒否、実v0.2.0の移行/再開検証 |
| Configuration | required | 正式ZIPに検証済みtechデータと実行設定を含む |
| Configuration Migration | not-required | 公開番号の昇格で追加の設定移行を行わない |
| API | required | 既存内部コマンド/Viewを保持。公開APIの新設なし |
| CLI | not-required | 新たな製品CLIは設けない。既存検証用起動引数は実体確認で使用可 |
| UI | required | 日本語の編成・準備・侵攻・勝敗・再挑戦・帰還/再開、防衛の実体確認 |
| README | required | 0.3.0の機能、ZIP、入手先、保存移行と旧版へ戻す手順 |
| Architecture Docs | required | 侵攻/防衛、保存文書形式2/旧処理版、定義hashの説明を保持 |
| User Docs | required | 侵攻操作、全滅/時間切れ、復帰、保存/バックアップの説明 |
| CHANGELOG | required | 0.3.0の侵攻追加と保存互換・既知制限を記録 |
| Version Metadata | required | .NET/Godot/Windows出力の版・説明を0.3.0へ整合。dev表記は履歴に限定。配布DLLのAssemblyInformationalVersionへ公開コミットのSHAを反映 |
| Commit | required | レビュー済み差分のコミット。英語本文と初心者向け日本語説明 |
| Push | required | 検証済みコミットをorigin/mainへpushして一致確認 |
| PR / Merge | not-required | main protected=false、rulesets=[]、push権限あり、必須PR規約なしを読取確認。今回はmainへ直接コミット/push |
| Tag | required | `v0.3.0` が公開コミットを指す。既存タグを勝手に移動しない |
| Build | required | Release/ExportReleaseビルド成功、警告・エラー0。win-x64を共通指定し、テスト/ゲームの`--locked-mode`復元で依存lockの再現性を確認 |
| CI | not-required | CIサービス新設なし。必須チェックはローカルで記録 |
| Packaging | required | Windows x64正式ZIP、同梱文書・第三者notice、SHA256、再展開後のSDK不要起動 |
| GitHub Release | required | v0.3.0、draft=false、prerelease=false、latestとして公開。英語/初心者向け日本語説明 |
| Artifact Publish | required | `TheConductorOfTheGuild-0.3.0-windows-x64.zip` と対応する `.sha256` をReleaseへ添付 |
| Published Verification | required | origin/main/タグ/Releaseのコミット一致、latest、資産サイズ/hash、公開ZIPの再取得と起動/保存/再開 |
| Release Notes | required | 収録範囲、移行と旧版へ戻す方法、実際の検証結果、未確認/非保証項目を英語・日本語で説明 |
| Backward Compatibility | required | 旧v0.2.0の進行中防衛・生成装備・履歴・定義hashの保持。公開旧DLLの10Wave golden比較 |

Issue/Milestone登録・完了処理はnot-required。公開契約のrequiredを、必要な証拠が未確認のまま完了扱いしない。保護規則が作業中に変わった場合は、その規則に従ってPR/Mergeの必要性を再評価する。

## Acceptance Criteria

- RC01：侵攻INV-AC01〜18と旧防衛の機能を保持し、正式版Release全123テストが成功する。
- RC02：版情報、文書、実行ファイル、ZIPが0.3.0で一致する。dev版の検証履歴と正式証拠を混同しない。
- RC03：正式ZIPの展開先でSDK設定なしの侵攻・防衛・保存/再開・旧v1移行を確認し、文書とnoticeを含む。
- RC04：レビュー済みコミット、origin/main、v0.3.0タグ、Release targetが同一の公開内容を指す。
- RC05：latest Releaseがv0.3.0かつ非draft/非prereleaseで、ZIPとSHA256のサイズ・hashが検証済み実体に一致する。
- RC06：公開後に資産を再取得し、hash照合、展開起動、保存/再開を検証する。ユーザーの実セーブは検証に使わない。

## 証拠と実行順

| 段階 | 記録先 |
| --- | --- |
| dev版の完了済み検証履歴 | `artifacts/invasion-v0.3.0-dev/` と [侵攻検証記録](invasion_verification_v0.3.0.md)のdev節 |
| 正式版の自動テスト | `artifacts/release-v0.3.0/test-results/release-validation.trx` |
| 正式ZIPの実体確認 | `artifacts/release-v0.3.0/package-validation/verification.json` |
| 公開後の再取得・実体確認 | `artifacts/release-v0.3.0/published-validation/verification.json` |
| 公開操作と最終照合 | Obsidianの今回のAct記録。commit/tag/Release/資産識別子とhashを記録 |

公開準備 → 全テスト/ビルド → 候補ZIP生成 → 展開実体確認 → 実際の結果で説明更新 → コミット/push → 公開コミットSHA入りの再ビルド/再包装 → 最終ZIPの再検証 → タグ → Releaseと資産公開 → 再ダウンロード照合・起動/保存確認 → Act完了。

公開後の実際のcommit/Release/資産識別子・hash・実行結果は、今回のObsidian Act記録とartifact JSONを正本とする。公開前コミット内の説明は確認手順と保存先を示し、公開後の結果を先に完了と書かない。検証結果の追記だけでリリースタグの後に未コミット差分を残さない。
