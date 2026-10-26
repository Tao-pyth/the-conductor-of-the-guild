# 公的ギルド配置準備UIの視覚QA

final result: passed

対象：実装された0.4.0-devの配置準備画面。ゲームの全受入条件や配布実体の検証結果は別途[UI検証記録](docs/ui_layout_verification_v0.4.0-dev.md)を参照する。

## 比較対象と正規化

- Source visual truth: `C:/Users/takuy/.codex/visualizations/2026/10/25/01a170d7-f125-7712-9b3e-ca3c1ecf4604/guild-fullhd-ui/guild-preparation-master.png`（1672×941、FHD1920×1080の設計イメージ）。
- Implementation: `artifacts/guild-ui-v0.4.0-dev/ui-visual-final/run-31a03a2e/DefenseFhd.png`（1920×1080）、DefenseHd.png（1280×720）、DefenseMinimum.png（1000×562）。
- State: 防衛Wave1準備、ユイを(8,4)へ配置、HP190/190、資源975、名簿6名/配置1名、通常攻撃の範囲表示。
- Game viewport: 論理1920×1080、CSS/device densityはネイティブGodotのため該当しない。PNGの解像度とwindowサイズはcapture.jsonへ別々に記録する。
- Full-view comparison input: 2026-10-25、同一functions.execのview_image結果へ参照画像と上記実画面3枚をまとめて入力し、原寸表示で視認。参照の生成PNGはほぼ16:9として、画面全体の比率・情報階層で比較した。別画像を合成したと称していない。
- Focused regions: 原寸フルビューで左名簿、右費用/操作、右下朱印、地図の範囲/座標を個別に読めたため、追加の切り抜き画像は作成しなかった。実入力・境界メタデータの検査を併用。

## 比較履歴と修正

### 初回：修正が必要

証拠：`artifacts/guild-ui-v0.4.0-dev/ui-first/run-c66136fe/DefenseHd.png`。

- [P2] 名簿の文字列が長く、配置状態が省略される。HPバーも参照より不足。修正：名前と状態を独立した行内領域にし、HP値/バーを追加。長い名前は名前領域だけ省略し、全文を詳細へ残す。
- [P2] 公的な帳票の見出しと朱印の強弱が弱い。修正：見出しへ明朝系SystemFont、ヘッダーを1行へ整理し、朱印の文字を大きくした。
- [P2] 地図の拠点が装飾のない四角で、採用画像のギルド拠点との対応が弱い。修正：透明の石造門/青緑旗の画像素材を実装し、実データのラベルを添えた。

### 修正後：passed

上記ui-visual-finalの実画面を再取得し、参照画像と同じ入力で比較。親と独立エージェントの双方が、P0/P1/P2の未解消課題を認めないことを確認した。

## Fidelity surfaces

| 項目 | 評価 |
| --- | --- |
| Fonts / typography | 見出しと朱印は明朝系、本文は日本語UI用sans。FHDを基準にHDへ同じ構成で縮尺。名前/状態/HP/費用/開始を読め、重なりなし。長文全文は中央詳細のスクロールへ分離 |
| Spacing / layout | 左名簿・中央地図・右配置記録を維持。地図を優先して名簿を参照より狭くしたのはDecideの領域配分に基づく調整。主要操作は画面内。三領域と描画/入力境界を実測 |
| Colors / tokens | 明るい白〜生成り/淡木枠/青緑/濃色本文、朱色の開始。暗い面が残る箇所や読めないコントラストを認めない。意味は文言/線種/形も併用 |
| Image quality / assets | 紙/木、紋章、ギルド挿絵、木製印鑑、武器、防衛門は実画像素材。透過と縦横比を保持。画面全体の焼き込み画像を操作UIとして貼っていない |
| Copy / content | 実際の名前・HP・武器種・費用・技能枠を表示。ミナは実データの槌で、参照画像の槍に合わせてゲームデータを変更しない。Rank/Lv・通貨・参考/実値・範囲の条件を区別 |

## Interaction evidence

- source統合17シナリオすべて成功、最終アセット反映後の重点5シナリオすべて成功。
- 名簿・範囲/技能範囲・詳細のフォーカス/スクロール・マップ入力・XP購入・標的優先・再配置・朱印開始を検証。
- 表示ボタン/F11/Escで全画面とウィンドウを往復し、元サイズ/位置、保存全文/RNGの不変を確認。
- 採用した情報構造、配色、画像素材、文字階層を実UIで評価した。描画の正方マスと範囲半径はコード/メタデータで別に検証。
- Windows全画面はExclusiveFullscreenの実モード。ブラウザーの画像プレビューをゲームの確認に代用していない。
- 最終コードのWindows実体でもCivic17条件と既存32回帰すべて成功。`final-package-civic/run-1135a63e`のDefenseFhd/InvasionFhdを親が再視認した。FHD/HDの実画面は`artifacts/guild-ui-v0.4.0-dev/ui-preview-fhd.png`と`ui-preview-hd.png`。通常ウィンドウ/最大化/全画面の復元と保存/RNG不変も成功した。

## Follow-up polish

- [P3] ヘッダーのギルド挿絵は参照より控えめ。主要情報/押印の優先を保てており、装飾の追加を必須にしない。
- [P3] 従来最小1000×640では補助説明が小さめ。主基準HD以上は読め、最小でも主要操作/文字の欠けや重なりは認めない。

## Implementation checklist

- [x] 参照と実装の両方を新規実画面で比較。
- [x] 初回P2を修正し、修正後の証拠を確認。
- [x] 実素材、文字階層、主要操作、3列、HD/最小、地図を確認。
- [x] 視覚QAの未解消P0/P1/P2なし。

実モニターでの全色覚条件、OS IME、読み上げ、全Windows倍率、最大描画負荷の保証は今回の視覚QAに含まない。

## 追加：実画面フィードバック7点（2026-10-25）

対象をHOME/Skill/Lv強化/加護取得/報告書/成長履歴/出撃画面へ広げた。ユーザー添付8枚を旧画面の証拠とし、今回の実画面は`artifacts/guild-feedback-v0.4.0-dev`配下へ保存した。旧QAの完了認定を今回へ流用していない。

- HOME過剰な名簿幅を300pxに制限して詳細へ幅を配分。プロフィール・能力・Skill・加護・装備・名称を見出し付き領域にした。装備タブの実押下と下端への到達も確認。
- Lv強化は横3カードに効果・用途・選択を配置。FHD/HD/最小で同じ行に収まり、重なりなし。
- 加護は現在6枠と候補を同画面で比較し、日本語効果・新規/進化/置換を区別。0/1/6枠と実取得/進化/置換の操作を確認。
- 結果は5列、6名2行/50名10行。HP0・損耗・帰還後HPとRank上昇見込みが読め、50名の末尾へ到達できる。
- 青い味方トークンと武器画像、Skill領域を準備/戦闘で確認。準備の下端メッセージ切れを発見して31px削減し、`source-footer-final/run-ddad973f`の再取得で解消を確認。
- 履歴は出撃1件と具体的な選択に絞り、表示連番は0001から付ける。監査連番は保存側で保持。

最終sourceの比較：`source-final/run-6c8ce0e4/{EquipmentFhd,ResultDefenseFhd,HistoryFhd}.png`、`source-footer-final/run-ddad973f/DefenseFhd.png`。3列/6枠/最小と50名は`source-feedback/run-05590297`。主担当と独立担当で視認し、最終修正後に未解消P0/P1/P2なし。実配布の確認は[追加検証記録](docs/ui_feedback_verification_v0.4.0-dev.md)と配布manifestを参照。

最終Windows配布実体も24追加UI＋17準備等＋32既存回帰の73条件に成功。配布PNG（package-feedback/run-593a4914、package-civic/run-6e375896）を主担当が視認し、HOME幅/青トークン/Skill/3列/所持6枠/5列結果と見込み表記/履歴0001を確認した。独立担当は先行実画面と最終HOME/準備フッターまで確認。未解消P0/P1/P2なし。文書反映後は実行関連ファイルの一致を最終manifestで照合する。
