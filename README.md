# Faro

<img src="assets/faro-icon.png" width="96" align="right" alt="Faro icon">

「Figma × UI Binding × Vibe Coding」を統合するUI/UXビジュアルエディタのプロトタイプです。
仕様は [`ui-editor-tool-spec.md`](ui-editor-tool-spec.md) を参照してください。

## 構成

| パス | 内容 |
|---|---|
| `src/Faro.Runtime` | ランタイムバインダー。UIグラフ(XML)→Avaloniaコントロール構築、`<Bind>`のReflection解決、生存期間・永続化、画面遷移 |
| `src/Faro.Editor` | エディター本体。Roslynレジストリ抽出、紐付け検証(赤バッジ+候補サジェスト)、コンポーネント明示同期、Dock 3ペイン、コードエディタ(AvaloniaEdit + csharp-ls)、バイブコーディング(チャット) |
| `samples/HelloFaro` | 仕様書の例をそのまま使ったFaroプロジェクト(UI/ Source/ Bindings/ Assets/) |
| `tests/Faro.Checks` | assertベースのセルフチェック |
| `spikes/HotReloadMethodInfo` | 仕様11.5のリスク検証 |

## 使い方

.NET 10 SDK が必要です。

```sh
dotnet run --project tests/Faro.Checks                      # セルフチェック
dotnet run --project src/Faro.Editor -- samples/HelloFaro   # エディターでプロジェクトを開く
cd samples/HelloFaro && dotnet watch run                    # アプリをホットリロード付きで実行(エディターのRunボタンと同じ)
```

## コードエディタ

- AvaloniaEdit + C#言語サーバー [csharp-ls](https://github.com/razzmatazz/csharp-language-server) をLSP(stdio)で接続。補完(`.` または Ctrl+Space)とエラーの波線表示
- csharp-ls は初回起動時に `ApplicationData/Faro/tools` へ固定バージョンで自動インストール(`dotnet tool install`)。クラッシュ時は3回まで自動再起動
- **保存(Ctrl+S)時のリネーム追従**:最後に保存した内容と比べ、クラス1つ／同じクラス内の同種メンバー1つが消えて1つ増えた場合をリネームとみなし、`Bindings/` の `target` を書き換える。判定できない変更は追従せず、赤バッジで知らせる
- 配色は標準の C# `.xshd`(明るい背景)か TextMate テーマ(環境設定で選択)

## バイブコーディング(チャット)

- プロバイダを切替可能: **Claude**(公式 C# SDK、既定モデル `claude-opus-5`、拒否時は Opus 4.8 へ自動フォールバック)／**OpenAI 互換**(OpenAI・Ollama・LM Studio 等、Base URL を指定)
- **API キーは環境変数のみ**(`ANTHROPIC_API_KEY` / `OPENAI_API_KEY`)。Faro はキーをディスクに書かない。プロバイダ・モデル名・Base URL は `ApplicationData/Faro/settings.json` に保存
- 生成されたファイルは差分表示 → **承認で反映**。次の場合は承認できない:Roslyn で構文エラーがある／対象クラスにコードエディタの未保存編集がある(依頼文にそのクラス名が含まれる場合は送信自体を止める)
- 書き込み先は `Source/` 配下の `.cs` のみ(モデル出力のパスは検証する)
- 承認した変更はコードエディタのバッファ経由で保存 → 手動編集と同じ Undo 履歴に入り(Ctrl+Z で戻せる)、リネーム追従も効く
- 新規クラスの生存期間(ScreenScoped/Singleton/Transient)と永続化をチャット欄で選択。生成クラスは `FaroObject` を継承し、変更通知を埋め込む
- キャンバスの赤バッジ(メンバーが存在しない紐付け)をクリックすると、Node・イベント／プロパティと型を埋めた依頼文がチャットに入る

## メニューと環境設定

- **File**:プロジェクトを開く(Ctrl+O、Faro を再起動して開き直す)／すべて保存(Ctrl+Shift+S)／実行(F5)／終了。未保存のコードがあれば確認してから閉じる
- **Edit**:元に戻す(Ctrl+Z)／やり直し(Ctrl+Y・Ctrl+Shift+Z)／コンポーネント同期／環境設定(Ctrl+,)
- **Undo/Redo(仕様§10)**:画面左上の ↶ ↷ とショートカット。最後に操作したペインで対象が切り替わる(右上に表示)
  - キャンバス → **UI グラフ履歴**:Faro が UI/・Bindings/ に書いた変更(現状はコンポーネント同期)を 1 手ずつ。Faro 外でファイルが変更されていたら上書きせずに中止
  - コードエディタ/チャット → **コード履歴**:表示中ファイルの履歴(手動編集と承認した AI 生成が合流)
- **Select**:すべての Node／選択解除(Ctrl+Shift+A)／ID で Node を選択／壊れた紐付けの Node を選択。キャンバスではクリックで Node を選択(Shift+クリックで追加・解除)。デザイン中はボタン等は反応しない
- **Window**:Canvas／Code／Vibe Coding の各パネルを前面に／全画面(F11)
- **Help**:Faro について
- **環境設定**(`ApplicationData/Faro/settings.json`、アプリ全体で共通)
  - 環境変数:`ANTHROPIC_API_KEY` / `OPENAI_API_KEY` の設定有無を表示(末尾4文字のみ)と設定方法。画面から編集はできない(キーはディスクに書かない方針)
  - テーマ:アプリ(System/Dark/Light)＋ユーザーの AXAML ResourceDictionary ファイル／コードエディタ(標準 `.xshd`、TextMate 内蔵テーマ、`.tmTheme` または VS Code の JSON テーマファイル)
  - プラグイン:今後対応(仕様§12 のとおりプロトタイプ範囲外)

## スキーマ補足(仕様書からの追加決定)

- サイジングは軸ごと: `widthSizing` / `heightSizing`(`Fill`/`Hug`/`Fixed`)。`sizing` は両軸共通の省略形、未指定は `Hug`。`Fixed` は `width` / `height` 属性で値を指定
- `UI/` の各XMLはルートが `<UIGraph>` か `<ComponentDef>` のどちらか1つ
- インスタンスはマスターのスナップショット(`<Node>`)を内部に保持し、エディターの「Sync components」を押すまで更新されない
- 生存期間・永続化はC#属性で指定: `[FaroLifetime(Lifetime.Singleton, Persistent = true)]`(未指定はScreenScoped)。永続化データは `ApplicationData/Faro/<アプリ名>/<クラス名>.json`
- `Navigate:Screen.Detail` の `Screen.` は省略可能な接頭辞で、UIGraphの `id="Detail"` を指す
- `Container.Grid` は `columns` 属性を持つ均等グリッド

## リスク検証結果(仕様11.5)

- **ホットリロードとReflectionキャッシュの整合性 → 問題なし。** `dotnet watch` でメソッド本体を書き換えると、書き換え前に取得した `MethodInfo` がそのまま新しい本体を実行し、ハンドルも同一(`spikes/HotReloadMethodInfo/run.sh` で再現可能)。バインダーは `MethodInfo` をキャッシュしてよい
- **LSPサーバーのプロセス管理 → 実用範囲。** csharp-ls の起動(initialize)は約1秒、プロジェクト読み込み後の最初の診断まで約7〜13秒(サンプルプロジェクト、クラウド環境で計測)。その間も編集はでき、補完・診断は準備でき次第反映される
