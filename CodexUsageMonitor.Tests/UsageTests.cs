using System.Collections.Immutable;
using System.Text.Json;
using CodexUsageMonitor.Core.Models;
using CodexUsageMonitor.Core.Presentation;
using CodexUsageMonitor.Core.Services;
using Xunit;

namespace CodexUsageMonitor.Tests;

public sealed class UsageTests
{
    [Fact]
    public void TooltipShowsBothWindowsWithLocalResetTimesAndCountdowns()
    {
        var utc = new DateTimeOffset(2026, 11, 6, 3, 0, 0, TimeSpan.Zero);
        var zone = TimeZoneInfo.CreateCustomTimeZone("TestJapan", TimeSpan.FromHours(9), "TestJapan", "TestJapan");
        var snapshot = new UsageSnapshot(utc, new(new(
            Primary: new(25, 300, utc.AddHours(2.5).ToUnixTimeSeconds()),
            Secondary: new(73, 10080, utc.AddDays(2).AddHours(4).ToUnixTimeSeconds()), PlanType: "pro")));

        var text = UsageTextFormatter.Tooltip(snapshot, timeZone: zone, now: utc);

        Assert.Contains("5h: 25% used | reset 11/06 14:30 (2h 30m left)", text);
        Assert.Contains("1w: 73% used | reset 11/08 16:00 (2d 4h left)", text);
        Assert.Contains("Updated 12:00", text);
        Assert.True(text.Length <= UsageTextFormatter.TooltipMaxLength);
    }

    [Theory]
    [InlineData(-60, "due")]
    [InlineData(0, "due")]
    [InlineData(30, "<1m left")]
    [InlineData(60, "1m left")]
    [InlineData(3599, "59m left")]
    [InlineData(3600, "1h left")]
    [InlineData(86400, "1d left")]
    public void ResetCountdownHandlesShortAndElapsedWindows(int secondsUntilReset, string expected)
    {
        var now = new DateTimeOffset(2026, 11, 6, 3, 0, 0, TimeSpan.Zero);
        var snapshot = new UsageSnapshot(now, new(new(
            Primary: new(25, 300, now.AddSeconds(secondsUntilReset).ToUnixTimeSeconds()))));

        Assert.Contains($"({expected})", UsageTextFormatter.Tooltip(snapshot, now: now));
        Assert.Contains($"({expected})", UsageTextFormatter.Details(snapshot, now: now));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(long.MinValue)]
    [InlineData(long.MaxValue)]
    public void UnavailableResetTimesDoNotProduceCountdowns(long? resetsAt)
    {
        var now = new DateTimeOffset(2026, 11, 6, 3, 0, 0, TimeSpan.Zero);
        var snapshot = new UsageSnapshot(now, new(new(Primary: new(25, 300, resetsAt))));

        Assert.Contains("reset unknown", UsageTextFormatter.Tooltip(snapshot, now: now));
        Assert.Contains("Resets: unknown", UsageTextFormatter.Details(snapshot, now: now));
        Assert.DoesNotContain("left)", UsageTextFormatter.Tooltip(snapshot, now: now));
        Assert.DoesNotContain("(due)", UsageTextFormatter.Details(snapshot, now: now));
    }

    [Fact]
    public void CountdownAdvancesFromCurrentTimeWhileCachedUsageStaysStale()
    {
        var retrievedAt = new DateTimeOffset(2026, 11, 6, 0, 0, 0, TimeSpan.Zero);
        var now = retrievedAt.AddHours(3);
        var zone = TimeZoneInfo.CreateCustomTimeZone("TestJapan", TimeSpan.FromHours(9), "TestJapan", "TestJapan");
        var snapshot = new UsageSnapshot(retrievedAt, new(new(
            Primary: new(25, 300, now.AddHours(2).ToUnixTimeSeconds()))));

        var tooltip = UsageTextFormatter.Tooltip(snapshot, "Offline", zone, now);
        var laterTooltip = UsageTextFormatter.Tooltip(snapshot, "Offline", zone, now.AddHours(1));
        var details = UsageTextFormatter.Details(snapshot, "Offline", zone, now: now.ToOffset(TimeSpan.FromHours(-5)));

        Assert.Contains("STALE", tooltip);
        Assert.Contains("reset 11/06 14:00 (2h left)", tooltip);
        Assert.Contains("reset 11/06 14:00 (1h left)", laterTooltip);
        Assert.Contains("Updated 09:00", laterTooltip);
        Assert.Contains("Last successful reading (stale)", details);
        Assert.Contains("Resets: 2026-11-06 14:00 +09:00 (2h left)", details);
    }

    [Fact]
    public void TooltipPreservesBothCountdownsBeforeOptionalMetadata()
    {
        var now = new DateTimeOffset(2026, 11, 6, 3, 0, 0, TimeSpan.Zero);
        var snapshot = new UsageSnapshot(now, new(new(
            Primary: new(100, 300, now.AddHours(4).AddMinutes(59).ToUnixTimeSeconds()),
            Secondary: new(99.9, 10080, now.AddDays(6).AddHours(23).ToUnixTimeSeconds()),
            PlanType: new string('x', 300), RateLimitReachedType: "rate_limit_reached")));

        var text = UsageTextFormatter.Tooltip(snapshot, timeZone: TimeZoneInfo.Utc, now: now);

        Assert.Contains("limit reached", text);
        Assert.Contains("5h: 100% used | reset 11/06 07:59 (4h 59m left)", text);
        Assert.Contains("1w: 99.9% used | reset 11/13 02:00 (6d 23h left)", text);
        Assert.DoesNotContain("…", text);
        Assert.True(text.Length <= UsageTextFormatter.TooltipMaxLength);
    }

    [Fact]
    public void DetailsIncludeCountdownsForEveryQuotaWindowAndSpendLimit()
    {
        var now = new DateTimeOffset(2026, 11, 6, 3, 0, 0, TimeSpan.Zero);
        var codex = new RateLimitBucket(
            Primary: new(25, 300, now.AddHours(2.5).ToUnixTimeSeconds()),
            Secondary: new(73, 10080, now.AddDays(2).AddHours(4).ToUnixTimeSeconds()),
            IndividualLimit: new("100", "25", 75, now.AddMinutes(10).ToUnixTimeSeconds()));
        var other = new RateLimitBucket(Primary: new(10, 60, now.AddMinutes(45).ToUnixTimeSeconds()));
        var snapshot = new UsageSnapshot(now, new(codex,
            ImmutableDictionary<string, RateLimitBucket>.Empty.Add("codex", codex).Add("other", other)));

        var text = UsageTextFormatter.Details(snapshot, timeZone: TimeZoneInfo.Utc, now: now);

        Assert.Contains("Resets: 2026-11-06 05:30 +00:00 (2h 30m left)", text);
        Assert.Contains("Resets: 2026-11-08 07:00 +00:00 (2d 4h left)", text);
        Assert.Contains("Resets: 2026-11-06 03:10 +00:00 (10m left)", text);
        Assert.Contains("Resets: 2026-11-06 03:45 +00:00 (45m left)", text);
    }

    [Fact]
    public void MultiBucketDataPrefersCodexAndKeepsOtherBucketsInDetails()
    {
        const string json = """
            {"rateLimits": {}, "rateLimitsByLimitId": {
              "other": {"limitName":"Other model", "primary":{"usedPercent":5}},
              "codex": {"primary":{"usedPercent":42,"windowDurationMins":300},"planType":"future-plan"}
            }, "rateLimitResetCredits":{"availableCount":3,"credits":null},"futureField":true}
            """;
        var usage = JsonSerializer.Deserialize<RateLimitsResponse>(json, AppServerProtocol.JsonOptions)!;
        var snapshot = new UsageSnapshot(DateTimeOffset.UtcNow, usage);

        Assert.Equal("codex", usage.GetBuckets().First().Key);
        Assert.Contains("42% used", UsageTextFormatter.Tooltip(snapshot));
        Assert.Contains("+1 buckets", UsageTextFormatter.Tooltip(snapshot));
        Assert.Contains("Other model", UsageTextFormatter.Details(snapshot));
        Assert.Contains("Available rate-limit resets: 3", UsageTextFormatter.Details(snapshot));
        Assert.Equal("future-plan", usage.GetBuckets().First().Value.PlanType);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"rateLimits\":null}")]
    [InlineData("{\"rateLimits\":{\"primary\":{}}}")]
    public void MissingRequiredDataDoesNotBecomeZeroUsage(string json)
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<RateLimitsResponse>(json, AppServerProtocol.JsonOptions));
    }

    [Fact]
    public void MissingWindowsAndInvalidResetAreReportedAsUnknown()
    {
        var missing = new UsageSnapshot(DateTimeOffset.UtcNow, new(new()));
        Assert.Contains("unavailable", UsageTextFormatter.Tooltip(missing));
        Assert.DoesNotContain("0%", UsageTextFormatter.Tooltip(missing));
        var invalid = missing with { Usage = new(new(Primary: new(0, null, long.MaxValue))) };
        Assert.Contains("Primary: 0% used | reset unknown", UsageTextFormatter.Tooltip(invalid));
    }

    [Fact]
    public void FailedRefreshMarksLastReadingStale()
    {
        var snapshot = new UsageSnapshot(DateTimeOffset.UtcNow, new(new(Primary: new(19, 60))));
        Assert.Contains("STALE", UsageTextFormatter.Tooltip(snapshot, "Offline"));
        Assert.Contains("19% used", UsageTextFormatter.Tooltip(snapshot, "Offline"));
        Assert.Contains("Offline", UsageTextFormatter.Details(snapshot, "Offline"));
        Assert.Contains("Usage unavailable", UsageTextFormatter.Tooltip(null, "Offline"));
    }

    [Fact]
    public void TooltipIsBoundedEvenForLongPlanAndUnusualValues()
    {
        var bucket = new RateLimitBucket(Primary: new(100, long.MaxValue, 0), Secondary: new(100, long.MaxValue, 0),
            PlanType: new string('x', 300), RateLimitReachedType: "rate_limit_reached");
        var snapshot = new UsageSnapshot(DateTimeOffset.UtcNow, new(bucket,
            ImmutableDictionary<string, RateLimitBucket>.Empty.Add("codex", bucket).Add("extra", bucket)));
        Assert.True(UsageTextFormatter.Tooltip(snapshot).Length <= 127);
        Assert.Contains("100% used", UsageTextFormatter.Tooltip(snapshot));
        Assert.True(UsageTextFormatter.IsLimited(snapshot.Usage));
    }

    [Fact]
    public void CreditsAndSpendLimitsArePreservedInRecordsAndDetails()
    {
        const string json = """
            {"rateLimits":{"credits":{"hasCredits":true,"unlimited":false,"balance":"12.50"},
              "individualLimit":{"limit":"100","used":"25","remainingPercent":75,"resetsAt":1800000000},
              "spendControlReached":false},"ordinaryUsageAllowed":false,
              "rateLimitResetCredits":{"availableCount":1,"credits":[
                {"id":"reset-1","resetType":"codexRateLimits","status":"available","grantedAt":1700000000,"expiresAt":null}]}}
            """;
        var snapshot = new UsageSnapshot(DateTimeOffset.UtcNow,
            JsonSerializer.Deserialize<RateLimitsResponse>(json, AppServerProtocol.JsonOptions)!);
        Assert.Contains("Credits: 12.50", UsageTextFormatter.Tooltip(snapshot));
        Assert.Contains("Individual spend: 25 / 100 (75% remaining)", UsageTextFormatter.Details(snapshot));
        Assert.Contains("Included usage allowed: No", UsageTextFormatter.Details(snapshot));
        Assert.Equal("reset-1", snapshot.Usage.RateLimitResetCredits!.Credits!.Value[0].Id);
    }
}
