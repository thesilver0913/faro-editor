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
Check(BindingCheck.Check(project, registry).Any(i => i.Message.StartsWith("Node 'price' does not exist")), "snapshot-internal ids are not screen nodes");

// Bindings are per screen (Bindings/<ScreenId>.xml): the same node id on another screen is not affected.
project = FaroProject.Load(root);
Check(project.BindsFor("MainScreen").Any(b => (string?)b.Attribute("nodeId") == "btn1") && !project.BindsFor("Detail").Any(b => (string?)b.Attribute("nodeId") == "btn1"), "binds belong to their screen's file");
project.Screens["MainScreen"].Root!.Element("Node")!.SetAttributeValue("id", "back"); // "back" is a Detail node id
Check(BindingCheck.Check(project, registry).All(i => i.Screen != "MainScreen" || i.NodeId != "back"), "same id on another screen doesn't pick up its bindings");
project = FaroProject.Load(root);
File.WriteAllText(Path.Combine(root, "Bindings/Nowhere.xml"), "<Bindings />");
Check(BindingCheck.Check(FaroProject.Load(root), registry).Any(i => i.Screen == "Nowhere" && i.Message.Contains("doesn't belong to any screen")), "orphan bindings file reported");
File.Delete(Path.Combine(root, "Bindings/Nowhere.xml"));
Check(CanvasEdit.NewId(project.Screens["Detail"], "Control.Button") == "button1" && CanvasEdit.Rename(project, project.Screens["Detail"], "back", "title").Error is null, "ids are unique per screen, not per project");
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

// UI graph history (spec §10): a Faro-made change is one step; undo/redo restore files; external edits are never overwritten.
project = FaroProject.Load(root);
var masterNode = project.Components["Comp.PrimaryButton"].Root!.Element("Node")!;
masterNode.SetAttributeValue("height", "40");
FaroProject.Save(project.Components["Comp.PrimaryButton"]);
project = FaroProject.Load(root);
var screenFile = Path.Combine(root, "UI/MainScreen.xml");
var beforeSync = File.ReadAllText(screenFile);
UiHistory.Commit("Sync components", ComponentSync.Sync(project));
var afterSync = File.ReadAllText(screenFile);
Check(afterSync != beforeSync && UiHistory.UndoLabel == "Sync components", "sync recorded as one UI step");
Check(UiHistory.Undo() is null && File.ReadAllText(screenFile) == beforeSync && UiHistory.RedoLabel == "Sync components", "UI undo restores the files");
Check(UiHistory.Redo() is null && File.ReadAllText(screenFile) == afterSync, "UI redo reapplies the step");
File.AppendAllText(screenFile, "<!-- edited elsewhere -->");
Check(UiHistory.Undo() is { } refused && refused.Contains("changed outside Faro") && File.ReadAllText(screenFile).Contains("edited elsewhere") && UiHistory.UndoLabel is null, "UI undo never overwrites an external edit");
File.WriteAllText(screenFile, afterSync);

// Component masters (spec §5): edited like screens; their own bindings apply inside every instance.
project = FaroProject.Load(root);
var paths = UiBuilder.InstancePaths(project.Screens["MainScreen"].Root!.Element("Node")!).ToList();
Check(paths.Contains(("btn1/", "Comp.PrimaryButton")) && paths.Contains(("orderList/", "Comp.OrderRow")) && paths.Count == 3, "instance paths for component-level bindings");
var nested = System.Xml.Linq.XElement.Parse("""<Node id="r" type="Container.Stack"><Node id="card" type="Instance" component="Comp.Card"><Node id="root" type="Container.Stack"><Node id="ok" type="Instance" component="Comp.PrimaryButton"><Node id="root" type="Control.Button" /></Node></Node></Node></Node>""");
Check(UiBuilder.InstancePaths(nested).Contains(("card/ok/", "Comp.PrimaryButton")), "nested instance paths");
File.WriteAllText(Path.Combine(root, "Bindings/Comp.OrderRow.xml"), """<Bindings><Bind nodeId="price" prop="Text" target="MyApp.Services.OrderService.Summary" mode="OneWay" /><Bind nodeId="nope" event="Click" target="MyApp.Services.OrderService.Submit" /></Bindings>""");
project = FaroProject.Load(root);
var componentIssues = BindingCheck.Check(project, registry).Where(i => i.Screen == "Comp.OrderRow").ToList();
Check(componentIssues.Count == 1 && componentIssues[0].NodeId == "nope", "component bindings checked against the master's nodes");
// Binding files use framework-neutral event/property names (Bindable), checked against the node's type.
var mainBinds = project.BindingFiles.First(d => FaroProject.ScreenOf(d) == "MainScreen");
mainBinds.Root!.Add(System.Xml.Linq.XElement.Parse("""<Bind nodeId="btn1" event="OnClick" target="MyApp.Services.OrderService.Submit" />"""));
var vocabIssues = BindingCheck.Check(project, registry).Where(i => i.Screen == "MainScreen").ToList();
Check(vocabIssues.Count == 1 && vocabIssues[0].Message == "Control.Button has no event 'OnClick'." && vocabIssues[0].Suggestions[0] == "Click", "Avalonia event names are rejected, the neutral name suggested");
Check(Bindable.TypeOf(CanvasEdit.Find(project.Screens["MainScreen"], "orderList")!) == "Container.Stack" && Bindable.For("Container.Stack")!.Props.ContainsKey("Visible")
    && Bindable.For(new Avalonia.Controls.Button())!.Events["Click"] == Avalonia.Controls.Button.ClickEvent && Bindable.For(new Avalonia.Controls.TextBox())!.Type == "Control.TextInput", "neutral names map to Avalonia per node type");
var changedBox = new Avalonia.Controls.TextBox();
var changedFired = false;
changedBox.AddHandler(Bindable.For(changedBox)!.Events["Changed"], (EventHandler<Avalonia.Interactivity.RoutedEventArgs>)((_, _) => changedFired = true));
changedBox.RaiseEvent(new Avalonia.Controls.TextChangedEventArgs(Avalonia.Controls.TextBox.TextChangedEvent));
Check(changedFired, "TextInput Changed reaches a runtime-style handler");
var rowMaster = project.Components["Comp.OrderRow"];
CanvasEdit.Add(project, rowMaster, "root", "Control.Text");
Check(ComponentSync.OutOfDate(project).Any(n => (string?)n.Attribute("id") == "orderList"), "editing a master leaves instances to sync");
UiHistory.CommitFiles("Rename component", ProjectFiles.RenameComponent(FaroProject.Load(root), "Comp.OrderRow", "Comp.Row"));
Check(File.Exists(Path.Combine(root, "Bindings/Comp.Row.xml")) && !File.Exists(Path.Combine(root, "Bindings/Comp.OrderRow.xml")), "component rename moves its bindings file");
UiHistory.CommitFiles("Delete component", ProjectFiles.Delete(FaroProject.Load(root), FaroProject.Load(root).Components["Comp.Row"]));
Check(!File.Exists(Path.Combine(root, "Bindings/Comp.Row.xml")), "component delete removes its bindings file");
Check(UiHistory.Undo() is null && UiHistory.Undo() is null && File.Exists(Path.Combine(root, "Bindings/Comp.OrderRow.xml")), "component rename/delete undo");
File.Delete(Path.Combine(root, "Bindings/Comp.OrderRow.xml"));

// Canvas editing (spec §5): add / props / overrides / rename with binding follow / move / delete with binds.
project = FaroProject.Load(root);
var main = project.Screens["MainScreen"];
var added = CanvasEdit.Add(project, main, "actions", "Control.Button");
Check((string?)added.Attribute("id") == "button1" && added.Parent == CanvasEdit.Find(main, "actions") && CanvasEdit.GetProp(added, "Text") == "Button", "add into selected container with a unique id");
var afterText = CanvasEdit.Add(project, main, "title", "Control.Text");
Check(afterText.ElementsBeforeSelf("Node").LastOrDefault() == CanvasEdit.Find(main, "title"), "add after a selected non-container");
var instance = CanvasEdit.Add(project, main, null, "", "Comp.PrimaryButton");
Check((string?)instance.Attribute("type") == "Instance" && instance.Element("Node") is not null && ComponentSync.OutOfDate(project).All(n => n != instance), "component instance added with its snapshot");
CanvasEdit.SetProp(instance, "Text", "OK");
Check(instance.Elements("Override").Single().Attribute("value")!.Value == "OK" && CanvasEdit.GetProp(instance, "Text") == "OK", "instance props edit as Override");
CanvasEdit.SetProp(added, "Text", null);
Check(CanvasEdit.GetProp(added, "Text") is null, "empty prop value removes it");
var (renameError, renamed2) = CanvasEdit.Rename(project, main, "btn1", "sendButton");
Check(renameError is null && project.Binds.Any(b => (string?)b.Attribute("nodeId") == "sendButton") && renamed2.Count == 2, "node rename follows bindings");
Check(CanvasEdit.Rename(project, main, "sendButton", "txt1").Error is not null && CanvasEdit.Rename(project, main, "sendButton", "a b").Error is not null, "rename rejects duplicates and spaces");
Check(CanvasEdit.Move(main, "sendButton", +1) && CanvasEdit.Find(main, "sendButton")!.ElementsBeforeSelf("Node").Last().Attribute("id")!.Value == "btnDetail", "move down");
Check(!CanvasEdit.Move(main, "root", -1), "root can't move");
var deleted = CanvasEdit.Delete(project, main, ["actions"]);
Check(CanvasEdit.Find(main, "sendButton") is null && !project.Binds.Any(b => (string?)b.Attribute("nodeId") is "sendButton" or "btnDetail") && deleted.Count == 2, "delete removes the subtree and its bindings");
Check(CanvasEdit.Delete(project, main, ["root"]).Count == 0, "root can't be deleted");
Check(CanvasEdit.BindingsFileFor(project, "Detail").BaseUri.EndsWith("Detail.xml"), "new binds go to the screen's bindings file");

// Drag and drop (MoveTo): reorder, move into another container, refuse invalid drops.
project = FaroProject.Load(root);
main = project.Screens["MainScreen"];
Check(CanvasEdit.MoveTo(main, "btnDetail", "actions", 0) && CanvasEdit.Find(main, "actions")!.Elements("Node").First().Attribute("id")!.Value == "btnDetail", "drag reorders within a container");
Check(CanvasEdit.MoveTo(main, "title", "actions", 99) && CanvasEdit.Find(main, "title")!.Parent == CanvasEdit.Find(main, "actions") && CanvasEdit.Find(main, "title")!.ElementsAfterSelf().Any() is false, "drag moves into another container (clamped to the end)");
Check(!CanvasEdit.MoveTo(main, "actions", "actions", 0) && !CanvasEdit.MoveTo(main, "root", "actions", 0) && !CanvasEdit.MoveTo(main, "txt1", "greeting", 0), "invalid drops refused (into itself, root, non-container)");

// Explorer file operations: new / rename (with follow) / delete, each one undoable step.
project = FaroProject.Load(root);
UiHistory.CommitFiles("New screen", ProjectFiles.NewScreen(project, "Settings"));
Check(FaroProject.Load(root).Screens.ContainsKey("Settings"), "new screen file");
Check(Throws<ArgumentException>(() => ProjectFiles.NewScreen(FaroProject.Load(root), "Settings")) && Throws<ArgumentException>(() => ProjectFiles.NewScreen(FaroProject.Load(root), "../evil")), "new screen rejects duplicates and path-like ids");
Check(UiHistory.Undo() is null && !FaroProject.Load(root).Screens.ContainsKey("Settings") && !File.Exists(Path.Combine(root, "UI/Settings.xml")), "undo removes the created file");
File.WriteAllText(Path.Combine(root, "faro.json"), """{ "name": "T", "startScreen": "Detail" }""");
UiHistory.CommitFiles("Rename screen", ProjectFiles.RenameScreen(FaroProject.Load(root), "Detail", "Info"));
project = FaroProject.Load(root);
Check(project.BindsFor("Info").Any(b => (string?)b.Attribute("nodeId") == "back") && !File.Exists(Path.Combine(root, "Bindings/Detail.xml")), "screen rename moves its bindings file");
Check(project.Screens.ContainsKey("Info") && !File.Exists(Path.Combine(root, "UI/Detail.xml")) && project.Binds.Any(b => (string?)b.Attribute("target") == "Navigate:Screen.Info")
    && project.StartScreen == "Info", "screen rename follows file, Navigate targets and start screen (faro.json)");
Check(UiHistory.Undo() is null && FaroProject.Load(root).Screens.ContainsKey("Detail"), "screen rename undoable");
File.WriteAllText(Path.Combine(root, "faro.json"), """{ "name": "T", "startScreen": "Detial" }""");
Check(BindingCheck.Check(FaroProject.Load(root), registry).Any(i => i.Message.Contains("Start screen 'Detial'") && i.Suggestions[0] == "Detail"), "a missing start screen is a problem");
File.WriteAllText(Path.Combine(root, "faro.json"), """{ "name": "T", "startScreen": "Detail" }""");
UiHistory.CommitFiles("Rename component", ProjectFiles.RenameComponent(FaroProject.Load(root), "Comp.PrimaryButton", "Comp.MainButton"));
project = FaroProject.Load(root);
Check(project.Components.ContainsKey("Comp.MainButton") && project.Screens["MainScreen"].Descendants("Node").Count(n => (string?)n.Attribute("component") == "Comp.MainButton") == 2, "component rename follows instances");
UiHistory.CommitFiles("Delete screen", ProjectFiles.Delete(project, project.Screens["Detail"]));
project = FaroProject.Load(root);
Check(!project.Screens.ContainsKey("Detail") && !File.Exists(Path.Combine(root, "Bindings/Detail.xml")), "screen delete removes its bindings file");
Check(UiHistory.Undo() is null && UiHistory.Undo() is null && FaroProject.Load(root).Screens.ContainsKey("Detail") && FaroProject.Load(root).Components.ContainsKey("Comp.PrimaryButton"), "deletes and renames undo in order");
var classText = ProjectFiles.ClassFile(Path.Combine(root, "Source"), "Services/Payments", "Invoice");
Check(classText.Contains("namespace MyApp.Services.Payments;") && classText.Contains("public class Invoice : FaroObject") && VibeCoding.SyntaxErrors(classText).Count == 0, "new class file uses the project namespace");

// Project setup (welcome screen): templates, vendored runtime package, initialize without overwriting.
var projects = Directory.CreateTempSubdirectory("faro-projects").FullName;
var empty = ProjectSetup.Create(projects, "EmptyApp", "Empty");
Check(ProjectSetup.IsFaroProject(empty) && FaroProject.Load(empty).Screens.ContainsKey("MainScreen")
    && File.ReadAllText(Path.Combine(empty, "EmptyApp.csproj")).Contains("PackageReference Include=\"Faro.Runtime\"")
    && Directory.EnumerateFiles(Path.Combine(empty, ".faro/packages"), "Faro.Runtime.*.nupkg").Any()
    && FaroProject.Load(empty).StartScreen == "MainScreen" && File.ReadAllText(Path.Combine(empty, "EmptyApp.csproj")).Contains("faro.json;"), "empty project: faro.json, start screen, csproj, vendored runtime");
var sampleProject = ProjectSetup.Create(projects, "SampleApp", "Sample");
Check(BindingCheck.Check(FaroProject.Load(sampleProject), Registry.Scan(Path.Combine(sampleProject, "Source"))).Count == 0 && FaroProject.Load(sampleProject).Screens.Count == 2, "sample project copies a working template");
Check(Throws<ArgumentException>(() => ProjectSetup.Create(projects, "EmptyApp", "Empty")) && Throws<ArgumentException>(() => ProjectSetup.Create(projects, "../bad", "Empty")) && Throws<ArgumentException>(() => ProjectSetup.Create(projects, "1st", "Empty"))
    && Throws<ArgumentException>(() => ProjectSetup.Create("relative/dir", "Ok", "Empty")) && Path.IsPathRooted(ProjectSetup.DefaultLocation), "project name and location validated");
var existing = Path.Combine(projects, "existing-folder");
Directory.CreateDirectory(existing);
File.WriteAllText(Path.Combine(existing, "Program.cs"), "// mine");
File.WriteAllText(Path.Combine(existing, "Mine.csproj"), "<Project />");
ProjectSetup.Initialize(existing);
Check(ProjectSetup.IsFaroProject(existing) && File.ReadAllText(Path.Combine(existing, "Program.cs")) == "// mine" && Directory.EnumerateFiles(existing, "*.csproj").Count() == 1
    && new[] { "UI", "Source", "Bindings", "Assets" }.All(f => Directory.Exists(Path.Combine(existing, f))), "initializing a folder adds what's missing and keeps existing files");
// Runtime update check: an older project gets the newer bundled package (versions compared numerically).
var fakeApp = Path.Combine(projects, "app");
Directory.CreateDirectory(Path.Combine(fakeApp, "runtime"));
var realPackage = ProjectSetup.BundledRuntime().Package;
File.Copy(realPackage, Path.Combine(fakeApp, "runtime", "Faro.Runtime.0.1.9.nupkg"));
var old = ProjectSetup.Create(projects, "OldApp", "Empty", fakeApp);
Check(ProjectSetup.ProjectRuntime(old) == new Version(0, 1, 9) && !ProjectSetup.RuntimeUpdateAvailable(old, fakeApp), "project runtime read from its csproj");
File.Copy(realPackage, Path.Combine(fakeApp, "runtime", "Faro.Runtime.0.1.10.nupkg"));
Check(ProjectSetup.BundledRuntime(fakeApp).Version == new Version(0, 1, 10) && ProjectSetup.RuntimeUpdateAvailable(old, fakeApp), "0.1.10 is newer than 0.1.9");
ProjectSetup.UpdateRuntime(old, fakeApp);
Check(ProjectSetup.ProjectRuntime(old) == new Version(0, 1, 10) && File.ReadAllText(Path.Combine(old, "faro.json")).Contains("\"0.1.10\"")
    && Directory.EnumerateFiles(Path.Combine(old, ".faro/packages")).Select(Path.GetFileName).SequenceEqual(["Faro.Runtime.0.1.10.nupkg"]) && !ProjectSetup.RuntimeUpdateAvailable(old, fakeApp), "runtime update vendors the package and bumps csproj and faro.json");
Check(ProjectSetup.ProjectRuntime(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../samples/HelloFaro"))) is null, "projects without the package reference are skipped");
Directory.Delete(projects, true);

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
