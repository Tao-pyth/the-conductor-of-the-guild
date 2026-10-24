# Published v0.2.0 compatibility fixtures

Generated on 2026-10-24 using the self-contained Windows executable from the published v0.2.0 package, not the current source implementation. All characters are newly generated test characters; no player save data is included.

- `profile-profile.json`: a newly initialized v1 profile.
- `battle-profile.json`, `battle-session.json`: six test adventurers deployed through the released application's battle smoke scenario. The session contains the original fixed definitions, match checkpoint, and wave-start checkpoint.
- `battle-golden.json`: authoritative end state produced by the **published Guild.Domain.dll** by restoring that wave-start checkpoint, running fixed updates, selecting `firepower` for each level choice, and starting subsequent waves until the defense completes.

The frozen definition hash is `ad265a687145776a58cde75ac0d1db26f7062dc2b26111f4ce06b05356dffeac`. The compatibility test compares every legacy field against this golden state. Added mode-specific fields are allowed; existing state, timers, random state, entities, and definitions must match.

日本語：公開済みの旧版で作った検証専用データです。新版で作り直した期待値に置き換えず、10Waveを終えた戦況と定義hashが変わらないことを確認します。
