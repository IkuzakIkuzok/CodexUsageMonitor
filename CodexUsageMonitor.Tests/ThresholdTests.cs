using System.Collections.Immutable;
using CodexUsageMonitor.Core.Models;
using CodexUsageMonitor.Core.Notifications;
using CodexUsageMonitor.Core.Presentation;
using Xunit;

namespace CodexUsageMonitor.Tests;

public sealed class ThresholdTests
{
    private const long ResetTime = 1800000000;
    private const long ReadingTime = ResetTime - 60;

    [Fact]
    public void ReachingThresholdExactlyAlertsOnceForRepeatedAndHigherReadings()
    {
        var monitor = new UsageThresholdMonitor([80]);
        Assert.Empty(monitor.Observe(Snapshot(79)));
        var alert = Assert.Single(monitor.Observe(Snapshot(80)));
        Assert.Equal([80], alert.ThresholdsPercent);
        Assert.Empty(monitor.Observe(Snapshot(80)));
        Assert.Empty(monitor.Observe(Snapshot(85)));
    }

    [Fact]
    public void FirstReadingAlreadyAboveThresholdAlertsOnce()
    {
        var monitor = new UsageThresholdMonitor([80, 90, 100]);
        Assert.Equal([80, 90], Assert.Single(monitor.Observe(Snapshot(95))).ThresholdsPercent);
        Assert.Empty(monitor.Observe(Snapshot(95)));
    }

    [Fact]
    public void JumpOverMultipleThresholdsGroupsEveryNewCrossing()
    {
        var monitor = new UsageThresholdMonitor([80, 90, 100]);
        Assert.Empty(monitor.Observe(Snapshot(75)));
        Assert.Equal([80, 90], Assert.Single(monitor.Observe(Snapshot(95))).ThresholdsPercent);
        Assert.Equal([100], Assert.Single(monitor.Observe(Snapshot(105))).ThresholdsPercent);
    }

    [Fact]
    public void FallingBelowAThresholdRearmsOnlyThatThreshold()
    {
        var monitor = new UsageThresholdMonitor([80, 90]);
        monitor.Observe(Snapshot(95));
        Assert.Empty(monitor.Observe(Snapshot(85)));
        Assert.Equal([90], Assert.Single(monitor.Observe(Snapshot(95))).ThresholdsPercent);
        Assert.Empty(monitor.Observe(Snapshot(20)));
        Assert.Equal([80, 90], Assert.Single(monitor.Observe(Snapshot(95))).ThresholdsPercent);
    }

    [Fact]
    public void PrimaryAndSecondaryWindowsAreTrackedIndependently()
    {
        var monitor = new UsageThresholdMonitor([80]);
        Assert.Equal("Primary", Assert.Single(monitor.Observe(Snapshot(80, 20))).WindowName);
        Assert.Equal("Secondary", Assert.Single(monitor.Observe(Snapshot(80, 80))).WindowName);
        Assert.Empty(monitor.Observe(Snapshot(80, 80)));
    }

    [Fact]
    public void AllBucketsAreMonitoredIndependently()
    {
        var monitor = new UsageThresholdMonitor([80]);
        var buckets = ImmutableDictionary<string, RateLimitBucket>.Empty
            .Add("codex", new(Primary: new(80)))
            .Add("other", new(LimitName: "Other model", Primary: new(80)));
        var snapshot = new UsageSnapshot(DateTimeOffset.UtcNow, new(new(), buckets));
        var alerts = monitor.Observe(snapshot);
        Assert.Equal(["codex", "other"], alerts.Select(alert => alert.BucketId));
        Assert.Equal("Other model", alerts[1].BucketName);
        Assert.Empty(monitor.Observe(snapshot));
    }

    [Fact]
    public void MissingWindowsAndResetMetadataDoNotRepeatNotifications()
    {
        var monitor = new UsageThresholdMonitor([80]);
        monitor.Observe(Snapshot(90));
        Assert.Empty(monitor.Observe(Snapshot(null)));
        Assert.Empty(monitor.Observe(Snapshot(90, resetsAt: null)));
        Assert.Empty(monitor.Observe(Snapshot(90)));
    }

    [Fact]
    public void NewQuotaWindowRearmsEvenIfFirstReadingIsAlreadyHigh()
    {
        var monitor = new UsageThresholdMonitor([80]);
        monitor.Observe(Snapshot(90));
        Assert.Single(monitor.Observe(Snapshot(90, resetsAt: ResetTime + 18000, retrievedAt: ResetTime)));
        Assert.Empty(monitor.Observe(Snapshot(90, resetsAt: ResetTime + 18000, retrievedAt: ResetTime + 30)));
    }

    [Fact]
    public void ResetTimeChangesBeforeTheDeadlineDoNotRepeatAlerts()
    {
        var monitor = new UsageThresholdMonitor([50, 80]);
        Assert.Equal([50], Assert.Single(monitor.Observe(Snapshot(60))).ThresholdsPercent);

        foreach (var offset in new[] { 1, -1, 30, 0, 3600, -3600, 0 })
        {
            Assert.Empty(monitor.Observe(Snapshot(60, resetsAt: ResetTime + offset)));
        }
        Assert.Equal([80], Assert.Single(monitor.Observe(Snapshot(85))).ThresholdsPercent);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(long.MinValue)]
    [InlineData(long.MaxValue)]
    public void InvalidResetMetadataDoesNotRepeatAlertsOrLoseTheKnownDeadline(long invalid)
    {
        var monitor = new UsageThresholdMonitor([50]);
        monitor.Observe(Snapshot(60));
        Assert.Empty(monitor.Observe(Snapshot(60, resetsAt: invalid)));
        Assert.Empty(monitor.Observe(Snapshot(60)));
        Assert.Single(monitor.Observe(Snapshot(60, resetsAt: ResetTime + 18000, retrievedAt: ResetTime)));
    }

    [Fact]
    public void DurationChangesAloneDoNotRepeatAlerts()
    {
        var monitor = new UsageThresholdMonitor([50]);
        monitor.Observe(Snapshot(60));
        foreach (var duration in new long?[] { 301, 0, -1, null, 300 })
        {
            Assert.Empty(monitor.Observe(Snapshot(60, durationMins: duration)));
        }
    }

    [Fact]
    public void ExpiredResetTimesAndStaleMetadataDoNotRepeatAlerts()
    {
        var monitor = new UsageThresholdMonitor([50]);
        monitor.Observe(Snapshot(60));
        Assert.Empty(monitor.Observe(Snapshot(60, retrievedAt: ResetTime + 30)));
        Assert.Empty(monitor.Observe(Snapshot(60, resetsAt: ResetTime + 10, retrievedAt: ResetTime + 30)));
        Assert.Single(monitor.Observe(Snapshot(60, resetsAt: ResetTime + 18000, retrievedAt: ResetTime + 60)));

        // An older deadline must not replace the new deadline and rearm the same window.
        Assert.Empty(monitor.Observe(Snapshot(60, retrievedAt: ResetTime + 90)));
        Assert.Empty(monitor.Observe(Snapshot(60, resetsAt: ResetTime + 18000, retrievedAt: ResetTime + 120)));
    }

    [Fact]
    public void ResettingOneWindowDoesNotRepeatTheOtherWindowsAlert()
    {
        var monitor = new UsageThresholdMonitor([50]);
        monitor.Observe(Snapshot(60, 60));
        var next = Snapshot(60, 60, retrievedAt: ResetTime) with
        {
            Usage = new(new(Primary: new(60, 300, ResetTime + 18000),
                Secondary: new(60, 10080, ResetTime)), AccountId: "account")
        };
        Assert.Equal("Primary", Assert.Single(monitor.Observe(next)).WindowName);
        Assert.Empty(monitor.Observe(next));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void UnknownAccountIdsDoNotRearmThresholds(string? unknown)
    {
        var monitor = new UsageThresholdMonitor([50]);
        monitor.Observe(Snapshot(60));
        Assert.Empty(monitor.Observe(Snapshot(60, accountId: unknown)));
        Assert.Empty(monitor.Observe(Snapshot(60)));
    }

    [Fact]
    public void SwitchingAccountsRearmsThresholds()
    {
        var monitor = new UsageThresholdMonitor([80]);
        monitor.Observe(Snapshot(90, accountId: "first"));
        Assert.Empty(monitor.Observe(Snapshot(90, accountId: null)));
        Assert.Empty(monitor.Observe(Snapshot(90, accountId: "first")));
        Assert.Single(monitor.Observe(Snapshot(90, accountId: "second")));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(-1)]
    public void InvalidUsageCannotRearmThresholds(double invalid)
    {
        var monitor = new UsageThresholdMonitor([80]);
        monitor.Observe(Snapshot(90));
        Assert.Empty(monitor.Observe(Snapshot(invalid)));
        Assert.Empty(monitor.Observe(Snapshot(90)));
    }

    [Fact]
    public void EmptyThresholdsDisableAlerts()
    {
        Assert.Empty(new UsageThresholdMonitor([]).Observe(Snapshot(100, 100)));
    }

    [Fact]
    public void NotificationIdentifiesWindowsUsageAndCrossedThresholds()
    {
        var alerts = new UsageThresholdMonitor([80, 90, 100]).Observe(Snapshot(95, 80));
        var text = UsageTextFormatter.ThresholdNotification(alerts);
        Assert.Contains("codex Primary (5h): 95% used; reached 80%, 90%.", text);
        Assert.Contains("codex Secondary (1w): 80% used; reached 80%.", text);
        Assert.EndsWith("Click for usage details.", text);
    }

    [Fact]
    public void NotificationFitsShellLimitAndPreservesDetailsHint()
    {
        var alert = new UsageThresholdAlert("codex", new string('x', 500), "Primary", new(100, 300),
            [.. Enumerable.Range(1, 100)]);
        var text = UsageTextFormatter.ThresholdNotification([alert, alert]);
        Assert.True(text.Length <= 255);
        Assert.Contains("100% used", text);
        Assert.EndsWith("Click for usage details.", text);
    }

    private static UsageSnapshot Snapshot(double? primary, double? secondary = null,
        long? resetsAt = ResetTime, string? accountId = "account", long retrievedAt = ReadingTime,
        long? durationMins = 300) => new(DateTimeOffset.FromUnixTimeSeconds(retrievedAt),
        new(new(Primary: primary is { } p ? new(p, durationMins, resetsAt) : null,
                Secondary: secondary is { } s ? new(s, 10080, resetsAt) : null), AccountId: accountId));
}
