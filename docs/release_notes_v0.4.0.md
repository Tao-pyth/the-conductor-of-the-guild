# The Conductor of the Guild 0.4.0-dev — Local development notes

## English

This local development candidate improves defensive range contact and decisions before growth, expedition and deployment. The civic-guild UI revision below is an additional change within the same 0.4.0-dev version. It is not a published v0.4.0 release. The latest formal baseline remains [v0.3.0](https://github.com/Tao-pyth/the-conductor-of-the-guild/releases/tag/v0.3.0).

### Civic-guild UI revision

The bright paper, light wood, guild crest and vermilion approval stamp carry the preparation screen's three-column layout: roster, tactical map and selected adventurer's deployment record. Costs, XP purchase, revival availability and targeting remain separate from long skill descriptions. Open the detailed abilities and skills in the central area, then return to the map. Pressing the approval stamp starts the existing wave or sector immediately; the brief stamp reaction adds no approval step.

The logical reference is Full HD 1920×1080, with the same layout at HD 1280×720 and the existing 1000×640 minimum window retained. Use the display button or F11 to toggle fullscreen; Esc also returns to the previous window size and position. Display mode is not persisted and does not update game state or saves. Square map cells, actual weapon types and the existing range semantics are retained.

The UI-revised local ZIP and SHA256 are stored under `artifacts/guild-ui-v0.4.0-dev`, separately from the earlier decision-support package. Actual revised-UI, display-mode and executable results are recorded in the [UI verification record](ui_layout_verification_v0.4.0-dev.md), under the [additional UI contract](ui_layout_contract_v0.4.0-dev.md). The earlier test counts below are historical decision-support results, not claims about the revised UI.

### Scope

- New defense matches use `defense-v2` to detect range contact along an enemy's movement segment. Range values, the fixed update interval and the existing attack sequence are retained.
- Existing defense saves keep their original rules, and invasion keeps `invasion-v1`. Profile/match document format 2 and SQLite schema 1 are unchanged; mixed or unsupported checkpoint rules are rejected before writing.
- The guild shows the next growth recipient, event Rank, current Rank, equipment, blessings and availability before choosing an event kind. Existing buttons bind the displayed recipient; no extra confirmation dialog is added.
- Adventurer comparisons provide actual saved weapon/skill slots, combat reference values and costs. Next-sortie references and frozen current-match values are distinguished.
- Preparation shows separate alert, action, normal-attack and selected-skill ranges, plus the next wave/sector preview. Circles describe distance and conditions, not a guarantee that an attack will trigger.

Defense continues to allow deployment from the owned roster, with the existing simultaneous limit. Invasion retains its fixed expedition of one to ten adventurers. A reference total for initially deploying the entire selected expedition is not an up-front charge and does not prevent leaving members in reserve.

Enemy stats, rewards, experience, revival, return healing and invasion growth timing are outside this change. The technical content set remains five weapon types, five skills, three blessings, one recipe, one defense stage and one invasion stage.

### Earlier decision-support package verification

The earlier decision-support artifact is `artifacts/TheConductorOfTheGuild-0.4.0-dev-windows-x64.zip` with a matching SHA256 file. Extract a verified local package and run `TheConductorOfTheGuild.exe` with its companion files. Commit, push, tag, GitHub Release and external publication are not part of this contract.

The earlier decision-support run passed all 192 automated tests with no failures or skips. Its Release and ExportRelease builds completed with no warnings or errors. That candidate ZIP passed 32 Windows executable scenarios without SDK settings, including legacy saves, both game modes, retries, settlement, inspection and restart. The [verification record](player_decision_support_verification_v0.4.0.md) identifies the TRX, fresh screenshots and final artifact identity checks. Japanese OS IME conversion remains unverified, and maximum-load rendering at 60fps is not guaranteed.

## 日本語 — 初めてコードを読む方へ

0.4.0-devは、育てる対象や編成・配置を、決める前に確認しやすくするローカル配布候補です。判断支援を追加した時点では全192テスト、候補ZIPのSDK不要32シナリオに成功し、ビルドは警告・エラー0でした。以下の公的ギルドUI改修後の確認結果は[UI検証記録](ui_layout_verification_v0.4.0-dev.md)に分けます。正式公開版はv0.3.0のままで、今回はコミットや外部公開を行いません。

### 明るい公的ギルドと押印

配置準備を、左の冒険者名簿、中央の地図、右の配置記録へ整理しました。白〜生成りの帳票、明るい木材、紋章と青緑の装いに、朱色の「出撃承認」を組み合わせています。費用や主要操作を先に確認でき、能力・技能の全文は中央の地図と切り替えて読めます。押印は従来のWave/区画開始そのもので、追加の承認手順はありません。

フルHD1920×1080を表示基準とし、HD1280×720でも同じ配置を使います。従来の最小1000×640も保持します。表示ボタンまたはF11で全画面へ切り替え、同じボタン・F11・Escで元のウィンドウへ戻れます。切替前の位置と大きさを戻し、ゲームの進行や保存形式は変えません。

今回のローカルZIP/SHA256は`artifacts/guild-ui-v0.4.0-dev`へ保存し、判断支援追加時の旧ZIPを残します。[UI改修契約](ui_layout_contract_v0.4.0-dev.md)と[UI検証記録](ui_layout_verification_v0.4.0-dev.md)に、実際の画面・入力・表示切替・配布物の確認結果を記録します。

新しく始めた防衛では、敵が移動途中に射程へ触れたことを検出します。既に進行中の防衛は当時の判定で再開し、再挑戦も同じ判定です。侵攻のルールは変えません。アプリの版を上げても、保存形式や進行中マッチのルールをまとめて新しいものへ置き換えない仕組みです。

成長ボタンの前には、次に成長する冒険者と今回のイベントRankを表示します。能力・技能・費用も拠点や準備で確認でき、準備画面では警戒・行動・攻撃・技能の範囲と次の敵を区別します。射程円の内側にいても、別の行動条件によって攻撃しない場合があります。

報酬や経験値、復帰・帰還回復、敵性能、侵攻の区画数などは今回調整しません。検証結果とローカルZIPの実体確認は[検証記録](player_decision_support_verification_v0.4.0.md)へ残します。OS日本語IMEの変換・候補確定は未確認で、最大負荷の描画60fpsも保証しません。

## Additional UI feedback revision / 実画面フィードバック対応

The current 0.4.0-dev also groups Home and adventurer information into labeled sections, presents three level-up cards, compares blessing candidates with all six owned slots, and shows a five-column result roster with HP damage and projected Rank changes. Result projections share settlement calculations without mutating saves or RNG. Growth history summarizes each completed sortie once while preserving the original audit records. Japanese presentation text is centralized in data/text/ja JSON catalogs.

HOME・Skill・加護・装備の区切りを追加し、強化は横3択、報告書は横5名で比較できます。Rank上昇は帰還時の見込みで、戦闘終了HPと帰還後HPは別表示です。成長履歴には何を選び、何の加護を取得したかを残します。表示文言は[JSONカタログ](display_text_catalog.md)で確認できます。

今回のWindows ZIPは`artifacts/guild-feedback-v0.4.0-dev`へ保存し、過去のローカルZIPを保持します。[追加契約](ui_feedback_contract_v0.4.0-dev.md)と[検証記録](ui_feedback_verification_v0.4.0-dev.md)を参照してください。正式公開・コミット等は対象外で、baselineはv0.3.0です。
