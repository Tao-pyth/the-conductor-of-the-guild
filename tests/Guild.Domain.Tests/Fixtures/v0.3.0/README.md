# Published v0.3.0 defense compatibility fixtures

These expectations were generated on 2026-10-25 (JST) with the self-contained assemblies from the [published v0.3.0 Windows package](https://github.com/Tao-pyth/the-conductor-of-the-guild/releases/tag/v0.3.0). They were **not generated with the current source implementation**. All six adventurers belong to a newly initialized test database; no player save is included.

## Released binaries

- Published commit / `AssemblyInformationalVersion`: `b7da6871f44f12807a879f92434b63ec6c9f4e65` / `0.3.0+b7da6871f44f12807a879f92434b63ec6c9f4e65`.
- `Guild.Domain.dll` SHA256: `66c198d49c54115b77aef9b75fde7345b9d21fe6dd0cd160f603bdb08fc47f6a`.
- `Guild.Application.dll` SHA256: `eb1971c5733eb63b6a8f7234d24b87be7376de3673e82d5b14a12bd4f685de20`.
- `Guild.Persistence.dll`, `Microsoft.Data.Sqlite.dll`, SQLitePCLRaw assemblies and `e_sqlite3.dll` came from that same package. The extraction harness referenced the released DLLs directly, rather than referencing repository projects.

## Files and integrity

| File | Origin | SHA256 |
| --- | --- | --- |
| `battle-session.json` | Released Application's saved session immediately after Wave 1 starts | `abb1dad3e43b6b81b92603a23c3662517ee50f8393b4778dd3565223b1a4861b` |
| `battle-golden.json` | Released Domain's complete ten-wave result, restored from that session | `4a5b2c7d1616fcb4fc960bc1126d5bb282bbb9251fc6a87253b522645a66f024` |

The fixed content hash is `5cb04688d790554e6478309b40d7b13c796c228ecde7cf0991926cdcea55ed1d`. Document format is 2, simulation version is 1, and the saved defense `RuleSetId` is null. The released content includes both modes; this fixture exercises its original defense rule.

## Capture procedure

1. Extract the published Windows ZIP and verify the DLL hashes above. Use its released `tech-content.json` payload. Build a separate .NET 10 console harness with direct `<Reference>` entries to those DLLs and the bundled SQLite dependencies. Do not load the development build through a project reference.
2. Construct the released `GameController` with a new, empty test-only SQLite path. Keep its six initial adventurers, generated weapons, HP, stats and RNG streams. Do not grant money, modify abilities, equip rewards, purchase levels, or revive anyone.
3. Send `StartMatch`, then `Deploy` for the six adventurers in their initial display order at `(2,4), (4,6), (6,4), (8,6), (10,4), (12,6)`. Send `StartWave`.
4. Read the `session` document from SQLite (`SELECT value FROM documents WHERE key='session'`) and store the unmodified JSON as `battle-session.json`.
5. Deserialize that document's `waveStart` with the **released** `DomainJson.Options`. Call the released `BattleSimulation.Restore(snapshot)`.
6. Repeatedly call `Step()`. Between waves, resolve every `PendingLevelChoices` through `GrantLevel(actor.Id, "firepower")`, in ascending `SpawnSequence`, then call `BeginWave()`. Stop at victory/defeat, with a safety bound of 30,000 fixed updates. No current implementation is used to calculate the expected end state.
7. Serialize the released `Capture()` result as `battle-golden.json`. The recorded result is victory at Wave 10, `TimeMs=80160`, `DefenseHp=20`, after 4,008 updates.

This is a deterministic engine compatibility oracle. Its fixed `firepower` choice policy isolates simulation behavior; it is not an observation of random UI level-choice offerings, user play time, or a win-rate estimate.

## Replay and maintenance

`DefenseRangeV2Tests.PublishedV030GoldenRetainsEveryLegacyFieldAcrossAllTenWaves` replays the supplied starting snapshot in the current implementation and recursively checks every field written by the released assembly. Newly added fields may exist, but legacy physics, timers, RNG, entities and definitions must match. The explicit `defense-v1` alias is also replayed; only its identifier is normalized for that comparison.

For independent reproduction, feed this supplied `waveStart` to the verified released Domain DLL and use the same fixed-update/level-choice loop. Newly initializing a test DB creates new GUIDs, so a fresh capture need not have the same byte hashes. Replaying the supplied snapshot must reproduce the supplied golden fields. Do not replace the expectation with output from the implementation under test to make a failed compatibility check pass.

日本語：公開済みv0.3.0のDLLで作った検証専用の開始点と期待値です。保存済みの定義・位置・時刻・乱数・旧防衛ルールを守れているかを確認します。実ユーザーのデータや、新版の処理で作り直した期待値は使用していません。
