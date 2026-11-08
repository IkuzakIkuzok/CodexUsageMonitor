using System.Text.Json;

namespace CodexUsageMonitor.Tests.FakeServer;

public static class Program
{
    public static async Task Main(string[] args)
    {
        var mode = args.FirstOrDefault() ?? "normal";
        var initialized = false;
        if (args.Length > 1) { await File.WriteAllTextAsync(args[1], Environment.ProcessId.ToString()); }
        while (await Console.In.ReadLineAsync() is { } line)
        {
            using var document = JsonDocument.Parse(line);
            var request = document.RootElement;
            var method = request.GetProperty("method").GetString();
            if (method == "initialized") { initialized = true; continue; }
            var id = request.GetProperty("id").GetInt64();
            if (method == "initialize")
            {
                await Console.Out.WriteLineAsync(JsonSerializer.Serialize(new { id, result = new { } }));
                continue;
            }
            if (!initialized || method != "account/rateLimits/read") { Environment.Exit(2); }
            if (mode == "hang") { await Task.Delay(Timeout.Infinite); }
            if (mode == "exit") { return; }
            if (mode == "error")
            {
                await Console.Out.WriteLineAsync(JsonSerializer.Serialize(new { id, error = new { code = -32000, message = "Sign in with ChatGPT" } }));
                continue;
            }
            await Console.Error.WriteAsync(new string('x', 100_000));
            await Console.Out.WriteLineAsync("""{"method":"account/rateLimits/updated","params":{"rateLimits":{}}}""");
            await Console.Out.WriteLineAsync(JsonSerializer.Serialize(new
            {
                id,
                result = new { rateLimits = new { primary = new { usedPercent = 38, windowDurationMins = 300, resetsAt = 1800000000 } } }
            }));
        }
    }
}
