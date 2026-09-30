namespace Faro.Editor;

/// <summary>
/// UI language: English text is the key; Japanese comes from the table below (missing entries stay English).
/// Chosen in the first-run wizard or Preferences; windows built after the change use it (menus after a restart).
/// </summary>
public static class L
{
    public static readonly string[] Languages = ["English", "日本語"];

    static bool Japanese => FaroSettings.Current.Language == "ja";

    public static string T(string english) => Japanese && Ja.TryGetValue(english, out var japanese) ? japanese : english;

    public static string F(string english, params object?[] args) => string.Format(T(english), args);

    /// <summary>A UI history step's label: a whole entry ("Rename node"), else "Verb rest" through "Verb {0}" ("Set width" → "width を設定").</summary>
    public static string Step(string label) =>
        !Japanese || Ja.ContainsKey(label) ? T(label) : label.Split(' ', 2) is [var verb, var rest] && Ja.ContainsKey(verb + " {0}") ? F(verb + " {0}", rest) : label;

    /// <summary>A word whose translation depends on where it's used ("Start" of an alignment isn't "Start" the app): "context|English" in the table.</summary>
    public static string T(string english, string context) => Japanese && Ja.TryGetValue(context + "|" + english, out var japanese) ? japanese : english;

    static readonly Dictionary<string, string> Ja = new()
    {
        // Menus (MainWindow.axaml) and panels
        ["_File"] = "ファイル(_F)", ["_Edit"] = "編集(_E)", ["_Select"] = "選択(_S)", ["_Window"] = "ウィンドウ(_W)", ["_Help"] = "ヘルプ(_H)",
        ["_Open Folder…"] = "フォルダを開く(_O)…", ["_Close Project"] = "プロジェクトを閉じる(_C)", ["_Trust Project…"] = "プロジェクトを信頼(_T)…",
        ["_Save"] = "保存(_S)", ["Save _As…"] = "名前を付けて保存(_A)…", ["_Run"] = "実行(_R)", ["S_top"] = "停止(_T)", ["Build Android _APK"] = "Android APK をビルド(_A)",
        ["Installing the Android SDK and JDK if needed (the first build downloads them)…"] = "必要なら Android SDK と JDK を入れます(初回はダウンロードします)…",
        ["The APK build failed (see above)."] = "APK のビルドに失敗しました(上の出力を参照)。", ["APK: {0}"] = "APK: {0}", ["E_xit"] = "終了(_X)",
        ["Java Android builds run on Linux only (GluonFX): use Linux, WSL or CI (`Faro.Editor --build-apk <folder>`)."] = "Java の Android ビルドは Linux でのみ動きます(GluonFX)。Linux・WSL・CI(`Faro.Editor --build-apk <フォルダ>`)を使ってください。",
        ["Set GRAALVM_HOME to Gluon's GraalVM ({0}), then build again."] = "環境変数 GRAALVM_HOME に Gluon の GraalVM({0})を設定してから、もう一度ビルドしてください。",
        ["Building with GluonFX (the first build downloads the Android SDK and takes several minutes)…"] = "GluonFX でビルドしています(初回は Android SDK をダウンロードし、数分かかります)…",
        ["_Undo"] = "元に戻す(_U)", ["_Redo"] = "やり直し(_R)", ["Cu_t"] = "切り取り(_T)", ["_Copy"] = "コピー(_C)", ["_Paste"] = "貼り付け(_P)", ["D_uplicate"] = "複製(_U)",
        ["_Delete"] = "削除(_D)", ["_Sync Components"] = "コンポーネントを同期(_S)", ["_Preferences…"] = "環境設定(_P)…",
        ["_All Nodes"] = "すべての Node(_A)", ["_None"] = "選択解除(_N)", ["Node by _ID…"] = "ID で Node を選択(_I)…", ["Nodes with _Broken Bindings"] = "紐付けが壊れた Node(_B)",
        ["_Explorer"] = "エクスプローラー(_E)", ["_Canvas"] = "キャンバス(_C)", ["_Inspector"] = "インスペクター(_I)", ["C_ode"] = "コード(_O)", ["Co_nsole"] = "コンソール(_N)",
        ["_Problems"] = "問題(_P)", ["_Output"] = "出力(_O)", ["_AI Chat"] = "AI チャット(_A)", ["_Reset Layout"] = "レイアウトを初期化(_R)",
        ["_Full Screen"] = "全画面(_F)", ["_About Faro"] = "Faro について(_A)",
        ["Add a node (or drag one from Parts)"] = "Node を追加(パーツからドラッグでも)", ["Delete the selected nodes (Del)"] = "選択した Node を削除(Del)",
        ["Move up (Alt+Up)"] = "前へ移動(Alt+↑)", ["Move down (Alt+Down)"] = "後ろへ移動(Alt+↓)", ["Zoom out (Ctrl+wheel)"] = "縮小(Ctrl+ホイール)", ["Zoom in (Ctrl+wheel)"] = "拡大(Ctrl+ホイール)",
        ["Fit to the pane width"] = "パネルの幅に合わせる", ["Run the app (F5) / Stop (Shift+F5)"] = "アプリを実行(F5)/停止(Shift+F5)",
        ["layout|false"] = "オフ", ["layout|true"] = "オン", ["layout|Start"] = "先頭", ["layout|Center"] = "中央", ["layout|End"] = "末尾", ["layout|SpaceBetween"] = "両端", ["layout|Auto"] = "自動",
        ["layout|Vertical"] = "縦", ["layout|Horizontal"] = "横", ["layout|Left"] = "左", ["layout|Right"] = "右", ["layout|Top"] = "上", ["layout|Bottom"] = "下",
        ["layout|Fill"] = "Fill", ["layout|Hug"] = "Hug", ["layout|Fixed"] = "Fixed", ["layout|Round"] = "丸", ["layout|Square"] = "角", ["layout|Filled"] = "Filled", ["layout|Outlined"] = "Outlined",
        ["Preview"] = "プレビュー", ["Try the screen here: typing, clicks and Navigate bindings work; code isn't run (Run does that)"] = "ここで画面を試す:入力・クリック・Navigate の紐付けが動く(コードは実行しない。実行は「実行」で)",
        ["Remove from Recent"] = "最近の一覧から外す", ["Layers"] = "レイヤー", ["Parts"] = "パーツ", ["_Layers"] = "レイヤー(_L)", ["_Parts"] = "パーツ(_P)", ["Containers"] = "コンテナ", ["Controls"] = "コントロール", ["Components"] = "コンポーネント",
        ["Children in a column or a row (auto layout)"] = "子を縦か横に並べる(オートレイアウト)", ["Children in rows that wrap"] = "子を折り返して並べる",
        ["Children in rows and columns"] = "子を行と列に並べる", ["Children stacked on top of each other, anchored to edges"] = "子を重ね、端や中央に固定する",
        ["A button (Click event)"] = "ボタン(Click イベント)", ["A text field (Text, Changed)"] = "テキスト入力(Text、Changed)", ["A label"] = "文字", ["An image from Assets/"] = "Assets/ の画像",
        ["A part built in code (FaroScript)"] = "コードで作る部品(FaroScript)",
        ["Explorer"] = "エクスプローラー", ["Canvas"] = "キャンバス", ["Inspector"] = "インスペクター", ["Code"] = "コード", ["Console"] = "コンソール",
        ["Problems"] = "問題", ["Output"] = "出力", ["AI Chat"] = "AI チャット",

        // Welcome, wizard, projects
        ["Welcome to Faro"] = "Faro へようこそ", ["Recent"] = "最近のプロジェクト", ["New Project…"] = "新規プロジェクト…", ["New Project"] = "新規プロジェクト",
        ["Open Folder…"] = "フォルダを開く…", ["Open Folder"] = "フォルダを開く", ["Template"] = "テンプレート", ["Language"] = "言語", ["Create"] = "作成", ["Cancel"] = "キャンセル",
        ["No recent projects yet. Create a new project or open a folder."] = "最近のプロジェクトはまだありません。新規作成するか、フォルダを開いてください。",
        ["The project starts untitled. Give it a name and a place with File › Save (or Save As…)."] = "プロジェクトは名前なしで始まります。ファイル › 保存(または名前を付けて保存)で名前と場所を決めてください。",
        ["Unsaved project from an earlier session"] = "前回のセッションで保存されなかったプロジェクト", ["Discard"] = "破棄", [" (not saved)"] = "(未保存)",
        ["{0} isn't a Faro project yet. Initialize it? Missing folders and files are added; existing files aren't changed."] = "{0} はまだ Faro プロジェクトではありません。初期化しますか？足りないフォルダとファイルだけを追加し、既存のファイルは変更しません。",
        ["Initialize"] = "初期化", ["Name"] = "名前", ["Location"] = "場所", ["Browse…"] = "参照…", ["Project Location"] = "プロジェクトの場所",
        ["Save Project As"] = "名前を付けてプロジェクトを保存", ["Save"] = "保存", ["Save project?"] = "プロジェクトを保存しますか？", ["Save As…"] = "名前を付けて保存…", ["Don't Save"] = "保存しない",
        ["{0} hasn't been saved yet. Save it before leaving?"] = "{0} はまだ保存されていません。閉じる前に保存しますか？",
        ["Unsaved changes"] = "未保存の変更", ["Some code files have unsaved changes. Close Faro and discard them?"] = "未保存のコードがあります。破棄して Faro を閉じますか？",
        ["Some code files have unsaved changes. Discard them?"] = "未保存のコードがあります。破棄しますか？", ["Discard and Close"] = "破棄して閉じる",
        ["Trust this project?"] = "このプロジェクトを信頼しますか？", ["Trust"] = "信頼する", ["Restricted Mode"] = "制限モード", ["Trust Project"] = "プロジェクトを信頼",
        ["Faro restores, builds and runs a project's code (language server, Script previews, Run). Trust it only if you know where it comes from. In restricted mode you can still view and edit it."] =
            "Faro はプロジェクトのコードを復元・ビルド・実行します(言語サーバー、Script のプレビュー、実行)。出どころが分かるプロジェクトだけを信頼してください。制限モードでも表示と編集はできます。",
        ["Trust {0}? Faro will restore, build and run its code."] = "{0} を信頼しますか？Faro がそのコードを復元・ビルド・実行します。",
        [" (Restricted Mode)"] = "(制限モード)", ["Faro.Runtime update"] = "Faro.Runtime の更新", ["Update"] = "更新",
        ["This project uses Faro.Runtime {0}. This Faro ships {1}. Update the project to it?"] = "このプロジェクトは Faro.Runtime {0} を使っています。この Faro には {1} が入っています。更新しますか？",
        ["About Faro"] = "Faro について",
        ["A visual UI editor: draw your app's screens and bind them to your C# or Java code."] = "アプリの画面を描いて、C# / Java のコードに紐付けるビジュアル UI エディターです。",
        ["MIT License. The open-source components Faro uses are under Licenses."] = "MIT ライセンス。Faro が使っているオープンソースは「ライセンス」から見られます。", ["Licenses"] = "ライセンス",

        // Explorer
        ["New Screen…"] = "新しい画面…", ["New Component…"] = "新しいコンポーネント…", ["New C# Class…"] = "新しい C# クラス…", ["New Folder…"] = "新しいフォルダ…",
        ["+ File"] = "+ ファイル", ["+ Folder"] = "+ フォルダ", ["New File…"] = "新しいファイル…", ["New File"] = "新しいファイル", ["File name:"] = "ファイル名:", ["Refresh"] = "最新の情報に更新", ["Collapse All"] = "すべて折りたたむ",
        ["Open as Text"] = "テキストとして開く", ["Copy Path"] = "パスをコピー", ["Copy Relative Path"] = "相対パスをコピー", ["Reveal in File Manager"] = "ファイルマネージャーで表示",
        ["Move"] = "移動", ["{0} has unsaved changes. Save it first."] = "{0} に未保存の変更があります。先に保存してください。",
        ["Rename…"] = "名前の変更…", ["Delete"] = "削除", ["New Screen"] = "新しい画面", ["New Component"] = "新しいコンポーネント", ["ID:"] = "ID:",
        ["New C# Class"] = "新しい C# クラス", ["New Java Class"] = "新しい Java クラス", ["New Java Class…"] = "新しい Java クラス…",
        ["Couldn't start {0}: {1}"] = "{0} を起動できませんでした: {1}", ["Couldn't access a file"] = "ファイルにアクセスできませんでした",
        ["Maven isn't installed, or `mvn` isn't on the PATH Faro sees. Install the JDK 21 and Maven from Preferences › Tools, or install them yourself (Ubuntu: sudo apt install maven openjdk-21-jdk) and restart Faro."]
            = "Maven が入っていないか、Faro から `mvn` が見えません。環境設定 › 部品 から JDK 21 と Maven を入れるか、自分で入れて(Ubuntu:sudo apt install maven openjdk-21-jdk)Faro を起動し直してください。",
        ["Tools"] = "部品", ["Installed"] = "導入済み", ["Not installed"] = "未導入",
        ["Tools Faro downloads when you need them. They go into Faro's own folder and are used only by Faro."] = "必要になったときに Faro がダウンロードする道具です。Faro 専用のフォルダに入り、Faro の中だけで使います。",
        ["Java projects (Run, builds)"] = "Java プロジェクト(実行・ビルド)", ["Completion and errors in Java code (installed on first use too)"] = "Java のコードの補完とエラー表示(初めて使うときにも自動で入ります)", ["Android APKs of Java projects (Linux only)"] = "Java プロジェクトの Android APK(Linux のみ)",
        ["Android APKs of C# projects"] = "C# プロジェクトの Android APK",
        ["Downloading {0}…"] = "{0} をダウンロードしています…", ["Unpacking…"] = "展開しています…", ["Installed in {0}."] = "{0} に入れました。", ["Couldn't install it: "] = "入れられませんでした: ",
        ["The .NET 10 SDK isn't installed, or `dotnet` isn't on the PATH Faro sees. Install it (https://dotnet.microsoft.com/download/dotnet/10.0), check `dotnet --version` in a terminal, then restart Faro."]
            = ".NET 10 SDK が入っていないか、Faro から `dotnet` が見えません。入れて(https://dotnet.microsoft.com/download/dotnet/10.0)、ターミナルで `dotnet --version` が動くことを確かめてから Faro を起動し直してください。", ["Class name:"] = "クラス名:", ["New Folder"] = "新しいフォルダ", ["Folder name:"] = "フォルダ名:",
        ["Rename Screen"] = "画面の名前変更", ["Rename Component"] = "コンポーネントの名前変更", ["New ID (references follow):"] = "新しい ID(参照も追従):",
        ["Rename"] = "名前の変更", ["New name:"] = "新しい名前:",
        ["Delete {0}? Bindings of its nodes are removed too. You can undo this with Ctrl+Z on the canvas."] = "{0} を削除しますか？その Node の紐付けも削除されます。キャンバスで Ctrl+Z を押すと元に戻せます。",
        ["Delete {0}? This can't be undone."] = "{0} を削除しますか？元に戻せません。", ["Delete {0} and everything in it? This can't be undone."] = "{0} と中身をすべて削除しますか？元に戻せません。",

        // Canvas
        ["+ Add"] = "+ 追加", ["Run"] = "実行", ["Stop"] = "停止", [" (container)"] = "(コンテナ)", ["Sync components ({0})"] = "コンポーネントを同期 ({0})",
        ["Selected: {0} ({1}) · "] = "選択中: {0} ({1}) · ", ["{0} nodes selected · "] = "{0} 個の Node を選択中 · ",
        ["{0} broken binding(s) · {1} bindable members"] = "壊れた紐付け {0} 件 · 紐付けできるメンバー {1} 件", ["Component master · {0} instance(s) to sync · "] = "コンポーネントのマスター · 同期待ちのインスタンス {0} 個 · ",
        [" · Unbuilt code changes: new members resolve after Run"] = " · 未ビルドのコード変更あり(新しいメンバーは実行後に解決)",
        [" · Restricted Mode (File › Trust Project…)"] = " · 制限モード(ファイル › プロジェクトを信頼)", ["Restricted Mode: File › Trust Project… to run it."] = "制限モード: ファイル › プロジェクトを信頼 で実行できるようになります。",
        ["No UI/*.xml screens in {0}"] = "{0} に UI/*.xml の画面がありません", ["Did you mean:"] = "もしかして:",

        // Inspector
        ["Select a node on the canvas."] = "キャンバスで Node を選択してください。", ["{0} nodes selected."] = "{0} 個の Node を選択中。",
        ["Layout"] = "レイアウト", ["Width"] = "幅", ["Height"] = "高さ", ["Anchor X"] = "横の固定", ["Anchor Y"] = "縦の固定", ["Align self"] = "個別の揃え",
        ["Fill weight"] = "Fill の比率", ["Row / Col"] = "行 / 列", ["Span R / C"] = "またぐ 行 / 列", ["Min W / H"] = "最小 幅 / 高", ["Max W / H"] = "最大 幅 / 高",
        ["Margin"] = "外側の余白", ["Direction"] = "方向", ["Columns"] = "列", ["Rows"] = "行", ["Gap"] = "間隔", ["Padding"] = "内側の余白", ["Alignment"] = "揃え", ["Justify"] = "並べ方",
        ["Justify has no effect while a child fills the main axis: it takes the free space."] = "Fill の子がいると並べ方は効きません(その子が空きを使うため)。",
        ["Properties"] = "プロパティ", ["Overrides"] = "上書き", ["Script"] = "Script", ["Class"] = "クラス", ["Repeatable (list)"] = "繰り返し(リスト)",
        ["Mock rows (canvas only): {0}"] = "モック行(キャンバスのみ): {0}", ["Bindings"] = "紐付け", ["+ Add binding"] = "+ 紐付けを追加", ["Remove binding"] = "紐付けを削除",
        ["Create with AI Chat"] = "AI チャットで作る", ["Did you mean"] = "もしかして",
        ["Numbers only."] = "数値だけを入力してください。", ["One number."] = "数値は1つだけです。", ["A whole number (0 or more)."] = "0 以上の整数を入力してください。",
        ["1, 2 or 4 numbers (top right bottom left)."] = "数値を 1・2・4 個(上 右 下 左)で入力してください。",
        ["Tracks like \"Auto, *, 2*, 120px\", or a count like \"3\"."] = "\"Auto, *, 2*, 120px\" のような指定か、\"3\" のような数を入力してください。",

        // Console, problems, chat, code
        ["No problems."] = "問題はありません。", ["Project"] = "プロジェクト", ["Build"] = "ビルド", ["Did you mean {0}?"] = "もしかして {0}？",
        ["Send"] = "送信", ["Stopped."] = "停止しました。", ["New classes:"] = "新しいクラス:", ["Persistent"] = "永続化", ["Model"] = "モデル", ["Model name"] = "モデル名",
        ["Describe the class or method you need… (Ctrl+Enter to send)"] = "必要なクラスやメソッドを説明してください…(Ctrl+Enter で送信)",
        ["Approve"] = "承認", ["Reject"] = "却下", ["New file: "] = "新規ファイル: ", ["Change: "] = "変更: ", ["Syntax error, "] = "構文エラー、",
        ["`{0}` is being edited in the code editor (unsaved changes). Save it first, then ask again."] = "`{0}` はコードエディタで編集中です(未保存)。保存してからもう一度依頼してください。",
        ["Ignored {0}: generated files must be {1} files under Source/."] = "{0} は無視しました: 生成できるのは Source/ 以下の {1} ファイルだけです。", ["`{0}` is being edited in the code editor (unsaved changes). Save it to approve."] = "`{0}` はコードエディタで編集中です(未保存)。保存すると承認できます。",
        ["Changed on disk"] = "ディスク上で変更されています", ["Overwrite"] = "上書き",
        ["{0} changed outside Faro since it was opened. Saving overwrites those changes."] = "{0} は開いた後に Faro の外で変更されました。保存するとその変更を上書きします。",
        ["Changed on disk too: saving will ask before overwriting."] = "ディスク上でも変更されています。保存するときに上書きを確認します。",
        ["Language server: starting…"] = "言語サーバー: 起動中…", ["Language server: ready"] = "言語サーバー: 準備完了", ["Language server unavailable: "] = "言語サーバーを使えません: ",
        ["Language server crashed, restarting…"] = "言語サーバーが落ちたので再起動しています…", ["Language server stopped (crashed 3 times)."] = "言語サーバーを停止しました(3回落ちました)。",
        ["Editor theme: "] = "エディタのテーマ: ", ["Undo"] = "元に戻す", ["Redo"] = "やり直し",
        ["Select Node by ID"] = "ID で Node を選択", ["Node ID on the current screen:"] = "現在の画面の Node ID:", ["No node '{0}' on the current screen."] = "現在の画面に Node '{0}' はありません。",

        // Problems (binding check)
        ["Node '{0}' does not exist on screen '{1}'."] = "Node '{0}' は画面 '{1}' にありません。", ["No target chosen yet."] = "紐付け先がまだ選ばれていません。",
        ["Screen '{0}' does not exist."] = "画面 '{0}' はありません。", ["Method '{0}' not found in Source/."] = "メソッド '{0}' が Source/ にありません。",
        ["Property '{0}' not found in Source/."] = "プロパティ '{0}' が Source/ にありません。", ["{0} has no event '{1}'."] = "{0} にイベント '{1}' はありません。",
        ["{0} has no property '{1}'."] = "{0} にプロパティ '{1}' はありません。", ["Component '{0}' does not exist."] = "コンポーネント '{0}' はありません。",
        ["Start screen '{0}' (faro.json) does not exist."] = "開始画面 '{0}'(faro.json)はありません。",
        ["Bindings/{0}.xml doesn't belong to any screen or component."] = "Bindings/{0}.xml に対応する画面・コンポーネントがありません。",
        ["Script class '{0}' (deriving from FaroScript) not found in Source/."] = "Script のクラス '{0}'(FaroScript の派生)が Source/ にありません。",
        ["No script class chosen yet."] = "Script のクラスがまだ選ばれていません。",

        // Preferences
        ["Preferences"] = "環境設定", ["Environment"] = "環境変数", ["Theme"] = "テーマ", ["Plugins"] = "プラグイン", ["App theme"] = "アプリのテーマ",
        ["Code editor theme"] = "コードエディタのテーマ", ["Clear"] = "クリア", ["Base URL"] = "ベース URL", ["Not set"] = "未設定", ["Set (…{0})"] = "設定済み(…{0})", ["To set one:"] = "設定方法:",
        ["API keys are read from environment variables only. Faro never writes them to disk."] = "API キーは環境変数からだけ読みます。Faro がキーをディスクに書くことはありません。",
        ["Custom theme file (Avalonia ResourceDictionary .axaml, overrides the colors above)"] = "テーマファイル(Avalonia の ResourceDictionary .axaml。上の色を上書き)",
        ["Plugins are coming in a later version."] = "プラグインは今後のバージョンで対応予定です。",
        ["(none)"] = "(なし)", ["Language changes apply to windows opened from now on; restart Faro to update the menus."] = "言語の変更はこれから開くウィンドウに反映されます。メニューは Faro を再起動すると切り替わります。",

        // First-run wizard
        ["Set up Faro"] = "Faro の初期設定", ["Choose how Faro looks. You can change all of this later in Preferences."] = "Faro の見た目を選んでください。あとから環境設定でいつでも変更できます。",
        ["Instance of {0}"] = "{0} のインスタンス", ["Event"] = "イベント", ["Property"] = "プロパティ",
        ["Undo: Code — {0}"] = "元に戻す対象: コード — {0}", ["no file"] = "ファイルなし", ["Undo: Canvas"] = "元に戻す対象: キャンバス",
        // Debugger
        ["_Debug"] = "デバッグ(_D)", ["_Start Debugging"] = "デバッグ開始(_S)", ["S_top Debugging"] = "デバッグ停止(_T)", ["_Continue"] = "続行(_C)", ["Step _Over"] = "ステップオーバー(_O)",
        ["Step _Into"] = "ステップイン(_I)", ["Toggle _Breakpoint"] = "ブレークポイントの切り替え(_B)", ["Debug"] = "デバッグ",
        ["Start Debugging (F6)"] = "デバッグ開始(F6)", ["Continue (F8)"] = "続行(F8)", ["Step Over (F10)"] = "ステップオーバー(F10)", ["Step Into (Shift+F10)"] = "ステップイン(Shift+F10)", ["Stop Debugging (Shift+F6)"] = "デバッグ停止(Shift+F6)",
        ["Call stack"] = "呼び出し履歴", ["Variables"] = "変数", ["The Java debugger (java-debug) isn't installed: Preferences › Tools, then restart Faro."] = "Java のデバッガー(java-debug)が入っていません。環境設定 › 部品 で入れてから Faro を再起動してください。",
        ["No main class found (is the language server still importing the project?)."] = "メインクラスが見つかりません(言語サーバーがまだプロジェクトを読み込み中かもしれません)。", ["Couldn't start the Java debugger: "] = "Java のデバッガーを起動できませんでした: ",
        ["Debugging Java code (a jdtls plugin, installed with it)"] = "Java のコードのデバッグ(jdtls のプラグイン。jdtls と一緒に入ります)",
        ["Restricted Mode: File › Trust Project… to debug."] = "制限モード: デバッグするには ファイル › プロジェクトを信頼… を選んでください。", ["Paused at {0}:{1}"] = "{0}:{1} で一時停止中", ["Running…"] = "実行中…",
        ["Click left of a line number in the code to set a breakpoint, then Debug."] = "コードの行番号の左をクリックしてブレークポイントを置き、デバッグを押します。",
        ["Building for debugging…"] = "デバッグ用にビルドしています…", ["The build failed (see above)."] = "ビルドに失敗しました(上の出力を参照)。", ["[Debugging stopped]"] = "[デバッグ終了]",
        ["The C# debugger (installed on first use too)"] = "C# のデバッガー(初めて使うときにも自動で入ります)",
        // Tokens and variants
        ["Tokens"] = "トークン", ["One per line: name = value. Sizes (space.m = 16), colors (color.primary = #6750A4) and text styles: text.title.fontFamily, .fontSize, .fontWeight, .lineHeight. Fields take \"$name\" ($space.m, $color.primary); a node's Text style takes $text.title. Changing a token restyles every screen."] = "1 行に 1 つ、名前 = 値。大きさ(space.m = 16)、色(color.primary = #6750A4)、文字スタイル(text.title.fontFamily・.fontSize・.fontWeight・.lineHeight)を書けます。各欄に \"$名前\"($space.m、$color.primary)、Node の「文字スタイル」に $text.title と入れて使います。トークンを変えると全画面に反映されます。",
        ["No token {0} (File › Project Design… › Tokens)."] = "トークン {0} がありません(ファイル › プロジェクトのデザイン… › トークン)。",
        ["Component"] = "コンポーネント", ["Component variant"] = "バリアント", ["(default)"] = "(既定)", ["New variant…"] = "新しいバリアント…", ["New variant"] = "新しいバリアント", ["Variant name for {0}:"] = "{0} のバリアント名:",
        ["Copy the component as a variant (edit it like a master; bindings are shared)"] = "コンポーネントをバリアントとして複製(マスターと同じように編集。紐付けは共通)",
        // Binding format
        ["Format, e.g. ¥{0:N0}"] = "書式(例: ¥{0:N0})", ["{0} is the value; {0:N0} adds thousands separators, {0:F2} two decimals"] = "{0} が値。{0:N0} で 3 桁区切り、{0:F2} で小数 2 桁",
        // Compare and data preview (canvas)
        ["One artboard"] = "1 枚で表示", ["All sizes"] = "全サイズを並べる", ["Light and dark"] = "ライトとダークを並べる",
        ["Compare the screen on every size, or in light and dark"] = "画面を全サイズ、またはライトとダークで並べて比べる",
        ["Data"] = "データ", ["not built yet (Run builds it)"] = "まだビルドされていません(実行でビルドされます)",
        [" · Data from the last build"] = " · 最後のビルドのデータを表示中", [" · Data: {0}"] = " · データ: {0}",
        ["Restricted Mode: File › Trust Project… to show data from the code."] = "制限モード: コードのデータを表示するには ファイル › プロジェクトを信頼… を選んでください。",
        ["Data preview is for C# projects (Java runs in its own JVM)."] = "データのプレビューは C# プロジェクトのみです(Java は別の JVM で動くため)。",
        ["Show the data the bindings bring from the last build (lists, texts) instead of the mock rows"] = "モック行の代わりに、紐付けが最後のビルドから持ってくるデータ(リスト・テキスト)を表示",
        // Command palette, history list, source control
        ["Command _Palette…"] = "コマンドパレット(_P)…", ["Command Palette"] = "コマンドパレット", ["Type a command"] = "コマンドを入力",
        ["_History"] = "履歴(_H)", ["History"] = "履歴", ["Opened"] = "開いたとき",
        ["_Source Control"] = "ソース管理(_S)", ["Source Control"] = "ソース管理", ["Commit message"] = "コミットメッセージ", ["Commit All"] = "すべてコミット",
        ["Pull"] = "プル", ["Push"] = "プッシュ", ["Initialize Repository"] = "リポジトリを作成", ["Write a commit message first."] = "先にコミットメッセージを書いてください。",
        ["Git is off in Restricted Mode."] = "制限モードでは Git は使えません。", ["Git isn't installed (git-scm.com)."] = "Git が入っていません(git-scm.com)。",
        ["This folder isn't a Git repository."] = "このフォルダは Git リポジトリではありません。", ["Branch: {0}"] = "ブランチ: {0}", ["No changes."] = "変更はありません。", ["Done."] = "完了しました。",
        ["Close"] = "閉じる", ["Project _Design…"] = "プロジェクトのデザイン(_D)…", ["Project Design"] = "プロジェクトのデザイン", ["Design language"] = "デザイン言語", ["Seed color"] = "シードカラー", ["Apply"] = "適用",
        ["Every design language takes its colors (light and dark) from the seed color. Material 3 also gives nodes options in the inspector; Cupertino (iOS), Neumorphism (soft raised shapes), NeoBrutalism (thick outlines, hard shadows) and Simple (quiet, neutral) restyle the parts as they are."] = "どのデザイン言語もシードカラーから色(ライト・ダーク)を作ります。Material 3 は各 Node の設定がインスペクターにあります。Cupertino(iOS 風)、Neumorphism(柔らかく浮き出る形)、NeoBrutalism(太い輪郭と硬い影)、Simple(落ち着いた無彩色)は部品をそのまま着せ替えます。",
        ["Material 3"] = "Material 3", ["Variant"] = "種類", ["Size"] = "サイズ", ["Shape"] = "形", ["Type"] = "文字スタイル", ["Emphasized"] = "強調", ["Color"] = "色", ["Surface"] = "面の色", ["Corner"] = "角丸", ["Elevation"] = "影の高さ",
        ["Open _Logs Folder"] = "ログフォルダーを開く(_L)", ["_Getting Started Guide"] = "はじめてガイド(_G)", ["Faro quit unexpectedly"] = "Faro が予期せず終了しました", ["Report Issue"] = "問題を報告",
        ["A crash report was saved. Sending it as an issue helps fix the problem. Nothing is sent without you."] = "クラッシュレポートを保存しました。Issue として送っていただくと修正に役立ちます。操作しない限り何も送信されません。",
        ["What were you doing when Faro quit? (optional)"] = "終了したとき、何をしていましたか？(任意)", ["Details"] = "詳細", ["Copy Details"] = "詳細をコピー", ["Restart Faro"] = "Faro を再起動",
        ["(Describe what you were doing.)"] = "(何をしていたかを書いてください)", ["(cut: paste the full details from the clipboard)"] = "(省略:全文はクリップボードから貼り付けてください)",
        ["Check for _Updates…"] = "更新を確認(_U)…", ["Check for Updates"] = "更新の確認", ["Updates"] = "更新", ["Update channel"] = "更新チャンネル",
        ["Stable"] = "安定版", ["Beta"] = "ベータ", ["Canary"] = "カナリア", ["Check Now"] = "今すぐ確認", ["Check for updates at startup (daily)"] = "起動時に更新を確認する(1日1回)",
        ["Stable: releases. Beta: previews of the next release. Canary: the latest development builds."] = "安定版: 正式リリース。ベータ: 次のリリースの先行版。カナリア: 最新の開発ビルド。",
        ["Current version: {0}. Releases come from github.com/{1}; the check only reads public release data."] = "現在のバージョン: {0}。リリースは github.com/{1} から取得します。確認では公開されているリリース情報を読むだけです。",
        ["Couldn't reach GitHub: "] = "GitHub に接続できませんでした: ",
        ["Couldn't download the update: "] = "更新をダウンロードできませんでした: ", ["The release page opens so you can download it there."] = "リリースページを開くので、そこからダウンロードしてください。", ["Faro {0} is up to date ({1})."] = "Faro {0} は最新です({1})。",
        ["Update available"] = "更新があります", ["Faro {0} is available ({1}). You have {2}."] = "Faro {0} が公開されています({1})。現在は {2} です。",
        ["Faro closes while the installer updates it, then starts again."] = "インストーラーが更新する間 Faro は終了し、終わると再び起動します。",
        ["Install"] = "インストール", ["Download"] = "ダウンロード", ["Release Notes"] = "リリースノート",
        ["Fit"] = "全体表示", ["Add"] = "追加", ["Cut"] = "切り取り", ["Copy"] = "コピー", ["Paste"] = "貼り付け", ["Duplicate"] = "複製",
        ["Move up"] = "前へ移動", ["Move down"] = "後ろへ移動", ["Select parent"] = "親を選択", ["Wrap in"] = "コンテナで囲む", ["Edit master component"] = "マスターを編集",
        ["A check box (Checked, Changed)"] = "チェックボックス(Checked、Changed)", ["An on/off switch (Checked, Changed)"] = "オン/オフのスイッチ(Checked、Changed)",
        ["A slider (Value, Changed)"] = "スライダー(Value、Changed)", ["A drop-down of choices (Options, Selected, Changed)"] = "選択肢のドロップダウン(Options、Selected、Changed)",
        ["A progress bar, 0 to 100 (Value)"] = "進捗バー、0〜100(Value)", ["A thin line between parts"] = "部品のあいだの細い線",
        ["Empty space that pushes its neighbors apart (e.g. a button to the right end)"] = "隣の部品を押し広げる空きスペース(ボタンを右端に寄せるときなど)",
        ["Align"] = "揃え", ["Align left"] = "左揃え", ["Align horizontal centers"] = "左右中央揃え", ["Align right"] = "右揃え",
        ["Align top"] = "上揃え", ["Align vertical centers"] = "上下中央揃え", ["Align bottom"] = "下揃え",
        ["Align right (along a row: a Spacer pushes it to the end)"] = "右揃え(横並びの中では、スペーサーで右端へ寄せる)",
        ["Align bottom (along a column: a Spacer pushes it to the end)"] = "下揃え(縦並びの中では、スペーサーで下端へ寄せる)",
        // Inspector › Appearance
        ["Appearance"] = "見た目", ["Fill / Text"] = "塗り / 文字色", ["Text token"] = "文字トークン", ["Font"] = "フォント", ["Size / Line"] = "サイズ / 行高", ["Weight"] = "太さ",
        ["Hover"] = "ホバー", ["Pressed"] = "押下", ["Disabled"] = "無効",
        ["A color (#6750A4, #806750A4, Red) or a color token ($color.primary)."] = "色(#6750A4、#806750A4、Red)か、色のトークン($color.primary)。",
        ["Normal, Medium, SemiBold, Bold… or 100–900."] = "Normal、Medium、SemiBold、Bold… か 100〜900。",
        ["Fill / text colors per state: hover, pressed (buttons), disabled. Empty: the design language's own."] = "状態ごとの塗り / 文字色:ホバー、押下(ボタン)、無効。空欄ならデザイン言語の色。",
        // Source Control › UI changes
        ["Binding added: {0} → {1}"] = "紐付けを追加: {0} → {1}", ["Binding removed: {0}"] = "紐付けを削除: {0}", ["Renamed {0} → {1}"] = "名前を変更: {0} → {1}",
        ["Added {0} to {1}"] = "{1} に {0} を追加", ["Added {0}"] = "{0} を追加", ["Removed {0}"] = "{0} を削除", ["synced with its component"] = "コンポーネントと同期",
        ["moved into {0}"] = "{0} の中へ移動", ["Reordered the children of {0}"] = "{0} の中の順番を変更",
        // UI history steps (History tab)
        ["Set {0}"] = "{0} を設定", ["Add {0}"] = "{0} を追加", ["Delete {0}"] = "{0} を削除", ["Move {0}"] = "{0} を移動", ["Resize {0}"] = "{0} のサイズ変更",
        ["Rename node"] = "Node の名前変更", ["Add image"] = "画像を追加", ["Add binding"] = "紐付けを追加", ["Rebind"] = "紐付け先を変更",
        ["Set script class"] = "Script のクラスを設定", ["Set variant"] = "バリアントを設定", ["Set repeatable"] = "繰り返しを設定", ["Set mock rows"] = "モック行を設定",
        ["Set binding target"] = "紐付け先を設定", ["Set binding mode"] = "紐付けのモードを設定", ["Set binding format"] = "紐付けの書式を設定",
        ["Sync components"] = "コンポーネントを同期", ["Set design"] = "デザインを設定", ["New screen"] = "新しい画面", ["New component"] = "新しいコンポーネント",
        ["Rename screen"] = "画面の名前変更", ["Rename component"] = "コンポーネントの名前変更", ["Delete screen"] = "画面を削除", ["Delete component"] = "コンポーネントを削除",
        ["{0} was changed outside Faro, so \"{1}\" can't be undone or redone."] = "{0} が Faro の外で変更されたため、「{1}」は元に戻せません(やり直せません)。",
        ["Saved {0}."] = "{0} を保存しました。", ["Saved. Bindings followed: "] = "保存しました。紐付けを追従: ",
        ["Start"] = "始める", ["Dark"] = "ダーク", ["Light"] = "ライト", ["System"] = "システムに合わせる",
    };
}
