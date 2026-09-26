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

// Runtime binder resolves the same target strings via reflection on the built assembly.
var asm = typeof(MyApp.Services.OrderService).Assembly;
Check(FaroApp.Resolve(asm, "MyApp.Services.OrderService.Submit") is MethodInfo, "runtime resolves method");
Check(FaroApp.Resolve(asm, "MyApp.Models.UserProfile.Name") is PropertyInfo, "runtime resolves property");
Check(Throws(() => FaroApp.Resolve(asm, "MyApp.Nope.Submit")), "runtime rejects unknown class");
Check(FaroApp.NavigateScreenId("Navigate:Screen.Detail", ["Detail"]) == "Detail", "navigate id");

Directory.Delete(root, true);
Console.WriteLine("All checks passed.");

static void Check(bool ok, string what)
{
    Console.WriteLine($"{(ok ? "ok  " : "FAIL")} {what}");
    if (!ok) Environment.Exit(1);
}

static bool Throws(Action a) { try { a(); return false; } catch (InvalidOperationException) { return true; } }
