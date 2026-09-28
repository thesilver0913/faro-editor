# Third-Party Notices

Faroは以下のオープンソースソフトウェアを利用しています。特記のないものは MIT License です。
MIT License全文は本リポジトリの `LICENSE` と同一の条文で、著作権表示のみ各ライブラリのものに読み替えます。

| ライブラリ | バージョン | 著作権表示 | リポジトリ | 用途 |
|---|---|---|---|---|
| Avalonia | 12.1.3 | Copyright (c) AvaloniaUI OÜ | https://github.com/AvaloniaUI/Avalonia | 出力アプリ(ランタイム)とエディター本体のUIフレームワーク |
| FluentAvalonia | 3.1.0 | Copyright (c) 2025 amwx | https://github.com/amwx/FluentAvalonia | エディター本体のUIテーマ |
| Dock | 12.1.0.6 | Copyright (c) Wiesław Šoltés | https://github.com/wieslawsoltes/Dock | エディター内パネルのドッキング |
| AvaloniaEdit | 12.0.0 | Copyright (c) 2017 Eli Arbel | https://github.com/AvaloniaUI/AvaloniaEdit | コードエディタ画面 |
| AvaloniaEdit.TextMate | 12.0.0 | Copyright 2017-2026 © The AvaloniaUI Project | https://github.com/AvaloniaUI/AvaloniaEdit | コードエディタの TextMate 配色 |
| TextMateSharp / TextMateSharp.Grammars | 2.0.3 | Copyright (c) 2021 Daniel Peñalba | https://github.com/danipen/TextMateSharp | TextMate 文法・テーマの読み込み(同梱の文法・テーマは VS Code 由来、MIT) |
| Anthropic C# SDK | 12.50.0 | Copyright 2023 Anthropic, PBC. | https://github.com/anthropics/anthropic-sdk-csharp | バイブコーディング(Claude プロバイダ) |
| MaterialColorUtilities | 0.3.0 | Copyright 2021 Google LLC(C# 移植: albi005)| https://github.com/albi005/MaterialColorUtilities | Material 3 の色の役割をシードカラーから作る(**Apache License 2.0**、ランタイム) |
| Roslyn (Microsoft.CodeAnalysis.CSharp) | 5.9.0 | Copyright (c) .NET Foundation and Contributors | https://github.com/dotnet/roslyn | Source/ の解析(レジストリ抽出) |

## フォント

| 名前 | 著作権表示 | ライセンス | 用途 |
|---|---|---|---|
| Inter 4.1 (Regular) | Copyright (c) 2016 The Inter Project Authors | SIL Open Font License 1.1(全文は `assets/fonts/Inter-LICENSE.txt`) | スプラッシュの版表示 |
| Noto Sans JP(Regular・Bold。可変フォントから固定の太さを切り出したもの) | Copyright 2014-2021 Adobe (http://www.adobe.com/), with Reserved Font Name 'Source' | SIL Open Font License 1.1(全文は `assets/fonts/NotoSansJP-LICENSE.txt`) | エディター全体の UI フォント |
| Google Sans Flex(Regular・Medium・Bold。同上) | Copyright 2015 The Google Sans Flex Authors | SIL Open Font License 1.1(全文は `assets/fonts/GoogleSansFlex-LICENSE.txt`) | Material 3 Expressive の書体。Faro.Runtime(C# のアプリ)と Java プロジェクトの `.faro/fonts` に同梱 |

## 実行時にダウンロードするツール(配布物には含まれません)

| 名前 | バージョン | 著作権表示 | リポジトリ | 用途 |
|---|---|---|---|---|
| csharp-ls | 0.28.0 | Copyright (c) 2020-2021 Saulius Menkevičius | https://github.com/razzmatazz/csharp-language-server | C#言語サーバー(LSP)。初回利用時に `dotnet tool install` で Faro の環境設定フォルダへ導入 |

## Java プロジェクトのビルドで Maven が取得するライブラリ(配布物には含まれません)

| 名前 | バージョン | ライセンス | リポジトリ | 用途 |
|---|---|---|---|---|
| OpenJFX (javafx-controls) | 21.0.5 | GPL v2 + Classpath Exception | https://github.com/openjdk/jfx | Java プロジェクトの UI フレームワーク |
| javafx-maven-plugin | 0.0.8 | Apache License 2.0 | https://github.com/openjfx/javafx-maven-plugin | `mvn javafx:run` |
| build-helper-maven-plugin | 3.6.0 | MIT | https://github.com/mojohaus/build-helper-maven-plugin | 同梱ランタイムのソースをビルドに加える |

## 開発ツール(配布物には含まれません)

| 名前 | 著作権表示 | リポジトリ | 用途 |
|---|---|---|---|
| Ponytail (Claude Code skill) | Copyright (c) 2026 DietrichGebert | https://github.com/DietrichGebert/ponytail | `.claude/skills/ponytail/` に同梱。AI支援開発時のコード肥大化防止ルール(ライセンス全文は同ディレクトリの `LICENSE`) |

依存バージョンを更新した際は、各リポジトリのLICENSEで著作権表示・ライセンスに変更がないか確認してください。
