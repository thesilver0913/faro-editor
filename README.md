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
dotnet run --project src/Faro.Editor                        # ウェルカム画面から開く・新規作成
dotnet run --project src/Faro.Editor -- samples/HelloFaro   # フォルダを指定して直接開く
cd samples/HelloFaro && dotnet watch run                    # アプリをホットリロード付きで実行(エディターのRunボタンと同じ)
```

## プロジェクトの作成と配布

- 起動するとウェルカム画面:**最近のプロジェクト**／**フォルダを開く**／**新規プロジェクト**(既定の場所は `ドキュメント/Faro/<名前>`、変更可、テンプレートは Empty か Sample、言語は C# 固定)。File メニューの「Open Folder…」「Close Project」も同じ流れ
- Faro プロジェクトの目印は `faro.json`(名前・言語・Runtime のバージョン・開始画面 `startScreen`)。`faro.json` のないフォルダは確認のうえ初期化(足りないフォルダ・ファイルだけ追加し、既存ファイルは変えない)
- **Faro.Runtime はプロジェクト内に同梱**:エディターのビルド時に `Faro.Runtime.<版>.nupkg` を作り、新規プロジェクトの `.faro/packages/` にコピーして `nuget.config` から参照する。フォルダごと別の場所・PC に移してもビルドできる(Avalonia 本体は nuget.org から取得)。`.faro/` はプロジェクトと一緒にバージョン管理する
- 開くときにパッケージ未復元なら `dotnet restore` を自動実行(スプラッシュに表示)
- **Runtime の更新チェック**:プロジェクトが参照している Faro.Runtime より新しい版を Faro が同梱していれば、開くときに更新を確認する。更新すると `.faro/packages` の nupkg、`.csproj` の参照、`faro.json` を新しい版にして restore する(バージョンは数値で比較。PR ごとに 0.0.1 ずつ上がり、0.1.9 の次は 0.2.0。作業中の Runtime は `0.1.8-dev1`, `-dev2` … とし、リリース版より前に並ぶ)
- **Run / Stop**:キャンバスの Run(F5)で `dotnet watch run` を起動し、出力を Console の Output タブに表示。もう一度押す(または Shift+F5)と止まる。エディターを閉じるとアプリも止まる
- `samples/HelloFaro` はこのリポジトリ内での開発用で、Runtime をプロジェクト参照している

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
- キャンバスの赤バッジをクリックすると、その Node を選択してインスペクターに**再紐づけパネル**(仕様§6)が出る:近い名前の候補をボタンで並べ、押すとその候補に付け替える。メンバーが存在しない場合は「Create with vibe coding」で、Node・イベント／プロパティと型を埋めた依頼文がチャットに入る

## キャンバス編集とインスペクター

- 既定レイアウト:左端にエクスプローラー、中央上にキャンバス、中央下にコード｜Console、右端にインスペクター。Console は VSCode 風に Problems／Output／Vibe Coding をタブで切り替える(依頼文を入れると Vibe Coding、Run で Output に切り替わる)
- **コンポーネントのマスター編集**:キャンバスの画面リスト(またはエクスプローラー)からコンポーネントを開くと、画面と同じようにマスターを編集できる。インスタンスへの反映は「Sync components」(明示同期)。マスターの紐付けは `Bindings/<コンポーネントID>.xml` に保存され、**全インスタンス共通**で実行時に各インスタンス内の Node に適用される(仕様§5 の 2 階建て。インスタンス固有の紐付けは画面の Bindings でインスタンス ID に付ける)
- **Problems タブ**(仕様§11、Console 内。件数をタブ名に表示):プロジェクト全体の壊れた紐付けを画面/コンポーネントごとに一覧。クリックでその画面を開いて Node を選択。Run 中のビルドエラー(C# のコンパイルエラー)も「Build」として並び、クリックでコードエディタの該当行へ。次のビルドが始まると消える
- **ドラッグ＆ドロップ**:キャンバス上で Node をドラッグして並べ替え・別コンテナへ移動(挿入位置を青線で表示、Auto Layout どおり座標指定はなし)
- キャンバスのツールバー:**+ Add**(Stack/Wrap/Grid/Button/TextInput/Text/Image とコンポーネント)、Delete(Del キー)、↑ ↓(Alt+↑/↓)。追加先は選択中のコンテナ内、選択が部品ならその直後、未選択ならルート末尾
- インスペクター:ID(変更すると紐付けも追従)、幅/高さの Fill/Hug/Fixed と固定値、コンテナの向き・gap・padding・揃え・列数、Prop(インスタンスでは Override)、repeatable
- 紐付け:イベント/プロパティごとに対象をレジストリ候補から選ぶ(入力で絞り込み)、TwoWay/OneWay、削除、追加。存在しないメンバーには「Create with vibe coding」
- Node を削除すると、その Node を指す紐付けも一緒に削除(1 手で元に戻せる)

## エクスプローラー

- `UI/` `Source/` `Bindings/` `Assets/` をツリー表示(外部でのファイル追加・削除も反映)。クリックで開く:画面 → キャンバス、`.cs` → コードエディタ。画像はホバーでプレビュー
- 右クリック:UI/ に新しい画面・コンポーネント、Source/ に新しい C# クラス(プロジェクトの名前空間、`FaroObject` 継承)・フォルダ、Assets/ にフォルダ
- 名前変更・削除:画面の ID 変更は Navigate 先と `faro.json` の開始画面も追従、コンポーネントの ID 変更は全インスタンスが追従。画面を削除するとその Node の紐付けも削除。これらは UI グラフ履歴に入り Ctrl+Z で戻せる。C# ファイル・Assets・フォルダの名前変更/削除は通常のファイル操作(削除は確認あり・元に戻せない、未保存のコードがあれば中止)

## メニューと環境設定

- **File**:プロジェクトを開く(Ctrl+O、Faro を再起動して開き直す)／すべて保存(Ctrl+Shift+S)／実行(F5)・停止(Shift+F5)／終了。未保存のコードがあれば確認してから閉じる
- **Edit**:元に戻す(Ctrl+Z)／やり直し(Ctrl+Y・Ctrl+Shift+Z)／削除(Del)／コンポーネント同期／環境設定(Ctrl+,)
- **Undo/Redo(仕様§10)**:Ctrl+Z / Ctrl+Y と Edit メニュー。最後に操作したペインで対象が切り替わる(右上に表示)
  - キャンバス・インスペクター → **UI グラフ履歴**:Faro が UI/・Bindings/ に書いた変更(キャンバス編集・コンポーネント同期)を 1 手ずつ。Faro 外でファイルが変更されていたら上書きせずに中止
  - コードエディタ/チャット → **コード履歴**:表示中ファイルの履歴(手動編集と承認した AI 生成が合流)
- **Select**:すべての Node／選択解除(Ctrl+Shift+A)／ID で Node を選択／壊れた紐付けの Node を選択。キャンバスではクリックで Node を選択(Shift+クリックで追加・解除)。デザイン中はボタン等は反応しない
- **Window**:Explorer／Canvas／Inspector／Code／Console(Problems・Output・Vibe Coding タブ)の各パネルを前面に／全画面(F11)
- **Help**:Faro について
- **環境設定**(`ApplicationData/Faro/settings.json`、アプリ全体で共通)
  - 環境変数:`ANTHROPIC_API_KEY` / `OPENAI_API_KEY` の設定有無を表示(末尾4文字のみ)と設定方法。画面から編集はできない(キーはディスクに書かない方針)
  - テーマ:アプリ(System/Dark/Light)＋ユーザーの AXAML ResourceDictionary ファイル／コードエディタ(標準 `.xshd`、TextMate 内蔵テーマ、`.tmTheme` または VS Code の JSON テーマファイル)
  - プラグイン:今後対応(仕様§12 のとおりプロトタイプ範囲外)

## スキーマ補足(仕様書からの追加決定)

- **紐付けは画面ごと**:`Bindings/<画面ID>.xml` の `<Bind>` はその画面にだけ適用される(`Bindings/<コンポーネントID>.xml` はそのコンポーネントの全インスタンスに適用)。Node の ID は画面内で一意(画面をまたいだ重複は可)。どの画面にも対応しない Bindings ファイルは警告。画面の名前変更・削除で対応する Bindings ファイルも移動・削除される
- **開始画面は `faro.json` の `startScreen`**(コードに書かないので言語に依存しない)。`Program.cs` は `FaroApp.Run(args, typeof(Program).Assembly)` だけ。存在しない画面を指していれば Problems に出る
- **`target` の書式**:`<クラスの識別子>.<メンバー名>`。**最後の `.` より後がメンバー名**、それより前(C# では `名前空間.クラス名`)は言語ごとのレジストリ抽出・ランタイムが決める識別子として扱う。将来の言語でクラスを持たない関数はモジュール名をクラスの位置に書く想定
- **イベント・プロパティ名はフレームワーク非依存**:`<Bind>` の `event`/`prop` には Node 種類ごとの共通名だけを使い、Avalonia の名前への対応は `Faro.Runtime/Bindable.cs` の表だけが持つ(別フレームワーク対応時はこの表を差し替える)。共通名以外(例:旧形式の `OnClick`)は Problems に出て候補(`Click`)を示す

  | Node 種類 | イベント | プロパティ |
  |---|---|---|
  | Control.Button | Click | Text, Visible, Enabled |
  | Control.TextInput | Changed | Text, Placeholder, Visible, Enabled |
  | Control.Text | – | Text, Visible, Enabled |
  | Control.Image | – | Visible, Enabled |
  | Container.* | – | Visible, Enabled |
  | Instance | マスターのルート Node の種類に従う | 同左 |

- サイジングは軸ごと: `widthSizing` / `heightSizing`(`Fill`/`Hug`/`Fixed`)。`sizing` は両軸共通の省略形、未指定は `Hug`。`Fixed` は `width` / `height` 属性で値を指定
- `UI/` の各XMLはルートが `<UIGraph>` か `<ComponentDef>` のどちらか1つ
- インスタンスはマスターのスナップショット(`<Node>`)を内部に保持し、エディターの「Sync components」を押すまで更新されない
- 生存期間・永続化はC#属性で指定: `[FaroLifetime(Lifetime.Singleton, Persistent = true)]`(未指定はScreenScoped)。永続化データは `ApplicationData/Faro/<アプリ名>/<クラス名>.json`
- `Navigate:Screen.Detail` の `Screen.` は省略可能な接頭辞で、UIGraphの `id="Detail"` を指す
- `Container.Grid` は `columns` 属性を持つ均等グリッド

## リスク検証結果(仕様11.5)

- **ホットリロードとReflectionキャッシュの整合性 → 問題なし。** `dotnet watch` でメソッド本体を書き換えると、書き換え前に取得した `MethodInfo` がそのまま新しい本体を実行し、ハンドルも同一(`spikes/HotReloadMethodInfo/run.sh` で再現可能)。バインダーは `MethodInfo` をキャッシュしてよい
- **LSPサーバーのプロセス管理 → 実用範囲。** csharp-ls の起動(initialize)は約1秒、プロジェクト読み込み後の最初の診断まで約7〜13秒(サンプルプロジェクト、クラウド環境で計測)。その間も編集はでき、補完・診断は準備でき次第反映される
