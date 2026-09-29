# Faro

<img src="assets/faro-icon.png" width="96" align="right" alt="Faro icon">

**アプリの画面を描いて、C# / Java のコードに紐付けるビジュアル UI エディター。**
Figma のように画面を描き、ボタンや一覧をコードのクラス・メソッドに紐付けると、そのまま動くアプリになります。“Faro” はイタリア語で灯台のことです。

[English](#english) · [はじめてガイド](docs/guide.md) · [ダウンロード](https://github.com/thesilver0913/faro-editor/releases)

![Faro](docs/images/main-window.png)

## はじめる

1. [Releases](https://github.com/thesilver0913/faro-editor/releases) から、お使いの OS のインストーラーを入れます(Windows / macOS / Linux)
2. 起動して「新規プロジェクト…」から **Sample** を選ぶと、画面・部品・紐付けの入った見本が開きます
3. あとは [はじめてガイド](docs/guide.md) に沿って、画面の作り方、コードとの紐付け、実行とデバッグまでを順に試せます

## できること

- **画面を描く**:Stack・Wrap・Grid・Overlay で並べる(絶対座標は使いません)。コンポーネントとバリアント、トークン、Fluent / Material 3、ライト / ダーク
- **コードとつなぐ**:ボタンのクリック、文字の表示と入力、一覧、行の選択、画面遷移と値渡しを、コードのメンバーに紐付け。名前を間違えたり変えたりすると赤いバッジで知らせ、候補から直せます
- **確かめる**:キャンバスでのプレビューと実データ表示、サイズやライト / ダークの並べ比べ、実行(C# はホットリロード)、ブレークポイントを使ったデバッグ
- **AI に頼む**:AI チャットに頼むとクラスを作ってくれます(Claude または OpenAI 互換。差分を見て承認したものだけ保存)
- **そのほか**:コードエディタ(補完・エラー表示)、Git、Android APK の出力、コマンドパレット、日本語 / 英語の UI

## 必要なもの

| 用途 | 必要なもの |
|---|---|
| C# のプロジェクト | .NET 10 SDK(Windows のインストーラーが用意します) |
| Java のプロジェクト | JDK 21 と Maven(Faro の 環境設定 › 部品 から入れられます) |
| AI チャット(任意) | 環境変数 `ANTHROPIC_API_KEY` または `OPENAI_API_KEY` |

インストーラーは未署名です。初回は Windows の SmartScreen で「詳細情報 › 実行」、macOS では右クリック › 開く を選んでください。

## ソースからビルドする

.NET 10 SDK が必要です。

```sh
dotnet run --project src/Faro.Editor                        # エディターを起動
dotnet run --project src/Faro.Editor -- samples/HelloFaro   # 見本のプロジェクトを開く
dotnet run --project tests/Faro.Checks                      # セルフチェック
```

## ドキュメント

- [はじめてガイド](docs/guide.md)([English](docs/guide.en.md)):使い方
- [開発者向けドキュメント](docs/development.md):構成、機能の詳細、ビルドとリリース
- [仕様書](ui-editor-tool-spec.md):設計の元になった仕様
- [リリースノート](docs/release-notes)

## ライセンス

[MIT License](LICENSE)。Faro が使っているオープンソースは [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) にあります。

---

## English

**A visual UI editor: draw your app's screens and bind them to your C# or Java code.** Lay out screens like in Figma, bind buttons and lists to your classes and methods, and it runs as a real app (Avalonia for C#, JavaFX for Java). “Faro” is Italian for lighthouse.

- **Get started**: install Faro from [Releases](https://github.com/thesilver0913/faro-editor/releases), create a new project from the **Sample** template, and follow the [Getting Started guide](docs/guide.en.md).
- **Features**: layout without absolute positions, components and variants, tokens, Fluent / Material 3; bindings for events, text, lists, row selection and navigation with broken-binding hints; preview with live data, side-by-side sizes and themes, run with hot reload (C#), debugger; AI chat (Claude or OpenAI-compatible); code editor with completion, Git, Android APKs, English / Japanese UI.
- **Requirements**: .NET 10 SDK for C# projects; JDK 21 and Maven for Java projects (installable from Preferences › Tools); `ANTHROPIC_API_KEY` or `OPENAI_API_KEY` for AI chat. The installers aren't signed yet.
- **Build from source**: `dotnet run --project src/Faro.Editor`.
- **License**: [MIT](LICENSE); third-party components in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
