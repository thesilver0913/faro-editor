# Faro プロジェクト指示

このリポジトリは、UI/UXビジュアルエディタ「Faro」のプロトタイプ実装用リポジトリです。

**作業を始める前に、必ず `ui-editor-tool-spec.md` を読んでください。** ここに書かれている決定事項は全て、claude.ai上での仕様検討チャットで既に合意済みの内容です。仕様の妥当性を再検討する必要はなく、実装フェーズに進んでください。

## プロジェクトの要点

- 名前:**Faro**(イタリア語で灯台の意)
- 基本思想:「Figma × UI Binding × Vibe Coding」の統合ツール
- プロトタイプ対象:C#系言語、UIフレームワークはAvaloniaに固定(実装フェーズで Java + JavaFX を追加)
- エディター自体の実装:C#/.NET(Avalonia)、UIはFluentAvalonia + Adobe Spectrumのトーンのハイブリッド
- 出力方式:ランタイムバインダー型(コード生成型ではない)
- レジストリ抽出:Roslynによるソースコード解析(未ビルドでも可)、実行時解決はSystem.Reflection

## 実装を始める際の指針

1. 仕様書の「12. 将来の拡張ポイント」に書かれている項目(デザイン言語自動切り替え、ブロック的ビジュアルスクリプティング、プラグイン機構)は、**プロトタイプの実装スコープに含めない**でください。範囲外だと明記されている決定事項です。
2. 「11.5 検証すべきリスク事項」に挙がっている項目(ホットリロードとReflectionキャッシュの整合性など)は、実装の初期段階で小さく検証してから、本実装に進むのが望ましいです。
3. 仕様書に書かれていない実装の詳細(クラス名、ファイル配置の細部など)は、都度質問するのではなく、仕様の思想(汎用性を保ちつつプロトタイプは絞る、という方針)に沿って合理的に判断して進めてください。

## ユーザーとの追加合意事項(実装フェーズで決定)

- バイブコーディングのLLMバックエンドは**プロバイダ差し替え可能**にする
- クラスの生存期間・永続化は**C#属性**(`[FaroLifetime(...)]`)でソースに書く
- ComponentDef編集のインスタンス反映は**明示的な同期操作**(インスタンスがスナップショットを保持)
- 紐付けは**画面ごと**(`Bindings/<画面ID>.xml` はその画面だけに適用、Node ID は画面内で一意)。`Bindings/<コンポーネントID>.xml` はそのコンポーネントの全インスタンス共通の紐付け
- 紐付けの `event`/`prop` は**フレームワーク非依存の共通名**(`Click`、`Text` など。Avalonia の `OnClick` 等は使わない)。Avalonia への対応表は `Faro.Runtime/Bindable.cs` だけに置く
- 紐付けの `target` は文字列1つのまま:**最後の `.` より後がメンバー名**、前は言語ごとに決まるクラスの識別子(C# では `名前空間.クラス名`)
- 開始画面は **`faro.json` の `startScreen`**(`Program.cs` には書かない)
- レイアウトは**絶対座標なし**のまま、Stack の `justify`/`alignSelf`/`weight`、全 Node の min/max・`margin`、辺ごとの `padding`、重ね置きの `Container.Overlay`(子を `anchorX`/`anchorY` で固定)で自由度を出す
- サイジングは**幅・高さで分ける**(`widthSizing`/`heightSizing`、`sizing`は両軸の省略形)
- 仕様書にない追加機能として、**インスペクター・キャンバス編集(ドラッグ＆ドロップ含む)・VSCode風エクスプローラー・メニューバー・環境設定・スプラッシュ**を実装する
- 起動時は**ウェルカム画面**(最近のプロジェクト／フォルダを開く／新規作成、テンプレート選択)。`faro.json` のないフォルダは確認のうえ初期化
- 新規プロジェクトは**名前なし(Untitled)で始め**、File › Save / Save As… で名前と場所(既定 `ドキュメント/Faro`)を決める
- インスタンスの中の Node にも紐付けられる(`nodeId="orderList/price"`)
- **リストの実データ**は repeatable なインスタンスへの `prop="Items"`(一覧のプロパティ)。中の Node への紐付けは、対象のクラスが要素の型ならその行の要素に付く
- 紐付けの**表示形式**は `format="¥{0:N0}"`(`{0}`・`N<桁>`・`F<桁>` は両言語で同じ)。イベントのメソッドは引数なしか 1 つ(行の要素か遷移で渡した値)。**遷移の値渡し**は `FaroApp.Navigate("画面ID", 値)` で、遷移先の紐付けは値のクラスのメンバーならその値を使う
- **トークン**は `faro.json` の `"tokens"`(`{"space.m": 16, "color.primary": "#6750A4", "text.title.fontSize": 22}`)、属性に `$space.m`(UiBuilder が読むときに置き換える。ファイルには `$` のまま)。**見た目**は Node の属性 `background`/`foreground`、状態ごとの `hoverBackground` など、`fontFamily`/`fontSize`/`fontWeight`/`lineHeight`、文字スタイルは `textStyle="$text.title"`(トークン `text.title.<項目>` の組)。**バリアント**は `UI/Comp.X@名前.xml` の別マスターで、インスタンスの `variant="名前"` が選ぶ(スナップショットはその版から、紐付けは Comp.X と共通)
- **ワークスペースの信頼**:初めて開くフォルダは Trust か Restricted Mode を選ぶ(Restricted では restore・言語サーバー・Script プレビュー・Run を止める)
- Grid は行・列のトラック指定(`columns="Auto, *, 2*, 120px"`、子は `row`/`column`/`rowSpan`/`columnSpan`)
- **Script 部品**(`Control.Script` + `class`):`FaroScript` を継承したクラスの `Build()` が見た目も動作もコードで作る。キャンバスは最後のビルド結果で実物を表示
- 画面の言語は English / 日本語(初回起動ウィザードと環境設定で選ぶ)。文言は英語をキーに `src/Faro.Editor/L.cs` の表で訳す。新しい文言を足したら表にも足す
- **Java(JavaFX)** も対象言語:`faro.json` の `language` が `Java`。Java 版ランタイムは `src/Faro.Runtime.Java`(ソース)で、各プロジェクトの `.faro/runtime-java` にコピーして Maven でアプリと一緒にビルドする。紐付けの `target` は `パッケージ.クラス.メンバー`(メンバーは public メソッドか Bean プロパティ名)。Java 固有のエディター処理は `src/Faro.Editor/JavaProject.cs` にまとめる(言語サーバーは jdtls:`LspClient` が起動し、`Components` が入れる)。Java 版ランタイムを変えたら C# 版と同じ振る舞いに揃える
- **デザイン言語**は `faro.json` の `design`(Fluent / Material3 / Cupertino / Neumorphism / NeoBrutalism / Simple / Carbon / Clay / Retro、`seedColor`、`theme`)。Fluent と Material3 以外の 7 つは共通のテンプレート `Faro.Runtime/Looks.axaml` と値の表 `DesignLooks.cs`(Java の CSS も同じ表から)で、Node ごとの設定も共通の `look.variant`(ボタン)・`look.surface`(コンテナ)。Node ごとの言語固有の設定は `m3.variant="Tonal"` のような `<言語>.<名前>` 属性で持ち、UiBuilder がスタイルクラス(`m3-variant-tonal`)に変えて `Faro.Runtime/Material3.axaml` が見た目を付ける(ほかの言語では無視される)
- **ライブ更新**:Run は `FARO_LIVE` にプロジェクトのフォルダを渡し、実行中のアプリ(C#・Java)が `UI/`・`Bindings/`・`faro.json` の保存を監視して、いまの画面を同じインスタンスのまま組み直す(UI 変更での再起動はしない)
- **色の紐付け**:全部品の `Foreground`、コンテナの `Background` に紐付けられる(値は `#RRGGBB` か `$color.x` の文字列。状態ごとの色より優先)。**入力部品**に `Control.NumberInput`(`Value`)と `Control.DateInput`(`Date` は `yyyy-MM-dd` の文字列)。コードから別のクラスのインスタンスは `FaroApp.Get<T>()`(Java `FaroApp.get(T.class)`)。`NumberInput` の刻みは `Step`。日付・数値の書式は `faro.json` の `"locale"`(`ja-JP` など。空なら端末の設定)
- キャンバスは `designScope` にアプリと同じ Avalonia の `FluentTheme` を足して、エディターの FluentAvalonia のテンプレートではなくアプリ本来の見た目で描く
- **アイコン部品**(`Control.Icon`、`Icon` プロパティ)は Material Icons(Apache 2.0)の抜粋を `Faro.Runtime/IconSet.cs` と Java の `IconSet.java` に同じ表で持つ(足すときは両方に。チェックが比べる)
- **フォント**はエディターに同梱せず、インスペクターの Aa(`FontPicker`)でオンラインの一覧(Fontsource の API)から選び、`FontLibrary` が TTF をダウンロード(Google のフォントは Google Fonts CSS API、ほかは Fontsource の CDN。設定フォルダの `fonts/` にキャッシュ)してプロジェクトの `Assets/Fonts/` にライセンスの文章と一緒に置く。ランタイムは `Assets/Fonts` を読み込んで `fontFamily` の名前で使う(C# `FaroApp.LoadFonts`、Java `loadFonts`)
- **Android** は APK の出力まで(エミュレーター連携はしない)。C# は `src/Faro.Editor/AndroidApk.cs` が `.faro/android` に Android 用プロジェクトを生成して `dotnet publish` する。ランタイムは `FaroApplication` がデスクトップ(Window)と Android(`IActivityApplicationLifetime`)の両方を扱う。Java は `JavaProject.WriteAndroid` が `.faro/android/pom.xml`(GluonFX、Linux のみ、`GRAALVM_HOME` 必須)を生成し、プロジェクトのファイルを `faro/` のリソースと `index.txt` で APK に入れる(Java ランタイムの `FaroApp.url` がフォルダかリソースかを切り替える)
- **Faro.Runtime は各プロジェクトの `.faro/packages/` に nupkg として同梱**(Runtime を変えたらバージョンを上げる:NuGet キャッシュが同じ版を使い回すため)
- **デバッガー**は `src/Faro.Editor/Debugger.cs`(DAP):C# は netcoredbg(ビルドした dll を `dotnet` で起動)、Java は jdtls に `bundles` で読み込ませた java-debug(`vscode.java.startDebugSession` のポートに TCP で接続し、`Main` を起動)。どちらも `Components` が入れる。画面は `DebugView`(Console の Debug タブ。ウォッチ式も)とコードエディタのブレークポイント欄(右クリックで条件・ヒット回数。ヒット回数は Faro が数える)
- 既定レイアウトは「左端エクスプローラー／中央上キャンバス／中央下コード｜Console(Problems・Output・AI Chat・History・Debug をタブで切り替え。AI Chat は仕様のバイブコーディング画面)／右端インスペクター」(仕様§14の常時表示のうちチャットはタブ切り替えに変更)
- 詳細は `docs/development.md`(開発者向けドキュメント。旧 README の内容)を参照。README は利用者向けの入口。仕様書の該当節には「実装での変更」注記があり、§16 に変更点と追加機能の一覧がある

## ブランチとバージョン

- 開発は `canary`、`main` はリリース用(リリースまで空。PR #1 は打ち消し済み)
- **PR ごとに `Directory.Build.props` の `<Version>` を 0.0.1 上げる**(`samples/HelloFaro/faro.json` の `runtime` も合わせる)。**作業中は `0.1.8-dev1` のように `-devN` を付け、Runtime を変えるたびに N を上げる**(同じ版の古い nupkg を NuGet キャッシュが使い回すため)。PR をマージできる状態になったら `-devN` を外す。末尾は 0〜9 で繰り上がる:0.1.8 → 0.1.9 → **0.2.0**(0.1.10 にはしない)。Faro.Runtime の nupkg はこの版で作られ、既存プロジェクトには開いたときに更新を案内する。**0.2.4 の次は 1.0.0**(最初の公開 `v1.0.0-beta.1`)。以降も PR ごとに 1.0.1 → 1.0.2 … と上げ、公開するときはタグでチャンネルの接尾辞を付ける。1.0.0 の正式版までは PR ごとに `1.0.0-beta.N` の N を上げた(1.0.0-beta.17 まで)。**1.0.0 の正式版(`v1.0.0`)の後は `1.0.1-beta.1` から開発を進め、PR ごとに `1.0.1-beta.N` の N を上げる**(ユーザーの指示)。次の正式版を出すときに `-beta.N` を外して `1.0.1` にし、その後は `1.0.2-beta.1` から同じように進める。公開は同じ版のタグ(`v1.0.1-beta.N`、正式版は `v1.0.1`)。正式版のリリースノートは `docs/release-notes/vX.Y.Z.md` に書く(`release.yml` が自動生成の PR 一覧の前に載せる)
- **リリースはタグで**:`vX.Y.Z`(Stable、`main`)/ `vX.Y.Z-beta.N` / `vX.Y.Z-canary.N` を push すると `release.yml` がインストーラー等を GitHub Release に載せ、Faro の更新確認が各チャンネルで拾う。版の並びは dev < canary < beta < 正式版
- リリースで `canary` を `main` にマージする前に、`main` の打ち消しコミット(f354289)を打ち消すこと。そのままマージすると、PR #1 の変更が「取り込み済み・打ち消し済み」と扱われて初期プロトタイプ分が入らない

## コーディング方針

- `.claude/skills/ponytail/` のPonytailスキル(SessionStartフックで自動有効化)に従い、コードを必要以上に増やさない
- 変更後は `dotnet run --project tests/Faro.Checks` を通す
