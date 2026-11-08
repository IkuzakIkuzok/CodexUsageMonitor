using System.Text.Json;
using CodexUsageMonitor.Core.Models;

namespace CodexUsageMonitor.Core.Services;

// Requests are serialized by the client; notifications may interleave replies.
/// <summary>Exchanges newline-delimited JSON messages with Codex app-server.</summary>
/// <param name="output">The server's standard output, from which responses are read.</param>
/// <param name="input">The server's standard input, to which requests are written.</param>
/// <remarks>The caller owns the streams and must serialize protocol operations.</remarks>
internal sealed class AppServerProtocol(TextReader output, TextWriter input)
{
    /// <summary>Serializer options matching the protocol's property names and required constructor data.</summary>
    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        RespectRequiredConstructorParameters = true,
        RespectNullableAnnotations = true
    };
    private long nextId;

    /// <summary>Completes the initialize request and sends the initialized notification.</summary>
    /// <param name="cancellationToken">A token that cancels the handshake.</param>
    /// <returns>A task that completes when the handshake messages have been exchanged.</returns>
    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await RequestAsync("initialize", new
        {
            clientInfo = new { name = "codex_usage_monitor", title = "Codex Usage Monitor", version = "1.0.0" }
        }, cancellationToken).ConfigureAwait(false);
        await WriteAsync(new { method = "initialized" }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Requests and deserializes <c>account/rateLimits/read</c>.</summary>
    /// <param name="cancellationToken">A token that cancels the request.</param>
    /// <returns>The account rate-limit response.</returns>
    /// <exception cref="JsonException">The response cannot be deserialized or contains a null bucket.</exception>
    public async Task<RateLimitsResponse> ReadRateLimitsAsync(CancellationToken cancellationToken)
    {
        var result = await RequestAsync("account/rateLimits/read", null, cancellationToken).ConfigureAwait(false);
        var usage = result.Deserialize<RateLimitsResponse>(JsonOptions)
            ?? throw new JsonException("Codex returned no usage data.");
        if (usage.RateLimitsByLimitId?.Values.Any(bucket => bucket is null) == true)
        {
            throw new JsonException("Codex returned an invalid usage bucket.");
        }
        return usage;
    }

    /// <summary>Sends a request and waits for its matching response, skipping notifications and rejecting server requests.</summary>
    /// <param name="method">The protocol method to invoke.</param>
    /// <param name="parameters">The request parameters, or <see langword="null"/> when none are needed.</param>
    /// <param name="cancellationToken">A token that cancels writing or waiting for a response.</param>
    /// <returns>A cloned result element that remains valid after its source document is disposed.</returns>
    /// <exception cref="IOException">The server output stream closes before the matching response arrives.</exception>
    /// <exception cref="InvalidOperationException">The server returns a protocol error.</exception>
    /// <exception cref="JsonException">A message is invalid JSON or the matching response has no result.</exception>
    private async Task<JsonElement> RequestAsync(string method, object? parameters, CancellationToken cancellationToken)
    {
        var id = ++this.nextId;
        await WriteAsync(new { id, method, @params = parameters }, cancellationToken).ConfigureAwait(false);
        while (true)
        {
            var line = await output.ReadLineAsync(cancellationToken).ConfigureAwait(false)
                ?? throw new IOException("Codex app-server disconnected. The monitor will reconnect on the next refresh.");
            if (string.IsNullOrWhiteSpace(line)) { continue; }
            using var document = JsonDocument.Parse(line);
            var message = document.RootElement;
            if (message.TryGetProperty("method", out _))
            {
                if (message.TryGetProperty("id", out var requestId))
                {
                    await WriteAsync(new
                    {
                        id = requestId.Clone(),
                        error = new { code = -32601, message = "Method not supported by the usage monitor." }
                    }, cancellationToken).ConfigureAwait(false);
                }
                continue;
            }
            if (!message.TryGetProperty("id", out var responseId)
                || responseId.ValueKind != JsonValueKind.Number
                || !responseId.TryGetInt64(out var value) || value != id)
            {
                continue;
            }
            if (message.TryGetProperty("error", out var error))
            {
                var reason = error.TryGetProperty("message", out var errorMessage)
                    ? errorMessage.GetString() : "Unknown app-server error.";
                throw new InvalidOperationException($"Codex: {reason}");
            }
            return message.TryGetProperty("result", out var result)
                ? result.Clone() : throw new JsonException("Codex returned a response without a result.");
        }
    }

    /// <summary>Serializes a message as one JSON line and flushes it to the server.</summary>
    /// <param name="message">The protocol message to serialize.</param>
    /// <param name="cancellationToken">A token that cancels writing or flushing.</param>
    /// <returns>A task that completes after the message is flushed.</returns>
    private async Task WriteAsync(object message, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(message, JsonOptions);
        await input.WriteLineAsync(json.AsMemory(), cancellationToken).ConfigureAwait(false);
        await input.FlushAsync(cancellationToken).ConfigureAwait(false);
    }
}
