using System.Diagnostics;
using CodexUsageMonitor.Core.Services;
using Xunit;

namespace CodexUsageMonitor.Tests;

public sealed class ClientTests
{
    [Fact]
    public async Task MultipleReadsReuseServerAndDrainStderr()
    {
        var starts = 0;
        await using var client = new CodexRateLimitClient(() => { starts++; return StartInfo("normal"); }, TimeSpan.FromSeconds(10));
        var results = await Task.WhenAll(client.ReadAsync(TestContext.Current.CancellationToken), client.ReadAsync(TestContext.Current.CancellationToken));
        Assert.All(results, result => Assert.Equal(38, result.Usage.RateLimits.Primary!.UsedPercent));
        Assert.Equal(1, starts);
    }

    [Theory]
    [InlineData("error")]
    [InlineData("exit")]
    public async Task FailureReconnectsOnNextRead(string mode)
    {
        var starts = 0;
        await using var client = new CodexRateLimitClient(() => StartInfo(starts++ == 0 ? mode : "normal"), TimeSpan.FromSeconds(10));
        await Assert.ThrowsAnyAsync<Exception>(() => client.ReadAsync(TestContext.Current.CancellationToken));
        Assert.Equal(38, (await client.ReadAsync(TestContext.Current.CancellationToken)).Usage.RateLimits.Primary!.UsedPercent);
        Assert.Equal(2, starts);
    }

    [Fact]
    public async Task TimedOutServerIsStoppedAndNextReadRecovers()
    {
        var starts = 0;
        var pidFile = Path.GetTempFileName();
        try
        {
            await using var client = new CodexRateLimitClient(() => StartInfo(starts++ == 0 ? "hang" : "normal", pidFile), TimeSpan.FromSeconds(3));
            await Assert.ThrowsAsync<TimeoutException>(() => client.ReadAsync(TestContext.Current.CancellationToken));
            AssertProcessExited(pidFile);
            Assert.Equal(38, (await client.ReadAsync(TestContext.Current.CancellationToken)).Usage.RateLimits.Primary!.UsedPercent);
        }
        finally { File.Delete(pidFile); }
    }

    [Fact]
    public async Task CancellationAndDisposalStopOwnedServer()
    {
        var pidFile = Path.GetTempFileName();
        try
        {
            await using var client = new CodexRateLimitClient(() => StartInfo("hang", pidFile), TimeSpan.FromSeconds(10));
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.ReadAsync(cancellation.Token));
            AssertProcessExited(pidFile);
            await client.DisposeAsync();
            await Assert.ThrowsAsync<ObjectDisposedException>(() => client.ReadAsync(TestContext.Current.CancellationToken));
        }
        finally { File.Delete(pidFile); }
    }

    [Fact]
    public async Task DisposingIdleClientStopsServer()
    {
        var pidFile = Path.GetTempFileName();
        try
        {
            await using var client = new CodexRateLimitClient(() => StartInfo("normal", pidFile), TimeSpan.FromSeconds(10));
            await client.ReadAsync(TestContext.Current.CancellationToken);
            await client.DisposeAsync();
            AssertProcessExited(pidFile);
        }
        finally { File.Delete(pidFile); }
    }

    private static ProcessStartInfo StartInfo(string mode, string? pidFile = null)
    {
        var info = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        info.ArgumentList.Add(typeof(FakeServer.Program).Assembly.Location);
        info.ArgumentList.Add(mode);
        if (pidFile is not null) { info.ArgumentList.Add(pidFile); }
        return info;
    }

    private static void AssertProcessExited(string pidFile)
    {
        var pid = int.Parse(File.ReadAllText(pidFile));
        try
        {
            using var process = Process.GetProcessById(pid);
            Assert.True(process.HasExited);
        }
        catch (ArgumentException) { /* Process no longer exists. */ }
    }
}
