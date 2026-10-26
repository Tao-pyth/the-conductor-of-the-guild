# The Conductor of the Guild

固定ジョブを持たない冒険者を、武器・能力値・加護で育て、防衛と侵攻に送り出すゲームです。

**v0.3.0（技術検証版）** は、育てた同じ冒険者で敵陣を攻略する侵攻モードを収録するWindows向けリリースです。初期6名から、防衛1ステージ10Waveと侵攻1ステージ3区画を選び、出撃・帰還・成長・募集・再出撃を遊べます。収録データは5武器種、5技能、3加護、1合成レシピの `tech-*` セットです。既存の技能150件・加護453件・レシピ300件の台帳は、今後の設計・調整資料として保持しています。

[v0.3.0の公開ページ](https://github.com/Tao-pyth/the-conductor-of-the-guild/releases/tag/v0.3.0)から、`TheConductorOfTheGuild-0.3.0-windows-x64.zip` と同名の `.sha256` を入手できます。[更新内容](CHANGELOG.md)、[リリース説明](docs/release_notes_v0.3.0.md)、[正式版の検証状況](docs/verification.md)も確認してください。

## ローカル開発版 0.5.0-dev：派遣・素材納品・金貨

冒険者1〜6名を次のマッチまで派遣し、Rank・現在武器種・所持加護種類から成功率を確認できます。派遣者はそのマッチへ出撃できません。主作戦の勝敗とは独立して、派遣成功なら素材満額、派遣失敗でも一部素材と少量のRank経験値を持ち帰ります。

木材・鉱石・薬草・魔力結晶を、防衛/侵攻と派遣から共有倉庫へ集めます。拠点の「素材・納品」で、4勢力との関係改善、完成武器5種との交換、金貨への換金を選べます。表示された武器実物は、従来のRankイベントで冒険者へ譲渡できます。

通貨名は金貨へ統一しました。任務用1000枚にギルド資金を任意に追加できます。再挑戦で拠出を再徴収せず、任務残金は帰還時に戻りません。保存文書形式は3へバックアップ付きで移行し、旧進行中マッチは既存ルールを保持します。

新しいローカル配布先は `artifacts/guild-economy-v0.5.0-dev/TheConductorOfTheGuild-0.5.0-dev-windows-x64.zip` と対応する `.sha256` です。[機能と初期値](docs/guild_economy_v0.5.0-dev.md)、[今回の実装契約](docs/guild_economy_contract_v0.5.0-dev.md)、[検証状況](docs/guild_economy_verification_v0.5.0-dev.md)を参照してください。正式公開版はv0.3.0のままです。

## 以前のローカル開発版 0.4.0-dev

新規防衛の移動途中の射程接触、成長対象の事前表示、能力・技能・費用の比較、準備中の範囲と次敵予告を追加する開発版です。正式リリースはv0.3.0のまま、今回の作業はローカル実装・動作確認・Windows ZIPまでです。

判断支援を追加した時点の成果物は`artifacts/TheConductorOfTheGuild-0.4.0-dev-windows-x64.zip`と対応する`.sha256`です。その時点では全192テストと、候補ZIPのSDK不要32シナリオに成功しました。当時の最終実体の照合記録は[判断支援の検証記録](docs/player_decision_support_verification_v0.4.0.md)、挙動は[機能仕様](docs/player_decision_support_v0.4.0.md)、完了条件は[ローカル契約](docs/release_contract_v0.4.0.md)を参照してください。

進行中の旧防衛は旧判定を保ち、修正は次の新規防衛へ適用します。侵攻と報酬・成長・敵性能の値は変更しません。

### 公的ギルドのUI改修

同じ0.4.0-devに、明るい紙・木材・紋章を使った公的ギルドの外観を追加します。配置準備は左の冒険者名簿、中央の地図、右の配置記録の3列です。選択者のHP・配置費用・XP購入・復帰・標的優先を確認し、朱印の「出撃承認」でWaveまたは区画を開始します。能力・技能の全文は「能力・技能の詳細」で中央の地図と切り替えて読めます。

フルHD1920×1080を表示の基準とし、HD1280×720でも同じ構成を使います。従来の最小ウィンドウ1000×640も保持します。画面右上の表示ボタンまたはF11で全画面とウィンドウを切り替え、Escでも全画面から戻れます。表示切替ではゲームの進行や保存を変更しません。

UI改修版のローカル成果物は`artifacts/guild-ui-v0.4.0-dev/TheConductorOfTheGuild-0.4.0-dev-windows-x64.zip`と対応する`.sha256`へ保存し、判断支援追加時のZIPとは分けます。今回の確認結果は[UI改修の検証記録](docs/ui_layout_verification_v0.4.0-dev.md)、範囲と完了条件は[UI改修契約](docs/ui_layout_contract_v0.4.0-dev.md)を参照してください。以前の成功数は、UI改修後の動作確認結果を意味しません。

### 0.4.0-dev：UIフィードバック7点の追加修正

HOMEと詳細はプロフィール・能力・Skill・加護・装備の見出し付き領域で表示します。Lv強化は横3択で効果と使いどころを比較でき、加護取得は現在の6枠と候補の新規取得/進化/置換を確認できます。冒険者トークンは凡例に合わせた青です。

作戦報告書は横5名のカードを必要な行数だけ並べ、戦闘終了HP・損耗・帰還後HPとRank上昇見込みを表示します。成長履歴には各出撃を1件にまとめ、Rankイベント種類や取得加護名を残します。画面の行為名は「出撃」にそろえ、防衛/侵攻のモードを併記します。

表示文言は[`data/text/ja`](data/text/ja)に集約しました。[文言カタログの説明](docs/display_text_catalog.md)、[追加契約](docs/ui_feedback_contract_v0.4.0-dev.md)、[今回の検証](docs/ui_feedback_verification_v0.4.0-dev.md)を参照してください。新しいローカルZIPは`artifacts/guild-feedback-v0.4.0-dev/TheConductorOfTheGuild-0.4.0-dev-windows-x64.zip`です。

## 侵攻モード

拠点から侵攻ステージを選び、最大10名を編成して、外郭・内郭・中枢の3区画を攻略します。各冒険者は自動で進軍・戦闘し、区画間に配置・Lv購入・復帰を行います。戦闘中は攻撃優先、一時停止、1倍・2倍速を切り替えられます。全滅または区画の戦闘時間300秒で敗北し、控えによる戦闘中の追加出撃はできません。

操作は[ガイド](docs/user_guide.md)、機構と技術検証用の初期値は[侵攻仕様](docs/invasion_mode_spec_v0.3.0_draft.md)、受入条件と動作確認の証拠は[侵攻検証記録](docs/invasion_verification_v0.3.0.md)を参照してください。敵性能・制限時間・報酬は調整用の値で、製品バランスの完成を意味しません。

公開v0.3.0では旧保存を文書形式2へ移行しました。現在のローカル0.5.0-devは旧1/2をバックアップ後に形式3へ移し、生成済み装備と進行中のマッチを保持します。旧版へ戻す場合は、アプリを閉じて対応する移行前バックアップを復元してください。移行後の保存を旧版へ直接渡して戻すことはできません。

## 遊び方

Windows向けZIPを展開し、フォルダー内の `TheConductorOfTheGuild.exe` を起動してください。配布フォルダーは関連ファイルと一緒に保持してください。遊ぶ際に.NET SDKやGodotエディターを別途インストールする必要はありません。

1. 拠点で冒険者を選び、名前・装備・成長履歴を確認します。
2. 出撃で防衛または侵攻を選びます。侵攻では遠征編成を選び、準備画面の出撃マスへ1名以上を配置します。
3. 準備画面の「出撃承認」を押してWaveまたは区画を開始します。Wave・区画の間は再配置・経験値購入・復帰ができます。
4. 勝敗決定後に帰還すると報酬が確定します。Rankイベントや装備整理を経て再出撃できます。

詳細は [操作ガイド](docs/user_guide.md)、実装と検証の範囲は [技術仕様](docs/technical_specification.md) と [検証結果](docs/verification.md) を参照してください。

日本語名の入力・保存・再読込は自動検証済みです。OSの日本語IMEによる変換・候補確定は未確認です。処理負荷は測定していますが、描画を含む最大負荷での60fpsは保証していません。

## 開発環境

- Godot **4.7.2 .NET版** と同版の .NET Export Templates
- .NET SDK **10.0.402**（`global.json`）
- Windows x64、Compatibility描画、OpenGL 3.3対応環境

通常版GodotではC#プロジェクトを開けないため、.NET版を使用します。必要なNuGetパッケージは初回ビルド時に復元し、各 `packages.lock.json` で固定します。

```powershell
dotnet test tests/Guild.Domain.Tests/Guild.Domain.Tests.csproj
dotnet build game/Guild.Game.csproj
godot --path game --editor
```

SDKやGodotを個別の場所へ配置した場合、実行ファイルのパスを直接指定できます。Windows ZIPの作成例:

```powershell
./scripts/package.ps1 -GodotPath 'C:/tools/Godot/Godot_console.exe' -DotnetPath 'C:/tools/dotnet/dotnet.exe' -Version '0.5.0-dev' -OutputDirectory 'artifacts/guild-economy-v0.5.0-dev'
```

この例の出力先は `artifacts/guild-economy-v0.5.0-dev/TheConductorOfTheGuild-0.5.0-dev-windows-x64.zip` です。旧ローカルZIPを残すため、別の出力先を明示します。スクリプトはローカルの配布物を作成し、外部公開しません。今回の条件は[派遣・納品の契約](docs/guild_economy_contract_v0.5.0-dev.md)、以前の条件は[UI改修契約](docs/ui_layout_contract_v0.4.0-dev.md)と[0.4ローカル契約](docs/release_contract_v0.4.0.md)、公開済みv0.3.0の条件は[当時の公開契約](docs/release_contract_v0.3.0.md)に分けて記録しています。

## 構成

| 場所 | 担当 |
| --- | --- |
| `src/Guild.Domain` | 厳密分数・乱数・定義・戦闘。Godotへの依存なし |
| `src/Guild.Application` | 操作受付、拠点、成長、マッチ管理、画面用情報 |
| `src/Guild.Persistence` | SQLite、一括保存、重複操作防止、履歴、互換性保護 |
| `game` | Godotの画面・入力・描画とWindows出力 |
| `data/tech-content.json` | 承認済みの技術検証用データ |
| `data/guild-economy.json` | 派遣3件・素材4種・納品13件の調整用カタログ |
| `tests/Guild.Domain.Tests` | 数値・戦闘・保存・アプリケーションのテスト |

`main.py` と `core/` の既存ファイル、設計文書、Excel管理台帳は保持しています。
