using System.Text;
using CodexUsageMonitor.Core.Configuration;
using CodexUsageMonitor.Core.Models;
using CodexUsageMonitor.Core.Notifications;
using CodexUsageMonitor.Core.Presentation;
using Xunit;

namespace CodexUsageMonitor.Tests;

public sealed class SettingsTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(30)]
    [InlineData(90)]
    [InlineData(MonitorSettings.MaxRefreshIntervalSeconds)]
    public void ValidIntervalsAreConvertedToTimerMilliseconds(int seconds)
    {
        var settings = LoadJson($$"""{"refreshIntervalSeconds":{{seconds}},"futureSetting":true}""");
        Assert.Equal(seconds, settings.RefreshIntervalSeconds);
        Assert.Equal(seconds * 1000L, settings.RefreshIntervalMilliseconds);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not JSON")]
    [InlineData("{")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("42")]
    [InlineData("{}")]
    [InlineData("{\"refreshIntervalSeconds\":null}")]
    [InlineData("{\"refreshIntervalSeconds\":0}")]
    [InlineData("{\"refreshIntervalSeconds\":-1}")]
    [InlineData("{\"refreshIntervalSeconds\":1.5}")]
    [InlineData("{\"refreshIntervalSeconds\":\"90\"}")]
    [InlineData("{\"refreshIntervalSeconds\":true}")]
    [InlineData("{\"refreshIntervalSeconds\":{}}")]
    [InlineData("{\"refreshIntervalSeconds\":2147484}")]
    [InlineData("{\"refreshIntervalSeconds\":99999999999999999999}")]
    public void InvalidSettingsFallBackToOneMinute(string json)
    {
        var settings = LoadJson(json);
        Assert.Equal(60, settings.RefreshIntervalSeconds);
        Assert.Equal(60_000, settings.RefreshIntervalMilliseconds);
    }

    [Fact]
    public void MissingFileUsesDefault()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        Assert.Equal(MonitorSettings.Default, MonitorSettings.Load(path));
    }

    [Fact]
    public void UnreadableFileUsesDefault()
    {
        var path = Path.GetTempFileName();
        try
        {
            using var locked = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            Assert.Equal(MonitorSettings.Default, MonitorSettings.Load(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Utf8ByteOrderMarkIsSupported()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, """{"refreshIntervalSeconds":120}""", new UTF8Encoding(true));
            Assert.Equal(120, MonitorSettings.Load(path).RefreshIntervalSeconds);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void RetryGuidanceUsesConfiguredInterval()
    {
        Assert.Contains("every 90 seconds", UsageTextFormatter.Details(null, "Offline", refreshIntervalSeconds: 90));
        Assert.Contains("every 60 seconds", UsageTextFormatter.Details(null, "Offline"));
    }

    [Fact]
    public void ThresholdsAreSortedAndDeduplicatedWhileInvalidEntriesAreIgnored()
    {
        var settings = LoadJson("""
            {"refreshIntervalSeconds":90,
             "notificationThresholdsPercent":[100,75,50,75,0,-1,101,84.5,"80",null,true,{}]}
            """);
        Assert.Equal([50, 75, 100], settings.NotificationThresholdsPercent);
        Assert.Equal(90, settings.RefreshIntervalSeconds);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"notificationThresholdsPercent\":null}")]
    [InlineData("{\"notificationThresholdsPercent\":80}")]
    [InlineData("{\"notificationThresholdsPercent\":\"80,90\"}")]
    [InlineData("{\"notificationThresholdsPercent\":[0,101,-1,1.5,\"90\",null]}")]
    public void MissingOrInvalidThresholdListUsesDefaults(string json)
    {
        Assert.Equal([80, 90, 100], LoadJson(json).NotificationThresholdsPercent);
    }

    [Fact]
    public void EmptyThresholdListDisablesNotifications()
    {
        Assert.Empty(LoadJson("""{"notificationThresholdsPercent":[]}""").NotificationThresholdsPercent);
    }

    [Fact]
    public void InvalidIntervalDoesNotDiscardValidThresholds()
    {
        var settings = LoadJson("""{"refreshIntervalSeconds":0,"notificationThresholdsPercent":[65]}""");
        Assert.Equal(60, settings.RefreshIntervalSeconds);
        Assert.Equal([65], settings.NotificationThresholdsPercent);
    }

    [Fact]
    public void InvalidThresholdsDoNotDiscardValidInterval()
    {
        var settings = LoadJson("""{"refreshIntervalSeconds":120,"notificationThresholdsPercent":false}""");
        Assert.Equal(120, settings.RefreshIntervalSeconds);
        Assert.Equal([80, 90, 100], settings.NotificationThresholdsPercent);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("[50,80,95]")]
    [InlineData("{}")]
    [InlineData("{\"amber\":50,\"red\":95}")]
    [InlineData("{\"amber\":80,\"orange\":50,\"red\":95}")]
    [InlineData("{\"amber\":50,\"orange\":50,\"red\":95}")]
    [InlineData("{\"amber\":50,\"orange\":80,\"red\":80}")]
    [InlineData("{\"amber\":0,\"orange\":80,\"red\":95}")]
    [InlineData("{\"amber\":50,\"orange\":80,\"red\":101}")]
    [InlineData("{\"amber\":50.5,\"orange\":80,\"red\":95}")]
    [InlineData("{\"amber\":\"50\",\"orange\":80,\"red\":95}")]
    [InlineData("{\"amber\":true,\"orange\":80,\"red\":95}")]
    [InlineData("{\"amber\":50,\"orange\":80,\"red\":99999999999999999999}")]
    public void InvalidColorSettingsUseDefaultsWithoutChangingOtherSettings(string colors)
    {
        var settings = LoadJson($$"""
            {"refreshIntervalSeconds":120,"iconColorThresholdsPercent":{{colors}},
             "notificationThresholdsPercent":[20,70,95]}
            """);

        Assert.Equal(IconUsageThresholds.Default, settings.IconThresholdsPercent);
        Assert.Equal([20, 70, 95], settings.NotificationThresholdsPercent);
        Assert.Equal(120, settings.RefreshIntervalSeconds);
    }

    [Theory]
    [InlineData(1, 2, 3)]
    [InlineData(50, 80, 95)]
    [InlineData(98, 99, 100)]
    public void ThreeIncreasingColorThresholdsAreAccepted(int amber, int orange, int red)
    {
        var settings = LoadJson($$$"""
            {"iconColorThresholdsPercent":{"amber":{{{amber}}},"orange":{{{orange}}},"red":{{{red}}}}}
            """);

        Assert.Equal(amber, settings.IconThresholdsPercent.Elevated);
        Assert.Equal(orange, settings.IconThresholdsPercent.High);
        Assert.Equal(red, settings.IconThresholdsPercent.Critical);
        Assert.Equal([80, 90, 100], settings.NotificationThresholdsPercent);
    }

    [Fact]
    public void MissingColorSettingDoesNotUseNotificationThresholds()
    {
        var settings = LoadJson("""{"notificationThresholdsPercent":[20,40,60,75,90]}""");
        Assert.Equal(IconUsageThresholds.Default, settings.IconThresholdsPercent);
        Assert.Equal([20, 40, 60, 75, 90], settings.NotificationThresholdsPercent);
    }

    [Fact]
    public void NotificationListCanHaveMoreThanThreeThresholdsIndependentOfColors()
    {
        var settings = LoadJson("""
            {"iconColorThresholdsPercent":{"amber":50,"orange":80,"red":95},
             "notificationThresholdsPercent":[20,40,60,75,90]}
            """);
        var snapshot = new UsageSnapshot(DateTimeOffset.UtcNow, new(new(Primary: new(65, 300))));

        Assert.Equal(UsageIconState.Elevated, UsageIconClassifier.Classify(snapshot, settings.IconThresholdsPercent));
        var alerts = new UsageThresholdMonitor(settings.NotificationThresholdsPercent).Observe(snapshot);
        Assert.Equal([20, 40, 60], Assert.Single(alerts).ThresholdsPercent);
    }

    [Fact]
    public void DisablingNotificationsPreservesCustomColors()
    {
        var settings = LoadJson("""
            {"iconColorThresholdsPercent":{"amber":50,"orange":80,"red":95},
             "notificationThresholdsPercent":[]}
            """);
        var snapshot = new UsageSnapshot(DateTimeOffset.UtcNow, new(new(Primary: new(85, 300))));

        Assert.Equal(UsageIconState.High, UsageIconClassifier.Classify(snapshot, settings.IconThresholdsPercent));
        Assert.Empty(new UsageThresholdMonitor(settings.NotificationThresholdsPercent).Observe(snapshot));
    }

    [Fact]
    public void InvalidNotificationsDoNotDiscardCustomColors()
    {
        var settings = LoadJson("""
            {"iconColorThresholdsPercent":{"amber":50,"orange":80,"red":95},
             "notificationThresholdsPercent":false}
            """);

        Assert.Equal(IconUsageThresholds.CreateOrDefault(50, 80, 95), settings.IconThresholdsPercent);
        Assert.Equal([80, 90, 100], settings.NotificationThresholdsPercent);
    }

    [Fact]
    public void LegacySharedListPreservesNotificationsAndItsThreeColorBoundaries()
    {
        var settings = LoadJson("""{"usageThresholdsPercent":[95,80,50,90,80]}""");
        Assert.Equal([50, 80, 90, 95], settings.NotificationThresholdsPercent);
        Assert.Equal(IconUsageThresholds.CreateOrDefault(50, 80, 95), settings.IconThresholdsPercent);
    }

    [Theory]
    [InlineData("[]", new int[] { })]
    [InlineData("[50]", new[] { 50 })]
    [InlineData("[50,95]", new[] { 50, 95 })]
    public void ShortLegacyListsPreserveNotificationsAndUseDefaultColors(string thresholds, int[] expected)
    {
        var settings = LoadJson($$"""{"usageThresholdsPercent":{{thresholds}}}""");
        Assert.Equal(expected, settings.NotificationThresholdsPercent);
        Assert.Equal(IconUsageThresholds.Default, settings.IconThresholdsPercent);
    }

    [Fact]
    public void ExplicitSettingsTakePrecedenceOverLegacyValues()
    {
        var settings = LoadJson("""
            {"usageThresholdsPercent":[20,40,60],
             "iconColorThresholdsPercent":{"amber":50,"orange":80,"red":95},
             "notificationThresholdsPercent":[]}
            """);
        Assert.Equal(IconUsageThresholds.CreateOrDefault(50, 80, 95), settings.IconThresholdsPercent);
        Assert.Empty(settings.NotificationThresholdsPercent);
    }

    [Fact]
    public void InvalidExplicitSettingsUseDefaultsRatherThanLegacyValues()
    {
        var settings = LoadJson("""
            {"usageThresholdsPercent":[20,40,60],
             "iconColorThresholdsPercent":null,"notificationThresholdsPercent":null}
            """);
        Assert.Equal(IconUsageThresholds.Default, settings.IconThresholdsPercent);
        Assert.Equal([80, 90, 100], settings.NotificationThresholdsPercent);
    }

    private static MonitorSettings LoadJson(string json)
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, json);
            return MonitorSettings.Load(path);
        }
        finally { File.Delete(path); }
    }
}
