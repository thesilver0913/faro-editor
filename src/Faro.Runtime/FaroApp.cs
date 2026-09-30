using System.ComponentModel;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Xml.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Themes.Fluent;

namespace Faro.Runtime;

/// <summary>Instance lifetime of a user class (spec §6.5). Classes without the attribute are ScreenScoped.</summary>
public enum Lifetime { Singleton, ScreenScoped, Transient }

[AttributeUsage(AttributeTargets.Class)]
public sealed class FaroLifetimeAttribute(Lifetime lifetime) : Attribute
{
    public Lifetime Lifetime { get; } = lifetime;

    /// <summary>Keep the public properties as JSON across app runs (saved when the instance is released).</summary>
    public bool Persistent { get; init; }
}

/// <summary>Change-notification base for user classes, so TwoWay bindings see code-side updates (spec §6).</summary>
public abstract class FaroObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void Set<T>(ref T field, T value, [CallerMemberName] string name = "")
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new(name));
    }
}

/// <summary>
/// Base class for a Script node's class (type="Control.Script" class="Ns.Class"): look, controls and behaviour
/// all written in code. Build() returns the Avalonia control shown in the node's place; lifetimes apply as usual.
/// </summary>
public abstract class FaroScript : FaroObject
{
    public abstract Control Build();
}

/// <summary>
/// The runtime binder: shows UIGraph screens and resolves &lt;Bind&gt; targets against the
/// user's built assembly with System.Reflection. Generated user projects call <see cref="Run"/>.
/// </summary>
public static class FaroApp
{
    static FaroProject project = null!;
    static Assembly userAssembly = null!;
    static Window? window; // desktop; Android shows one view
    static ContentControl host = null!; // holds the current screen
    static TextBox? errors; // Android: errors show over the screen
    static string start = "";
    static string? current; // the screen shown (live reload rebuilds it)
    static readonly Dictionary<Type, object> singletons = [];
    static Dictionary<Type, object> screenScoped = [];

    /// <summary>Starts the app on faro.json's "startScreen" (<paramref name="startScreen"/> overrides it).</summary>
    public static void Run(string[] args, Assembly assembly, string? startScreen = null)
    {
        Init(assembly, AppContext.BaseDirectory, startScreen);
        AppBuilder.Configure<FaroApplication>().UsePlatformDetect().StartWithClassicDesktopLifetime(args);
        Release(screenScoped.Values.Concat(singletons.Values));
    }

    /// <summary>Loads the project for <see cref="FaroApplication"/>; the Android head calls it with the unpacked project folder.</summary>
    public static void Init(Assembly assembly, string projectRoot, string? startScreen = null)
    {
        userAssembly = assembly;
        project = FaroProject.Load(projectRoot);
        UiBuilder.ScriptFactory = name => userAssembly.GetType(name) is { } type && typeof(FaroScript).IsAssignableFrom(type)
            ? ((FaroScript)InstanceOf(type)).Build()
            : throw new InvalidOperationException($"'{name}' is not a FaroScript class in {userAssembly.GetName().Name}.");
        start = startScreen ?? project.StartScreen ?? "MainScreen";
    }

    internal static FaroProject Project => project;

    internal static void Attach(IApplicationLifetime? lifetime)
    {
        host = new ContentControl();
        if (lifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = window = new Window { Width = 480, Height = 720, Content = host };
            window.Opened += (_, _) => Navigate(start); // after Opened: the error window needs a visible owner
            WatchLive();
        }
        else if (lifetime is IActivityApplicationLifetime activity) // Android: a new view per activity, the same screen
        {
            // Android may end a backgrounded app without Run returning: persistent instances are saved on the way out.
            if (Application.Current?.TryGetFeature<IActivatableLifetime>() is { } activatable)
                activatable.Deactivated += (_, _) => { foreach (var o in screenScoped.Values.Concat(singletons.Values)) Save(o); };
            activity.MainViewFactory = () =>
            {
                (host.Parent as Panel)?.Children.Clear();
                errors = new TextBox { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, IsVisible = false, MaxHeight = 240, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Bottom };
                var view = new Grid { Children = { host, errors } };
                if (host.Content is null) Navigate(start);
                return view;
            };
        }
    }

    /// <summary>What the last Navigate passed: the new screen's binds to members of its class use it (e.g. the tapped row's item).</summary>
    public static object? Parameter { get; private set; }

    /// <summary>Opens a screen by UIGraph id, optionally with a value for its binds (spec §7: user code navigates with parameters).</summary>
    public static void Navigate(string screenId, object? parameter = null)
    {
        if (!project.Screens.TryGetValue(screenId, out var graph))
        {
            ShowError($"Navigate: screen '{screenId}' does not exist.");
            return;
        }
        Release(screenScoped.Values);
        screenScoped = [];
        Parameter = parameter;
        Show(screenId, graph);
    }

    static void Show(string screenId, XDocument graph)
    {
        current = screenId;
        if (errors is not null) errors.IsVisible = false; // Android: the last screen's errors
        var byId = new Dictionary<string, Control>();
        host.Content = UiBuilder.Build(graph.Root!.Element("Node")!, byId, project.Root);
        if (window is not null) window.Title = screenId;
        var failed = Bind(graph.Root!.Element("Node")!, screenId, byId, events: true);
        if (failed.Count > 0) ShowError(string.Join("\n\n", failed));
    }

    /// <summary>
    /// Live reload: when the editor runs the app it passes the project folder in FARO_LIVE. Saved changes to UI/, Bindings/
    /// and faro.json (design, tokens) rebuild the current screen in place, with the same instances, so its state stays.
    /// </summary>
    static void WatchLive()
    {
        if (Environment.GetEnvironmentVariable("FARO_LIVE") is not { Length: > 0 } dir || !Directory.Exists(dir)) return;
        var timer = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) }; // editors save in bursts
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            try
            {
                var design = project.Design;
                project = FaroProject.Load(dir);
                if (project.Design != design && Application.Current is { } app) FaroApplication.ApplyDesign(app, project.Design);
                if (current is not null && project.Screens.TryGetValue(current, out var graph)) Show(current, graph);
            }
            catch (Exception e) { ShowError($"Live reload: {e.Message}"); } // a half-written file: the next save retries
        };
        var watcher = new FileSystemWatcher(dir) { IncludeSubdirectories = true, EnableRaisingEvents = true };
        FileSystemEventHandler changed = (_, e) =>
        {
            var path = Path.GetRelativePath(dir, e.FullPath).Replace('\\', '/');
            if (path.StartsWith("UI/") || path.StartsWith("Bindings/") || path == "faro.json")
                Avalonia.Threading.Dispatcher.UIThread.Post(() => { timer.Stop(); timer.Start(); });
        };
        watcher.Changed += changed;
        watcher.Created += changed;
        watcher.Deleted += changed;
        watcher.Renamed += (s, e) => changed(s, e);
        live = watcher;
    }

    static FileSystemWatcher? live; // kept alive for the app's lifetime

    /// <summary>
    /// Design-time data (the editor's canvas): the screen's property bindings (not events) on controls built from it,
    /// with new instances of the classes in <paramref name="assembly"/> (nothing is persisted). Returns the errors.
    /// </summary>
    public static List<string> Preview(Assembly assembly, FaroProject faroProject, string screenId, Dictionary<string, Control> byId, object? parameter = null)
    {
        (userAssembly, project, Parameter) = (assembly, faroProject, parameter);
        singletons.Clear();
        screenScoped = [];
        return project.Graph(screenId)?.Root?.Element("Node") is { } node ? Bind(node, screenId, byId, events: false) : [];
    }

    /// <summary>Screen binds, then component-level binds (Bindings/&lt;ComponentId&gt;.xml) inside every instance of that component.</summary>
    static List<string> Bind(XElement node, string screenId, Dictionary<string, Control> byId, bool events)
    {
        var binds = project.BindsFor(screenId).Select(b => (Key: (string?)b.Attribute("nodeId") ?? "", Bind: b))
            .Concat(UiBuilder.InstancePaths(node).SelectMany(p => project.BindsFor(p.Component).Select(b => (Key: p.Prefix + ((string?)b.Attribute("nodeId") ?? ""), Bind: b))))
            .Where(b => events || b.Bind.Attribute("event") is null)
            .ToList();
        var lists = byId.Where(p => p.Value is RepeatHost).Select(p => p.Key + "/").ToList();
        var errors = new List<string>();
        foreach (var (key, bind) in binds)
            if (!lists.Any(key.StartsWith) && byId.TryGetValue(key, out var control)) // binds inside a list apply per row (BindItems)
                Try(errors, bind, () =>
                {
                    if (control is RepeatHost list && (string?)bind.Attribute("prop") == "Items") BindItems(list, bind, key, binds, errors);
                    else Apply(bind, control, null);
                });
        return errors;
    }

    /// <summary>"Navigate:Screen.Detail" → "Detail" (the "Screen." prefix is optional).</summary>
    public static string NavigateScreenId(string target, ICollection<string> screens)
    {
        var id = target["Navigate:".Length..];
        return !screens.Contains(id) && id.StartsWith("Screen.") ? id["Screen.".Length..] : id;
    }

    /// <summary>Splits "Ns.Class.Member" and finds the member in the user assembly.</summary>
    public static MemberInfo Resolve(Assembly assembly, string target)
    {
        var dot = target.LastIndexOf('.');
        var type = dot > 0 ? assembly.GetType(target[..dot]) : null;
        if (type is null) throw new InvalidOperationException($"Class '{(dot > 0 ? target[..dot] : target)}' not found in {assembly.GetName().Name}.");
        return type.GetMember(target[(dot + 1)..], BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static).FirstOrDefault()
            ?? throw new InvalidOperationException($"'{type.Name}' has no public member '{target[(dot + 1)..]}'.");
    }

    static void Try(List<string> errors, XElement bind, Action apply)
    {
        try { apply(); }
        catch (Exception e) { errors.Add($"{bind}\n  → {e.Message}"); }
    }

    /// <summary>
    /// prop="Items" on a repeatable instance: one copy of it per item of a list property, redrawn when the
    /// list changes (INotifyCollectionChanged) or is replaced (PropertyChanged). Binds inside a row use the
    /// row's item when the target's class is the item's type.
    /// </summary>
    static void BindItems(RepeatHost list, XElement bind, string key, List<(string Key, XElement Bind)> binds, List<string>? firstErrors)
    {
        var target = (string?)bind.Attribute("target") ?? throw new InvalidOperationException("Bind has no target.");
        var prop = Resolve(userAssembly, target) as PropertyInfo ?? throw new InvalidOperationException($"'{target}' is not a property.");
        if (!typeof(System.Collections.IEnumerable).IsAssignableFrom(prop.PropertyType)) throw new InvalidOperationException($"'{target}' is not a list.");
        var owner = prop.GetMethod!.IsStatic ? null : InstanceOf(prop.DeclaringType!);
        var prefix = key[..^((string?)list.Node.Attribute("id") ?? "").Length];
        System.Collections.Specialized.INotifyCollectionChanged? watched = null;
        System.Collections.Specialized.NotifyCollectionChangedEventHandler changed = null!;
        var screen = host?.Content; // the editor's preview has no host
        void Render()
        {
            if (watched is not null) watched.CollectionChanged -= changed;
            if (host?.Content != screen) return; // a singleton owner outlives the screen: stop once it is left
            var items = prop.GetValue(owner) as System.Collections.IEnumerable;
            watched = items as System.Collections.Specialized.INotifyCollectionChanged;
            if (watched is not null) watched.CollectionChanged += changed;
            list.Children.Clear();
            var errors = new List<string>();
            foreach (var item in items ?? Array.Empty<object>())
            {
                var row = new Dictionary<string, Control>();
                list.Children.Add(UiBuilder.Copy(list.Node, row, project.Root, prefix));
                foreach (var (k, b) in binds)
                    if (k.StartsWith(key + "/") && row.TryGetValue(k, out var control)) Try(errors, b, () => Apply(b, control, item));
            }
            if (errors.Count == 0) return;
            if (firstErrors is not null) firstErrors.AddRange(errors); // the first rows: reported with the screen's other errors
            else if (host is not null) ShowError(string.Join("\n\n", errors));
        }
        changed = (_, _) => Render();
        if (owner is INotifyPropertyChanged notify)
        {
            PropertyChangedEventHandler replaced = null!;
            replaced = (_, e) =>
            {
                if (host?.Content != screen) notify.PropertyChanged -= replaced; // a singleton owner: let go of the left screen
                else if (e.PropertyName == prop.Name) Render();
            };
            notify.PropertyChanged += replaced;
        }
        Render();
        firstErrors = null;
    }

    static void Apply(XElement bind, Control control, object? item)
    {
        // Members of the row item's class (inside a list) or of the navigation parameter's class bind to that object.
        object? Given(Type type) => new[] { item, Parameter }.FirstOrDefault(type.IsInstanceOfType);
        object? Source(MemberInfo member, bool isStatic) => isStatic ? null : Given(member.DeclaringType!) ?? InstanceOf(member.DeclaringType!);
        var target = (string?)bind.Attribute("target") ?? throw new InvalidOperationException("Bind has no target.");
        if ((string?)bind.Attribute("event") is { } eventName)
        {
            var routed = Bindable.For(control)?.Events.GetValueOrDefault(eventName)
                ?? throw new InvalidOperationException($"{Bindable.For(control)?.Type ?? control.GetType().Name} has no event '{eventName}'.");
            // A tap anywhere on a container (a row) counts; one with its own colors (Appearance) already has a fill its states can replace.
            if (control is Border { Background: null } container && !container.Classes.Any(c => c.StartsWith("faro-look-"))) container.Background = Brushes.Transparent;
            if (target.StartsWith("Navigate:"))
            {
                var screen = NavigateScreenId(target, project.Screens.Keys);
                if (!project.Screens.ContainsKey(screen)) throw new InvalidOperationException($"Screen '{screen}' does not exist.");
                control.AddHandler(routed, (EventHandler<RoutedEventArgs>)((_, _) => Navigate(screen, item))); // a row passes its item
                return;
            }
            // No parameters, or one: the row's item (selection) or the navigation parameter, whichever its type takes.
            var method = Resolve(userAssembly, target) as MethodInfo ?? throw new InvalidOperationException($"'{target}' is not a method.");
            var parameters = method.GetParameters();
            object?[]? args = parameters.Length switch
            {
                0 => null,
                1 => [Given(parameters[0].ParameterType) ?? throw new InvalidOperationException($"'{target}' takes a {parameters[0].ParameterType.Name}: bind it inside a list of them, or on a screen opened with one.")],
                _ => throw new InvalidOperationException($"'{target}' must take no parameters, or one (the row's item or the screen's parameter)."),
            };
            control.AddHandler(routed, (EventHandler<RoutedEventArgs>)((_, _) =>
            {
                try { method.Invoke(Source(method, method.IsStatic), args); }
                catch (TargetInvocationException e) { ShowError($"{target} threw:\n{e.InnerException}"); }
            }));
        }
        else
        {
            var propName = (string?)bind.Attribute("prop") ?? throw new InvalidOperationException("Bind needs 'event' or 'prop'.");
            var avaloniaProp = Bindable.For(control)?.Props.GetValueOrDefault(propName)
                ?? throw new InvalidOperationException($"{Bindable.For(control)?.Type ?? control.GetType().Name} has no property '{propName}'.");
            var prop = Resolve(userAssembly, target) as PropertyInfo ?? throw new InvalidOperationException($"'{target}' is not a property.");
            control.Bind(avaloniaProp, new ReflectionBinding(prop.Name)
            {
                Source = Source(prop, prop.GetMethod?.IsStatic == true),
                Mode = (string?)bind.Attribute("mode") == "TwoWay" ? BindingMode.TwoWay : BindingMode.OneWay,
                StringFormat = (string?)bind.Attribute("format"), // "¥{0:N0}"; N and F are the ones Java formats the same way
            });
        }
    }

    static object InstanceOf(Type type)
    {
        var attr = type.GetCustomAttribute<FaroLifetimeAttribute>();
        var store = attr?.Lifetime switch
        {
            Lifetime.Transient => null,
            Lifetime.Singleton => singletons,
            _ => screenScoped,
        };
        if (store?.TryGetValue(type, out var existing) == true) return existing;
        var path = PersistPath(type);
        object? saved = null;
        if (attr?.Persistent == true && File.Exists(path))
            try { saved = JsonSerializer.Deserialize(File.ReadAllText(path), type); }
            catch (Exception e) when (e is JsonException or NotSupportedException or IOException) { } // a damaged save: start fresh
        var created = saved ?? Activator.CreateInstance(type)!;
        if (store is not null) store[type] = created;
        return created;
    }

    static void Release(IEnumerable<object> instances)
    {
        foreach (var o in instances)
        {
            Save(o);
            (o as IDisposable)?.Dispose();
        }
    }

    static void Save(object o)
    {
        if (o.GetType().GetCustomAttribute<FaroLifetimeAttribute>()?.Persistent != true) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PersistPath(o.GetType()))!);
            File.WriteAllText(PersistPath(o.GetType()), JsonSerializer.Serialize(o, o.GetType()));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException) { Console.Error.WriteLine($"Faro: couldn't save {o.GetType()}: {e.Message}"); }
    }

    static string PersistPath(Type type) => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Faro", userAssembly.GetName().Name!, type.FullName + ".json");

    /// <summary>Binding/runtime errors in a small window whose text can be selected and copied (spec §6); on Android, over the screen.</summary>
    static void ShowError(string text)
    {
        if (window is null)
        {
            (errors!.Text, errors.IsVisible) = (text, true);
            return;
        }
        new Window
        {
            Title = "Faro: binding error",
            Width = 640,
            Height = 320,
            Content = new TextBox { Text = text, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, FontFamily = FontFamily.Parse("monospace") },
        }.Show(window);
    }
}

/// <summary>The app: Fluent plus the project's design language, showing <see cref="FaroApp"/>'s screens in a window (desktop) or one view (Android).</summary>
public sealed class FaroApplication : Application
{
    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
        ApplyDesign(this, FaroApp.Project.Design);
    }

    /// <summary>The design language over Fluent (again on a live reload that changes it).</summary>
    internal static void ApplyDesign(Application app, AppDesign design)
    {
        while (app.Styles.Count > 1) app.Styles.RemoveAt(1);
        app.Resources = new ResourceDictionary();
        design.Apply(app.Styles, app.Resources);
        app.RequestedThemeVariant = design.Variant;
    }

    public override void OnFrameworkInitializationCompleted()
    {
        FaroApp.Attach(ApplicationLifetime);
        base.OnFrameworkInitializationCompleted();
    }
}
