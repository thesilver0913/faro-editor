using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;

namespace Faro.Editor;

/// <summary>
/// Minimal LSP client over stdio (spec §8.5): JSON-RPC with Content-Length framing,
/// requests, notifications and publishDiagnostics. The server is csharp-ls for C# projects and
/// Eclipse jdtls for Java ones (on the JDK 21 Faro runs Java with), each installed on first use
/// into Faro's app-data folder at a pinned version.
/// </summary>
public sealed class LspClient : IDisposable
{
    const string ServerVersion = "0.28.0";
    static readonly string ToolDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Faro", "tools");

    readonly Process process;
    readonly ConcurrentDictionary<int, TaskCompletionSource<JsonNode?>> pending = new();
    int nextId;

    /// <summary>(document uri, diagnostics array). Raised on a background thread.</summary>
    public event Action<string, JsonArray>? Diagnostics;
    public event Action? Exited;

    LspClient(Process process)
    {
        this.process = process;
        process.EnableRaisingEvents = true;
        process.Exited += (_, _) =>
        {
            foreach (var p in pending.Values) p.TrySetCanceled();
            Exited?.Invoke();
        };
        new Thread(ReadLoop) { IsBackground = true, Name = "lsp-read" }.Start();
    }

    public static async Task<LspClient> StartAsync(string root)
    {
        var java = JavaProject.Is(root);
        var start = java ? await Jdtls(root) : await CSharpLs();
        start.WorkingDirectory = root;
        var client = new LspClient(Process.Start(start)!);
        client.process.BeginErrorReadLine(); // drain stderr so the server never blocks on it
        await client.Request("initialize", new JsonObject
        {
            ["processId"] = Environment.ProcessId,
            ["rootUri"] = new Uri(root).AbsoluteUri,
            ["capabilities"] = new JsonObject { ["textDocument"] = new JsonObject { ["publishDiagnostics"] = new JsonObject(), ["completion"] = new JsonObject() } },
            // jdtls: Eclipse's .project/.classpath/.settings go to its data folder, not into the user's project
            ["initializationOptions"] = new JsonObject
            {
                ["settings"] = new JsonObject { ["java"] = new JsonObject { ["import"] = new JsonObject { ["generatesMetadataFilesAtProjectRoot"] = false } } },
                ["bundles"] = java && Components.JavaDebug is { } debug ? new JsonArray(debug) : new JsonArray(), // the Java debugger's plugin
            },
        });
        client.Notify("initialized", new JsonObject());
        return client;
    }

    static ProcessStartInfo Server(string file, IEnumerable<string> args) => new(file, args)
    {
        CreateNoWindow = true, // Windows: no empty console window next to Faro (it and the processes it starts)
        RedirectStandardInput = true,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
    };

    static async Task<ProcessStartInfo> CSharpLs()
    {
        var server = Path.Combine(ToolDir, OperatingSystem.IsWindows() ? "csharp-ls.exe" : "csharp-ls");
        if (!File.Exists(server))
        {
            using var install = Process.Start(new ProcessStartInfo("dotnet", ["tool", "install", "--tool-path", ToolDir, "csharp-ls", "--version", ServerVersion]) { CreateNoWindow = true })!;
            await install.WaitForExitAsync();
            if (install.ExitCode != 0) throw new InvalidOperationException($"Installing csharp-ls {ServerVersion} failed (exit {install.ExitCode}).");
        }
        return Server(server, []);
    }

    /// <summary>Eclipse jdtls (it imports the project's pom.xml): its data folder lives in Faro's tools folder, one per project.</summary>
    static async Task<ProcessStartInfo> Jdtls(string root)
    {
        if (Components.Jdtls is null && !await Components.All.First(c => c.Name.StartsWith("Java language server")).Install(_ => { }, CancellationToken.None))
            throw new InvalidOperationException("Installing the Java language server (jdtls) failed: Preferences › Tools shows why.");
        if (Components.JavaDebug is null) await Components.All.First(c => c.Name.StartsWith("Java debugger")).Install(_ => { }, CancellationToken.None); // best effort: debugging needs it
        var home = Components.Jdtls!;
        var launcher = Directory.GetFiles(Path.Combine(home, "plugins"), "org.eclipse.equinox.launcher_*.jar").Single();
        var config = OperatingSystem.IsWindows() ? "config_win" : (OperatingSystem.IsMacOS() ? "config_mac" : "config_linux")
            + (System.Runtime.InteropServices.RuntimeInformation.OSArchitecture == System.Runtime.InteropServices.Architecture.Arm64 ? "_arm" : "");
        var data = Path.Combine(ToolDir, "jdtls-data", Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(root))))[..16]);
        var java = Environment.GetEnvironmentVariable("JAVA_HOME") is { Length: > 0 } javaHome ? Path.Combine(javaHome, "bin", "java") : "java";
        return Server(java, ["-Declipse.application=org.eclipse.jdt.ls.core.id1", "-Dosgi.bundles.defaultStartLevel=4", "-Declipse.product=org.eclipse.jdt.ls.core.product",
            "-Djava.import.generatesMetadataFilesAtProjectRoot=false", // before the workspace opens (also sent as a setting)
            "-Xmx1G", "--add-modules=ALL-SYSTEM", "--add-opens", "java.base/java.util=ALL-UNNAMED", "--add-opens", "java.base/java.lang=ALL-UNNAMED",
            "-jar", launcher, "-configuration", Path.Combine(home, config), "-data", data]);
    }

    public Task<JsonNode?> Request(string method, JsonNode @params)
    {
        var id = Interlocked.Increment(ref nextId);
        var tcs = new TaskCompletionSource<JsonNode?>(TaskCreationOptions.RunContinuationsAsynchronously);
        pending[id] = tcs;
        Send(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["method"] = method, ["params"] = @params });
        return tcs.Task;
    }

    public void Notify(string method, JsonNode @params) =>
        Send(new JsonObject { ["jsonrpc"] = "2.0", ["method"] = method, ["params"] = @params });

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

    void ReadLoop()
    {
        var stdout = process.StandardOutput.BaseStream;
        while (Next(stdout) is { } message)
        {
            if (message["method"] is null && message["id"] is { } id)
            {
                if (pending.TryRemove((int)id, out var tcs))
                    if (message["error"] is { } error) tcs.TrySetException(new InvalidOperationException(error["message"]?.ToString()));
                    else tcs.TrySetResult(message["result"]);
            }
            else if ((string?)message["method"] == "textDocument/publishDiagnostics")
                Diagnostics?.Invoke((string)message["params"]!["uri"]!, message["params"]!["diagnostics"]!.AsArray());
            else if (message["id"] is { } requestId) // server→client request we don't implement
                Send(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = requestId.DeepClone(), ["result"] = null });
        }
        try { if (!process.HasExited) process.Kill(); } // unreadable output: restart it (Exited) rather than hang
        catch (InvalidOperationException) { }
    }

    /// <summary>The next message, or null once the server is gone or sends something unreadable (this thread must not throw: it would end Faro).</summary>
    public static JsonNode? Next(Stream stream)
    {
        try { return ReadMessage(stream); }
        catch (Exception e) when (e is IOException or System.Text.Json.JsonException or FormatException or OverflowException or ObjectDisposedException)
        {
            Log.Error("Language server / debugger connection", e);
            return null;
        }
    }

    /// <summary>One Content-Length framed JSON message (LSP and the debugger's DAP use the same framing), or null at the end.</summary>
    public static JsonNode? ReadMessage(Stream stream)
    {
        var length = 0;
        while (ReadLine(stream) is { } line && line.Length > 0)
            if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase)) length = int.Parse(line[15..]);
        if (length == 0) return null;
        var body = new byte[length];
        stream.ReadExactly(body);
        return JsonNode.Parse(body);
    }

    static string? ReadLine(Stream stream)
    {
        var sb = new StringBuilder();
        for (int b; (b = stream.ReadByte()) != '\n';)
        {
            if (b < 0) return null;
            if (b != '\r') sb.Append((char)b);
        }
        return sb.ToString();
    }

    public void Dispose()
    {
        if (!process.HasExited) process.Kill();
        process.Dispose();
    }
}
