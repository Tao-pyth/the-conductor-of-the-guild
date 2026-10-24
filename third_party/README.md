# Third-party notices

This folder accompanies the Windows distribution of The Conductor of the Guild. Upstream license and notice files are retained without editing. `sources.json` records their origins and SHA-256 hashes.

| Component | Distributed version / source | Files |
| --- | --- | --- |
| Godot Engine and Godot C# bindings | 4.7.2-stable | `godot/LICENSE.txt`, `godot/COPYRIGHT.txt`, `godot/AUTHORS.md` |
| .NET Runtime for Windows x64 | Microsoft.NETCore.App.Runtime.win-x64 10.0.13 | `dotnet/LICENSE.TXT`, `dotnet/THIRD-PARTY-NOTICES.TXT` |
| Microsoft.Data.Sqlite / Microsoft.Data.Sqlite.Core | 10.0.13 | `microsoft-data-sqlite/LICENSE.txt` |
| SQLitePCLRaw core, provider.e_sqlite3, bundle_e_sqlite3 and lib.e_sqlite3 | 2.1.13 | `sqlitepclraw/LICENSE.TXT`, `sqlitepclraw/NOTICE.TXT`, `sqlitepclraw/PACKAGE-NOTICES.txt` |
| SQLite native engine | Included through SQLitePCLRaw.lib.e_sqlite3 | `sqlite/LICENSE.md` and the SQLite notice in `sqlitepclraw/NOTICE.TXT` |
| PCG32 reference algorithm | PCG basic C implementation, adapted to C# | `pcg/LICENSE.txt`, `pcg/NOTICE.txt` |

The .NET notices were copied from the runtime NuGet package used by the self-contained export. The .NET SDK and its development tools are not part of the game distribution. SQLitePCLRaw 2.1.13 declares Apache-2.0 in its NuGet package metadata; its upstream common license and notice are included because an identically named version tag was unavailable at retrieval time. The recorded source hashes identify the exact documents included here.

These notices describe third-party components. They do not select a license for the project's original source code or design documents.

## 日本語

ゲームに同梱するエンジン・実行基盤・保存ライブラリ・乱数の参考実装について、提供元のライセンス原文と出典をまとめています。原文は変更せず、取得元とハッシュを記録しています。この一覧によって、プロジェクト独自のコードや設計資料へ新しいライセンスを付けるものではありません。
