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
/// The runtime binder: shows UIGraph screens and resolves &lt;Bind&gt; targets against the
/// user's built assembly with System.Reflection. Generated user projects call <see cref="Run"/>.
/// </summary>
public static class FaroApp
{
    static FaroProject project = null!;
    static Assembly userAssembly = null!;
    static Window window = null!;
    static readonly Dictionary<Type, object> singletons = [];
    static Dictionary<Type, object> screenScoped = [];

    public static void Run(string[] args, Assembly assembly, string startScreen)
    {
        userAssembly = assembly;
        project = FaroProject.Load(AppContext.BaseDirectory);
        AppBuilder.Configure(() => new RuntimeApp(startScreen)).UsePlatformDetect().StartWithClassicDesktopLifetime(args);
        Release(screenScoped.Values.Concat(singletons.Values));
    }

    /// <summary>Opens a screen by UIGraph id. Also the API user code calls for parameterised navigation (spec §7).</summary>
    public static void Navigate(string screenId)
    {
        if (!project.Screens.TryGetValue(screenId, out var graph))
        {
            ShowError($"Navigate: screen '{screenId}' does not exist.");
            return;
        }
        Release(screenScoped.Values);
        screenScoped = [];

        var byId = new Dictionary<string, Control>();
        window.Content = UiBuilder.Build(graph.Root!.Element("Node")!, byId, project.Root);
        window.Title = screenId;

        var errors = new List<string>();
        void Bind(XElement bind, string prefix)
        {
            if (!byId.TryGetValue(prefix + ((string?)bind.Attribute("nodeId") ?? ""), out var control)) return;
            try { Apply(bind, control); }
            catch (Exception e) { errors.Add($"{bind}\n  → {e.Message}"); }
        }
        foreach (var bind in project.BindsFor(screenId)) Bind(bind, "");
        // Component-level bindings (Bindings/<ComponentId>.xml) apply inside every instance of that component.
        foreach (var (prefix, component) in UiBuilder.InstancePaths(graph.Root!.Element("Node")!))
            foreach (var bind in project.BindsFor(component)) Bind(bind, prefix);
        if (errors.Count > 0) ShowError(string.Join("\n\n", errors));
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

    static void Apply(XElement bind, Control control)
    {
        var target = (string?)bind.Attribute("target") ?? throw new InvalidOperationException("Bind has no target.");
        if ((string?)bind.Attribute("event") is { } eventName)
        {
            var routed = RoutedEventRegistry.Instance.GetRegistered(control.GetType()).FirstOrDefault(e => "On" + e.Name == eventName)
                ?? throw new InvalidOperationException($"{control.GetType().Name} has no event '{eventName}'.");
            if (target.StartsWith("Navigate:"))
            {
                var screen = NavigateScreenId(target, project.Screens.Keys);
                if (!project.Screens.ContainsKey(screen)) throw new InvalidOperationException($"Screen '{screen}' does not exist.");
                control.AddHandler(routed, (EventHandler<RoutedEventArgs>)((_, _) => Navigate(screen)));
                return;
            }
            // ponytail: parameterless methods only; pass event args/node values when a use case needs them
            var method = Resolve(userAssembly, target) as MethodInfo ?? throw new InvalidOperationException($"'{target}' is not a method.");
            if (method.GetParameters().Length > 0) throw new InvalidOperationException($"'{target}' must take no parameters.");
            control.AddHandler(routed, (EventHandler<RoutedEventArgs>)((_, _) =>
            {
                try { method.Invoke(method.IsStatic ? null : InstanceOf(method.DeclaringType!), null); }
                catch (TargetInvocationException e) { ShowError($"{target} threw:\n{e.InnerException}"); }
            }));
        }
        else
        {
            var propName = (string?)bind.Attribute("prop") ?? throw new InvalidOperationException("Bind needs 'event' or 'prop'.");
            var avaloniaProp = AvaloniaPropertyRegistry.Instance.FindRegistered(control, propName)
                ?? throw new InvalidOperationException($"{control.GetType().Name} has no property '{propName}'.");
            var prop = Resolve(userAssembly, target) as PropertyInfo ?? throw new InvalidOperationException($"'{target}' is not a property.");
            control.Bind(avaloniaProp, new ReflectionBinding(prop.Name)
            {
                Source = InstanceOf(prop.DeclaringType!),
                Mode = (string?)bind.Attribute("mode") == "TwoWay" ? BindingMode.TwoWay : BindingMode.OneWay,
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
        var created = attr?.Persistent == true && File.Exists(path)
            ? JsonSerializer.Deserialize(File.ReadAllText(path), type)!
            : Activator.CreateInstance(type)!;
        if (store is not null) store[type] = created;
        return created;
    }

    static void Release(IEnumerable<object> instances)
    {
        foreach (var o in instances)
        {
            if (o.GetType().GetCustomAttribute<FaroLifetimeAttribute>()?.Persistent == true)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(PersistPath(o.GetType()))!);
                File.WriteAllText(PersistPath(o.GetType()), JsonSerializer.Serialize(o, o.GetType()));
            }
            (o as IDisposable)?.Dispose();
        }
    }

    static string PersistPath(Type type) => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Faro", userAssembly.GetName().Name!, type.FullName + ".json");

    /// <summary>Binding/runtime errors in a small window whose text can be selected and copied (spec §6).</summary>
    static void ShowError(string text) => new Window
    {
        Title = "Faro: binding error",
        Width = 640,
        Height = 320,
        Content = new TextBox { Text = text, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, FontFamily = FontFamily.Parse("monospace") },
    }.Show(window);

    sealed class RuntimeApp(string startScreen) : Application
    {
        public override void Initialize() => Styles.Add(new FluentTheme());

        public override void OnFrameworkInitializationCompleted()
        {
            ((IClassicDesktopStyleApplicationLifetime)ApplicationLifetime!).MainWindow = window = new Window { Width = 480, Height = 720 };
            // After Opened: the error window needs a visible owner.
            window.Opened += (_, _) => Navigate(startScreen);
            base.OnFrameworkInitializationCompleted();
        }
    }
}
