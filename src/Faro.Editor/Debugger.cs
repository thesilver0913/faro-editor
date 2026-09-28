using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;
using Avalonia.Threading;

namespace Faro.Editor;

/// <summary>
/// The C# debugger: Samsung's netcoredbg over the Debug Adapter Protocol (the one VS Code speaks). Debug builds the
/// project, launches its dll under netcoredbg with the breakpoints set in the code view, and on a stop shows the
/// call stack and the frame's variables (Console › Debug) and the line in the code view. Trusted C# projects only.
/// </summary>
public static class Debugger
{
    /// <summary>Breakpoints: file path → 1-based lines (kept for the session, sent whenever a debug run starts or they change).</summary>
    public static readonly Dictionary<string, SortedSet<int>> Breakpoints = [];

    /// <summary>Anything shown changed (UI thread): breakpoints, running/stopped, the stack, the variables.</summary>
    public static event Action? Changed;

    public static bool Active => session is not null;
    public static (string Path, int Line)? Stopped { get; private set; }
    public static List<(string Name, string? Path, int Line, int Id)> Frames { get; private set; } = [];
    public static List<(string Name, string Value, string? Type)> Variables { get; private set; } = [];

    static Dap? session;
    static int thread;

    public static void Toggle(string path, int line)
    {
        var lines = Breakpoints.TryGetValue(path, out var set) ? set : Breakpoints[path] = [];
        if (!lines.Remove(line)) lines.Add(line);
        if (session is not null) SetBreakpoints(path);
        Changed?.Invoke();
    }

    public static async void Start()
    {
        if (session is not null || Workspace.Running || !Workspace.Trusted) return;
        if (Workspace.IsJava) { Workspace.Add(L.T("The debugger is for C# projects for now.")); return; }
        ConsoleView.Show(ConsoleView.Tab.Debug);
        Workspace.Add(L.T("Building for debugging…"));
        if (await AndroidApk.Exec("dotnet", ["build", Workspace.Root], line => Dispatcher.UIThread.Post(() => Workspace.Add(line)), CancellationToken.None) != 0
            || ScriptPreview.LatestBuild(Workspace.Root) is not { } dll)
        {
            Workspace.Add(L.T("The build failed (see above)."));
            return;
        }
        if (Components.Netcoredbg is null && !await Components.All.First(c => c.Name == "netcoredbg").Install(line => Dispatcher.UIThread.Post(() => Workspace.Add(line)), CancellationToken.None))
            return;
        try { session = new Dap(Components.Netcoredbg!); }
        catch (System.ComponentModel.Win32Exception e) { Workspace.Add(L.F("Couldn't start {0}: {1}", "netcoredbg", e.Message)); return; }
        session.Event += (name, body) => Dispatcher.UIThread.Post(() => OnEvent(name, body));
        session.Exited += () => Dispatcher.UIThread.Post(End);
        Changed?.Invoke();
        try
        {
            await session.Request("initialize", new JsonObject { ["clientID"] = "faro", ["adapterID"] = "coreclr", ["linesStartAt1"] = true, ["columnsStartAt1"] = true, ["pathFormat"] = "path" });
            // The app reads its project files from its own folder (the build output), like `dotnet run`.
            await session.Request("launch", new JsonObject { ["program"] = Dotnet(), ["args"] = new JsonArray(dll), ["cwd"] = Path.GetDirectoryName(dll), ["stopAtEntry"] = false });
        }
        catch (InvalidOperationException e) { Workspace.Add("netcoredbg: " + e.Message); Stop(); }
    }

    static async void OnEvent(string name, JsonNode? body)
    {
        switch (name)
        {
            case "initialized":
                foreach (var path in Breakpoints.Keys) SetBreakpoints(path);
                await Try(() => session!.Request("configurationDone", new JsonObject()));
                break;
            case "stopped":
                thread = (int?)body?["threadId"] ?? thread;
                var stack = await Try(() => session!.Request("stackTrace", new JsonObject { ["threadId"] = thread, ["levels"] = 30 }));
                Frames = [.. (stack?["stackFrames"]?.AsArray() ?? []).Select(f => ((string)f!["name"]!, (string?)f["source"]?["path"], (int?)f["line"] ?? 0, (int)f["id"]!))];
                await Select(Frames.FindIndex(f => f.Path is not null)); // the first frame with source (not in the framework)
                break;
            case "continued":
                (Stopped, Frames, Variables) = (null, [], []);
                Changed?.Invoke();
                break;
            case "output" when (body?["output"]) is JsonValue text:
                foreach (var line in ((string)text!).TrimEnd('\n', '\r').Split('\n')) Workspace.Add(line.TrimEnd('\r'));
                break;
            case "terminated" or "exited":
                End();
                break;
        }
    }

    /// <summary>Shows a frame of the stack: its line in the code view and its variables.</summary>
    public static async Task Select(int index)
    {
        if (session is null || index < 0 || index >= Frames.Count) { Changed?.Invoke(); return; }
        var frame = Frames[index];
        Stopped = frame.Path is { } path ? (path, frame.Line) : null;
        var scopes = await Try(() => session.Request("scopes", new JsonObject { ["frameId"] = frame.Id }));
        Variables = [];
        foreach (var scope in scopes?["scopes"]?.AsArray() ?? [])
        {
            foreach (var v in await Children((int)scope!["variablesReference"]!))
            {
                Variables.Add(((string)v!["name"]!, (string?)v["value"] ?? "", (string?)v["type"]));
                if ((int?)v["variablesReference"] is > 0 and var inner) // one level of members ("this.SubmitCount")
                    Variables.AddRange((await Children(inner)).Take(30).Select(m => ($"{v["name"]}.{m!["name"]}", (string?)m["value"] ?? "", (string?)m["type"])));
            }
        }
        if (Stopped is { } at) CodeView.Open(at.Path, at.Line);
        Changed?.Invoke();
    }

    static async Task<JsonArray> Children(int reference) =>
        (await Try(() => session!.Request("variables", new JsonObject { ["variablesReference"] = reference })))?["variables"]?.AsArray() ?? [];

    public static void Continue() => Step("continue");
    public static void StepOver() => Step("next");
    public static void StepInto() => Step("stepIn");

    static async void Step(string command)
    {
        if (session is null || Stopped is null) return;
        (Stopped, Frames, Variables) = (null, [], []);
        Changed?.Invoke();
        await Try(() => session.Request(command, new JsonObject { ["threadId"] = thread }));
    }

    public static async void Stop()
    {
        if (session is null) return;
        await Try(() => session.Request("disconnect", new JsonObject { ["terminateDebuggee"] = true }));
        End();
    }

    static void End()
    {
        if (session is null) return;
        session.Dispose();
        session = null;
        (Stopped, Frames, Variables) = (null, [], []);
        Workspace.Add(L.T("[Debugging stopped]"));
        Changed?.Invoke();
    }

    static async void SetBreakpoints(string path) => await Try(() => session!.Request("setBreakpoints", new JsonObject
    {
        ["source"] = new JsonObject { ["path"] = path },
        ["breakpoints"] = new JsonArray([.. Breakpoints[path].Select(l => (JsonNode)new JsonObject { ["line"] = l })]),
    }));

    static async Task<JsonNode?> Try(Func<Task<JsonNode?>> request)
    {
        try { return await request(); }
        catch (Exception e) when (e is InvalidOperationException or TaskCanceledException or NullReferenceException) { return null; }
    }

    /// <summary>The dotnet host Faro runs builds with (netcoredbg starts the app's dll through it).</summary>
    static string Dotnet()
    {
        var name = OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet";
        return (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator).Select(d => Path.Combine(d, name)).FirstOrDefault(File.Exists) ?? name;
    }

    /// <summary>A Debug Adapter Protocol connection over stdio (Content-Length framing, like LSP; requests, responses, events).</summary>
    sealed class Dap : IDisposable
    {
        readonly Process process;
        readonly ConcurrentDictionary<int, TaskCompletionSource<JsonNode?>> pending = new();
        int seq;
        public event Action<string, JsonNode?>? Event;
        public event Action? Exited;

        public Dap(string adapter)
        {
            process = Process.Start(new ProcessStartInfo(adapter, ["--interpreter=vscode"])
            {
                CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            })!;
            process.EnableRaisingEvents = true;
            process.Exited += (_, _) => { foreach (var p in pending.Values) p.TrySetCanceled(); Exited?.Invoke(); };
            process.BeginErrorReadLine();
            new Thread(Read) { IsBackground = true, Name = "dap-read" }.Start();
        }

        public Task<JsonNode?> Request(string command, JsonNode arguments)
        {
            var id = Interlocked.Increment(ref seq);
            var tcs = new TaskCompletionSource<JsonNode?>(TaskCreationOptions.RunContinuationsAsynchronously);
            pending[id] = tcs;
            Send(new JsonObject { ["seq"] = id, ["type"] = "request", ["command"] = command, ["arguments"] = arguments });
            return tcs.Task;
        }

        void Send(JsonObject message)
        {
            var body = Encoding.UTF8.GetBytes(message.ToJsonString());
            lock (process)
            {
                if (process.HasExited) return;
                var stdin = process.StandardInput.BaseStream;
                stdin.Write(Encoding.ASCII.GetBytes($"Content-Length: {body.Length}\r\n\r\n"));
                stdin.Write(body);
                stdin.Flush();
            }
        }

        void Read()
        {
            var stdout = process.StandardOutput.BaseStream;
            while (LspClient.ReadMessage(stdout) is { } message)
                switch ((string?)message["type"])
                {
                    case "response" when pending.TryRemove((int)message["request_seq"]!, out var tcs):
                        if ((bool?)message["success"] == true) tcs.TrySetResult(message["body"]);
                        else tcs.TrySetException(new InvalidOperationException((string?)message["message"] ?? "failed"));
                        break;
                    case "event":
                        Event?.Invoke((string)message["event"]!, message["body"]);
                        break;
                    case "request": // an adapter→client request we don't implement (e.g. runInTerminal)
                        Send(new JsonObject { ["seq"] = Interlocked.Increment(ref seq), ["type"] = "response", ["request_seq"] = message["seq"]!.DeepClone(), ["success"] = false, ["command"] = message["command"]!.DeepClone() });
                        break;
                }
        }

        public void Dispose()
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            process.Dispose();
        }
    }
}
