using System.Reflection;
using System.Xml.Linq;
using Faro.Editor;
using Faro.Runtime;

// Runs against a scratch copy of samples/HelloFaro so the sample itself is never modified.
var sample = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../samples/HelloFaro"));
var root = Directory.CreateTempSubdirectory("faro-check").FullName;
foreach (var dir in new[] { "UI", "Bindings", "Source" })
    foreach (var f in Directory.EnumerateFiles(Path.Combine(sample, dir), "*", SearchOption.AllDirectories))
    {
        var dest = Path.Combine(root, Path.GetRelativePath(sample, f));
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        File.Copy(f, dest);
    }

// Registry: Roslyn finds members without a build.
var registry = Registry.Scan(Path.Combine(root, "Source"));
Check(registry.Any(m => m.Target == "MyApp.Services.OrderService.Submit" && m.IsMethod), "registry has Submit()");
Check(registry.Any(m => m.Target == "MyApp.Models.UserProfile.Name" && !m.IsMethod), "registry has Name");

// Binding check: the sample is clean; a typo is caught with the right suggestion.
var project = FaroProject.Load(root);
Check(BindingCheck.Check(project, registry).Count == 0, "sample has no broken bindings");
project.Binds.First(b => (string?)b.Attribute("nodeId") == "btn1").SetAttributeValue("target", "MyApp.Services.OrderService.Submt");
project.Binds.First(b => (string?)b.Attribute("nodeId") == "btnDetail").SetAttributeValue("target", "Navigate:Screen.Detial");
var issues = BindingCheck.Check(project, registry);
Check(issues.Count == 2, "two broken bindings found");
project.Binds.First().SetAttributeValue("nodeId", "price");
Check(BindingCheck.Check(project, registry).Any(i => i.Message == "Node 'price' does not exist."), "snapshot-internal ids are not screen nodes");
Check(issues[0].Suggestions[0] == "MyApp.Services.OrderService.Submit", "method suggestion");
Check(issues[1].Suggestions[0] == "Navigate:Screen.Detail", "screen suggestion");
Check(BindingCheck.Distance("kitten", "sitting") == 3, "levenshtein");

// Component sync is explicit: editing the master only marks instances out of date until Sync.
project = FaroProject.Load(root);
ComponentSync.Sync(project).ForEach(FaroProject.Save);
Check(ComponentSync.OutOfDate(FaroProject.Load(root)).Count == 0, "all instances synced");
var master = project.Components["Comp.PrimaryButton"];
master.Root!.Element("Node")!.SetAttributeValue("sizing", "Fill");
FaroProject.Save(master);
project = FaroProject.Load(root);
var stale = ComponentSync.OutOfDate(project);
Check(stale.Count == 2 && stale.All(n => (string?)n.Element("Node")!.Attribute("sizing") is null), "instances keep old snapshot until sync");
ComponentSync.Sync(project).ForEach(FaroProject.Save);
Check(ComponentSync.OutOfDate(FaroProject.Load(root)).Count == 0, "re-sync applies master edit");
Check(FaroProject.Load(root).Screens["MainScreen"].Descendants("Override").Any(), "overrides survive sync");

// Rename on save (spec §6): member and class renames are detected and followed by bindings.
var code = File.ReadAllText(Path.Combine(root, "Source/Services/OrderService.cs"));
var renamed = code.Replace("void Submit()", "void Send()");
Check(Registry.Renames(code, renamed) is [("MyApp.Services.OrderService.Submit", "MyApp.Services.OrderService.Send")], "member rename detected");
Check(Registry.Renames(code, renamed.Replace("class OrderService", "class Orders")) is [("MyApp.Services.OrderService", "MyApp.Services.Orders"), ("MyApp.Services.Orders.Submit", "MyApp.Services.Orders.Send")], "class + member rename detected");
Check(Registry.Renames(code, code.Replace("public void Submit() => SubmitCount++;", "public void A() { }\n    public void B() { }")).Count == 0, "ambiguous change is not a rename");
project = FaroProject.Load(root);
Check(Registry.FollowRenames(project, [("MyApp.Services.OrderService", "MyApp.Services.Orders")]).Count == 2, "class rename rewrites both binding files");
Check(project.Binds.Count(b => ((string?)b.Attribute("target"))!.StartsWith("MyApp.Services.Orders.")) == 2, "targets follow class rename");

// Vibe coding: parse generated files, keep writes inside Source/, block broken syntax, diff.
var reply = "Here you go.\n\nFile: Source/Services/Cart.cs\n```csharp\nnamespace MyApp.Services;\npublic class Cart { }\n```\n**File: `../Evil.cs`**\n```csharp\nclass X { }\n```";
var generated = VibeCoding.ParseFiles(reply);
Check(generated.Count == 2 && generated[0].Path == "Source/Services/Cart.cs" && generated[0].Code.Contains("class Cart"), "generated files parsed");
Check(VibeCoding.ResolvePath(root, "Source/Services/Cart.cs") == Path.Combine(root, "Source/Services/Cart.cs"), "path inside Source/ accepted");
Check(new[] { "../Evil.cs", "Source/../Evil.cs", "/etc/passwd.cs", "Source/notes.txt", "UI/MainScreen.cs" }.All(p => VibeCoding.ResolvePath(root, p) is null), "paths outside Source/ or non-.cs rejected");
Check(VibeCoding.SyntaxErrors(generated[0].Code).Count == 0 && VibeCoding.SyntaxErrors("public class { void }").Count > 0, "syntax check");
Check(string.Concat(VibeCoding.Diff("a\nb\nc", "a\nx\nc").Select(d => d.Op)) == " -+ ", "line diff");
var system = VibeCoding.SystemPrompt(FaroProject.Load(root), root, Lifetime.Singleton, true);
Check(system.Contains("[FaroLifetime(Lifetime.Singleton, Persistent = true)]") && system.Contains("class OrderService") && system.Contains("Detail"), "system prompt has lifetime, sources and screens");

// Both providers stream text from their real wire formats (served by a local stand-in server).
using (var server = new System.Net.HttpListener())
{
    var port = new Random().Next(20000, 60000);
    server.Prefixes.Add($"http://127.0.0.1:{port}/");
    server.Start();
    var requests = new List<(string Path, string? Beta, string Body)>();
    _ = Task.Run(async () =>
    {
        while (server.IsListening)
        {
            var ctx = await server.GetContextAsync();
            var body = await new StreamReader(ctx.Request.InputStream).ReadToEndAsync();
            lock (requests) requests.Add((ctx.Request.Url!.AbsolutePath, ctx.Request.Headers["anthropic-beta"], body));
            ctx.Response.ContentType = "text/event-stream";
            var sse = ctx.Request.Url.AbsolutePath.EndsWith("/messages")
                ? string.Concat(
                    Event("message_start", """{"type":"message_start","message":{"id":"msg_1","type":"message","role":"assistant","model":"claude-opus-5","content":[],"stop_reason":null,"stop_sequence":null,"usage":{"input_tokens":1,"output_tokens":1}}}"""),
                    Event("content_block_start", """{"type":"content_block_start","index":0,"content_block":{"type":"text","text":""}}"""),
                    Event("content_block_delta", """{"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":"Hello "}}"""),
                    Event("content_block_delta", """{"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":"Claude"}}"""),
                    Event("content_block_stop", """{"type":"content_block_stop","index":0}"""),
                    Event("message_delta", """{"type":"message_delta","delta":{"stop_reason":"end_turn","stop_sequence":null},"usage":{"output_tokens":2}}"""),
                    Event("message_stop", """{"type":"message_stop"}"""))
                : "data: {\"choices\":[{\"delta\":{\"content\":\"Hello \"}}]}\n\ndata: {\"choices\":[{\"delta\":{\"content\":\"OpenAI\"}}]}\n\ndata: [DONE]\n\n";
            var bytes = System.Text.Encoding.UTF8.GetBytes(sse);
            await ctx.Response.OutputStream.WriteAsync(bytes);
            ctx.Response.Close();
        }
    });
    Environment.SetEnvironmentVariable("ANTHROPIC_BASE_URL", $"http://127.0.0.1:{port}");
    Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", "test-key");
    ChatTurn[] turns = [new(true, "hi")];
    var claude = string.Concat(await new ClaudeProvider("claude-opus-5").Stream("sys", turns, default).ToListAsync());
    var openai = string.Concat(await new OpenAiCompatibleProvider($"http://127.0.0.1:{port}/v1", "local-model").Stream("sys", turns, default).ToListAsync());
    Check(claude == "Hello Claude", "Claude provider streams text");
    Check(openai == "Hello OpenAI", "OpenAI-compatible provider streams text");
    var claudeRequest = requests.First(r => r.Path == "/v1/messages");
    Check(claudeRequest.Beta?.Contains("server-side-fallback") == true && claudeRequest.Body.Contains("\"fallbacks\"") && claudeRequest.Body.Contains("\"model\":\"claude-opus-5\""), "Claude request has model and refusal fallback");
    Check(requests.First(r => r.Path == "/v1/chat/completions").Body.Contains("\"role\":\"system\""), "OpenAI request carries the system prompt");
    server.Stop();
}

// Editor themes (spec §15): built-in TextMate themes, and custom .tmTheme (XML plist) / VS Code JSON files.
Check(new FaroSettings { EditorTheme = FaroSettings.BuiltInEditorTheme }.LoadEditorTheme() is null, "built-in xshd theme");
Check(Enum.GetNames<TextMateSharp.Grammars.ThemeName>().All(t => new FaroSettings { EditorTheme = t }.LoadEditorTheme() is not null), "every TextMate built-in theme loads");
var tmTheme = Path.Combine(root, "test.tmTheme");
File.WriteAllText(tmTheme, """
    <?xml version="1.0" encoding="UTF-8"?>
    <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
    <plist version="1.0"><dict>
      <key>name</key><string>Test</string>
      <key>settings</key><array>
        <dict><key>settings</key><dict><key>background</key><string>#101010</string><key>foreground</key><string>#EEEEEE</string></dict></dict>
        <dict><key>scope</key><string>keyword</string><key>settings</key><dict><key>foreground</key><string>#FF0000</string></dict></dict>
      </array>
    </dict></plist>
    """);
var json = System.Text.Json.Nodes.JsonNode.Parse(FaroSettings.TmThemeToJson(File.ReadAllText(tmTheme)))!;
Check((string?)json["settings"]![1]!["scope"] == "keyword" && (string?)json["settings"]![0]!["settings"]!["background"] == "#101010", "tmTheme plist converts to JSON");
Check(new FaroSettings { EditorTheme = FaroSettings.CustomEditorTheme, EditorThemeFile = tmTheme }.LoadEditorTheme() is not null, "custom .tmTheme loads");
var vscodeTheme = Path.Combine(root, "test.json");
File.WriteAllText(vscodeTheme, """{ "name": "T", "colors": { "editor.background": "#000000" }, "tokenColors": [ { "scope": "keyword", "settings": { "foreground": "#00FF00" } } ] }""");
Check(new FaroSettings { EditorTheme = FaroSettings.CustomEditorTheme, EditorThemeFile = vscodeTheme }.LoadEditorTheme() is not null, "custom VS Code JSON theme loads");

var appTheme = Path.Combine(root, "theme.axaml");
File.WriteAllText(appTheme, """<ResourceDictionary xmlns="https://github.com/avaloniaui" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"><SolidColorBrush x:Key="FaroAccent" Color="#FF8800" /></ResourceDictionary>""");
Check(FaroSettings.LoadThemeFile(appTheme).ContainsKey("FaroAccent"), "custom AXAML app theme loads");
File.WriteAllText(appTheme, """<Border xmlns="https://github.com/avaloniaui" />""");
Check(Throws<InvalidDataException>(() => FaroSettings.LoadThemeFile(appTheme)), "non-ResourceDictionary theme file rejected");

// Runtime binder resolves the same target strings via reflection on the built assembly.
var asm = typeof(MyApp.Services.OrderService).Assembly;
Check(FaroApp.Resolve(asm, "MyApp.Services.OrderService.Submit") is MethodInfo, "runtime resolves method");
Check(FaroApp.Resolve(asm, "MyApp.Models.UserProfile.Name") is PropertyInfo, "runtime resolves property");
Check(Throws<InvalidOperationException>(() => FaroApp.Resolve(asm, "MyApp.Nope.Submit")), "runtime rejects unknown class");
Check(FaroApp.NavigateScreenId("Navigate:Screen.Detail", ["Detail"]) == "Detail", "navigate id");

Directory.Delete(root, true);
Console.WriteLine("All checks passed.");

static void Check(bool ok, string what)
{
    Console.WriteLine($"{(ok ? "ok  " : "FAIL")} {what}");
    if (!ok) Environment.Exit(1);
}

static string Event(string name, string json) => $"event: {name}\ndata: {json}\n\n";

static bool Throws<T>(Action a) where T : Exception { try { a(); return false; } catch (T) { return true; } }
