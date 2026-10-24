# The Conductor of the Guild v0.3.0

## English

This Windows x64 technical validation release adds an invasion mode using the adventurers you have already raised. Choose an expedition of up to ten adventurers, advance through three sectors, defeat guards and fixed turrets, and destroy the final stronghold. The existing ten-wave defense stage remains available from the same guild.

### Included

- Automatic marching, combat and skills, with four targeting priorities, pause and 1×/2× speed.
- Sector preparation for deployment, reserves, level experience purchases and revival. Health, cooldown progress, temporary upgrades and status effects carry between sectors.
- Explicit defeat rules for an eliminated deployed party or a sector time limit. Reserves cannot join during combat; simultaneous party elimination and stronghold destruction counts as defeat.
- Sector-start recovery after interruption, match-start retries, and a single final settlement for rewards, Rank experience and injuries.
- A technical content set: five weapon types, five skills, three blessings, one fusion recipe, one ten-wave defense stage and one three-sector invasion stage.

### Play

Download `TheConductorOfTheGuild-0.3.0-windows-x64.zip` and its `.sha256` file from the [release page](https://github.com/Tao-pyth/the-conductor-of-the-guild/releases/tag/v0.3.0). Extract the ZIP and run `TheConductorOfTheGuild.exe`, keeping its companion files together. Players do not need the Godot editor or .NET SDK.

### Save compatibility

Version 1 profile and match documents are validated before modification, backed up through SQLite's Backup API, and migrated together to document format 2. Generated weapon values, assigned skills, history, frozen content definitions and active defense matches are retained. Unsupported future formats and corrupt ownership, roster or definition data are rejected before migration.

The backup is stored beside the save with a `.before-documents-v2-...backup` suffix. To return to v0.2.0, close the game and restore that pre-migration backup while keeping a separate copy of the newer save. The old version cannot read migrated format 2; the backup contains progress only up to migration. Updating from the already migrated 0.3.0-dev does not regenerate the save.

### Validation and limits

The formal 0.3.0 Release run passed all 123 automated tests, with no failures or skipped tests, and both Release and ExportRelease builds completed without warnings or errors. The 18 invasion acceptance criteria retain their implementation evidence, including comparisons against actual v0.2.0 saves and the published defense implementation. The formal release-candidate ZIP passed seven executable scenarios: invasion startup, victory, defeat and retry, guild return, reopening, new defense victory, and migrated legacy defense victory. These checks used a Windows-only executable search path without SDK settings.

The final package is rebuilt from the publication commit so its assembly information identifies the published source. The release procedure requires another executable check of that package, then a download, SHA256 comparison, startup and save/reopen check of the published assets. Actual final-package and post-publication results are recorded separately in the [verification record](https://github.com/Tao-pyth/the-conductor-of-the-guild/blob/v0.3.0/docs/invasion_verification_v0.3.0.md) and its referenced artifact/Act evidence.

Enemy values, the 300-second sector limit and rewards are technical validation settings, not final production balance. Japanese OS IME conversion and candidate confirmation remain unverified. Maximum-load rendering at 60fps is not guaranteed. Armor, accessories, personality profiles, purification and production UNIQUE balancing are outside this content set. The larger design catalogues remain planning material.

## 日本語 — 初めてコードを読む方へ

v0.3.0では、これまで育てた冒険者で敵陣を攻める「侵攻モード」を追加しました。拠点から侵攻ステージを選び、1〜10名を編成して、外郭・内郭・中枢を攻略します。冒険者は自動で進軍・攻撃し、プレイヤーは初期配置、攻撃優先、区画間の成長や復帰を選びます。従来の防衛10Waveも引き続き遊べます。

区画間ではHP・技能の待ち時間・強化・状態異常を持ち越します。戦闘中に控えを追加したり途中撤退したりする操作はありません。部隊の全滅または区画の戦闘時間300秒で敗北し、全滅と拠点破壊が同じ更新で重なった場合も敗北になります。敗北後の再挑戦と、終了後の帰還・報酬確定は別の操作です。

保存の内部形式は1から2へ変わります。旧データを確認し、同じ保存フォルダーにバックアップを作ってから、プロフィールと進行中マッチをまとめて更新します。生成済み装備や進行中の防衛を作り直さずに引き継ぐための変更です。v0.2.0に戻すときはゲームを閉じて移行前バックアップを復元してください。移行後の進行はその古いバックアップには含まれません。

正式0.3.0でも全123テストを再実行し、成功123・失敗0・スキップ0を確認しました。Release/ExportReleaseビルドは警告・エラー0です。侵攻18項目の受入条件を維持し、正式候補ZIPではSDK設定なしで侵攻開始・勝利・敗北再挑戦・帰還・再開・新規防衛勝利・旧保存移行後の防衛勝利の7シナリオに合格しました。

最終配布物は公開コミットの識別子を含めて再ビルド・再確認し、公開後にもZIPを再取得してhash・起動・保存/再開を照合します。実際の最終確認結果は開発版の記録と分けて、検証記録が示すartifact JSONとAct記録に残します。収録は5武器種・5技能・3加護・1合成レシピ・防衛1ステージ・侵攻1ステージです。敵性能・300秒・報酬は技術検証用の初期値で、全候補の収録や正式バランス完成を意味しません。

OSの日本語IMEによる変換・候補確定は未確認で、最大負荷の描画60fpsも保証していません。詳しい操作は[操作ガイド](https://github.com/Tao-pyth/the-conductor-of-the-guild/blob/v0.3.0/docs/user_guide.md)、仕組みは[技術仕様](https://github.com/Tao-pyth/the-conductor-of-the-guild/blob/v0.3.0/docs/technical_specification.md)、確認範囲は[検証記録](https://github.com/Tao-pyth/the-conductor-of-the-guild/blob/v0.3.0/docs/invasion_verification_v0.3.0.md)を参照してください。
