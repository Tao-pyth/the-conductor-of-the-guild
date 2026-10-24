# 実装・検証結果

## v0.2.0 正式公開版 — 2026-10-24

正式公開依頼に基づき、[公開契約](release_contract_v0.2.0.md)に従って0.2.0へ版を昇格した。保存形式・戦闘処理版と収録範囲は維持している。

- Release全自動テスト: **72件成功、失敗0、スキップ0**。
- 検証記録: `artifacts/release-v0.2.0/test-results/release-validation.trx`。
- 公開先: [GitHub Release v0.2.0](https://github.com/Tao-pyth/the-conductor-of-the-guild/releases/tag/v0.2.0)。
- 配布資産: `TheConductorOfTheGuild-0.2.0-windows-x64.zip` と対応するSHA256ファイル。
- Godot/.NET/PCG32/SQLite関連の第三者ライセンス・通知を配布物へ同梱する。
- 公開版ZIPの実体確認と、公開後の再ダウンロード照合は上記公開契約の必須項目とし、結果をOODA Act記録へ保存する。

OSのIME変換未確認と最大負荷60fpsの非保証は、以前の検証範囲から引き継ぐ。ここから下はローカル0.2.0-devの検証履歴であり、正式公開版の資産名・ハッシュとは区別する。

## 0.2.0-dev ローカル検証履歴

検証日: 2026-10-24。正式リリース基準はv0.1.0を維持し、今回はローカル技術検証版を作成した。

## 成果物

- Windows x64 ZIP: `artifacts/TheConductorOfTheGuild-0.2.0-dev-windows-x64.zip`
- サイズ: 79,119,643 bytes
- SHA256: `543D19090673BE1C4A024D6E0EC4513691FBF741B1B8F2F30432A4134F26759D`
- 最終ZIPの別フォルダー展開先: `artifacts/final-zip-validation/TheConductorOfTheGuild/`

Godot 4.7.2 .NET、.NET SDK 10.0.402 / runtime 10.0.13、Microsoft.Data.Sqlite 10.0.13。Release/ExportReleaseを最適化し、Godot出力では終了コードだけでなくERRORログも検出する。

## 自動検証

```powershell
dotnet test tests/Guild.Domain.Tests/Guild.Domain.Tests.csproj --configuration Release --logger 'trx;LogFileName=release-validation.trx' --results-directory artifacts/test-results
```

**72件成功、失敗0、スキップ0。** 記録: `artifacts/test-results/release-validation.trx`。ビルド警告・エラー0。

| 契約 | 確認した内容 |
| --- | --- |
| AC01 | 厳密分数、段階演算、PCG32既知列、重み/生成の前提検証 |
| AC02 | 巨大整数・分数のJSON/SQLite往復、負数を含む表示ceil、巨大比の描画値変換 |
| AC03 | TX途中失敗のrollback、commit後応答消失の回復、同ID再要求、内容不一致・revision競合の拒否 |
| AC04 | 同試行のWave開始点復元、復帰時の恒久/一時補正分離、別試行の再挑戦処理 |
| AC05 | 120fps相当の1倍と30fps相当の2倍で、同じ戦闘時間の状態が一致 |
| AC06 | 同ID2枠、CD進行保持、成分ID/slot重複防止、派生還元guard、1000回のCD変更 |
| AC07 | 旧定義実体と計算済み値の復元、更新後の次出撃への新定義適用 |
| AC08 | 10Wave勝利→精算→成長→募集→再出撃、合成/発見、候補固定/0件、所有権 |
| AC09 | 日本語UI・入力欄→変更ボタン→保存→別プロセス再開、最終ZIP実体の起動 |
| AC10 | 10名・200敵・400投射物の処理時間、CD変更負荷と分母増大の測定 |

配置費用の二重消費、保存確定後の応答消失、Wave保存失敗時の乱数巻戻し、成長履歴の対象者、衰弱の先行Clamp、恒久加護の戦闘への受渡しに回帰検証を追加した。

## Windows画面・配布物

最終ZIPを別フォルダーへ展開し、DOTNET_ROOTなし・dotnetがPATHにない状態で実行した。Guild/Battle/日本語名変更/同DB再起動の4ケースは終了コード0、エラー出力なし。

- 記録: `artifacts/final-zip-validation/verification.json` と同フォルダーのログ・PNG
- 最終ビルド/出力ログ: `artifacts/final-package.log`
- 追加画面: Preparation/LevelChoice/Victory/RankEventも起動・撮影・視認済み（`artifacts/ui-validation/`）

日本語入力の自動検証はGodot内のキーイベントから既存UIの保存経路を通している。**OSの日本語IMEで変換・候補確定する操作は未確認。** AC09の扱いに従い、この残項を明記している。

## 性能

実機: Intel Core i5-12400F、メモリ32GiB、GeForce RTX 3060、Windows x64。

| 条件 | 結果 |
| --- | --- |
| 10冒険者・200敵・400投射物、20更新の準備実行→開始点へ復元→100更新、Release | 258.655ms、平均2.587ms/更新（最終TRX） |
| 同じ負荷の別プロセス測定 | 平均5.541ms/更新 |
| 分数CD補正1000回、進行率と保存値を検証 | 34.159ms、最大分母1626bit（最終TRX） |
| 配布EXEの通常6名デモ、V-Sync有効、4秒の起動検証 | FPS出力144/123、終了0（`fps.stdout.log`） |

負荷試験では400投射物を空き行へ通し、個数を維持して更新・近傍探索を測定する。試験の性能collectionは他テストと並列実行しない。数値は環境・ウォームアップ状態に依存し、描画を含む最大負荷での60fpsや全ステージの最悪時間を保証する値にはしない。

## 範囲と残項

- tech-*の5武器・5技能・3加護・1レシピ・1ステージを収録。候補台帳全件の正式収録は別契約。
- 初期個性、防具/装飾品、浄化、UNIQUE性能の製品実装は今回の対象外。初回クリアの雫支給は記録するが、浄化操作の完成とは扱わない。
- 条件付き加護、未対応の作用先は有効データの読込時に明示拒否する。共通攻撃値0から技能限定加算だけで正値へ戻す特殊定義も明示拒否する。
- 既存Pythonファイル、候補台帳、Excelを保持。U016/U041/U044の承認済み方針を仕様一覧・引継ぎに反映した。
- Commit/Push/PR/Tag/GitHub Release/外部公開は今回のRelease Contractでnot-required。公開操作は行っていない。

ローカル実装・検証・文書・パッケージの契約は完了。OODAの結果は `completed_no_release` とする。
