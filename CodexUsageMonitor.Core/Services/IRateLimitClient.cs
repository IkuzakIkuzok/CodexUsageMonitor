using CodexUsageMonitor.Core.Models;

namespace CodexUsageMonitor.Core.Services;

/// <summary>Retrieves account usage snapshots and asynchronously releases owned resources.</summary>
public interface IRateLimitClient : IAsyncDisposable
{
    /// <summary>Retrieves the current account rate limits.</summary>
    /// <param name="cancellationToken">A token that cancels the retrieval.</param>
    /// <returns>A task containing the successful reading and its retrieval timestamp.</returns>
    Task<UsageSnapshot> ReadAsync(CancellationToken cancellationToken = default);
}
