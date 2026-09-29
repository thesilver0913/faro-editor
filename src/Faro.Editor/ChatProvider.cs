using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Anthropic;
using Anthropic.Models.Beta;
using Anthropic.Models.Beta.Messages;

namespace Faro.Editor;

public sealed record ChatTurn(bool IsUser, string Text);

/// <summary>A swappable LLM backend for vibe coding. API keys come from environment variables only.</summary>
public interface IChatProvider
{
    IAsyncEnumerable<string> Stream(string system, IReadOnlyList<ChatTurn> history, CancellationToken ct);
}

/// <summary>Claude via the official Anthropic SDK (reads ANTHROPIC_API_KEY), streamed.</summary>
public sealed class ClaudeProvider(string model) : IChatProvider
{
    public async IAsyncEnumerable<string> Stream(string system, IReadOnlyList<ChatTurn> history, [EnumeratorCancellation] CancellationToken ct)
    {
        AnthropicClient client = new();
        var parameters = new MessageCreateParams
        {
            Model = model,
            MaxTokens = 64000,
            System = system,
            Messages = [.. history.Select(t => new BetaMessageParam { Role = t.IsUser ? Role.User : Role.Assistant, Content = t.Text })],
            // Refusal fallback: a policy decline is re-served by Opus 4.8 inside the same call.
            // ponytail: list form; switch to "default" (per-category routing) once the C# SDK exposes it
            Betas = [AnthropicBeta.ServerSideFallback2026_06_01],
            Fallbacks = new List<BetaFallbackParam> { new(Anthropic.Models.Messages.Model.ClaudeOpus4_8) },
        };
        await foreach (var e in client.Beta.Messages.CreateStreaming(parameters, ct))
            if (e.TryPickContentBlockDelta(out var delta) && delta.Delta.TryPickText(out var text))
                yield return text.Text;
    }
}

/// <summary>
/// Any OpenAI-compatible /chat/completions endpoint (OpenAI, Ollama, LM Studio, …), streamed over SSE.
/// Reads OPENAI_API_KEY; local servers usually need none.
/// </summary>
public sealed class OpenAiCompatibleProvider(string baseUrl, string model) : IChatProvider
{
    static readonly HttpClient http = new() { Timeout = Timeout.InfiniteTimeSpan };

    public async IAsyncEnumerable<string> Stream(string system, IReadOnlyList<ChatTurn> history, [EnumeratorCancellation] CancellationToken ct)
    {
        var messages = new JsonArray(new JsonObject { ["role"] = "system", ["content"] = system });
        foreach (var t in history) messages.Add(new JsonObject { ["role"] = t.IsUser ? "user" : "assistant", ["content"] = t.Text });
        using var request = new HttpRequestMessage(HttpMethod.Post, baseUrl.TrimEnd('/') + "/chat/completions")
        {
            Content = new StringContent(new JsonObject { ["model"] = model, ["stream"] = true, ["messages"] = messages }.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        if (Environment.GetEnvironmentVariable("OPENAI_API_KEY") is { Length: > 0 } key)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);

        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"{(int)response.StatusCode} {response.ReasonPhrase}: {await response.Content.ReadAsStringAsync(ct)}");
        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(ct));
        while (await reader.ReadLineAsync(ct) is { } line)
        {
            if (!line.StartsWith("data:")) continue;
            var data = line[5..].Trim();
            if (data == "[DONE]") yield break;
            if (JsonNode.Parse(data)?["choices"]?[0]?["delta"]?["content"]?.GetValue<string>() is { } text)
                yield return text;
        }
    }
}
