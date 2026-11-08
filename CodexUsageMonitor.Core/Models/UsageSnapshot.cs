using System.Collections.Immutable;

namespace CodexUsageMonitor.Core.Models;

// Records keep usage independent of the UI and ready for future persistence.
/// <summary>A successful usage reading and the time it was retrieved.</summary>
/// <param name="RetrievedAt">The timestamp recorded after retrieval completes.</param>
/// <param name="Usage">The rate-limit data returned by Codex.</param>
public sealed record UsageSnapshot(DateTimeOffset RetrievedAt, RateLimitsResponse Usage);

/// <summary>The account usage data returned by <c>account/rateLimits/read</c>.</summary>
/// <param name="RateLimits">The default rate-limit bucket.</param>
/// <param name="RateLimitsByLimitId">Buckets indexed by limit identifier, when provided.</param>
/// <param name="RateLimitResetCredits">Available quota-reset credits, when provided.</param>
/// <param name="AccountId">The account identifier, when provided.</param>
/// <param name="OrdinaryUsageAllowed">Whether included usage is allowed, or <see langword="null"/> if unspecified.</param>
public sealed record RateLimitsResponse(
    RateLimitBucket RateLimits,
    ImmutableDictionary<string, RateLimitBucket>? RateLimitsByLimitId = null,
    ResetCreditsSummary? RateLimitResetCredits = null,
    string? AccountId = null,
    bool? OrdinaryUsageAllowed = null)
{
    /// <summary>Enumerates named buckets, falling back to the default bucket when no map is available.</summary>
    /// <returns>The Codex bucket first, followed by other buckets ordered by identifier.</returns>
    public IEnumerable<KeyValuePair<string, RateLimitBucket>> GetBuckets()
    {
        if (this.RateLimitsByLimitId is { Count: > 0 })
        {
            return this.RateLimitsByLimitId.OrderBy(pair => pair.Key == "codex" ? 0 : 1)
                .ThenBy(pair => pair.Key, StringComparer.Ordinal);
        }

        return [new(this.RateLimits.LimitId ?? "codex", this.RateLimits)];
    }
}

/// <summary>Usage windows and related account limits for one metered bucket.</summary>
/// <param name="LimitId">The bucket identifier, when provided.</param>
/// <param name="LimitName">The display name, when provided.</param>
/// <param name="Primary">The primary quota window, when provided.</param>
/// <param name="Secondary">The secondary quota window, when provided.</param>
/// <param name="Credits">The credit balance information, when provided.</param>
/// <param name="PlanType">The account plan reported by Codex.</param>
/// <param name="RateLimitReachedType">The reported type of reached limit, or <see langword="null"/> if absent.</param>
/// <param name="IndividualLimit">The individual spending limit, when provided.</param>
/// <param name="SpendControlReached">Whether the spending limit has been reached, if known.</param>
/// <param name="NormalModelSlug">The normal model identifier reported by Codex, when provided.</param>
public sealed record RateLimitBucket(
    string? LimitId = null,
    string? LimitName = null,
    RateLimitWindow? Primary = null,
    RateLimitWindow? Secondary = null,
    CreditsSnapshot? Credits = null,
    string? PlanType = null,
    string? RateLimitReachedType = null,
    SpendControlLimit? IndividualLimit = null,
    bool? SpendControlReached = null,
    string? NormalModelSlug = null);

/// <summary>Reported usage for a quota window.</summary>
/// <param name="UsedPercent">The percentage consumed, as reported by the server.</param>
/// <param name="WindowDurationMins">The window duration in minutes, if known.</param>
/// <param name="ResetsAt">The reset timestamp in Unix seconds, if known.</param>
public sealed record RateLimitWindow(double UsedPercent, long? WindowDurationMins = null, long? ResetsAt = null);
/// <summary>Credit availability and balance reported for a bucket.</summary>
/// <param name="HasCredits">Whether credits are available.</param>
/// <param name="Unlimited">Whether the credit allowance is unlimited.</param>
/// <param name="Balance">The balance text supplied by the server, if available.</param>
public sealed record CreditsSnapshot(bool HasCredits, bool Unlimited, string? Balance = null);
/// <summary>An individual spending allowance and its consumption.</summary>
/// <param name="Limit">The spending limit text supplied by the server.</param>
/// <param name="Used">The consumed amount text supplied by the server.</param>
/// <param name="RemainingPercent">The percentage of the allowance remaining.</param>
/// <param name="ResetsAt">The reset timestamp in Unix seconds.</param>
public sealed record SpendControlLimit(string Limit, string Used, int RemainingPercent, long ResetsAt);
/// <summary>The available quota-reset count and optional credit details.</summary>
/// <param name="AvailableCount">The number of available quota resets reported by the server.</param>
/// <param name="Credits">Individual reset-credit details, when provided.</param>
public sealed record ResetCreditsSummary(long AvailableCount, ImmutableArray<ResetCredit>? Credits = null);
/// <summary>A credit that can be used to reset a quota.</summary>
/// <param name="Id">The credit identifier.</param>
/// <param name="ResetType">The server-defined reset type.</param>
/// <param name="Status">The server-defined credit status.</param>
/// <param name="GrantedAt">The grant timestamp in Unix seconds.</param>
/// <param name="ExpiresAt">The expiration timestamp in Unix seconds, when provided.</param>
/// <param name="Title">The display title, when provided.</param>
/// <param name="Description">The description, when provided.</param>
public sealed record ResetCredit(
    string Id, string ResetType, string Status, long GrantedAt,
    long? ExpiresAt = null, string? Title = null, string? Description = null);
