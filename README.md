# The Conductor of the Guild

固定ジョブを持たない冒険者を、武器・能力値・加護で育てるタワーディフェンスです。

現在の正式リリースは **v0.2.0（技術検証版）** です。初期6名、1ステージ10Wave、5武器種、5技能、3加護、1合成レシピの `tech-*` データで、出撃・帰還・成長・募集・再出撃を遊べます。既存の技能150件・加護453件・レシピ300件の台帳は、今後の設計・調整資料として保持しています。

[v0.2.0の公開ページ](https://github.com/Tao-pyth/the-conductor-of-the-guild/releases/tag/v0.2.0)から、`TheConductorOfTheGuild-0.2.0-windows-x64.zip` をダウンロードしてください。[更新内容](CHANGELOG.md)と[リリース説明](docs/release_notes_v0.2.0.md)も確認できます。

## 遊び方

Windows向けZIPを展開し、フォルダー内の `TheConductorOfTheGuild.exe` を起動してください。配布フォルダーは関連ファイルと一緒に保持してください。遊ぶ際に.NET SDKやGodotエディターを別途インストールする必要はありません。

1. 拠点で冒険者を選び、名前・装備・成長履歴を確認します。
2. 出撃後、冒険者を選んで経路外のマスをクリックし、1名以上を配置します。
3. Wave開始を押します。Wave間は再配置・経験値購入・復帰ができます。
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
./scripts/package.ps1 -GodotPath 'C:/tools/Godot/Godot_console.exe' -DotnetPath 'C:/tools/dotnet/dotnet.exe' -Version '0.2.0'
```

出力先は `artifacts/TheConductorOfTheGuild-0.2.0-windows-x64.zip` です。このスクリプトはローカルの配布物を作成します。公開版はGitタグ `v0.2.0` とGitHub Releaseで管理します。

## 構成

| 場所 | 担当 |
| --- | --- |
| `src/Guild.Domain` | 厳密分数・乱数・定義・戦闘。Godotへの依存なし |
| `src/Guild.Application` | 操作受付、拠点、成長、マッチ管理、画面用情報 |
| `src/Guild.Persistence` | SQLite、一括保存、重複操作防止、履歴、互換性保護 |
| `game` | Godotの画面・入力・描画とWindows出力 |
| `data/tech-content.json` | 承認済みの技術検証用データ |
| `tests/Guild.Domain.Tests` | 数値・戦闘・保存・アプリケーションのテスト |

`main.py` と `core/` の既存ファイル、設計文書、Excel管理台帳は保持しています。
