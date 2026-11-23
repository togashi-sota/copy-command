# COPY COMMAND（コピコマ）

**自分の命令が、敵の武器になる。**
マス目の上で戦う、1人用の戦術ローグライク（PC・日本語）です。
自分が出した命令を、敵もそれぞれの位置からまねます。わざと外した攻撃が、まねた敵には当たる——敵を使って敵を倒すゲームです。

**▶ ブラウザで遊ぶ：https://togashi-sota.github.io/copy-command/**
（パソコンの Chrome／Edge。スマホ・タブレットには対応していません）
タイトル画面の「5分でこのゲームを体験」から、まねのしくみを短く遊べます。

> 個人制作。企画は本人、プログラムの大部分は生成 AI の Claude Code が書きました（下の「作り方」）。

---

## 遊び方

1. 手札から命令（札）を選び、向きを決めて「実行」。
2. 自分が動いたあと、敵も**同じ命令を自分の位置から**まねる。
3. 敵の攻撃の予告（赤いマス）を見ながら、まねられても困らない一手・まねさせて得をする一手を考える。

- 全8階・16部屋・敵7種・ボス「鏡の主」。
- マウスだけで遊べます（キーボードの操作もあり）。

## 作り方（役割分担）

- **本人**：企画（核のルール「自分の命令を敵もまねる」）、ゲームの方向性とルールの判断、実際に遊んでの問題の発見、どの案を採るかの判断。
- **ChatGPT（生成 AI）**：企画の相談・仕様の整理・レビュー。
- **Claude Code（生成 AI）**：コードの実装・自動テスト・シミュレーション・ビルド。
- 絵と音は、すべてこのゲームのためにプログラムで描画・合成したものです（他人の素材・画像生成 AI・音楽生成 AI の出力は使っていません）。

## 動かし方（Unity で開く）

- Unity **6000.6.3f1**（Unity 6）で、このフォルダを開きます。
- 場面：`Assets/_Project/Scenes/Battle.unity`
- 自動テスト：Unity の Test Runner（EditMode）。`Assets/_Project/Tests/EditMode/`

## フォルダの見方

```
Assets/_Project/Scripts/        ゲームの本体（C#）
  Rules/                        ルールの本体（命令の実行・敵がまねる処理。画面を使わないのでテストしやすい）
  CommandExecutor.cs            ルールを実行して、起きた出来事を演出に渡す窓口
  Act1/                         1回の冒険（8階）の流れ・部屋・セーブ・チュートリアル
Assets/_Project/Tests/EditMode/ 自動テスト
Assets/_Project/Editor/         ビルドの道具と、遊びのバランスを調べるシミュレーション
Assets/WebGLTemplates/          ブラウザ版のページ
docs/                           ブラウザで遊べる版（GitHub Pages で公開）
```

## ライセンス

© togashi-sota. All rights reserved.
コードを読むこと、遊ぶこと、採用選考・評価のために社内で共有することは自由です。許可のない再配布・改変しての配布・販売はしないでください。

フォント：BIZ UDPゴシック（Copyright 2022 The BIZ UDGothic Project Authors、SIL Open Font License 1.1）。ライセンスの全文は `Assets/_Project/Resources/Fonts/BIZUDPGothic-OFL.txt`。
ゲームエンジン：Unity（Unity Technologies）。
