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

    static readonly Dictionary<string, string> Ja = new()
    {
        // Menus (MainWindow.axaml) and panels
        ["_File"] = "ファイル(_F)", ["_Edit"] = "編集(_E)", ["_Select"] = "選択(_S)", ["_Window"] = "ウィンドウ(_W)", ["_Help"] = "ヘルプ(_H)",
        ["_Open Folder…"] = "フォルダを開く(_O)…", ["_Close Project"] = "プロジェクトを閉じる(_C)", ["_Trust Project…"] = "プロジェクトを信頼(_T)…",
        ["_Save"] = "保存(_S)", ["Save _As…"] = "名前を付けて保存(_A)…", ["_Run"] = "実行(_R)", ["S_top"] = "停止(_T)", ["E_xit"] = "終了(_X)",
        ["_Undo"] = "元に戻す(_U)", ["_Redo"] = "やり直し(_R)", ["Cu_t"] = "切り取り(_T)", ["_Copy"] = "コピー(_C)", ["_Paste"] = "貼り付け(_P)", ["D_uplicate"] = "複製(_U)",
        ["_Delete"] = "削除(_D)", ["_Sync Components"] = "コンポーネントを同期(_S)", ["_Preferences…"] = "環境設定(_P)…",
        ["_All Nodes"] = "すべての Node(_A)", ["_None"] = "選択解除(_N)", ["Node by _ID…"] = "ID で Node を選択(_I)…", ["Nodes with _Broken Bindings"] = "紐付けが壊れた Node(_B)",
        ["_Explorer"] = "エクスプローラー(_E)", ["_Canvas"] = "キャンバス(_C)", ["_Inspector"] = "インスペクター(_I)", ["C_ode"] = "コード(_O)", ["Co_nsole"] = "コンソール(_N)",
        ["_Problems"] = "問題(_P)", ["_Output"] = "出力(_O)", ["_AI Chat"] = "AI チャット(_A)", ["_Reset Layout"] = "レイアウトを初期化(_R)",
        ["_Full Screen"] = "全画面(_F)", ["_About Faro"] = "Faro について(_A)",
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
        ["Figma × UI Binding × Vibe Coding — a visual UI editor prototype.\n“Faro” is Italian for lighthouse."] = "Figma × UI Binding × Vibe Coding を統合した UI エディタのプロトタイプ。\n「Faro」はイタリア語で灯台。",
        ["MIT License. Third-party components: see THIRD-PARTY-NOTICES.md."] = "MIT License。第三者のコンポーネントは THIRD-PARTY-NOTICES.md を参照。",

        // Explorer
        ["New Screen…"] = "新しい画面…", ["New Component…"] = "新しいコンポーネント…", ["New C# Class…"] = "新しい C# クラス…", ["New Folder…"] = "新しいフォルダ…",
        ["+ File"] = "+ ファイル", ["+ Folder"] = "+ フォルダ", ["New File…"] = "新しいファイル…", ["New File"] = "新しいファイル", ["File name:"] = "ファイル名:", ["Refresh"] = "最新の情報に更新", ["Collapse All"] = "すべて折りたたむ",
        ["Open as Text"] = "テキストとして開く", ["Copy Path"] = "パスをコピー", ["Copy Relative Path"] = "相対パスをコピー", ["Reveal in File Manager"] = "ファイルマネージャーで表示",
        ["Move"] = "移動", ["{0} has unsaved changes. Save it first."] = "{0} に未保存の変更があります。先に保存してください。",
        ["Rename…"] = "名前の変更…", ["Delete"] = "削除", ["New Screen"] = "新しい画面", ["New Component"] = "新しいコンポーネント", ["ID:"] = "ID:",
        ["New C# Class"] = "新しい C# クラス", ["Class name:"] = "クラス名:", ["New Folder"] = "新しいフォルダ", ["Folder name:"] = "フォルダ名:",
        ["Rename Screen"] = "画面の名前変更", ["Rename Component"] = "コンポーネントの名前変更", ["New ID (references follow):"] = "新しい ID(参照も追従):",
        ["Rename"] = "名前の変更", ["New name:"] = "新しい名前:",
        ["Delete {0}? Bindings of its nodes are removed too. You can undo this with Ctrl+Z on the canvas."] = "{0} を削除しますか？その Node の紐付けも削除されます。キャンバスで Ctrl+Z を押すと元に戻せます。",
        ["Delete {0}? This can't be undone."] = "{0} を削除しますか？元に戻せません。", ["Delete {0} and everything in it? This can't be undone."] = "{0} と中身をすべて削除しますか？元に戻せません。",

        // Canvas
        ["+ Add"] = "+ 追加", ["Run"] = "実行", ["Stop"] = "停止", [" (container)"] = "(コンテナ)", ["Sync components ({0})"] = "コンポーネントを同期 ({0})",
        ["Selected: {0} ({1}) · "] = "選択中: {0} ({1}) · ", ["{0} nodes selected · "] = "{0} 個の Node を選択中 · ",
        ["{0} broken binding(s) · {1} registry members"] = "壊れた紐付け {0} 件 · レジストリ {1} 件", ["Component master · {0} instance(s) to sync · "] = "コンポーネントのマスター · 同期待ちのインスタンス {0} 個 · ",
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
        ["Mock rows (canvas only): {0}"] = "モック行(キャンバスのみ): {0}", ["Bindings"] = "紐付け", ["+ Add binding"] = "+ 紐付けを追加",
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
        ["Ignored {0}: generated files must be .cs files under Source/."] = "{0} は無視しました: 生成できるのは Source/ 以下の .cs ファイルだけです。",
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
        ["Plugins are planned for a later version (spec §12: outside the prototype scope)."] = "プラグインは今後のバージョンで対応予定です(仕様§12: プロトタイプの範囲外)。",
        ["(none)"] = "(なし)", ["Language changes apply to windows opened from now on; restart Faro to update the menus."] = "言語の変更はこれから開くウィンドウに反映されます。メニューは Faro を再起動すると切り替わります。",

        // First-run wizard
        ["Set up Faro"] = "Faro の初期設定", ["Choose how Faro looks. You can change all of this later in Preferences."] = "Faro の見た目を選んでください。あとから環境設定でいつでも変更できます。",
        ["Instance of {0}"] = "{0} のインスタンス", ["Event"] = "イベント", ["Property"] = "プロパティ",
        ["History: Code — {0}"] = "履歴: コード — {0}", ["no file"] = "ファイルなし", ["History: UI graph"] = "履歴: UI グラフ",
        ["Fit"] = "全体表示", ["Add"] = "追加", ["Cut"] = "切り取り", ["Copy"] = "コピー", ["Paste"] = "貼り付け", ["Duplicate"] = "複製",
        ["Move up"] = "前へ移動", ["Move down"] = "後ろへ移動", ["Select parent"] = "親を選択", ["Wrap in"] = "コンテナで囲む", ["Edit master component"] = "マスターを編集",
        ["Start"] = "始める", ["Dark"] = "ダーク", ["Light"] = "ライト", ["System"] = "システムに合わせる",
    };
}
