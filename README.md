# Faro

<img src="assets/faro-icon.png" width="96" align="right" alt="Faro icon">

「Figma × UI Binding × Vibe Coding」を統合するUI/UXビジュアルエディタのプロトタイプです。
仕様は [`ui-editor-tool-spec.md`](ui-editor-tool-spec.md) を参照してください。

## 構成

| パス | 内容 |
|---|---|
| `src/Faro.Runtime` | ランタイムバインダー。UIグラフ(XML)→Avaloniaコントロール構築、`<Bind>`のReflection解決、生存期間・永続化、画面遷移 |
| `src/Faro.Editor` | エディター本体。Roslynレジストリ抽出、紐付け検証(赤バッジ+候補サジェスト)、コンポーネント明示同期、Dock 3ペイン、コードエディタ(AvaloniaEdit + csharp-ls)、バイブコーディング(チャット) |
| `src/Faro.Runtime.Java` | Java 版ランタイムバインダー(JavaFX)。同じ UI/・Bindings/ を JavaFX で表示し、紐付けを Java のリフレクションで解決する。各 Java プロジェクトにソースで同梱 |
| `samples/HelloFaro` | 仕様書の例をそのまま使ったFaroプロジェクト(UI/ Source/ Bindings/ Assets/) |
| `samples/HelloFaroJava` | 同じサンプルの Java(JavaFX)版 |
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

PR と `canary`/`main` への push では、GitHub Actions(`.github/workflows/checks.yml`)が Linux・Windows・macOS で `tests/Faro.Checks` を実行する。

## プロジェクトの作成と配布

- **ワークスペースの信頼(Workspace Trust)**:初めて開くフォルダでは「Trust / Restricted Mode」を確認する。開くと restore(MSBuild)・言語サーバー・Script のプレビュー(ビルド済み DLL の実行)でプロジェクトのコードが動くため。Restricted Mode では表示と編集だけで、それらと Run は止まり、Script は枠表示。File › Trust Project… で信頼して開き直せる。Faro が作った Untitled と、その Save As 先は最初から信頼済み
- 起動するとウェルカム画面:**最近のプロジェクト**／**フォルダを開く**／**新規プロジェクト**(テンプレートは Empty か Sample、言語は C#(Avalonia)か Java(JavaFX)。作成後は変えられない)。File メニューの「Open Folder…」「Close Project」も同じ流れ
- **新規プロジェクトは名前なし(Untitled)で始まる**:設定フォルダの `Faro/Untitled/UntitledN` に作られ、最初の **File › Save**(Ctrl+S)か **Save As…**(Ctrl+Shift+S)で名前と場所(既定は `ドキュメント/Faro`)を決める。Save As はプロジェクトを `bin/`・`obj/` 抜きで複製し、`faro.json` と `.csproj` の名前を付け替えて開き直す。保存せずに閉じようとすると「Save As… / Don't Save / Cancel」を確認し、捨てた Untitled は次の起動時に削除(削除するのは Untitled フォルダだけ)。Faro が落ちるなどして残った Untitled は、ウェルカム画面に「(not saved)」として出て、開き直すか Discard できる
- **Save** はコードの未保存分をすべて保存する(キャンバスの編集は操作のたびにファイルへ書かれる)。保存済みのプロジェクトでの Save As は別名の複製を作って開く
- Faro プロジェクトの目印は `faro.json`(名前・言語・Runtime のバージョン・開始画面 `startScreen`)。`faro.json` のないフォルダは確認のうえ初期化(足りないフォルダ・ファイルだけ追加し、既存ファイルは変えない)
- **Faro.Runtime はプロジェクト内に同梱**:エディターのビルド時に `Faro.Runtime.<版>.nupkg` を作り、新規プロジェクトの `.faro/packages/` にコピーして `nuget.config` から参照する。フォルダごと別の場所・PC に移してもビルドできる(Avalonia 本体は nuget.org から取得)。`.faro/` はプロジェクトと一緒にバージョン管理する
- 開くときにパッケージ未復元なら `dotnet restore` を自動実行(スプラッシュに表示)
- **Runtime の更新チェック**:プロジェクトが参照している Faro.Runtime より新しい版を Faro が同梱していれば、開くときに更新を確認する。更新すると `.faro/packages` の nupkg、`.csproj` の参照、`faro.json` を新しい版にして restore する(バージョンは数値で比較。PR ごとに 0.0.1 ずつ上がり、0.1.9 の次は 0.2.0。作業中の Runtime は `0.1.8-dev1`, `-dev2` … とし、リリース版より前に並ぶ)
- **初回起動のウィザード**:言語(English / 日本語、選ぶとその場で切り替わる)・テーマ・プラグイン(今後対応の案内)を選ぶ。言語は環境設定の「言語」タブでも変えられる(開いているメニューは再起動で切り替わる)。画面の文言は英語をキーにした表 `src/Faro.Editor/L.cs` で訳す
- **キャンバスのズーム**:−/+、全体表示(Fit、パネル幅に合わせる)、Ctrl+ホイール、倍率をクリックで 100%
- **キャンバスの右クリックメニュー**:追加・切り取り(Ctrl+X)・コピー・貼り付け・複製・削除・前後へ移動・親を選択・コンテナで囲む(Stack/Overlay/Grid、ID と紐付けはそのまま)・名前の変更・マスターを編集(インスタンス)
- **エクスプローラー(VSCode 風)**:プロジェクトフォルダ全体を表示(bin/obj/.git は非表示)。C# 以外のテキストファイル(faro.json、.csproj、XML など)もコードエディタで開ける(言語サーバーは C# だけ)。新しいファイル/フォルダ、切り取り・コピー・貼り付け、名前の変更(F2)、削除(Del)、ドラッグで移動、OS のファイルマネージャーからドロップしてコピー、パスのコピー、ファイルマネージャーで表示。UI/ と Bindings/ のファイルは画面に属するため、普通のファイルとしては移動・貼り付けしない(画面・コンポーネントの名前変更と削除は従来どおり参照も追従)
- **キャンバスの便利機能**:Node のコピー(Ctrl+C)・貼り付け(Ctrl+V)・複製(Ctrl+D)。ID がかぶるものは番号を振り直し、紐付けも新しい ID で複製する。プレビューの大きさを Phone / Tablet / Desktop で切り替え(アプリ全体の設定に保存)
- **画像**:Image の Source などは Assets/ の画像から選べる。エクスプローラーの画像をキャンバスへドラッグすると Image Node として追加。モック行で画像の Source も差し替えられる
- **Run / Stop**:キャンバスの Run(F5)で `dotnet watch run` を起動し、出力を Console の Output タブに表示。もう一度押す(または Shift+F5)と止まる。エディターを閉じるとアプリも止まる
- **レイアウトの保存**(仕様§14):パネルの大きさとウィンドウサイズをアプリ全体の設定(`settings.json`)に保存し、次回起動時に戻す。Window → Reset Layout で既定に戻る。パネルの移動・タブ化・切り離しは保存しない
- **外部での変更**:コードエディタで開いているファイルが Faro の外で変わると、未保存の編集がなければ読み込み直す。編集中なら状態行で知らせ、保存するときに上書きしてよいか確認する
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
- キャンバスの赤バッジをクリックすると、その Node を選択してインスペクターに**再紐づけパネル**(仕様§6)が出る:近い名前の候補をボタンで並べ、押すとその候補に付け替える。メンバーが存在しない場合は「Create with AI Chat」で、Node・イベント／プロパティと型を埋めた依頼文がチャットに入る

## キャンバス編集とインスペクター

- 既定レイアウト:左端にエクスプローラー、中央上にキャンバス、中央下にコード｜Console、右端にインスペクター。Console は VSCode 風に Problems／Output／AI Chat をタブで切り替える(依頼文を入れると AI Chat、Run で Output に切り替わる)
- **コンポーネントのマスター編集**:キャンバスの画面リスト(またはエクスプローラー)からコンポーネントを開くと、画面と同じようにマスターを編集できる。インスタンスへの反映は「Sync components」(明示同期)。マスターの紐付けは `Bindings/<コンポーネントID>.xml` に保存され、**全インスタンス共通**で実行時に各インスタンス内の Node に適用される(仕様§5 の 2 階建て。インスタンス固有の紐付けは画面の Bindings でインスタンス ID に付ける。インスタンスの**中の Node** にも `nodeId="orderList/price"` のようにパスで付けられ、インスペクターではインスタンスを選ぶと対象 Node を選べる。ID 変更・削除にも追従)
- **Script 部品**(`Control.Script`):見た目もボタンも動作も全部コードで書きたい人向け。`class` 属性に `FaroScript` を継承したクラスを指定し、`public override Control Build()` が返す Avalonia のコントロールがその場所に入る(生存期間の属性も有効)。キャンバスは**最後のビルド結果**(`bin/` の DLL)を読み込んで実物を表示し、未ビルドや失敗時は枠とメッセージ。クラスが見つからなければ Problems に出て、インスペクターから「Create with AI Chat」。サンプルの Detail 画面に `MyApp.Views.Stamp` の例がある
- **Problems タブ**(仕様§11、Console 内。件数をタブ名に表示):プロジェクト全体の壊れた紐付けを画面/コンポーネントごとに一覧。クリックでその画面を開いて Node を選択。Run 中のビルドエラー(C# のコンパイルエラー)も「Build」として並び、クリックでコードエディタの該当行へ。次のビルドが始まると消える
- **ドラッグ＆ドロップ**:キャンバス上で Node をドラッグして並べ替え・別コンテナへ移動(挿入位置を青線で表示、Auto Layout どおり座標指定はなし)
- キャンバスのツールバー:**+ Add**(Stack/Wrap/Grid/Button/TextInput/Text/Image とコンポーネント)、Delete(Del キー)、↑ ↓(Alt+↑/↓)。追加先は選択中のコンテナ内、選択が部品ならその直後、未選択ならルート末尾
- インスペクター:ID(変更すると紐付けも追従)、幅/高さの Fill/Hug/Fixed と固定値、コンテナの向き・gap・padding・揃え・列数、Prop(インスタンスでは Override)、repeatable と**モック行**(仕様§10.5:1行1件、値は `|` 区切りで中の Text に入る。キャンバスだけに複数行で表示され、実行時は無視。UI の XML に `<MockRow><Set node="…" value="…" /></MockRow>` として保存)
- 紐付け:イベント/プロパティごとに対象をレジストリ候補から選ぶ(入力で絞り込み)、TwoWay/OneWay、削除、追加。存在しないメンバーには「Create with AI Chat」
- Node を削除すると、その Node を指す紐付けも一緒に削除(1 手で元に戻せる)

## エクスプローラー

- `UI/` `Source/` `Bindings/` `Assets/` をツリー表示(外部でのファイル追加・削除も反映)。クリックで開く:画面 → キャンバス、`.cs` → コードエディタ。画像はホバーでプレビュー
- 右クリック:UI/ に新しい画面・コンポーネント、Source/ に新しい C# クラス(プロジェクトの名前空間、`FaroObject` 継承)・フォルダ、Assets/ にフォルダ
- 名前変更・削除:画面の ID 変更は Navigate 先と `faro.json` の開始画面も追従、コンポーネントの ID 変更は全インスタンスが追従。画面を削除するとその Node の紐付けも削除。これらは UI グラフ履歴に入り Ctrl+Z で戻せる。C# ファイル・Assets・フォルダの名前変更/削除は通常のファイル操作(削除は確認あり・元に戻せない、未保存のコードがあれば中止)

## メニューと環境設定

- **File**:プロジェクトを開く(Ctrl+O、Faro を再起動して開き直す)／閉じる／保存(Ctrl+S)・名前を付けて保存(Ctrl+Shift+S)／実行(F5)・停止(Shift+F5)／終了。Untitled や未保存のコードがあれば確認してから閉じる
- **Edit**:元に戻す(Ctrl+Z)／やり直し(Ctrl+Y・Ctrl+Shift+Z)／削除(Del)／コンポーネント同期／環境設定(Ctrl+,)
- **Undo/Redo(仕様§10)**:Ctrl+Z / Ctrl+Y と Edit メニュー。最後に操作したペインで対象が切り替わる(右上に表示)
  - キャンバス・インスペクター → **UI グラフ履歴**:Faro が UI/・Bindings/ に書いた変更(キャンバス編集・コンポーネント同期)を 1 手ずつ。Faro 外でファイルが変更されていたら上書きせずに中止
  - コードエディタ/チャット → **コード履歴**:表示中ファイルの履歴(手動編集と承認した AI 生成が合流)
- **Select**:すべての Node／選択解除(Ctrl+Shift+A)／ID で Node を選択／壊れた紐付けの Node を選択。キャンバスではクリックで Node を選択(Shift+クリックで追加・解除)。デザイン中はボタン等は反応しない
- **Window**:Explorer／Canvas／Inspector／Code／Console(Problems・Output・AI Chat タブ)の各パネルを前面に／全画面(F11)
- **Help**:更新の確認／ログフォルダーを開く／Faro について
- **環境設定**(`ApplicationData/Faro/settings.json`、アプリ全体で共通)
  - 環境変数:`ANTHROPIC_API_KEY` / `OPENAI_API_KEY` の設定有無を表示(末尾4文字のみ)と設定方法。画面から編集はできない(キーはディスクに書かない方針)
  - テーマ:アプリ(System/Dark/Light)＋ユーザーの AXAML ResourceDictionary ファイル／コードエディタ(標準 `.xshd`、TextMate 内蔵テーマ、`.tmTheme` または VS Code の JSON テーマファイル)
  - プラグイン:今後対応(仕様§12 のとおりプロトタイプ範囲外)
  - 更新:チャンネル(Stable / Beta / Canary)、起動時の確認(1日1回、`-dev` 版では確認しない)、今すぐ確認

## デザイン言語(Material 3 Expressive)

- File › Project Design… で、アプリの見た目を **Fluent**(Avalonia 標準)か **Material 3 Expressive** から選ぶ。`faro.json` の `design` に保存し(元に戻せる)、キャンバスと実行中のアプリの両方に反映する
- Material 3 は**シードカラー**から色の役割(Primary、Surface、コンテナなど。ライト・ダーク両方)を作る([MaterialColorUtilities](https://github.com/albi005/MaterialColorUtilities))。テーマは System / Light / Dark(キャンバスでは System をライトで表示)
- **Node ごとの設定**(インスペクターの「Material 3」欄。属性は `m3.〜`)
  - Button:種類(Filled / Tonal / Outlined / Text / Elevated)、サイズ(XS〜XL)、形(Round / Square)。押している間は角が小さくなり、離すとバネのように戻る(Expressive の形の変化)
  - TextInput:Filled / Outlined
  - Text:文字スタイル(Display〜Label)、強調、色
  - コンテナ:面の色(Surface、コンテナの濃さ、Primary など)、角丸、影の高さ
- 既知の制限:フォントは OS 標準(Roboto Flex は同梱しない)。無効状態は全体を薄くするだけ

## Java(JavaFX)プロジェクト

- 新規プロジェクトで **Java (JavaFX)** を選ぶと、Maven プロジェクト(`pom.xml`、`Source/Main.java`)ができる。**JDK 21 以降と Maven** が必要。UI/・Bindings/・Assets/ の形式、キャンバス、インスペクター、紐付けの検証は C# と同じ
- **ランタイム**:Java 版ランタイムバインダー(`faro.runtime` パッケージ)を `.faro/runtime-java` にソースで同梱し、`build-helper-maven-plugin` でアプリと一緒にコンパイルする(C# の nupkg 同梱に当たる。Faro を更新すると開いたときに入れ替えを案内する)
- **紐付け**:`target` は `パッケージ.クラス.メンバー`。イベントは引数なしの public メソッド(`myapp.services.OrderService.submit`)、プロパティは Bean プロパティ(`getName()`/`isName()`、TwoWay なら `setName(...)` も → `myapp.models.UserProfile.name`)
- 変更通知は `FaroObject` を継承して `changed("name", "greeting")` を呼ぶ。生存期間は `@FaroLifetime(value = Lifetime.SINGLETON, persistent = true)`(永続化は文字列・数値・真偽値の Bean プロパティを `.properties` に保存)
- **Script 部品**は `FaroScript` を継承して `build()` で JavaFX の Node を返す。キャンバスでは枠表示(実物は実行時)
- **実行**:キャンバスの Run で `mvn javafx:run`(ホットリロードなし、保存後にもう一度 Run)。javac のエラーは Problems に出る
- **Material 3**:エディターが `faro.json` の `design` から JavaFX 用 CSS(`.faro/design.css`)を作り、ランタイムが読み込む(押したときの形の変化はバネなしで切り替わるだけ)
- **レジストリ**:.java の宣言をソースから読み取る(未ビルドでも可)。コメント・文字列・入れ子のクラスは除外
- 既知の制限:Java の言語サーバー(補完・参照追従)は未対応で色分けのみ。AI Chat の生成コードは承認前の構文チェックなし(ビルドで検出)

## インストールと更新、ログ

- **インストーラー**:Windows は Inno Setup のウィザード(`installer/faro.iss`、English / 日本語、既定はユーザーごとのインストールで管理者権限不要)、Linux は `tar.gz`(展開して `Faro.Editor` を実行)。どちらも .NET 10 SDK が必要(Faro 自体とプロジェクトのビルドに使う)
- **更新の確認**:GitHub Releases を読む(送る情報はなし)。Stable は正式版、Beta は `-beta.N` も、Canary は `-canary.N` も対象。Windows では新しいセットアップを取得してサイレント実行し、Faro を閉じて更新後に起動し直す。Linux はリリースページを開く
- **リリースの作り方**:タグを push すると `.github/workflows/release.yml` がビルドして GitHub Release を作る。`v0.2.4`(Stable、`main` から)/ `v0.2.4-beta.1`(Beta)/ `v0.2.4-canary.1`(Canary、`canary` から)。`-` を含むタグはプレリリースになる。インストーラーやワークフローを変えた PR ではリリースせずにビルドだけ行う。タグを push しなくても、Actions › Release › Run workflow でブランチとタグ名を指定すれば、そのブランチの先頭をそのタグでリリースできる
- **ログとクラッシュレポート**:設定フォルダの `logs/`(Windows は `%APPDATA%\Faro\logs`)に日ごとのログ `faro-YYYYMMDD.log` と、落ちたときの `crash-*.txt`(版・OS・スタックトレース)を残す(14日で削除)。次の起動時に「予期せず終了しました」と知らせ、レポートを開くか GitHub の Issue を作れる。どこにも自動送信はしない
- **コード署名**:未対応(Windows で SmartScreen の警告が出る)

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
  | Control.Script | – | Visible, Enabled |
  | Container.* | – | Visible, Enabled |
  | Instance | マスターのルート Node の種類に従う | 同左 |

- サイジングは軸ごと: `widthSizing` / `heightSizing`(`Fill`/`Hug`/`Fixed`)。`sizing` は両軸共通の省略形、未指定は `Hug`。`Fixed` は `width` / `height` 属性で値を指定
- `UI/` の各XMLはルートが `<UIGraph>` か `<ComponentDef>` のどちらか1つ
- インスタンスはマスターのスナップショット(`<Node>`)を内部に保持し、エディターの「Sync components」を押すまで更新されない
- 生存期間・永続化はC#属性で指定: `[FaroLifetime(Lifetime.Singleton, Persistent = true)]`(未指定はScreenScoped)。永続化データは `ApplicationData/Faro/<アプリ名>/<クラス名>.json`
- `Navigate:Screen.Detail` の `Screen.` は省略可能な接頭辞で、UIGraphの `id="Detail"` を指す
- `Container.Grid` は行・列のトラックを持つグリッド(CSS grid 相当):`columns="Auto, *, 2*, 120px"`(数だけなら `"3"` で3等分)、`rows` も同様(省略時は必要な数の Auto 行)。子は `row`/`column`(0始まり)と `rowSpan`/`columnSpan` で置き、指定のない子は空いたセルを左上から順に埋める。セル内の揃えは Fill で伸ばすか、`alignment`/`alignSelf`
- **絶対座標を使わないレイアウトの追加オプション**(Figma の Auto Layout／Constraints、Android の ConstraintLayout・Box、CSS flexbox に相当):
  - Stack:`justify`(主軸の Start/Center/End/SpaceBetween)、子の `alignSelf`(交差軸の揃えを上書き)、Fill の比率 `weight`
  - 全 Node:`minWidth`/`maxWidth`/`minHeight`/`maxHeight`、`margin`
  - `padding`/`margin` は `8`・`8 16`(上下 左右)・`8 16 8 16`(上 右 下 左、CSS と同じ順)
  - `Container.Overlay`:子を重ねて置き、各子を `anchorX`(Left/Center/Right)・`anchorY`(Top/Center/Bottom)で親の辺か中央に固定。Fill はその軸いっぱい、距離は `margin`。画像の上の文字、隅のバッジ、下に固定するボタンなどに使う

## リスク検証結果(仕様11.5)

- **ホットリロードとReflectionキャッシュの整合性 → 問題なし。** `dotnet watch` でメソッド本体を書き換えると、書き換え前に取得した `MethodInfo` がそのまま新しい本体を実行し、ハンドルも同一(`spikes/HotReloadMethodInfo/run.sh` で再現可能)。バインダーは `MethodInfo` をキャッシュしてよい
- **未ビルドとの差分の可視化 → 簡易インジケータを実装。** `Source/` の保存が最後のビルド(`bin/` のアセンブリ、または Run 中のビルド成功・ホットリロード成功)より新しいと、キャンバスの状態行に「Unbuilt code changes」を表示
- **LSPサーバーのプロセス管理 → 実用範囲。** csharp-ls の起動(initialize)は約1秒、プロジェクト読み込み後の最初の診断まで約7〜13秒(サンプルプロジェクト、クラウド環境で計測)。その間も編集はでき、補完・診断は準備でき次第反映される
