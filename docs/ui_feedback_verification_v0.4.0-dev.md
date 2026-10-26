# UIフィードバック7点の検証記録

2026-10-25。対象0.4.0-dev、正式baseline v0.3.0。完了条件は[追加ローカル契約](ui_feedback_contract_v0.4.0-dev.md)。本記録は今回の実測で更新し、以前の成功件数を今回の結果へ転用しません。

## 実装と自動テスト

- 指摘①〜⑦を実装。日本語カタログ、青い味方、HOME等の区分、Lv横3択、現在所持と候補の加護比較、結果5列、1出撃1件の成長履歴を確認。
- 全207テスト成功、失敗0、skip0。既存192件＋文言4件＋結果/履歴/加護11件。TRX：`artifacts/guild-feedback-v0.4.0-dev/test-results/feedback-final.trx`。
- 結果照会は実帰還精算とRank/HP/衰弱/資源が一致し、閲覧で保存/履歴/RNGを変更しない。再送で二重精算しない。侵攻非出撃者の正負の次回効果によるHP表示も同じ保存基準へ統一した。
- locked-mode Release復元成功。Debug/Release/ExportReleaseは警告0・エラー0。ログは同成果物フォルダー直下の`locked-restore.log`、`debug-build.log`、`release-build.log`、`export-release-build.log`。

## ソース実画面

- 追加UI21条件成功：`source-feedback/run-05590297/verification.json`。HOME/装備/50名、Lv3列、加護0/1/6枠、両モード結果/50名10行、履歴、FHD/HD/最小サイズを確認。
- 従来UI17条件成功：`source-civic/run-a876c0ac/verification.json`。両モード準備、詳細/フォーカス、F11/Esc/最大化復元、押印、XP購入、長名/50名を確認。
- 最終修正と操作の重点6条件成功：`source-final/run-6c8ce0e4/verification.json`。装備タブ実押下/末尾到達、加護の新規取得/進化/6枠置換を実ボタンから実行して保存・Revision増分1を確認、結果の先頭5カード横境界、履歴表示番号を確認。
- 準備のSkill枠追加で下端メッセージが切れるP2を発見。右欄ラベルを統合して31pxを確保し、フッター境界も検査対象へ追加。最終3重点条件成功：`source-footer-final/run-ddad973f/verification.json`。FHD/最小/詳細で本文とフッターを視認。
- 初回失敗：新しい6枠試験fixtureのIDにtech-接頭辞がなく、定義検証で拒否された。試験定義を修正し、正常な通常抽選を使用する新規保存で再確認した。製品定義は変更していない。

試験は明示した隔離DBとAPPDATA/LOCALAPPDATAのみ。50名試験は隔離資源を投入して正規募集/枠拡張、6枠試験は独立した追加の技術検証加護定義を使用し、製品データの件数・バランスの証拠と混同しない。

## 視覚QA

主担当と独立担当がPNGを視認。HOMEの名簿過剰幅を修正し、ラベル付きプロフィール/能力/Skill/加護/装備、横3択の同一行・効果/用途/ボタン、現在6枠と候補新規/進化、5列結果のHP0/損耗/帰還HP/Rank↑、50名10行末尾、青トークンと武器画像を確認。既知P2は修正後再取得し、未解消P0/P1/P2なし。[視覚QA](../design-qa.md)にも記録。

## Windows配布実体

新規展開したWindows実体（`candidate-851660ff/TheConductorOfTheGuild`）で**全73条件成功**。各条件は終了コード0・期待マーカー一致・実行エラーなし・SDK設定なし・隔離試験DBのみ。

| 検証群 | 成功数 | 記録（artifacts/guild-feedback-v0.4.0-dev配下） |
| --- | --- | --- |
| 今回の追加UI・取得操作 | 24/24 | `package-feedback/run-593a4914/verification.json` |
| 準備/表示切替/押印等 | 17/17 | `package-civic/run-6e375896/verification.json` |
| 保存/再開/旧形式/両モード等 | 32/32 | `package-regression/run-3073e9a3/verification.json` |

全207テストとDebug/Release/ExportRelease成功を含むコードを配布。最終配布のHOME幅、青い味方、Skill領域、3列強化、所持6枠、Rank等の見込み表記、履歴0001をPNGで確認した。FHD/HD/最小のwindow/viewport/PNG実寸は各capture.jsonを参照。50名10行の末尾にも到達できる。

配布には確認用JSON3ファイル（application188/UI289/history34の計511キー）と説明書を同梱する。JSONはApplication DLLへ埋込済で、同梱コピーは文言確認用。文言を変える場合はソースを編集して再ビルドする。

最終の記録を同梱する際は、検証済み実行関連211ファイルのhashを維持する。JSON形式のruntimeconfig/deps等も照合に含め、説明書と確認用テキストJSONだけを実行関連から除く。最終ZIP/hash/実行ファイル一致/再展開後の起動確認は、自己参照hashを避けて同フォルダーの`final-manifest.json`へ記録する。最終配布の`verification.json`も合わせて参照する。

旧UI ZIPのSHA256は`45b55c5535d4750c3dcb0902fda4352c5029d8d18fd9ff148cd066c0fc2c783d`で維持した。保存先は新しい`artifacts/guild-feedback-v0.4.0-dev/TheConductorOfTheGuild-0.4.0-dev-windows-x64.zip`と同名`.sha256`。

## 受入条件の対応

| AC | 根拠 |
| --- | --- |
| 01 | 511キーJSON、欠落/重複/書式のテスト、配布24条件 |
| 02 | 最終StampDefense等の青トークン・出撃見出し、ゲームモード保持 |
| 03 | 最終HOME/装備末尾/準備詳細/戦闘のラベル付き領域とSkill |
| 04 | LevelFhd/Hd/Minimumの横3列、効果/用途/選択、既存XP操作 |
| 05 | 加護0/1/6枠、日本語効果、実ボタンでの新規/進化/置換と保存 |
| 06 | 結果6名2行/50名10行、5列横境界、HP/Rank見込みと末尾到達 |
| 07 | 4種のモード/勝敗で精算一致・照会不変・再送、非出撃正負効果の境界2件 |
| 08 | 最終出撃1件、具体的選択名、旧監査保持・旧不明情報を捏造しない・再読込 |
| 09 | 全207テスト、各Build警告/エラー0、配布73条件、視覚P2修正後確認 |
| 10 | 別保存先ZIP/SHA256、文書/JSON同梱、実行関連照合と最終起動のmanifest |

## 検証の限界

OS日本語IMEの一般操作、読み上げ、全モニター/表示倍率、最大負荷60fpsの保証は含めない。実ユーザーDBは使用していない。正式公開/Commit/Push/PR/Tagは対象外で、baselineはv0.3.0を維持。
