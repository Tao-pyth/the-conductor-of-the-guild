# The Conductor of the Guild 0.5.0-dev

## English

This local development build adds one optional dispatch party of one to six adventurers, four shared materials, thirteen delivery contracts, faction cooperation, and optional guild-gold contributions to the 1,000-gold operation budget. Dispatch success uses Rank, the equipped weapon type, and currently held blessing types. Dispatch rewards are independent of the main operation outcome; a failed dispatch still returns some materials.

New operation rewards and dispatch results settle once on guild return. Retries keep the fixed dispatch result and do not charge the contribution again. Equipment delivery shows the actual offered weapon and grants it to guild inventory for the existing Rank-event transfer. Unused operation gold is never returned.

Save documents migrate from versions 1/2 to 3 after validation and backup. Existing active operations retain their previous behavior. This is a local ZIP, not a published v0.5.0 release; the formal baseline remains v0.3.0. Current validation evidence and remaining limits are recorded in the bundled verification guide.

## 日本語 — 初めて遊ぶ方・コードを読む方へ

冒険者を戦闘と派遣へ分け、集めた素材を関係改善・装備・金貨のどれに使うか選べるようにします。派遣は次の出撃で始まり、帰還までその冒険者を戦闘へ出せません。成功率と成功/失敗の持帰り素材を事前に確認できます。

出撃時の任務金貨1000枚に、ギルドの貯金を追加できます。再挑戦で追加金貨を再徴収しませんが、任務で余った金貨は帰還時に戻りません。契約報酬は残金とは別です。

保存の内部形式が3になり、旧形式は検査してバックアップを作ってからまとめて移します。古い進行中任務は途中で新ルールへ変えません。派遣や納品の初期値は調整用で、物語全体や全装備・全加護を収録した意味ではありません。

今回の実装・検証の実績は[検証記録](guild_economy_verification_v0.5.0-dev.md)、操作と初期値は[機能説明](guild_economy_v0.5.0-dev.md)を参照してください。
