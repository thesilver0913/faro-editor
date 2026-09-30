using System.Reflection;
using System.Xml.Linq;
using Faro.Editor;
using Faro.Runtime;

// Settings go to a scratch file: the checks never touch the user's Faro settings.
Environment.SetEnvironmentVariable("FARO_SETTINGS", Path.Combine(Directory.CreateTempSubdirectory("faro-settings").FullName, "settings.json"));

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
// Variants: "Comp.PrimaryButton@Outlined" is a copy of the master; an instance picks it with variant="Outlined" and shares the binds.
var addedVariant = ProjectFiles.NewVariant(FaroProject.Load(root), "Comp.PrimaryButton", "Outlined").Single();
File.WriteAllText(addedVariant.Key, addedVariant.Value!.Replace("<Node id=\"root\"", "<Node m3.variant=\"Outlined\" id=\"root\""));
var variantProject = FaroProject.Load(root);
var picked = CanvasEdit.Find(variantProject.Screens["MainScreen"], "btn1")!;
picked.SetAttributeValue("variant", "Outlined");
Check(addedVariant.Key.EndsWith("Comp.PrimaryButton@Outlined.xml") && ProjectFiles.Variants(variantProject, "Comp.PrimaryButton").SequenceEqual(["Outlined"])
    && ComponentSync.OutOfDate(variantProject).Contains(picked) && ComponentSync.Sync(variantProject).Count > 0 && (string?)picked.Element("Node")!.Attribute("m3.variant") == "Outlined"
    && variantProject.BindsFor("Comp.PrimaryButton@Outlined").SequenceEqual(variantProject.BindsFor("Comp.PrimaryButton"))
    && Throws<ArgumentException>(() => ProjectFiles.NewVariant(variantProject, "Comp.PrimaryButton", "Outlined"))
    && ProjectFiles.RenameComponent(variantProject, "Comp.PrimaryButton", "Comp.Btn").Keys.Any(k => k.EndsWith("Comp.Btn@Outlined.xml")), "variants: copied, picked, synced, sharing binds, renamed with their component");
File.Delete(addedVariant.Key);

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

// History list: jump to any step (undo or redo several at once).
var historyFile = Path.Combine(root, "UI", "HistoryCheck.xml");
foreach (var n in new[] { "one", "two", "three" }) UiHistory.CommitFiles("Write " + n, new Dictionary<string, string?> { [historyFile] = n });
Check(UiHistory.Labels.TakeLast(3).SequenceEqual(["Write one", "Write two", "Write three"]) && UiHistory.GoTo(UiHistory.Applied - 2) is null && File.ReadAllText(historyFile) == "one"
    && UiHistory.Labels.Count == UiHistory.Applied + 2 && UiHistory.GoTo(UiHistory.Applied + 2) is null && File.ReadAllText(historyFile) == "three", "history list jumps back and forward");
File.Delete(historyFile);
UiHistory.Clear();

// Debugger: breakpoints toggle per line; LSP and DAP share the Content-Length framing.
Debugger.Toggle("/p/A.cs", 3); Debugger.Toggle("/p/A.cs", 5); Debugger.Toggle("/p/A.cs", 3);
var dapBody = """{"type":"event","event":"stopped","body":{"threadId":1}}""";
var framed = new MemoryStream(System.Text.Encoding.UTF8.GetBytes($"Content-Length: {System.Text.Encoding.UTF8.GetByteCount(dapBody)}\r\n\r\n{dapBody}"));
Check(Debugger.Breakpoints["/p/A.cs"].SequenceEqual([5]) && (string?)LspClient.ReadMessage(framed)?["event"] == "stopped" && LspClient.ReadMessage(framed) is null, "breakpoints toggle; debug adapter messages are framed like LSP");
Debugger.Breakpoints.Clear();

// Source control: `git status --porcelain=v1 -b` lines.
var (gitBranch, gitFiles) = GitView.ParseStatus(["## main...origin/main [ahead 1]", " M UI/MainScreen.xml", "?? Source/New.cs", "R  Old.cs -> Renamed.cs"]);
Check(gitBranch == "main...origin/main [ahead 1]" && gitFiles.SequenceEqual([("M", "UI/MainScreen.xml"), ("??", "Source/New.cs"), ("R", "Renamed.cs")]), "git status parsed");

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
Check(vocabIssues[0].Fix == "event" && BindingCheck.Check(project, registry).Where(i => i.Message.Contains("not found in Source")).All(i => i.Fix == "target"), "issues name the attribute their suggestions replace");
// Mock rows (spec §10.5): the canvas shows one copy per row; the files and the runtime keep a single node.
var orderList = CanvasEdit.Find(project.Screens["MainScreen"], "orderList")!;
Check(MockData.Fields(orderList).SequenceEqual(["name", "price"]) && MockData.Rows(orderList).Count == 3 && MockData.Rows(orderList)[1].SequenceEqual(["みかん", "¥80"]), "mock rows read by field");
var expanded = MockData.Expand(project.Screens["MainScreen"].Root!.Element("Node")!);
var copies = expanded.Descendants("Node").Where(n => ((string?)n.Attribute("id"))?.StartsWith("orderList") == true).ToList();
Check(copies.Select(n => (string?)n.Attribute("id")).SequenceEqual(["orderList", "orderList~2", "orderList~3"])
    && CanvasEdit.GetProp(copies[1].Descendants("Node").First(n => (string?)n.Attribute("id") == "name"), "Text") == "みかん"
    && project.Screens["MainScreen"].Descendants("Node").Count(n => (string?)n.Attribute("id") == "orderList") == 1, "mock rows expand on a copy only");
var mockBuilt = new Dictionary<string, Avalonia.Controls.Control>();
UiBuilder.Build(project.Screens["MainScreen"].Root!.Element("Node")!, mockBuilt, root);
Check(!mockBuilt.Keys.Any(k => k.Contains('~')) && mockBuilt.ContainsKey("orderList/name"), "the runtime ignores mock rows");
// Design language: faro.json "design", per-node options as style classes, M3 colors from the seed.
Check(FaroProject.Load(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../samples/HelloFaro"))).Design is { Language: "Material3", Theme: "Light" } && AppDesign.Read(null) == new AppDesign()
    && AppDesign.Read(System.Text.Json.Nodes.JsonNode.Parse("""{"language":"Nope","seedColor":"red?","theme":"Dim"}""")) == new AppDesign(), "design read, unknown values fall back");
Check(mockBuilt["btnDetail"].Classes.Contains("m3-variant-tonal") && mockBuilt["header"].Classes.Contains("m3-corner-xl") && !mockBuilt["btn1"].Classes.Any(), "m3.* attributes become style classes");
var overridden = UiBuilder.Build(System.Xml.Linq.XElement.Parse("""<Node id="b" type="Instance" component="Comp.X" m3.variant="Text"><Node id="root" type="Control.Button" m3.variant="Outlined" /></Node>"""), new Dictionary<string, Avalonia.Controls.Control>(), root);
Check(overridden.Classes.Contains("m3-variant-text") && !overridden.Classes.Contains("m3-variant-outlined"), "an instance's design options win over its master's");
var scheme = AppDesign.Scheme(MaterialColorUtilities.Palettes.CorePalette.Of(0xFF6750A4), dark: false);
Check(scheme["M3Primary"] is Avalonia.Media.ISolidColorBrush { Color: var primary } && primary == Avalonia.Media.Color.Parse("#65558F")
    && scheme["M3Surface"] is Avalonia.Media.ISolidColorBrush { Color.R: > 250 }, "M3 color roles from the seed");
var sampleDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../samples/HelloFaro"));
var designed = ProjectSetup.DesignChange(sampleDir, new AppDesign("Material3", "#006A6A", "Dark"), new Dictionary<string, string> { ["space.m"] = "16", ["color.primary"] = "#6750A4" }).Single();
Check(AppDesign.Read(System.Text.Json.Nodes.JsonNode.Parse(designed.Value!)!["design"]) == new AppDesign("Material3", "#006A6A", "Dark")
    && !ProjectSetup.DesignChange(sampleDir, new AppDesign(), new Dictionary<string, string>()).Single().Value!.Contains("design") && designed.Value!.Contains("startScreen"), "design written to faro.json");
Check((double?)System.Text.Json.Nodes.JsonNode.Parse(designed.Value!)!["tokens"]!["space.m"] == 16, "tokens written to faro.json");
var mockCopy = new System.Xml.Linq.XElement(orderList);
MockData.SetRows(mockCopy, [["a", "1"], ["b", ""]]);
Check(MockData.Rows(mockCopy).Count == 2 && MockData.Rows(mockCopy)[1].SequenceEqual(["b", ""]) && mockCopy.Elements("MockRow").Last().Elements("Set").Count() == 1, "mock rows written back");
// Bindings to nodes inside an instance ("orderList/price"): checked against the snapshot, followed on rename/delete.
var mainGraph = project.Screens["MainScreen"];
Check(CanvasEdit.FindPath(mainGraph, "orderList/price") is { } innerPrice && (string?)innerPrice.Attribute("type") == "Control.Text"
    && CanvasEdit.FindPath(mainGraph, "orderList/nope") is null && CanvasEdit.FindPath(mainGraph, "price") is null, "node paths reach inside instance snapshots only");
var innerFile = project.BindingFiles.First(d => FaroProject.ScreenOf(d) == "MainScreen");
innerFile.Root!.Elements("Bind").Where(b => (string?)b.Attribute("event") == "OnClick").Remove();
innerFile.Root!.Add(System.Xml.Linq.XElement.Parse("""<Bind nodeId="orderList/price" prop="Text" target="MyApp.Services.OrderService.Summary" mode="OneWay" />"""),
    System.Xml.Linq.XElement.Parse("""<Bind nodeId="orderList/nope" prop="Text" target="MyApp.Services.OrderService.Summary" />"""));
var innerIssues = BindingCheck.Check(project, registry).Where(i => i.Screen == "MainScreen").ToList();
Check(innerIssues.Count == 1 && innerIssues[0].NodeId == "orderList/nope", "a bind inside an instance is checked like any other (and Items on a list)");
CanvasEdit.Rename(project, mainGraph, "orderList", "items");
Check(project.BindsFor("MainScreen").Count(b => ((string?)b.Attribute("nodeId"))?.StartsWith("items/") == true) == 5 && !project.BindsFor("MainScreen").Any(b => ((string?)b.Attribute("nodeId"))?.StartsWith("orderList") == true), "renaming an instance follows binds inside it");
CanvasEdit.Delete(project, mainGraph, ["items"]);
Check(!project.BindsFor("MainScreen").Any(b => ((string?)b.Attribute("nodeId"))?.StartsWith("items/") == true), "deleting an instance removes binds inside it");
project = FaroProject.Load(root);
// Script nodes: a FaroScript class builds the control; the runtime wraps it so bindings see a Script node.
Check(Registry.ScriptClasses(Path.Combine(root, "Source")).SequenceEqual(["MyApp.Views.Stamp"]) && !registry.Any(m => m.Target.EndsWith(".Build")), "script classes found; their Build() isn't a binding target");
var scriptGraph = System.Xml.Linq.XElement.Parse("""<Node id="s" type="Control.Script" class="MyApp.Views.Stamp" />""");
UiBuilder.ScriptFactory = null;
Check(UiBuilder.Build(scriptGraph, new Dictionary<string, Avalonia.Controls.Control>(), root) is Avalonia.Controls.ContentControl { Content: Avalonia.Controls.Border } placeholder
    && Bindable.For(placeholder)!.Type == "Control.Script", "a script without a factory shows a placeholder");
UiBuilder.ScriptFactory = name => ((FaroScript)Activator.CreateInstance(typeof(MyApp.Views.Stamp).Assembly.GetType(name)!)!).Build();
Check(UiBuilder.Build(scriptGraph, new Dictionary<string, Avalonia.Controls.Control>(), root) is Avalonia.Controls.ContentControl { Content: Avalonia.Controls.Border { Child: Avalonia.Controls.StackPanel } }, "a script builds its control in code");
UiBuilder.ScriptFactory = _ => throw new InvalidOperationException("boom");
Check(UiBuilder.Build(scriptGraph, new Dictionary<string, Avalonia.Controls.Control>(), root) is Avalonia.Controls.ContentControl { Content: Avalonia.Controls.Border { Child: Avalonia.Controls.TextBlock { Text: var failed } } } && failed!.Contains("boom"), "a failing script shows its error in place");
UiBuilder.ScriptFactory = null;
Check(ScriptPreview.Build(sampleDir, "MyApp.Views.Stamp") is Avalonia.Controls.Border && Throws<InvalidOperationException>(() => ScriptPreview.Build(sampleDir, "MyApp.Views.Nope")), "the canvas previews scripts from the last build");
CanvasEdit.Find(project.Screens["Detail"], "stamp")!.SetAttributeValue("class", "MyApp.Views.Stmp");
Check(BindingCheck.Check(project, registry, Registry.ScriptClasses(Path.Combine(root, "Source"))).Any(i => i.NodeId == "stamp" && i.Suggestions[0] == "MyApp.Views.Stamp"), "a script node naming a missing class is a problem");
project = FaroProject.Load(root);
// Layout options without coordinates: justify / weight / alignSelf / min-max / sides in Stacks, anchors in Overlays.
Check(UiBuilder.Sides("8") == new Avalonia.Thickness(8) && UiBuilder.Sides("8 16") == new Avalonia.Thickness(16, 8, 16, 8) && UiBuilder.Sides("1 2 3 4") == new Avalonia.Thickness(4, 1, 2, 3), "padding/margin use CSS order");
Avalonia.Controls.Grid StackGrid(string xml) => (Avalonia.Controls.Grid)((Avalonia.Controls.Border)UiBuilder.Build(System.Xml.Linq.XElement.Parse(xml), new Dictionary<string, Avalonia.Controls.Control>(), root)).Child!;
var spread = StackGrid("""<Node id="r" type="Container.Stack" direction="Horizontal" justify="SpaceBetween" gap="8"><Node id="a" type="Control.Text" /><Node id="b" type="Control.Text" alignSelf="End" /></Node>""");
Check(spread.ColumnDefinitions.Count == 3 && spread.ColumnDefinitions[1].Width.IsStar && spread.ColumnSpacing == 0 && spread.Children[1].VerticalAlignment == Avalonia.Layout.VerticalAlignment.Bottom, "SpaceBetween spreads the leftover space; alignSelf overrides the container");
// Tokens: "$space.m" in a number attribute is faro.json's value; an unknown token counts as no value.
UiBuilder.Tokens = new Dictionary<string, string> { ["space.m"] = "12" };
var tokened = StackGrid("""<Node id="r" type="Container.Stack" gap="$space.m"><Node id="a" type="Control.Text" margin="$space.m 4" maxWidth="$nope" /></Node>""");
Check(tokened.RowSpacing == 12 && tokened.Children[0].Margin == new Avalonia.Thickness(4, 12, 4, 12) && double.IsPositiveInfinity(tokened.Children[0].MaxWidth), "tokens resolve in number attributes");
UiBuilder.Tokens = new Dictionary<string, string>();
var centered = StackGrid("""<Node id="r" type="Container.Stack" justify="Center"><Node id="a" type="Control.Text" /></Node>""");
var weighted = StackGrid("""<Node id="r" type="Container.Stack" justify="Center"><Node id="a" type="Control.Text" heightSizing="Fill" weight="2" /><Node id="b" type="Control.Text" heightSizing="Fill" minHeight="10" maxWidth="90" margin="4 8" /></Node>""");
Check(centered.VerticalAlignment == Avalonia.Layout.VerticalAlignment.Center && weighted.RowDefinitions[0].Height == new Avalonia.Controls.GridLength(2, Avalonia.Controls.GridUnitType.Star)
    && weighted.VerticalAlignment == Avalonia.Layout.VerticalAlignment.Stretch && weighted.Children[1] is { MinHeight: 10, MaxWidth: 90 } b && b.Margin == new Avalonia.Thickness(8, 4, 8, 4), "justify packs, Fill weight shares, min/max/margin apply");
var overlay = StackGrid("""<Node id="o" type="Container.Overlay"><Node id="bg" type="Control.Text" sizing="Fill" /><Node id="badge" type="Control.Text" anchorX="Right" anchorY="Top" margin="8" /><Node id="cta" type="Control.Button" anchorX="Center" anchorY="Bottom" /></Node>""");
Check(overlay.RowDefinitions.Count == 0 && overlay.Children[0] is { HorizontalAlignment: Avalonia.Layout.HorizontalAlignment.Stretch, VerticalAlignment: Avalonia.Layout.VerticalAlignment.Stretch }
    && overlay.Children[1] is { HorizontalAlignment: Avalonia.Layout.HorizontalAlignment.Right, VerticalAlignment: Avalonia.Layout.VerticalAlignment.Top }
    && overlay.Children[2] is { HorizontalAlignment: Avalonia.Layout.HorizontalAlignment.Center, VerticalAlignment: Avalonia.Layout.VerticalAlignment.Bottom }, "Overlay layers children at their anchors");
// Grid with explicit tracks: sizes, explicit cells with spans, reading-order fill of the rest, Auto rows as needed.
Check(UiBuilder.Tracks("Auto, *, 2*, 120px") is [{ IsAuto: true }, { IsStar: true, Value: 1 }, { IsStar: true, Value: 2 }, { IsAbsolute: true, Value: 120 }]
    && UiBuilder.Tracks("3")!.Count == 3 && UiBuilder.Tracks("wide") is null && UiBuilder.Tracks("") is null, "grid track syntax");
var form = StackGrid("""<Node id="g" type="Container.Grid" columns="Auto, *" gap="4"><Node id="title" type="Control.Text" row="0" column="0" columnSpan="2" /><Node id="l1" type="Control.Text" /><Node id="v1" type="Control.TextInput" widthSizing="Fill" /><Node id="l2" type="Control.Text" alignSelf="End" /></Node>""");
int[] Cell(int i) => [Avalonia.Controls.Grid.GetRow(form.Children[i]), Avalonia.Controls.Grid.GetColumn(form.Children[i]), Avalonia.Controls.Grid.GetColumnSpan(form.Children[i])];
Check(form.ColumnDefinitions.Count == 2 && form.RowDefinitions.Count == 3 && Cell(0).SequenceEqual([0, 0, 2]) && Cell(1).SequenceEqual([1, 0, 1]) && Cell(2).SequenceEqual([1, 1, 1]) && Cell(3).SequenceEqual([2, 0, 1])
    && form.Children[2].HorizontalAlignment == Avalonia.Layout.HorizontalAlignment.Stretch && form.Children[3] is { HorizontalAlignment: Avalonia.Layout.HorizontalAlignment.Right, VerticalAlignment: Avalonia.Layout.VerticalAlignment.Bottom }, "grid places children by cell, span and reading order");
Check(StackGrid("""<Node id="g" type="Container.Grid" columns="2"><Node id="a" type="Control.Text" column="9" columnSpan="5" row="x" /></Node>""") is { Children: [var clamped] } && Avalonia.Controls.Grid.GetColumn(clamped) == 1 && Avalonia.Controls.Grid.GetColumnSpan(clamped) == 1, "out-of-range cells are clamped, bad numbers ignored");
// Copy / paste / duplicate: fresh ids where taken, binds follow the copies (inner paths too), other screens keep ids.
var clip = CanvasEdit.Copy(project, project.Screens["MainScreen"], ["btn1", "orderList", "actions"]);
Check(clip.Nodes.Select(n => (string?)n.Attribute("id")).SequenceEqual(["actions", "orderList"]) && clip.Binds.Count == 6, "copy takes whole subtrees (a selected child goes with its parent) and their binds");
var (pasted, pasteChanged) = CanvasEdit.Paste(project, project.Screens["MainScreen"], "orderList", clip, after: true);
var pastedIds = pasted.SelectMany(n => n.DescendantsAndSelf("Node")).Where(n => n.Parent?.Attribute("type")?.Value != "Instance").Select(n => (string?)n.Attribute("id")).ToList();
Check(pastedIds.Contains("actions2") && pastedIds.Contains("btn2") && pastedIds.Contains("orderList2") && pasted[0].ElementsBeforeSelf("Node").Last().Attribute("id")!.Value == "orderList"
    && project.BindsFor("MainScreen").Any(b => (string?)b.Attribute("nodeId") == "btn2" && (string?)b.Attribute("event") == "Click")
    && project.BindsFor("MainScreen").Any(b => (string?)b.Attribute("nodeId") == "orderList2/price") && pasteChanged.Count == 2, "paste renames taken ids and copies binds to the new ids");
var (onDetail, _) = CanvasEdit.Paste(project, project.Screens["Detail"], null, CanvasEdit.Copy(project, project.Screens["MainScreen"], ["txt1"]));
Check((string?)onDetail[0].Attribute("id") == "txt1" && project.BindsFor("Detail").Any(b => (string?)b.Attribute("nodeId") == "txt1"), "pasting on another screen keeps free ids");
Check(BindingCheck.Check(project, registry).Where(i => i.Screen is "MainScreen" or "Detail").All(i => i.NodeId is not ("btn2" or "orderList2/price" or "txt1")), "pasted binds resolve");
project = FaroProject.Load(root);
// Images: Assets/ paths for Source pickers, and mock rows can swap an image's Source too.
Check(ProjectFiles.Images(sample).SequenceEqual(["Assets/logo.png"]), "asset images listed as project-relative paths");
var cards = System.Xml.Linq.XElement.Parse("""<Node id="r" type="Container.Stack"><Node id="card" type="Container.Stack" repeatable="true"><Node id="pic" type="Control.Image" /><Node id="t" type="Control.Text" /><MockRow><Set node="pic" value="Assets/a.png" /><Set node="t" value="A" /></MockRow><MockRow><Set node="pic" value="Assets/b.png" /><Set node="t" value="B" /></MockRow></Node></Node>""");
var shownCards = MockData.Expand(cards).Elements("Node").ToList();
Check(MockData.Fields(cards.Element("Node")!).SequenceEqual(["pic", "t"]) && shownCards.Count == 2
    && UiBuilder.Prop(shownCards[1].Element("Node")!, "Source") == "Assets/b.png" && UiBuilder.Prop(shownCards[1].Elements("Node").Last(), "Text") == "B", "mock rows fill image sources and texts");
var wrapped = CanvasEdit.Wrap(project.Screens["MainScreen"], ["greeting", "txt1"], "Container.Stack");
Check(wrapped is not null && wrapped.Elements("Node").Select(n => (string?)n.Attribute("id")).SequenceEqual(["txt1", "greeting"]) && wrapped.ElementsBeforeSelf("Node").Last().Attribute("id")!.Value == "header"
    && CanvasEdit.Wrap(project.Screens["MainScreen"], ["txt1", "btn1"], "Container.Stack") is null, "wrap siblings in a container at their place; non-siblings refused");
project = FaroProject.Load(root);
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
// Java projects (JavaFX, Maven): the same UI files, a registry from .java sources, the runtime vendored as sources.
var javaSample = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../samples/HelloFaroJava"));
var javaRegistry = Registry.Scan(Path.Combine(javaSample, "Source"));
Check(javaRegistry.Any(m => m.Target == "myapp.services.OrderService.submit" && m.IsMethod && m.Signature == "void submit()")
    && javaRegistry.Any(m => m.Target == "myapp.models.UserProfile.name" && !m.IsMethod && m.Signature == "String name")
    && !javaRegistry.Any(m => m.Target.EndsWith(".setName") || m.Target.EndsWith(".build") || m.Target.EndsWith(".main"))
    && Registry.ScriptClasses(Path.Combine(javaSample, "Source")).SequenceEqual(["myapp.views.Stamp"]), "Java registry: methods, bean properties, script classes");
Check(BindingCheck.Check(FaroProject.Load(javaSample), javaRegistry, Registry.ScriptClasses(Path.Combine(javaSample, "Source"))).Count == 0, "Java sample has no broken bindings");
Check(JavaProject.Parse("""
    package a.b; // { not a brace
    /* public class Fake { public void no() {} } */
    @Deprecated(since = "1") public final class Real<T> extends Base {
        private String s = "}{;";
        public static class Inner { public void nested() { } }
        public boolean isOn() { return true; }
        public <R> java.util.List<R> items(int n, String m) throws Exception { return null; }
        public Real() { }
        void hidden() { }
    }
    class Other { public void no() { } }
    """).Select(m => m.Target).SequenceEqual(["a.b.Real.on", "a.b.Real.items"]), "Java scanner skips comments, strings, nested and non-public code");
var javaApp = ProjectSetup.Create(projects, "JavaApp", "Sample", language: JavaProject.Language);
Check(JavaProject.Is(javaApp) && File.ReadAllText(Path.Combine(javaApp, "pom.xml")).Contains("<artifactId>JavaApp</artifactId>") && File.Exists(Path.Combine(javaApp, "Source/Main.java"))
    && File.Exists(Path.Combine(javaApp, ".faro/runtime-java/faro/runtime/FaroApp.java")) && !Directory.Exists(Path.Combine(javaApp, ".faro/packages"))
    && BindingCheck.Check(FaroProject.Load(javaApp), Registry.Scan(Path.Combine(javaApp, "Source"))).Count == 0
    && ProjectSetup.ProjectRuntime(javaApp) == ProjectSetup.BundledRuntime().Version, "Java project: pom, Main, vendored runtime sources, working sample");
Check(JavaProject.ClassFile("shop/cart", "Cart") is var javaClass && javaClass.StartsWith("package shop.cart;") && javaClass.Contains("public class Cart extends FaroObject")
    && VibeCoding.ResolvePath(javaApp, "Source/shop/Cart.java") is not null && VibeCoding.ResolvePath(javaApp, "Source/shop/Cart.cs") is null, "Java class files and AI paths");
Check(BuildError.Parse("[ERROR] /home/me/App/Source/app/Foo.java:[12,5] cannot find symbol", out _) == new BuildError("/home/me/App/Source/app/Foo.java", 12, 5, "javac", "cannot find symbol"), "Maven compile error parsed");
Check(JavaProject.DesignCss(new AppDesign("Material3")) is { } javaCss && javaCss.Contains("m3-primary: #65558f;") && javaCss.Contains(".button.m3-variant-tonal")
    && JavaProject.DesignCss(new AppDesign()) is null, "JavaFX Material 3 stylesheet from the seed");
// Every resource Looks.axaml draws with is in each language's table, light and dark; the Java CSS comes out whole
var looksXaml = File.ReadAllText(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../src/Faro.Runtime/Looks.axaml")));
var lookKeys = System.Text.RegularExpressions.Regex.Matches(looksXaml, @"DynamicResource (Faro\w+)").Select(m => m.Groups[1].Value).Distinct().ToList();
var palette = MaterialColorUtilities.Palettes.CorePalette.Of(0xFF6750A4, MaterialColorUtilities.Palettes.Style.TonalSpot);
var missingLook = DesignLooks.Languages.SelectMany(l => new[] { false, true }.SelectMany(dark => lookKeys.Where(k => k != "FaroFont" && !DesignLooks.Look(l, palette, dark).ContainsKey(k)).Select(k => $"{l}/{(dark ? "dark" : "light")}: {k}"))).ToList();
Check(lookKeys.Count > 30 && missingLook.Count == 0 && AppDesign.Read(System.Text.Json.Nodes.JsonNode.Parse("""{"language":"Neumorphism"}""")).Language == "Neumorphism", "design languages fill every look resource" + (missingLook.Count > 0 ? ": " + string.Join(", ", missingLook.Take(5)) : ""));
Check(DesignLooks.Languages.All(l => JavaProject.DesignCss(new AppDesign(l, AppDesign.DefaultSeed, "Dark")) is { } css && css.Contains(".check-box.faro-switch > .box") && !css.Contains("NaN") && !css.Contains("{{"))
    && JavaProject.DesignCss(new AppDesign("NeoBrutalism"))!.Contains("-fx-translate-x: 4"), "JavaFX stylesheets for the other design languages");
// Accessibility (Problems): contrast of the design language's pairs and of node colors, small touch targets, unlabeled fields
var a11yProject = FaroProject.Load(sampleDir);
a11yProject.Screens["X"] = System.Xml.Linq.XDocument.Parse("""
    <UIGraph id="X"><Node id="root" type="Container.Stack" background="#FFFFFF">
      <Node id="gray" type="Control.Text" foreground="#BBBBBB" /><Node id="tiny" type="Control.Button" widthSizing="Fixed" width="30" />
      <Node id="bare" type="Control.TextInput" /><Node id="label" type="Control.Text" /><Node id="named" type="Control.TextInput" />
    </Node></UIGraph>
    """);
var a11y = AccessibilityCheck.Check(a11yProject).Where(i => i.Screen == "X").Select(i => i.NodeId).ToList();
Check(a11y.SequenceEqual(["gray", "tiny", "bare"]) && Math.Abs(AccessibilityCheck.Ratio(Avalonia.Media.Colors.Black, Avalonia.Media.Colors.White) - 21) < 0.01
    && AppDesign.Languages.Where(l => l != "Fluent").All(l => new[] { false, true }.All(dark => AccessibilityCheck.Colors(new AppDesign(l), dark) is { } p
        && AccessibilityCheck.Ratio(p.OnButton, p.Button) >= 4.5 && AccessibilityCheck.Ratio(p.OnSurface, p.Surface) >= 4.5 && AccessibilityCheck.Ratio(p.Muted, p.Field) >= 3)), "accessibility warnings (contrast, touch size, unlabeled field); every design language passes its own contrast");
// Canvas spacing handles and Distribute: one padding side (all four equal → one value), the gap, a Stack spread
var spaced = System.Xml.Linq.XDocument.Parse("""<UIGraph id="S"><Node id="root" type="Container.Stack" padding="8 16"><Node id="a" type="Control.Text" /><Node id="b" type="Control.Text" /></Node></UIGraph>""");
Check(CanvasEdit.SetSpacing(spaced, "root", false, 0, 12) && (string?)spaced.Root!.Element("Node")!.Attribute("padding") == "12 16 8 16"
    && CanvasEdit.SetSpacing(spaced, "root", false, 1, 8) && CanvasEdit.SetSpacing(spaced, "root", false, 3, 8) && CanvasEdit.SetSpacing(spaced, "root", false, 0, 8)
    && (string?)spaced.Root!.Element("Node")!.Attribute("padding") == "8"
    && CanvasEdit.SetSpacing(spaced, "root", true, 0, 20.4) && (string?)spaced.Root!.Element("Node")!.Attribute("gap") == "20"
    && !CanvasEdit.SetSpacing(spaced, "a", true, 0, 4) && !CanvasEdit.Distribute(spaced, ["a"])
    && CanvasEdit.Distribute(spaced, ["a", "b"]) && (string?)spaced.Root!.Element("Node")!.Attribute("justify") == "SpaceBetween",
    "canvas spacing handles and distribute");
// Live reload: older projects' dotnet watch restart on UI changes is dropped when their runtime is updated
var liveParent = Path.Combine(Path.GetTempPath(), "faro-live-" + Guid.NewGuid().ToString("N"));
var liveDir = ProjectSetup.Create(liveParent, "LiveApp", "Empty");
var liveCsproj = Directory.EnumerateFiles(liveDir, "*.csproj").First();
File.WriteAllText(liveCsproj, File.ReadAllText(liveCsproj).Replace("</ItemGroup>", "  <!-- dotnet watch restarts the app when the UI graph or bindings change (spec §9). -->\n    <Watch Include=\"UI/**;Bindings/**\" />\n  </ItemGroup>"));
ProjectSetup.UpdateRuntime(liveDir);
Check(!File.ReadAllText(liveCsproj).Contains("<Watch") && File.ReadAllText(liveCsproj).Contains("Faro.Runtime"), "live reload replaces dotnet watch's restart in older projects");
Directory.Delete(liveParent, recursive: true);
// Icon part: the same symbol table in both runtimes, drawn from the node's Icon prop
var javaIcons = System.Text.RegularExpressions.Regex.Matches(File.ReadAllText(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../src/Faro.Runtime.Java/faro/runtime/IconSet.java"))),
    @"Map\.entry\(""(\w+)"", ""([^""]+)""\)").ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value);
var iconNode = UiBuilder.Build(System.Xml.Linq.XElement.Parse("""<Node id="i" type="Control.Icon"><Prop name="Icon" value="home" /></Node>"""), new Dictionary<string, Avalonia.Controls.Control>(), root);
Check(IconSet.Paths.Count > 50 && javaIcons.Count == IconSet.Paths.Count && IconSet.Paths.All(p => javaIcons.GetValueOrDefault(p.Key) == p.Value)
    && IconSet.Paths.Values.All(d => System.Text.RegularExpressions.Regex.IsMatch(d, @"^[MmLlHhVvCcSsQqTtAaZz0-9.,\- ]+$")) && iconNode is FaroIcon { Icon: "home" } && Bindable.For(iconNode)?.Props.ContainsKey("Icon") == true,
    "icon part: the same symbols in both runtimes, built and bindable");
// Screen flow (canvas): Navigate binds, including a component's, as links; columns by steps from the start screen
var flowProject = FaroProject.Load(sampleDir);
var flow = ScreenFlow.Edges(flowProject);
Check(flow.Contains(("MainScreen", "Detail")) && flow.Contains(("Detail", "MainScreen"))
    && ScreenFlow.Columns(flowProject, flow) is [["MainScreen"], ["Detail"]], "screen flow from Navigate bindings");
// Per-node options for those languages: look.variant / look.surface become the classes both stylesheets draw
var carded = UiBuilder.Build(System.Xml.Linq.XElement.Parse("""<Node id="c" type="Container.Stack" look.surface="Card"><Node id="b" type="Control.Button" look.variant="Outlined" /></Node>"""), new Dictionary<string, Avalonia.Controls.Control>(), root);
Check(carded.Classes.Contains("look-surface-card") && carded is Avalonia.Controls.Border { Child: Avalonia.Controls.Panel { Children: [Avalonia.Controls.Button outlined] } } && outlined.Classes.Contains("look-variant-outlined")
    && JavaProject.DesignCss(new AppDesign("Retro")) is { } retroCss && retroCss.Contains(".button.look-variant-outlined") && retroCss.Contains(".look-surface-inset")
    && retroCss.Contains("-fx-border-color: #ffffff #808080 #808080 #ffffff") && JavaProject.DesignCss(new AppDesign("Carbon"))!.Contains("-fx-background-insets: 0, 0 0 1 0"),
    "per-node look options, Retro bevels and Carbon's field rule");
Check(Throws<ArgumentException>(() => ProjectSetup.Create(projects, "EmptyApp", "Empty")) && Throws<ArgumentException>(() => ProjectSetup.Create(projects, "../bad", "Empty")) && Throws<ArgumentException>(() => ProjectSetup.Create(projects, "1st", "Empty"))
    && Throws<ArgumentException>(() => ProjectSetup.Create("relative/dir", "Ok", "Empty")) && Path.IsPathRooted(ProjectSetup.DefaultLocation), "project name and location validated");
var existing = Path.Combine(projects, "existing-folder");
Directory.CreateDirectory(existing);
File.WriteAllText(Path.Combine(existing, "Program.cs"), "// mine");
File.WriteAllText(Path.Combine(existing, "Mine.csproj"), "<Project />");
ProjectSetup.Initialize(existing);
Check(ProjectSetup.IsFaroProject(existing) && File.ReadAllText(Path.Combine(existing, "Program.cs")) == "// mine" && Directory.EnumerateFiles(existing, "*.csproj").Count() == 1
    && new[] { "UI", "Source", "Bindings", "Assets" }.All(f => Directory.Exists(Path.Combine(existing, f))), "initializing a folder adds what's missing and keeps existing files");
// Untitled projects and Save As: new projects start untitled; Save As copies to a name and place, the untitled one is discarded.
ProjectSetup.UntitledRoot = Path.Combine(projects, "untitled-root");
var untitled1 = ProjectSetup.CreateUntitled("Sample");
File.WriteAllText(Path.Combine(untitled1, "bin-marker.txt"), "");
Directory.CreateDirectory(Path.Combine(untitled1, "bin/Debug"));
File.WriteAllText(Path.Combine(untitled1, "bin/Debug/App.dll"), "");
Check(Path.GetFileName(untitled1) == "Untitled1" && Path.GetFileName(ProjectSetup.CreateUntitled("Empty")) == "Untitled2" && ProjectSetup.IsUntitled(untitled1) && !ProjectSetup.IsUntitled(empty), "new projects start untitled");
var shop = ProjectSetup.SaveAs(untitled1, projects, "Shop");
Check(File.Exists(Path.Combine(shop, "Shop.csproj")) && !File.Exists(Path.Combine(shop, "Untitled1.csproj")) && File.ReadAllText(Path.Combine(shop, "faro.json")).Contains("\"Shop\"")
    && File.Exists(Path.Combine(shop, "bin-marker.txt")) && Directory.Exists(Path.Combine(shop, "Assets")) && !Directory.Exists(Path.Combine(shop, "bin")) && FaroProject.Load(shop).Screens.Count == 2, "Save As copies the project under the new name, without build output");
Check(FaroSettings.Current.PendingDeletes.Contains(untitled1) && Directory.Exists(untitled1), "the untitled original is queued, not deleted while open");
var untitled2 = Path.Combine(ProjectSetup.UntitledRoot, "Untitled2");
Check(ProjectSetup.LeftoverUntitled().SequenceEqual([untitled2]), "leftover untitled projects are offered, discarded ones aren't");
ProjectSetup.DeletePending();
Check(!Directory.Exists(untitled1) && !FaroSettings.Current.PendingDeletes.Contains(untitled1), "queued untitled projects are deleted later");
Check(ProjectSetup.IsTrusted(untitled1) && !ProjectSetup.IsTrusted(empty) && ProjectSetup.IsTrusted(shop), "untitled projects and their Save As copies are trusted; other folders aren't until asked");
ProjectSetup.Trust(empty);
Check(ProjectSetup.IsTrusted(empty) && !ProjectSetup.IsTrusted(Path.Combine(projects, "SampleApp")), "trust is per folder");
ProjectSetup.Discard(shop);
Check(!FaroSettings.Current.PendingDeletes.Contains(shop) && Throws<ArgumentException>(() => ProjectSetup.SaveAs(shop, projects, "Shop")) && Throws<ArgumentException>(() => ProjectSetup.SaveAs(shop, shop, "Inside")), "only untitled folders are ever discarded; Save As validates the target");

// Runtime update check: an older project gets the newer bundled package (versions compared numerically).
var fakeApp = Path.Combine(projects, "app");
Directory.CreateDirectory(Path.Combine(fakeApp, "runtime"));
var realPackage = ProjectSetup.BundledRuntime().Package;
File.Copy(realPackage, Path.Combine(fakeApp, "runtime", "Faro.Runtime.0.1.9.nupkg"));
var old = ProjectSetup.Create(projects, "OldApp", "Empty", fakeApp);
Check(ProjectSetup.ProjectRuntime(old) == "0.1.9" && !ProjectSetup.RuntimeUpdateAvailable(old, fakeApp), "project runtime read from its csproj");
File.Copy(realPackage, Path.Combine(fakeApp, "runtime", "Faro.Runtime.0.1.10.nupkg"));
Check(ProjectSetup.BundledRuntime(fakeApp).Version == "0.1.10" && ProjectSetup.RuntimeUpdateAvailable(old, fakeApp), "0.1.10 is newer than 0.1.9");
ProjectSetup.UpdateRuntime(old, fakeApp);
Check(ProjectSetup.ProjectRuntime(old) == "0.1.10" && File.ReadAllText(Path.Combine(old, "faro.json")).Contains("\"0.1.10\"")
    && Directory.EnumerateFiles(Path.Combine(old, ".faro/packages")).Select(Path.GetFileName).SequenceEqual(["Faro.Runtime.0.1.10.nupkg"]) && !ProjectSetup.RuntimeUpdateAvailable(old, fakeApp), "runtime update vendors the package and bumps csproj and faro.json");
// Run output: compiler errors become Problems; a new build clears them.
var errorLine = "dotnet watch ❌ /home/me/My App/Source/Services/OrderService.cs(7,10): error CS1002: ; expected [/home/me/My App/App.csproj]";
Check(BuildError.Parse(errorLine, out var startsBuild) == new BuildError("/home/me/My App/Source/Services/OrderService.cs", 7, 10, "CS1002", "; expected") && !startsBuild, "build error parsed from dotnet watch output");
Check(BuildError.Parse("dotnet watch 🔨 Building /home/me/App/App.csproj ...", out startsBuild) is null && startsBuild
    && BuildError.Parse("dotnet watch 🔨     2 Error(s)", out startsBuild) is null && !startsBuild, "build start detected, other lines ignored");
// Unbuilt indicator (spec §11.5): saved code newer than the built assembly.
var unbuilt = Directory.CreateTempSubdirectory("faro-unbuilt").FullName;
Directory.CreateDirectory(Path.Combine(unbuilt, "Source"));
Directory.CreateDirectory(Path.Combine(unbuilt, "bin/Debug/net10.0"));
File.WriteAllText(Path.Combine(unbuilt, "App.csproj"), "<Project />");
File.WriteAllText(Path.Combine(unbuilt, "Source/A.cs"), "class A { }");
File.WriteAllText(Path.Combine(unbuilt, "bin/Debug/net10.0/App.dll"), "");
File.SetLastWriteTimeUtc(Path.Combine(unbuilt, "Source/A.cs"), DateTime.UtcNow.AddMinutes(-2));
Workspace.Open(unbuilt);
var builtBefore = !Workspace.Unbuilt;
File.SetLastWriteTimeUtc(Path.Combine(unbuilt, "Source/A.cs"), DateTime.UtcNow.AddMinutes(1));
Workspace.Reload();
Check(builtBefore && Workspace.Unbuilt, "code saved after the last build is flagged as unbuilt");
Directory.Delete(unbuilt, true);
// UI language: English is the key; Japanese from the table, unknown text stays as is.
FaroSettings.Current.Language = "ja";
Check(L.T("_Save") == "保存(_S)" && L.F("Screen '{0}' does not exist.", "Top") == "画面 'Top' はありません。" && L.T("MyApp.Views.MyScript") == "MyApp.Views.MyScript", "Japanese UI text");
Check(L.T("Start", "layout") == "先頭" && L.T("Start") == "始める" && L.T("Nope", "layout") == "Nope", "words translated by context");
FaroSettings.Current.Language = "en";
Check(L.T("_Save") == "_Save", "English UI text");
string[] ordered = ["0.1.8-dev1", "0.1.8-dev2", "0.1.8-canary.3", "0.1.8-canary.10", "0.1.8-beta.1", "0.1.8", "0.1.9-dev1", "0.1.10"];
Check(ordered.OrderBy(v => ProjectSetup.VersionKey(v)).SequenceEqual(ordered) && ProjectSetup.VersionKey("0.1.8-beta") is null, "dev builds sort before their release");
// Update check: channels pick from GitHub releases (JSON as the API returns it).
var releases = Updates.Parse("""
[{"tag_name":"v0.3.0-canary.2","html_url":"c","draft":false,"assets":[]},
 {"tag_name":"v0.2.9-beta.1","html_url":"b","draft":false,"assets":[{"name":"Faro-0.2.9-beta.1-win-x64-setup.exe","browser_download_url":"https://x/setup.exe"},{"name":"Faro-0.2.9-beta.1-linux-x64.tar.gz","browser_download_url":"https://x/l.tar.gz"}]},
 {"tag_name":"v0.2.8","html_url":"s","draft":false,"assets":[]},
 {"tag_name":"v0.4.0","html_url":"d","draft":true,"assets":[]}]
""");
Check(releases.Count == 3 && releases[1] is { Version: "0.2.9-beta.1", WindowsSetup: "https://x/setup.exe", LinuxArchive: "https://x/l.tar.gz" }, "releases parsed, drafts skipped");
Check(Updates.Newest(releases, "0.2.4", "Stable")?.Version == "0.2.8" && Updates.Newest(releases, "0.2.4", "Beta")?.Version == "0.2.9-beta.1"
    && Updates.Newest(releases, "0.2.4", "Canary")?.Version == "0.3.0-canary.2" && Updates.Newest(releases, "0.2.8", "Stable") is null, "update channels");
// Crash reports land in the (scratch) settings folder's logs and are offered once at the next start.
var crash = Log.Crash(new InvalidOperationException("boom"));
Check(crash is not null && File.ReadAllText(crash).Contains("boom") && Log.UnseenCrash() == crash, "crash report saved and offered");
FaroSettings.Current.CrashesSeen = DateTime.UtcNow.AddSeconds(1);
Check(Log.UnseenCrash() is null, "a seen crash report isn't offered again");
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
Check(project.Binds.Count(b => ((string?)b.Attribute("target"))!.StartsWith("MyApp.Services.Orders.")) == 4, "targets follow class rename");

// Vibe coding: parse generated files, keep writes inside Source/, block broken syntax, diff.
var reply = "Here you go.\n\nFile: Source/Services/Cart.cs\n```csharp\nnamespace MyApp.Services;\npublic class Cart { }\n```\n**File: `../Evil.cs`**\n```csharp\nclass X { }\n```";
var generated = VibeCoding.ParseFiles(reply);
Check(generated.Count == 2 && generated[0].Path == "Source/Services/Cart.cs" && generated[0].Code.Contains("class Cart"), "generated files parsed");
Check(VibeCoding.ResolvePath(root, "Source/Services/Cart.cs") == Path.GetFullPath(Path.Combine(root, "Source", "Services", "Cart.cs")), "path inside Source/ accepted"); // normalized: \\ on Windows
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
// Canvas data preview: the screen's property bindings from the built classes (the list from OrderService.Orders), no events.
var previewProject = FaroProject.Load(sample);
var previewIds = new Dictionary<string, Avalonia.Controls.Control>();
UiBuilder.Build(previewProject.Screens["MainScreen"].Root!.Element("Node")!, previewIds, sample);
var previewErrors = FaroApp.Preview(asm, previewProject, "MainScreen", previewIds);
Check(previewErrors.Count == 0 && previewIds["orderList"] is RepeatHost { Children.Count: 1 }, "data preview fills the list from the code");
// Navigation parameter: the Detail screen's binds to Order members resolve against the order it was opened with (values need a
// window to show, so the app itself is the visual check).
var detailIds = new Dictionary<string, Avalonia.Controls.Control>();
UiBuilder.Build(previewProject.Screens["Detail"].Root!.Element("Node")!, detailIds, sample);
Check(FaroApp.Preview(asm, previewProject, "Detail", detailIds, new MyApp.Models.Order { Name = "みかん", Price = 1234 }).Count == 0 && FaroApp.Parameter is MyApp.Models.Order { Name: "みかん" }, "a screen opened with a parameter binds to it");

// Android APK head: a valid application id, Source/ compiled, the project's references re-rooted to .faro/android.
Check(AndroidApk.ApplicationId("Hello Faro!") == "io.faro.hellofaro" && AndroidApk.ApplicationId("123") == "io.faro.app", "android application id");
var headProject = System.Xml.Linq.XDocument.Load(AndroidApk.Write(sample));
Check(headProject.Descendants("ProjectReference").Any(r => Path.GetFullPath(Path.Combine(AndroidApk.Head(sample), (string)r.Attribute("Include")!)) == Path.GetFullPath(Path.Combine(sample, "../../src/Faro.Runtime/Faro.Runtime.csproj")))
    && headProject.Descendants("Compile").Any(c => (string?)c.Attribute("Include") == "../../Source/**/*.cs"), "android head compiles Source/ with the project's references");
Directory.Delete(AndroidApk.Head(sample), true);
// Java (GluonFX): user classes listed for reflection, the runtime path re-rooted, project files indexed as resources.
var javaPom = File.ReadAllText(JavaProject.WriteAndroid(javaSample));
var javaIndex = File.ReadAllLines(Path.Combine(AndroidApk.Head(javaSample), "resources", "faro", "index.txt"));
Check(javaPom.Contains("<list>myapp.services.OrderService</list>") && javaPom.Contains("<faro.runtime>../../../../src/Faro.Runtime.Java</faro.runtime>")
    && System.Xml.Linq.XDocument.Parse(javaPom).Root is not null
    && javaIndex.Contains("faro.json") && javaIndex.Contains("UI/MainScreen.xml") && javaIndex.Contains("Bindings/MainScreen.xml"), "java android pom and bundled project files");
Directory.Delete(AndroidApk.Head(javaSample), true);

// Crash watcher: a clean exit (marker gone) shows nothing; a crash shows the report written since the start, else a new one.
var crashes = Path.Combine(root, "crashes");
Directory.CreateDirectory(crashes);
File.WriteAllText(Path.Combine(crashes, "crash-old.txt"), "old");
File.SetLastWriteTimeUtc(Path.Combine(crashes, "crash-old.txt"), DateTime.UtcNow.AddHours(-1));
File.WriteAllText(Path.Combine(crashes, "running-1"), "");
var noManaged = CrashWatcher.Outcome(crashes, 1);
File.SetLastWriteTimeUtc(noManaged!, DateTime.UtcNow.AddMinutes(-2)); // an earlier session's report
File.WriteAllText(Path.Combine(crashes, "running-2"), "");
File.SetLastWriteTimeUtc(Path.Combine(crashes, "running-2"), DateTime.UtcNow.AddMinutes(-1));
File.WriteAllText(Path.Combine(crashes, "crash-new.txt"), "boom");
Check(CrashWatcher.Outcome(crashes, 3) is null && noManaged is not null && File.ReadAllText(noManaged).Contains("without a .NET exception")
    && CrashWatcher.Outcome(crashes, 2) == Path.Combine(crashes, "crash-new.txt") && !File.Exists(Path.Combine(crashes, "running-2"))
    && CrashWatcher.IssueUrl(null, new string('x', 50_000), "crash-new.txt").Length < 8000, "crash watcher outcome and issue link");

// Appearance: color / text-style tokens, a node's own value over its style, state colors (styles, and Fluent's button resources).
var savedTokens = UiBuilder.Tokens;
UiBuilder.Tokens = new Dictionary<string, string> { ["color.primary"] = "#6750A4", ["text.title.fontSize"] = "22", ["text.title.fontWeight"] = "Bold", ["text.title.fontFamily"] = "Inter" };
var looked = (Avalonia.Controls.Button)UiBuilder.Build(System.Xml.Linq.XElement.Parse("""
    <Node id="go" type="Control.Button" background="$color.primary" hoverBackground="#FF0000" disabledForeground="Gray" textStyle="$text.title" fontWeight="Normal"><Prop name="Text" value="Go" /></Node>
    """), new Dictionary<string, Avalonia.Controls.Control>(), root);
Check(looked.FontSize == 22 && looked.FontWeight == Avalonia.Media.FontWeight.Normal && looked.FontFamily.Name == "Inter"
    && looked.Resources["ButtonBackgroundPointerOver"] is Avalonia.Media.ISolidColorBrush { Color: var hover } && hover == Avalonia.Media.Color.Parse("#FF0000")
    && looked.Resources.ContainsKey("ButtonForegroundDisabled") && looked.Styles.Count == 3, "appearance: tokens, text style, state colors");
UiBuilder.Tokens = savedTokens;

// Align buttons: along a row Spacers push the node (right = one before, center = both sides, left = none), across it alignSelf, in an Overlay anchors.
var aligned = XDocument.Parse("""
    <UIGraph id="A"><Node id="root" type="Container.Stack">
      <Node id="row" type="Container.Stack" direction="Horizontal"><Node id="title" type="Control.Text" /><Node id="ok" type="Control.Button" /></Node>
      <Node id="layer" type="Container.Overlay"><Node id="badge" type="Control.Text" /></Node>
    </Node></UIGraph>
    """);
string Kids(string id) => string.Join(",", CanvasEdit.Find(aligned, id)!.Elements("Node").Select(n => (string?)n.Attribute("type") == "Control.Spacer" ? "_" : (string)n.Attribute("id")!));
CanvasEdit.Align(aligned, "ok", true, "End");
var right = Kids("row");
CanvasEdit.Align(aligned, "ok", true, "Center");
var center = Kids("row");
CanvasEdit.Align(aligned, "ok", true, "Start");
CanvasEdit.Align(aligned, "ok", false, "Center");
CanvasEdit.Align(aligned, "badge", true, "End");
CanvasEdit.Align(aligned, "badge", false, "Center");
Check(right == "title,_,ok" && center == "title,_,ok,_" && Kids("row") == "title,ok" && (string?)CanvasEdit.Find(aligned, "ok")!.Attribute("alignSelf") == "Center"
    && (string?)CanvasEdit.Find(aligned, "badge")!.Attribute("anchorX") == "Right" && (string?)CanvasEdit.Find(aligned, "badge")!.Attribute("anchorY") == "Center"
    && !CanvasEdit.Align(aligned, "root", true, "End") && UiBuilder.Sizing(new XElement("Node", new XAttribute("type", "Control.Spacer")), "width") == "Fill", "align buttons (spacers, alignSelf, anchors)");

// New parts: check box, switch, slider, select, progress, divider (built from their Props, bindable by neutral names).
var parts = XElement.Parse("""
    <Node id="root" type="Container.Stack">
      <Node id="agree" type="Control.CheckBox"><Prop name="Text" value="OK" /><Prop name="Checked" value="true" /></Node>
      <Node id="wifi" type="Control.Switch"><Prop name="Text" value="Wi-Fi" /></Node>
      <Node id="volume" type="Control.Slider"><Prop name="Minimum" value="0" /><Prop name="Maximum" value="10" /><Prop name="Value" value="7" /></Node>
      <Node id="size" type="Control.Select"><Prop name="Options" value="S, M , L" /><Prop name="Selected" value="M" /></Node>
      <Node id="done" type="Control.Progress"><Prop name="Value" value="40" /></Node>
      <Node id="line" type="Control.Divider" />
    </Node>
    """);
var partIds = new Dictionary<string, Avalonia.Controls.Control>();
UiBuilder.Build(parts, partIds, root);
Check(partIds["agree"] is Avalonia.Controls.CheckBox { IsChecked: true } && partIds["wifi"] is Avalonia.Controls.ToggleSwitch { IsChecked: false }
    && partIds["volume"] is Avalonia.Controls.Slider { Value: 7, Maximum: 10 } && partIds["size"] is Avalonia.Controls.ComboBox { SelectedItem: "M" } select && select.ItemCount == 3
    && partIds["done"] is Avalonia.Controls.ProgressBar { Value: 40 } && UiBuilder.Sizing(parts.Elements("Node").Last(), "width") == "Fill"
    && Bindable.For(partIds["wifi"])!.Type == "Control.Switch" && Bindable.For(partIds["agree"])!.Props.ContainsKey("Checked")
    && Bindable.For(partIds["volume"])!.Events.ContainsKey("Changed") && Bindable.For(partIds["size"])!.Props.ContainsKey("Options")
    && Bindable.For(partIds["line"])!.Type == "Control.Divider" && Bindable.For(new Avalonia.Controls.Button())!.Type == "Control.Button", "new parts build and bind");

// The Java sample's generated Material 3 stylesheet is committed: it must match the generator (regenerated here if not; commit the result).
var committedCss = Path.Combine(javaSample, ".faro", "design.css");
var cssUpToDate = File.ReadAllText(committedCss) == JavaProject.DesignCss(FaroProject.Load(javaSample).Design);
if (!cssUpToDate) JavaProject.WriteDesignCss(javaSample, FaroProject.Load(javaSample).Design);
Check(cssUpToDate, "samples/HelloFaroJava/.faro/design.css is up to date");

// Source Control shows a UI file's change as nodes: added, removed, moved, reordered, changed attributes and texts, instance synced.
var uiBefore = """
    <UIGraph id="Main"><Node id="root" type="Container.Stack">
      <Node id="a" type="Control.Text"><Prop name="Text" value="Hi" /></Node>
      <Node id="b" type="Control.Button" width="120" />
      <Node id="c" type="Control.Text" />
      <Node id="box" type="Container.Stack" />
      <Node id="card" type="Instance" component="Comp.Card"><Node id="root" type="Container.Stack" /></Node>
    </Node></UIGraph>
    """;
var uiAfter = """
    <UIGraph id="Main"><Node id="root" type="Container.Stack">
      <Node id="b" type="Control.Button" width="200" background="#6750A4" />
      <Node id="a" type="Control.Text"><Prop name="Text" value="Hello" /></Node>
      <Node id="box" type="Container.Stack"><Node id="c" type="Control.Text" /></Node>
      <Node id="d" type="Control.Image" />
      <Node id="card" type="Instance" component="Comp.Card"><Node id="root" type="Container.Stack" gap="8" /></Node>
    </Node></UIGraph>
    """;
var uiChanges = GitView.UiChanges(uiBefore, uiAfter);
Check(uiChanges.Contains("+ Added d (Image) to root") && uiChanges.Contains("~ a (Text): Text \"Hi\" → \"Hello\"")
    && uiChanges.Contains("~ b (Button): width 120 → 200, background (none) → #6750A4") && uiChanges.Contains("~ c (Text): moved into box")
    && uiChanges.Contains("~ card (Comp.Card): synced with its component") && uiChanges.Contains("~ Reordered the children of root (Stack)")
    && GitView.UiChanges(uiAfter, null).Count == 7 && GitView.UiChanges("<oops", uiAfter).Count == 0
    && GitView.UiChanges("""<Bindings><Bind nodeId="b" event="Click" target="X.Go" /></Bindings>""", """<Bindings><Bind nodeId="b" event="Click" target="X.Run" /><Bind nodeId="a" prop="Text" target="X.Name" /></Bindings>""")
        .SequenceEqual(["+ Binding added: a · Text → X.Name", "~ b · Click: target X.Go → X.Run"]), "UI diff by node and binding");

// Scale: 40 screens × ~100 nodes with bindings, 200 classes; loading, analysis and building every screen stay quick.
var big = Path.Combine(root, "big");
Directory.CreateDirectory(Path.Combine(big, "UI")); Directory.CreateDirectory(Path.Combine(big, "Bindings")); Directory.CreateDirectory(Path.Combine(big, "Source"));
File.WriteAllText(Path.Combine(big, "faro.json"), "{ \"name\": \"Big\", \"startScreen\": \"S0\", \"tokens\": { \"space.m\": 16 } }");
for (var c = 0; c < 200; c++)
    File.WriteAllText(Path.Combine(big, "Source", $"C{c}.cs"), $"namespace Big;\npublic class C{c} : Faro.Runtime.FaroObject\n{{\n" + string.Concat(Enumerable.Range(0, 10).Select(m => $"    public string P{m} {{ get; set; }} = \"\";\n    public void M{m}() {{ }}\n")) + "}\n");
for (var sIndex = 0; sIndex < 40; sIndex++)
{
    var rows = string.Concat(Enumerable.Range(0, 25).Select(r => $"""
        <Node id="row{r}" type="Container.Stack" direction="Horizontal" gap="$space.m" background="#10000000" hoverBackground="#20000000">
          <Node id="t{r}" type="Control.Text"><Prop name="Text" value="Row {r}" /></Node>
          <Node id="i{r}" type="Control.TextInput" widthSizing="Fill"><Prop name="Placeholder" value="…" /></Node>
          <Node id="b{r}" type="Control.Button"><Prop name="Text" value="Go" /></Node>
        </Node>
        """));
    File.WriteAllText(Path.Combine(big, "UI", $"S{sIndex}.xml"), $"""<UIGraph id="S{sIndex}"><Node id="root" type="Container.Stack" padding="$space.m">{rows}</Node></UIGraph>""");
    File.WriteAllText(Path.Combine(big, "Bindings", $"S{sIndex}.xml"), "<Bindings>" + string.Concat(Enumerable.Range(0, 25).Select(r =>
        $"""<Bind nodeId="t{r}" prop="Text" target="Big.C{(sIndex * 5 + r) % 200}.P{r % 10}" /><Bind nodeId="b{r}" event="Click" target="Big.C{(sIndex * 5 + r) % 200}.M{r % 10}" />""")) + "</Bindings>");
}
var clock = System.Diagnostics.Stopwatch.StartNew();
var bigProject = FaroProject.Load(big);
var tLoad = clock.ElapsedMilliseconds; clock.Restart();
var bigRegistry = Registry.Scan(Path.Combine(big, "Source"));
var tScan = clock.ElapsedMilliseconds; clock.Restart();
var bigScripts = Registry.ScriptClasses(Path.Combine(big, "Source"));
var tScripts = clock.ElapsedMilliseconds; clock.Restart();
var bigIssues = BindingCheck.Check(bigProject, bigRegistry, bigScripts);
var tCheck = clock.ElapsedMilliseconds; clock.Restart();
foreach (var (bigId, bigGraph) in bigProject.Screens) UiBuilder.Build(bigGraph.Root!.Element("Node")!, new Dictionary<string, Avalonia.Controls.Control>(), big);
var tBuild = clock.ElapsedMilliseconds;
clock.Restart(); // what every canvas edit costs: reload and re-check (Source/ unchanged: nothing is re-parsed)
BindingCheck.Check(FaroProject.Load(big), Registry.Scan(Path.Combine(big, "Source")), Registry.ScriptClasses(Path.Combine(big, "Source")));
var tReload = clock.ElapsedMilliseconds;
Console.WriteLine($"     scale: {bigProject.Screens.Count} screens, {bigProject.Screens.Values.Sum(g => g.Descendants("Node").Count())} nodes, {bigRegistry.Count} members — load {tLoad} ms, first scan {tScan + tScripts} ms, check {tCheck} ms, build all {tBuild} ms, reload after an edit {tReload} ms");
Check(bigIssues.Count == 0 && tReload < 1000, "a big project reloads quickly after an edit");

// Audit fixes: history labels in Japanese, an unreadable server message ends the read loop (no throw), a broken faro.json is reported.
var language = FaroSettings.Current.Language;
FaroSettings.Current.Language = "ja";
Check(L.Step("Set width") == "width を設定" && L.Step("Delete screen") == "画面を削除" && L.Step("Frobnicate x") == "Frobnicate x", "history labels translated");
FaroSettings.Current.Language = language;
Check(LspClient.Next(new MemoryStream("Content-Length: 5\r\n\r\n{oops"u8.ToArray())) is null && LspClient.Next(new MemoryStream("Content-Length: x\r\n\r\n"u8.ToArray())) is null, "unreadable LSP/DAP message ends the read loop quietly");
var brokenJson = Path.Combine(root, "broken-json");
Directory.CreateDirectory(brokenJson);
File.WriteAllText(Path.Combine(brokenJson, "faro.json"), "{ \"name\": ");
Check(Throws<InvalidDataException>(() => FaroProject.Load(brokenJson)), "broken faro.json is reported as a load error");

Directory.Delete(root, true);
Console.WriteLine("All checks passed.");

static void Check(bool ok, string what)
{
    Console.WriteLine($"{(ok ? "ok  " : "FAIL")} {what}");
    if (!ok) Environment.Exit(1);
}

static string Event(string name, string json) => $"event: {name}\ndata: {json}\n\n";

static bool Throws<T>(Action a) where T : Exception { try { a(); return false; } catch (T) { return true; } }
