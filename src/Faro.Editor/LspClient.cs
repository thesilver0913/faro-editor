using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;

namespace Faro.Editor;

/// <summary>
/// Minimal LSP client over stdio (spec §8.5): JSON-RPC with Content-Length framing,
/// requests, notifications and publishDiagnostics. The server is csharp-ls, installed on
/// first use into Faro's app-data folder at a pinned version.
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
        var server = Path.Combine(ToolDir, OperatingSystem.IsWindows() ? "csharp-ls.exe" : "csharp-ls");
        if (!File.Exists(server))
        {
            using var install = Process.Start("dotnet", ["tool", "install", "--tool-path", ToolDir, "csharp-ls", "--version", ServerVersion])!;
            await install.WaitForExitAsync();
            if (install.ExitCode != 0) throw new InvalidOperationException($"Installing csharp-ls {ServerVersion} failed (exit {install.ExitCode}).");
        }
        var client = new LspClient(Process.Start(new ProcessStartInfo(server)
        {
            WorkingDirectory = root,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        })!);
        client.process.BeginErrorReadLine(); // drain stderr so the server never blocks on it
        await client.Request("initialize", new JsonObject
        {
            ["processId"] = Environment.ProcessId,
            ["rootUri"] = new Uri(root).AbsoluteUri,
            ["capabilities"] = new JsonObject { ["textDocument"] = new JsonObject { ["publishDiagnostics"] = new JsonObject(), ["completion"] = new JsonObject() } },
        });
        client.Notify("initialized", new JsonObject());
        return client;
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
        while (ReadMessage(stdout) is { } message)
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
    }

    static JsonNode? ReadMessage(Stream stream)
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
