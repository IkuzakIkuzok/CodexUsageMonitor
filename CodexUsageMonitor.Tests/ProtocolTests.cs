using System.Text.Json;
using CodexUsageMonitor.Core.Services;
using Xunit;

namespace CodexUsageMonitor.Tests;

public sealed class ProtocolTests
{
    [Fact]
    public async Task HandshakePrecedesReadAndInterleavedMessagesAreHandled()
    {
        using var output = new StringReader("""
            {"id":1,"result":{}}
            {"method":"account/rateLimits/updated","params":{"rateLimits":{}}}
            {"id":2,"method":"unsupported/request","params":{}}
            {"id":99,"result":{"rateLimits":{"primary":{"usedPercent":99}}}}
            {"id":2,"result":{"rateLimits":{"primary":{"usedPercent":27}}}}
            """);
        using var input = new StringWriter();
        var protocol = new AppServerProtocol(output, input);
        await protocol.InitializeAsync(CancellationToken.None);
        var response = await protocol.ReadRateLimitsAsync(CancellationToken.None);

        Assert.Equal(27, response.RateLimits.Primary!.UsedPercent);
        var sent = input.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(4, sent.Length);
        using var initialize = JsonDocument.Parse(sent[0]);
        Assert.Equal("initialize", initialize.RootElement.GetProperty("method").GetString());
        Assert.Equal("codex_usage_monitor", initialize.RootElement.GetProperty("params").GetProperty("clientInfo").GetProperty("name").GetString());
        Assert.Contains("initialized", sent[1]);
        Assert.Contains("account/rateLimits/read", sent[2]);
        Assert.Contains("-32601", sent[3]);
    }

    [Fact]
    public async Task RpcErrorsIncludeTheServerMessage()
    {
        using var output = new StringReader("""{"id":1,"error":{"code":-32000,"message":"Sign in with ChatGPT"}}""");
        using var input = new StringWriter();
        var protocol = new AppServerProtocol(output, input);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => protocol.ReadRateLimitsAsync(CancellationToken.None));
        Assert.Contains("Sign in with ChatGPT", error.Message);
    }

    [Fact]
    public async Task DisconnectIsReportedWithoutWaitingForTimeout()
    {
        using var output = new StringReader("");
        using var input = new StringWriter();
        var protocol = new AppServerProtocol(output, input);
        await Assert.ThrowsAsync<IOException>(() => protocol.InitializeAsync(CancellationToken.None));
    }
}
