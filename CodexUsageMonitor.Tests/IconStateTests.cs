using System.Collections.Immutable;
using CodexUsageMonitor.Core.Configuration;
using CodexUsageMonitor.Core.Models;
using CodexUsageMonitor.Core.Presentation;
using Xunit;

namespace CodexUsageMonitor.Tests;

public sealed class IconStateTests
{
    private static readonly IconUsageThresholds Colors = IconUsageThresholds.CreateOrDefault(50, 80, 100);

    [Theory]
    [InlineData(0, UsageIconState.Normal)]
    [InlineData(49.9, UsageIconState.Normal)]
    [InlineData(50, UsageIconState.Elevated)]
    [InlineData(79.9, UsageIconState.Elevated)]
    [InlineData(80, UsageIconState.High)]
    [InlineData(99.9, UsageIconState.High)]
    [InlineData(100, UsageIconState.Critical)]
    [InlineData(110, UsageIconState.Critical)]
    public void ConfiguredThresholdsDefineColorBoundaries(double percent, UsageIconState expected)
    {
        Assert.Equal(expected, UsageIconClassifier.Classify(Snapshot(new(Primary: new(percent))), Colors));
    }

    [Fact]
    public void HighestUsageAcrossAllBucketsAndWindowsDeterminesColor()
    {
        var buckets = ImmutableDictionary<string, RateLimitBucket>.Empty
            .Add("codex", new(Primary: new(10), Secondary: new(20)))
            .Add("other", new(Primary: new(25), Secondary: new(92)));
        var snapshot = new UsageSnapshot(DateTimeOffset.UtcNow, new(new(), buckets));
        Assert.Equal(UsageIconState.High, UsageIconClassifier.Classify(snapshot, Colors));
    }

    [Fact]
    public void MissingAndInvalidUsageNeverLooksLikeLowUsage()
    {
        Assert.Equal(UsageIconState.Unknown, UsageIconClassifier.Classify(null, Colors));
        Assert.Equal(UsageIconState.Unknown, UsageIconClassifier.Classify(Snapshot(new()), Colors));
        Assert.Equal(UsageIconState.Unknown, UsageIconClassifier.Classify(
            Snapshot(new(Primary: new(double.NaN), Secondary: new(-1))), Colors));
        Assert.Equal(UsageIconState.Elevated, UsageIconClassifier.Classify(
            Snapshot(new(Primary: new(double.PositiveInfinity), Secondary: new(50))), Colors));
    }

    [Fact]
    public void FailedRefreshUsesErrorIconEvenWithCachedUsage()
    {
        Assert.Equal(UsageIconState.Error, UsageIconClassifier.Classify(null, Colors, hasError: true));
        Assert.Equal(UsageIconState.Error, UsageIconClassifier.Classify(
            Snapshot(new(Primary: new(100))), Colors, hasError: true));
    }

    [Fact]
    public void BackendLimitsOverrideLowOrMissingPercentages()
    {
        Assert.Equal(UsageIconState.Critical, UsageIconClassifier.Classify(
            Snapshot(new(Primary: new(10), RateLimitReachedType: "rate_limit_reached")), Colors));
        Assert.Equal(UsageIconState.Critical, UsageIconClassifier.Classify(
            Snapshot(new(SpendControlReached: true)), Colors));
        Assert.Equal(UsageIconState.Critical, UsageIconClassifier.Classify(
            new(DateTimeOffset.UtcNow, new(new(), OrdinaryUsageAllowed: false)), Colors));
    }

    [Theory]
    [InlineData(79, UsageIconState.Normal)]
    [InlineData(80, UsageIconState.Elevated)]
    [InlineData(90, UsageIconState.High)]
    [InlineData(100, UsageIconState.Critical)]
    public void DefaultColorThresholdsDefineAllThreeTransitions(double percent, UsageIconState expected)
    {
        Assert.Equal(expected, UsageIconClassifier.Classify(Snapshot(new(Primary: new(percent))), IconUsageThresholds.Default));
    }

    [Theory]
    [InlineData(94.9, UsageIconState.High)]
    [InlineData(95, UsageIconState.Critical)]
    [InlineData(100, UsageIconState.Critical)]
    [InlineData(110, UsageIconState.Critical)]
    public void CriticalThresholdCanBeBelowFullUsage(double percent, UsageIconState expected)
    {
        var colors = IconUsageThresholds.CreateOrDefault(50, 80, 95);
        Assert.Equal(expected, UsageIconClassifier.Classify(Snapshot(new(Primary: new(percent))), colors));
    }

    [Theory]
    [InlineData(0, 100)]
    [InlineData(25, 75)]
    [InlineData(50, 50)]
    [InlineData(80, 20)]
    [InlineData(100, 0)]
    [InlineData(110, 0)]
    [InlineData(33.3, 66.7)]
    public void BothHalvesShowRemainingRatherThanUsedPercentage(double used, double remaining)
    {
        var appearance = UsageIconClassifier.Describe(Snapshot(new(Primary: new(used), Secondary: new(used))), Colors);
        Assert.NotNull(appearance.Primary.RemainingPercent);
        Assert.NotNull(appearance.Secondary.RemainingPercent);
        Assert.Equal(remaining, appearance.Primary.RemainingPercent.Value, precision: 6);
        Assert.Equal(remaining, appearance.Secondary.RemainingPercent.Value, precision: 6);
    }

    [Fact]
    public void EachHalfUsesLowestRemainingQuotaForItsWindowAcrossBuckets()
    {
        var buckets = ImmutableDictionary<string, RateLimitBucket>.Empty
            .Add("codex", new(Primary: new(10), Secondary: new(20)))
            .Add("other", new(Primary: new(25), Secondary: new(92)));
        var snapshot = new UsageSnapshot(DateTimeOffset.UtcNow, new(new(), buckets));
        var appearance = UsageIconClassifier.Describe(snapshot, Colors);
        Assert.Equal(new UsageWindowAppearance(UsageIconState.Normal, 75), appearance.Primary);
        Assert.Equal(new UsageWindowAppearance(UsageIconState.High, 8), appearance.Secondary);
    }

    [Theory]
    [InlineData(10, 92, UsageIconState.Normal, UsageIconState.High)]
    [InlineData(92, 10, UsageIconState.High, UsageIconState.Normal)]
    [InlineData(50, 100, UsageIconState.Elevated, UsageIconState.Critical)]
    [InlineData(100, 0, UsageIconState.Critical, UsageIconState.Normal)]
    public void PrimaryAndSecondaryKeepIndependentPercentagesAndColors(double primary, double secondary,
        UsageIconState primaryState, UsageIconState secondaryState)
    {
        var appearance = UsageIconClassifier.Describe(Snapshot(new(
            Primary: new(primary, 300), Secondary: new(secondary, 10080))), Colors);

        Assert.Equal(new UsageWindowAppearance(primaryState, 100 - primary), appearance.Primary);
        Assert.Equal(new UsageWindowAppearance(secondaryState, 100 - secondary), appearance.Secondary);
    }

    [Fact]
    public void UnknownOrStaleReadingsDoNotBecomeEmptyOrFullPies()
    {
        var appearances = new[]
        {
            UsageIconClassifier.Describe(null, Colors),
            UsageIconClassifier.Describe(Snapshot(new()), Colors),
            UsageIconClassifier.Describe(Snapshot(new(Primary: new(double.NaN))), Colors),
            UsageIconClassifier.Describe(Snapshot(new(Primary: new(25), Secondary: new(80))), Colors, hasError: true)
        };
        Assert.All(appearances, appearance =>
        {
            Assert.Null(appearance.Primary.RemainingPercent);
            Assert.Null(appearance.Secondary.RemainingPercent);
        });
    }

    [Fact]
    public void MissingOrInvalidWindowsDoNotBorrowTheOtherHalfsData()
    {
        var primaryOnly = UsageIconClassifier.Describe(Snapshot(new(Primary: new(25, 300))), Colors);
        Assert.Equal(new UsageWindowAppearance(UsageIconState.Normal, 75), primaryOnly.Primary);
        Assert.Equal(new UsageWindowAppearance(UsageIconState.Unknown, null), primaryOnly.Secondary);

        foreach (var primary in new RateLimitWindow?[] { null, new(double.NaN), new(double.PositiveInfinity), new(-1) })
        {
            var secondaryOnly = UsageIconClassifier.Describe(Snapshot(new(Primary: primary, Secondary: new(80, 10080))), Colors);
            Assert.Equal(new UsageWindowAppearance(UsageIconState.Unknown, null), secondaryOnly.Primary);
            Assert.Equal(new UsageWindowAppearance(UsageIconState.High, 20), secondaryOnly.Secondary);
        }
    }

    [Fact]
    public void BackendLimitChangesColorWithoutInventingQuotaPercentages()
    {
        var known = UsageIconClassifier.Describe(
            Snapshot(new(Primary: new(25), Secondary: new(80), SpendControlReached: true)), Colors);
        Assert.Equal(new UsageWindowAppearance(UsageIconState.Critical, 75), known.Primary);
        Assert.Equal(new UsageWindowAppearance(UsageIconState.Critical, 20), known.Secondary);
        var unknown = UsageIconClassifier.Describe(Snapshot(new(SpendControlReached: true)), Colors);
        Assert.Equal(new UsageWindowAppearance(UsageIconState.Critical, null), unknown.Primary);
        Assert.Equal(new UsageWindowAppearance(UsageIconState.Critical, null), unknown.Secondary);
    }

    [Fact]
    public void PercentageChangesUpdateAppearanceEvenWithinOneColorBand()
    {
        var first = UsageIconClassifier.Describe(Snapshot(new(Primary: new(10))), Colors);
        var second = UsageIconClassifier.Describe(Snapshot(new(Primary: new(25))), Colors);
        Assert.Equal(first.State, second.State);
        Assert.NotEqual(first, second);
        Assert.Equal(second, UsageIconClassifier.Describe(Snapshot(new(Primary: new(25))), Colors));
    }

    [Fact]
    public void SecondaryChangesUpdateAppearanceEvenWhenPrimaryUsageIsHigher()
    {
        var first = UsageIconClassifier.Describe(Snapshot(new(Primary: new(92), Secondary: new(10))), Colors);
        var second = UsageIconClassifier.Describe(Snapshot(new(Primary: new(92), Secondary: new(25))), Colors);

        Assert.Equal(first.Primary, second.Primary);
        Assert.Equal(first.State, second.State);
        Assert.NotEqual(first, second);
        Assert.Equal(75, second.Secondary.RemainingPercent);
        Assert.Equal(second, UsageIconClassifier.Describe(Snapshot(new(Primary: new(92), Secondary: new(25))), Colors));
    }

    private static UsageSnapshot Snapshot(RateLimitBucket bucket) => new(DateTimeOffset.UtcNow, new(bucket));
}
