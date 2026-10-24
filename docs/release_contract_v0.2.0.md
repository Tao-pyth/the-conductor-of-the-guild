# v0.2.0 正式公開契約

- 基準: v0.1.0（ユーザー指定）。公開対象: **v0.2.0**。
- 根拠: 2026-10-24の「正式公開とコミットをお願いします。」というユーザー指示。
- 対象: 完了済み0.2.0-dev技術検証版を、機能範囲を保って正式なGitHub Releaseへ公開する。
- この文書は今回の公開操作に関して、以前の [ローカル実装契約](implementation_contract.md) のnot-required分類を更新する。以前の実装・検証記録は履歴として保持する。

## Scope

版情報・README・操作ガイド・CHANGELOG・英語/日本語リリース説明を0.2.0へそろえる。第三者ライセンス文と出典を配布物へ同梱し、Windows ZIPを再生成・再確認する。既存ソース、設計資料、Excel台帳を含むプロジェクト成果をレビューしてコミットする。ユーザーデータ・キャッシュ・テストDB・ロックファイルを公開対象へ混入させない。

既存台帳全件の収録、新機能、未確定ゲーム仕様の確定、リポジトリ可視性の変更は今回対象外。OSのIME変換未確認・最大負荷60fps非保証は既存の残項として公開説明に残す。

## Release Contract

| 項目 | 分類・必要証拠 |
| --- | --- |
| Version Metadata / Documentation / CHANGELOG / Release Notes | required: 0.2.0で整合。英語本文と初心者向け日本語説明 |
| Tests | required: Release全自動テスト、失敗0 |
| Build / Packaging | required: Windows x64 ZIP、ライセンス文同梱、SHA256 |
| Backward Compatibility | required: 保存形式/戦闘処理版を維持し、従来の互換テスト通過 |
| Commit / Push | required: レビュー済みプロジェクト成果をmainへコミット・push。英語本文と日本語説明 |
| PR / Merge | not-required: 現行mainは無保護、ルールセットなし、必須PR規約なし |
| Tag | required: v0.2.0が公開コミットを指す |
| GitHub Release | required: v0.2.0、draft=false、prerelease=false、latestとして公開 |
| Artifact Publish | required: Windows ZIPと対応するSHA256ファイルをReleaseへ添付 |
| Published Verification | required: main/タグ/Releaseコミットの一致、latest、資産サイズ/hash、公開資産の再ダウンロードと展開起動 |
| Issue / Milestone / CI setup | not-required: 対象open Issue/PRなし。今回新設しない |

## Acceptance Criteria

- RC01: 実装済みのAC01〜AC10を保持し、Releaseテストが全件成功する。
- RC02: 版情報・文書・配布資産が0.2.0を示し、以前のdev結果と区別できる。
- RC03: ZIP実体の起動・日本語名保存/再開を確認し、ライセンス文と操作ガイドを含む。
- RC04: コミット・origin/main・v0.2.0タグが同じ公開内容を指す。
- RC05: GitHubのlatest Releaseがv0.2.0で、添付ZIPとSHA256が検証した内容に一致する。
- RC06: 公開後にZIPを取得し、展開先のEXEがSDK設定なしで起動・保存できる。

## Workflow

公開準備→全テスト→ZIP再生成→実体確認→公開前の検証記録→コミット→push→タグ→GitHub Releaseと資産公開→再ダウンロード検証→ObsidianのAct記録完了。既存の実行時データは操作しない。
