using System.Diagnostics;
using CodexUsageMonitor.Core.Models;

namespace CodexUsageMonitor.Core.Services;

/// <summary>Retrieves usage through an owned Codex app-server process over standard input and output.</summary>
/// <remarks>Reads are serialized. Failed requests stop the process so a later read can reconnect.</remarks>
public sealed class CodexRateLimitClient : IRateLimitClient
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly Func<ProcessStartInfo> startInfoFactory;
    private readonly TimeSpan requestTimeout;
    private Process? process;
    private AppServerProtocol? protocol;
    private Task? errorDrain;
    private bool disposed;

    /// <summary>Creates a client using Codex executable discovery and a 30-second request timeout.</summary>
    public CodexRateLimitClient() : this(CodexCommand.CreateStartInfo, TimeSpan.FromSeconds(30)) { }

    /// <summary>Creates a client with a custom process configuration and request timeout.</summary>
    /// <param name="startInfoFactory">Creates the process start information whenever a connection is needed.</param>
    /// <param name="requestTimeout">The timeout covering initialization and retrieval after acquiring the request gate.</param>
    internal CodexRateLimitClient(Func<ProcessStartInfo> startInfoFactory, TimeSpan requestTimeout)
    {
        this.startInfoFactory = startInfoFactory;
        this.requestTimeout = requestTimeout;
    }

    /// <summary>Reads account usage, starting and initializing the server when needed.</summary>
    /// <param name="cancellationToken">A token that cancels waiting for the request gate or retrieving usage.</param>
    /// <returns>A successful usage reading with a UTC retrieval timestamp.</returns>
    /// <exception cref="ObjectDisposedException">The client has been disposed.</exception>
    /// <exception cref="OperationCanceledException">The caller cancels the operation.</exception>
    /// <exception cref="TimeoutException">Initialization or retrieval exceeds the configured timeout.</exception>
    public async Task<UsageSnapshot> ReadAsync(CancellationToken cancellationToken = default)
    {
        await this.gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(this.disposed, this);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(this.requestTimeout);
            try
            {
                if (this.process is null || this.process.HasExited)
                {
                    await StopProcessAsync().ConfigureAwait(false);
                    this.process = Process.Start(this.startInfoFactory())
                        ?? throw new IOException("Could not start Codex app-server.");
                    this.errorDrain = DrainErrorsAsync(this.process.StandardError);
                    this.protocol = new AppServerProtocol(this.process.StandardOutput, this.process.StandardInput);
                    await this.protocol.InitializeAsync(timeout.Token).ConfigureAwait(false);
                }
                var usage = await this.protocol!.ReadRateLimitsAsync(timeout.Token).ConfigureAwait(false);
                return new UsageSnapshot(DateTimeOffset.UtcNow, usage);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                await StopProcessAsync().ConfigureAwait(false);
                throw new TimeoutException($"Codex did not respond within {this.requestTimeout.TotalSeconds:0} seconds. The monitor will retry automatically.");
            }
            catch
            {
                await StopProcessAsync().ConfigureAwait(false);
                throw;
            }
        }
        finally { this.gate.Release(); }
    }

    /// <summary>Continuously discards server error output to keep the process pipe from filling.</summary>
    /// <param name="reader">The server's standard error stream.</param>
    /// <returns>A task that completes when the stream ends or becomes unavailable.</returns>
    private static async Task DrainErrorsAsync(StreamReader reader)
    {
        // Drain to avoid blocking the server; do not accumulate private logs.
        var buffer = new char[4096];
        try
        {
            while (await reader.ReadAsync(buffer.AsMemory()).ConfigureAwait(false) != 0) { }
        }
        catch (IOException) { }
        catch (ObjectDisposedException) { }
    }

    /// <summary>Clears the connection and releases the owned server process, terminating it after a shutdown grace period.</summary>
    /// <returns>A task representing process shutdown and error-stream cleanup.</returns>
    /// <remarks>The caller must hold the request gate.</remarks>
    private async Task StopProcessAsync()
    {
        var previous = this.process;
        var drain = this.errorDrain;
        this.process = null;
        this.protocol = null;
        this.errorDrain = null;
        if (previous is null) { return; }
        try
        {
            try { previous.StandardInput.Close(); }
            catch (IOException) { }
            if (!previous.HasExited)
            {
                using var grace = new CancellationTokenSource(TimeSpan.FromSeconds(1));
                try { await previous.WaitForExitAsync(grace.Token).ConfigureAwait(false); }
                catch (OperationCanceledException)
                {
                    try { previous.Kill(entireProcessTree: true); }
                    catch (InvalidOperationException) { }
                    await previous.WaitForExitAsync().ConfigureAwait(false);
                }
            }
            if (drain is not null)
            {
                await drain.WaitAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
            }
        }
        catch (InvalidOperationException) { }
        catch (System.ComponentModel.Win32Exception) { }
        catch (TimeoutException) { }
        finally { previous.Dispose(); }
    }

    /// <summary>Waits for any active read and releases the owned server process.</summary>
    /// <returns>A task that completes after the client is marked disposed and process cleanup finishes.</returns>
    public async ValueTask DisposeAsync()
    {
        await this.gate.WaitAsync().ConfigureAwait(false);
        try
        {
            this.disposed = true;
            await StopProcessAsync().ConfigureAwait(false);
        }
        finally { this.gate.Release(); }
    }
}
