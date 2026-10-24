# The Conductor of the Guild v0.2.0

## English

This release publishes a playable Windows x64 technical validation build. Start with six adventurers, defend a ten-wave stage, return to the guild, resolve growth events, recruit or manage equipment, and prepare another sortie.

### Included

- Godot 4.7.2 .NET presentation with independent C# game rules and SQLite persistence.
- Fixed 20ms combat updates, projectiles, status effects, automatic skills, separate skill-slot cooldowns, retries, revival, and wave-start recovery.
- Exact rational calculations, arbitrary-size growth values, reproducible random streams, atomic saves, duplicate-operation protection, and growth history.
- A technical content set: five weapon types, five skills, three blessings, one fusion recipe, and one ten-wave stage.

### Play

Download `TheConductorOfTheGuild-0.2.0-windows-x64.zip` from the [release page](https://github.com/Tao-pyth/the-conductor-of-the-guild/releases/tag/v0.2.0), extract the folder, and run `TheConductorOfTheGuild.exe`. Keep its companion files together. Players do not need the Godot editor or .NET SDK.

### Validation and compatibility

The implementation passed 72 automated tests. The packaged application was checked for startup, combat, Japanese name entry, saving, and reopening without a development SDK on the executable search path. Save data keeps the existing format; active matches retain their starting definitions, and existing generated weapon values are preserved.

Japanese OS IME conversion and candidate confirmation remain unverified. Performance measurements depend on the documented workload and machine; maximum-load rendering at 60fps is not guaranteed. Armor, accessories, personality profiles, purification, and production UNIQUE balancing are outside this content set. The larger design catalogues remain planning material.

## 日本語 — 初めてコードを読む方へ

v0.2.0は、ゲームの基本サイクルと保存の仕組みを確認できる技術検証リリースです。ZIPを展開してEXEを起動し、冒険者を配置すると10Waveの防衛戦を遊べます。帰還後は成長イベントや募集・装備整理を行い、再び出撃できます。

画面とゲームルールを分けたため、画面を開かずに計算や戦闘をテストできます。保存では、報酬・資源消費・履歴をまとめて確定し、同じ操作が届いても二重に反映しない仕組みを入れています。途中終了からの再開と、敗北後の再挑戦も別の処理です。

収録内容は5武器種・5技能・3加護・1合成レシピ・1ステージ10Waveです。設計台帳にある全技能・全加護を収録した版ではありません。自動テスト72件と配布物の動作を確認していますが、OSの日本語IMEによる変換・候補確定は未確認で、最大負荷の描画60fpsも保証していません。

操作方法は [操作ガイド](https://github.com/Tao-pyth/the-conductor-of-the-guild/blob/v0.2.0/docs/user_guide.md)、実装の構成は [技術仕様](https://github.com/Tao-pyth/the-conductor-of-the-guild/blob/v0.2.0/docs/technical_specification.md)、確認した範囲は [検証結果](https://github.com/Tao-pyth/the-conductor-of-the-guild/blob/v0.2.0/docs/verification.md) を参照してください。
