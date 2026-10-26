# 派遣・素材納品・金貨 — 0.5.0-dev 検証記録

2026-10-26。実装契約AC01〜10と全requiredの検証完了。以下は今回の最終実体で確認した結果であり、過去版の成功結果とは区別する。

visual result: passed

## 自動検証

最終全296件成功、失敗/skip0。既存207件と、新規Domain10・保存41・Application38の89件を含む。派遣の準備可否/範囲表示も派遣不可へ統一した最終コードで再実行した。TRXは `artifacts/guild-economy-v0.5.0-dev/tests/economy-final.trx`。

初回統合は旧形式2/バックアップ名v2の期待5件が失敗したが、新しい移行仕様に合わせて期待だけ更新し解消。戦闘fixtureは変更していない。全296件の中間統合も成功しており、最終実行と分けて記録した。

Debug/Release/ExportReleaseビルドは警告0/エラー0。ログは同ディレクトリの `debug-build.log`・`release-build.log`・`export-release-build.log`。DebugはGodotSharpEditorを含み配布用と参照が異なるため通常復元、Release/ExportReleaseはlocked-mode復元を確認した。Debug確認後はRelease用lockへ戻し、再度locked-mode成功を確認している。

## 実画面

9シナリオ×3解像度の27経路を分離DBで確認し、修正対象は再実行して成功した。GUIの信号を通した操作と、閲覧時の保存/RNG不変・範囲/ボタン/スクロール到達を検証。数値fixtureはRank20/能力100/金貨5000/素材1000等を明示しており、経済バランスや自然育成の実測ではない。

- `gui-validation/run-139c71ac/verification.json`：元の全27件。21成功、帰還6件のHOME復帰欠落を検出。
- `gui-validation/run-e824aa8c/verification.json`：修正後12件成功。納品3種/不足時無消費、帰還と報告を確認。
- `gui-validation/run-8adbccfc/verification.json`：報告の上下重複を短縮し、帰還6件成功。全文は「最新の帰還報告」に保持。
- `gui-validation/run-21957be7/verification.json`：基礎20%も明示した最終派遣3件成功。

主担当も最小派遣・最小侵攻帰還報告・FHD納品・FHD資金拠出のPNGを直接確認した。指定ウィンドウ寸法は1920×1080/1280×720/1000×640。最小時のPNGはアスペクト保持により1000×562であり、ウィンドウ寸法とは区別してcapture.jsonへ記録した。長文・候補一覧はスクロールで到達し、主要操作が切れない。

## 配布実体

Windows ZIPを別保存先へ作成し、新規展開した実体で全51ケース成功。全ケースでSDK用環境変数とSDKのPATHを外し、専用APPDATA/LOCALAPPDATAと明示DBパスを使用した。配布対象にDB/backupは0件。

| 検証 | 成功数 | authoritative記録（artifacts/guild-economy-v0.5.0-dev配下） |
| --- | ---: | --- |
| 新経済9シナリオ×3解像度 | 27/27 | final-economy/run-e3fb77e9/verification.json |
| HOME/装備/Lv/加護/結果/履歴の回帰 | 7/7 | final-feedback/run-928022dd/verification.json |
| 防衛/侵攻/詳細/表示切替/全画面/長名/50名 | 7/7 | final-civic/run-2ab227ad/verification.json |
| 旧v0.2/0.3保存の移行・旧防衛勝利、敗北再挑戦/帰還、日本語名再開 | 10/10 | final-legacy/run-4fd57c22/verification.json |

GUIでは全員派遣拒否・3種納品と不足時無消費・拠出境界・派遣者の配置拒否・主戦闘帰還・再起動を確認。主戦闘勝敗×派遣成否の全4組と故障時一度限り精算は、実行ファイルの画面ケースだけから一般化せず、Applicationの自動テストで検証している。

初期候補ZIPは85,857,015bytes、SHA256 dfde77e1018e5dcd5eb2a72b50df46801cab32d76e6ec1fd8ba8735989b522d7。説明と過去UIガイド同梱を更新した再exportでは219/220実行関連ファイルが一致し、EXEだけ19bytes変化した。差は埋込lockの依存版0.4→0.5の3文字と対応PCK管理MD5の16bytesで、DLL/コードの差ではない。最終EXE hashは279064d85bf33d2457e21a81a7d1d36ddb42ebc57e992819aeb5404c8447135dで、上表の全51件をこの実体で再実行して成功した。

納品ZIPは、その検証済み実体から本記録だけを最新化して再圧縮し、新規展開後の全220実行関連ファイル一致と重点5ケースの成功を確認した。最終ZIPは85,894,040bytes、SHA256は `72697f681dab669280fe27054d3e0d3da6b1dc3b0e60853c942d8a311648bbb1`。SDK不要の最終確認は上表51件と重点5件の計56件となる。このZIPの再ビルド/再exportは行っていない。最終監査は `artifacts/guild-economy-v0.5.0-dev/final-audit.json` を正本とする。

## Acceptance Criteria

| AC | 証拠 |
| --- | --- |
| 01 成功率・重複・単調性・上限 | EconomyDomainTestsとApplication preview/出発照合、全296件TRX |
| 02 人数/予約/取消/出撃拘束 | 1〜6名(7名所有で6名可)と全員拒否のTheory、両モード不可DTO/コマンド、GUI27 |
| 03 独立4組/HP効果/XP | MainBattleAndDispatchResultsSettleIndependently...4組、参加者RankXP混入なし、GUI帰還 |
| 04 再挑戦/再開/再送/照会 | Roll/金貨維持・最終試行素材・純粋View/preview・Commit故障、GUI再開 |
| 05 3納品/実物/対立維持 | 納品種別Theoryと従来譲渡、装備実物/メッセージ一致、関係上限/対立保持、GUI納品 |
| 06 金貨 | 0/全額/不足/負数/巨大整数Theory、残金不返還、GUI拠出 |
| 07 保存互換 | EconomyPersistenceTests41件と旧回帰、配布実体で旧保存4ケース/バックアップ |
| 08 全回帰/build | 全296件失敗/skip0、Debug/Release/ExportRelease警告エラー0 |
| 09 実画面 | 3解像度の新経済27経路と直接PNG確認、実寸capture metadata |
| 10 版/説明/ZIP | 0.5.0-dev/0.5.0.0、英日説明、ZIPとSHA256、SDK不要51ケースと最終照合 |

実装時の契約ではCommit/Push/PR/Tag/正式公開はnot-requiredだった。後続のGitHub更新依頼については次節に記録する。旧ZIPを保持し、正式baseline v0.3.0を変更していない。実装作業単位は契約/Obsidianで追跡し、Issueの登録/変更は行っていない。

## GitHub反映に向けた追加検証

2026-10-26、ユーザーの後続依頼により、0.5.0-dev実装と必要な関連文書をGitHubのmainへ反映する。前提となる0.4.0-devの判断支援/UI/文言を含め、独立したストーリー案・別UI提案・ADR等の変更は対象外とする。正式リリースやZIP公開は含まない。

コミット対象のindexだけを `artifacts/github-update-0.5.0-dev/staged-tree` へ展開し、Release構成とlocked-mode復元で全296テスト成功（失敗/skip0）、ゲームのReleaseビルドも警告0/エラー0を確認した。TRXは `artifacts/github-update-0.5.0-dev/tests/github-staged.trx`。配布ZIPは変更せず、今回の文書修正はリポジトリ上の記録に限定する。対象文書の相対リンク不足0、必要な画像/JSON/テスト素材の同梱と、DB/配布バイナリの混入がないことも確認した。

## 残る限界

実操作込みの経済バランス/満足感、OS日本語IMEの変換、描画最大負荷60fpsは未検証。実ユーザーDBは使用していない。
