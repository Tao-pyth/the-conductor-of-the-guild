# 実装技術仕様 v0.2.0

v0.2.0は、Windows向けの正式な技術検証リリース。実装した技術検証サイクルと収録データを公開範囲とする。

## 採用構成と境界

Godot 4.7.2 .NET版 / Compatibility、C# / .NET 10、Microsoft.Data.Sqlite 10.0.13、JSON、xUnit。SDKとNuGetの版を固定し、画面・操作受付・ゲームルール・保存を分離する。

実装と検証の範囲は [implementation_contract.md](implementation_contract.md)、今回の正式公開条件は [release_contract_v0.2.0.md](release_contract_v0.2.0.md) に記録する。製品要求の入口は [specification_status_v0.1.0.md](specification_status_v0.1.0.md) とし、今回採用した武器更新・表示丸め以外の未確定仕様を確定済みにしない。

## 数値と乱数

- 基礎値・恒久成長・計算済み値を分ける。Rank/XP/恒久成長はBigInteger、途中の補正はRational。
- 固定加算→加算割合→乗算と段階ClampをParameterCalculatorへ集約。最終結果の整数化は用途側で行う。
- 分数は既約、分母正。保存はnumerator/denominator文字列、巨大整数も10進文字列で、SQLite REALへ変換しない。
- 合成の素材III成分は分数のままI=2/3、II=1、III=4/3で継承。表示時だけ単位変換後の値を小数第2位に数学的ceil。
- PCG32-XSH-RR（64bit state/stream、unchecked演算、参照初期化）を使用。戦闘・成長・生成は独立stream、範囲抽選は棄却法。候補と完成武器は結果も保存する。
- パラメータ登録と加護による操作許可を区別する。未対応の作用先や条件を持つ有効コンテンツは明示的に拒否する。

## 戦闘と技能

20msの固定更新。描画のdeltaは蓄積して必要な更新をすべて実行し、2倍速は更新回数を増やす。期限は分数msで保持し、期限後の最初の更新で判定する。時刻順・処理段階・生成順を固定する。

位置はマス単位の厳密分数で移動余りを保持。射程は円形・境界内、通常投射物は非追尾で移動区間上の最初の対象へ命中する。無理数を含む幾何の平方根だけは1/1,000,000マス精度の決定的近似を使い、能力計算の丸めと分離する。

実枠ごとのCD進行率、castID、成分ID、還元済みguardを分離する。同IDのS1/S2搭載は許可し、通常ready/ULT待ちをマッチ開始と復帰時だけ設定する。CD変更では進行率を維持し、多段/派生を新castとして再誘発させない。

## 状態と保存

PlayerProfile（恒久）とMatchSession（固定構成・可変戦闘）を分離する。match/attempt/event/castのIDを独立管理し、再挑戦・同試行の途中再開・対象者の復帰を別操作にする。

SaveStore.Commitは現在状態、消費、履歴、処理済みIDを単一TXで確定する。ExpectedRevisionで古い状態の上書きを拒否し、同じID/同じ内容は前回応答、同じID/違う内容は拒否する。確定後の応答消失ではDBの受付記録で成功を回復する。

SQLiteは単一書込窓口、WAL、synchronous=FULL。保存形式の将来版は変更せず拒否し、構造移行前にはSQLite Backup APIでバックアップする。

履歴は監査・閲覧用で全件保持し、冒険者と10Rank区間を記録する。現在状態の復元には履歴再生を使用しない。

## 定義更新

マッチのSetupに関連する定義実体とhashを保存し、各Wave開始点から参照する。保存形式・コンテンツ・戦闘処理の版を別管理し、対応できない戦闘処理版は明示的に拒否する。

既存武器の生成済み値・SkillID割当は保持し、次出撃で新しい技能効果を参照する。選択済みRankイベントも候補と必要定義を固定し、再起動で再抽選しない。

## 技術検証データ

`data/tech-content.json` を正本とし、ビルドで `game/content` へコピーする。Godot出力はこのJSONをPCKへ含める。アプリはGodot FileAccessで読み、実行用キャッシュを展開して純C#へ渡す。

tech-*の1ステージ10Wave、5武器、5技能、3加護、1レシピを収録。武器予算100、通常性能最低40、生成最大20試行、技能数分布10/25/30/5/15/15%。個別数値と初期報酬1000等は技術検証値として扱う。Excel・既存候補台帳は直接実行せず維持する。

## 公開と検証の境界

Gitタグ `v0.2.0` に対応するWindows x64 ZIPを [GitHub Release](https://github.com/Tao-pyth/the-conductor-of-the-guild/releases/tag/v0.2.0) で公開する。保存形式と戦闘処理の版は公開番号から独立しており、今回の公開番号への昇格だけで保存データを再生成しない。

OS日本語IMEの変換・候補確定は未確認。性能測定値は条件付きの実測であり、最大負荷を描画する場合の60fpsや、すべてのPCの性能を保証しない。実行コマンドと確認範囲は [検証結果](verification.md) を参照する。
