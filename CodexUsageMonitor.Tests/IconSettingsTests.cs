using System.Text.Json;
using CodexUsageMonitor.Core.Configuration;
using CodexUsageMonitor.Core.Models;
using CodexUsageMonitor.Core.Notifications;
using CodexUsageMonitor.Core.Presentation;
using Xunit;

namespace CodexUsageMonitor.Tests;

public sealed class IconSettingsTests
{
    [Fact]
    public void SemanticStatesLoadIndependentThresholdsAndColors()
    {
        var settings = LoadJson("""
            {"iconStates":{
              "normal":{"color":"#112233"},
              "elevated":{"thresholdPercent":25,"color":"#445566"},
              "high":{"thresholdPercent":65,"color":"#778899"},
              "critical":{"thresholdPercent":90,"color":"#aAbBcC"}},
             "notificationThresholdsPercent":[20,40,60,80,100]}
            """);

        Assert.Equal(IconUsageThresholds.CreateOrDefault(25, 65, 90), settings.IconThresholdsPercent);
        Assert.Equal("#112233", settings.IconColors.Normal.HexCode);
        Assert.Equal("#445566", settings.IconColors.Elevated.HexCode);
        Assert.Equal("#778899", settings.IconColors.High.HexCode);
        Assert.Equal("#AABBCC", settings.IconColors.Critical.HexCode);
        Assert.Equal([20, 40, 60, 80, 100], settings.NotificationThresholdsPercent);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("#123")]
    [InlineData("112233")]
    [InlineData("#11223344")]
    [InlineData("#12345G")]
    [InlineData("# 12345")]
    [InlineData("#12345 ")]
    [InlineData("red")]
    public void InvalidColorFallsBackWithoutDiscardingItsThresholdOrOtherColors(string? code)
    {
        var settings = LoadJson($$$$"""
            {"iconStates":{
              "normal":{"color":"#010203"},
              "elevated":{"thresholdPercent":50,"color":{{{{JsonSerializer.Serialize(code)}}}}},
              "high":{"thresholdPercent":80,"color":"#040506"},
              "critical":{"thresholdPercent":95,"color":"#070809"}}}
            """);

        Assert.Equal(IconStateColors.Default.Elevated, settings.IconColors.Elevated);
        Assert.Equal("#010203", settings.IconColors.Normal.HexCode);
        Assert.Equal("#040506", settings.IconColors.High.HexCode);
        Assert.Equal("#070809", settings.IconColors.Critical.HexCode);
        Assert.Equal(IconUsageThresholds.CreateOrDefault(50, 80, 95), settings.IconThresholdsPercent);
    }

    [Theory]
    [InlineData("true")]
    [InlineData("123456")]
    [InlineData("[]")]
    [InlineData("{}")]
    public void NonStringColorUsesOnlyThatStatesDefault(string color)
    {
        var settings = LoadJson($$$$"""
            {"iconStates":{"critical":{"thresholdPercent":95,"color":{{{{color}}}}}}}
            """);
        Assert.Equal(IconStateColors.Default.Critical, settings.IconColors.Critical);
        Assert.Equal(IconUsageThresholds.CreateOrDefault(80, 90, 95), settings.IconThresholdsPercent);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("101")]
    [InlineData("50.5")]
    [InlineData("\"50\"")]
    [InlineData("null")]
    [InlineData("true")]
    [InlineData("{}")]
    [InlineData("99999999999999999999")]
    public void InvalidThresholdUsesItsDefaultWithoutDiscardingColors(string threshold)
    {
        var settings = LoadJson($$$$"""
            {"iconStates":{
              "elevated":{"thresholdPercent":{{{{threshold}}}},"color":"#123456"},
              "high":{"thresholdPercent":85},"critical":{"thresholdPercent":95}}}
            """);

        Assert.Equal(IconUsageThresholds.CreateOrDefault(80, 85, 95), settings.IconThresholdsPercent);
        Assert.Equal("#123456", settings.IconColors.Elevated.HexCode);
    }

    [Theory]
    [InlineData(80, 50, 95)]
    [InlineData(50, 50, 95)]
    [InlineData(50, 80, 80)]
    [InlineData(50, 90, 80)]
    public void InvalidSeverityOrderRestoresThresholdsWithoutDiscardingOtherSettings(int elevated, int high, int critical)
    {
        var settings = LoadJson($$$$"""
            {"refreshIntervalSeconds":120,"notificationThresholdsPercent":[20,70],"iconStates":{
              "elevated":{"thresholdPercent":{{{{elevated}}}},"color":"#112233"},
              "high":{"thresholdPercent":{{{{high}}}},"color":"#445566"},
              "critical":{"thresholdPercent":{{{{critical}}}},"color":"#778899"}}}
            """);

        Assert.Equal(IconUsageThresholds.Default, settings.IconThresholdsPercent);
        Assert.Equal("#112233", settings.IconColors.Elevated.HexCode);
        Assert.Equal("#445566", settings.IconColors.High.HexCode);
        Assert.Equal("#778899", settings.IconColors.Critical.HexCode);
        Assert.Equal(120, settings.RefreshIntervalSeconds);
        Assert.Equal([20, 70], settings.NotificationThresholdsPercent);
    }

    [Fact]
    public void PartialStateSettingsUseDefaultsForMissingFields()
    {
        var settings = LoadJson("""
            {"iconStates":{"normal":{"color":"#000000"},"elevated":{"thresholdPercent":50},
                           "high":{"color":"#FFFFFF"}}}
            """);

        Assert.Equal(IconUsageThresholds.CreateOrDefault(50, 90, 100), settings.IconThresholdsPercent);
        Assert.Equal("#000000", settings.IconColors.Normal.HexCode);
        Assert.Equal("#FFFFFF", settings.IconColors.High.HexCode);
        Assert.Equal(IconStateColors.Default.Elevated, settings.IconColors.Elevated);
        Assert.Equal(IconStateColors.Default.Critical, settings.IconColors.Critical);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("false")]
    [InlineData("42")]
    [InlineData("{}")]
    public void ExplicitSemanticSectionTakesPrecedenceOverLegacyThresholds(string states)
    {
        var settings = LoadJson($$$$"""
            {"iconStates":{{{{states}}}},"iconColorThresholdsPercent":{"amber":20,"orange":40,"red":60},
             "usageThresholdsPercent":[30,50,70],"notificationThresholdsPercent":[]}
            """);

        Assert.Equal(IconUsageThresholds.Default, settings.IconThresholdsPercent);
        Assert.Equal(IconStateColors.Default, settings.IconColors);
        Assert.Empty(settings.NotificationThresholdsPercent);
    }

    [Theory]
    [InlineData("{\"iconColorThresholdsPercent\":{\"amber\":50,\"orange\":80,\"red\":95}}")]
    [InlineData("{\"usageThresholdsPercent\":[50,80,95]}")]
    public void OlderConfigurationsPreserveThresholdsAndOriginalPalette(string json)
    {
        var settings = LoadJson(json);
        Assert.Equal(IconUsageThresholds.CreateOrDefault(50, 80, 95), settings.IconThresholdsPercent);
        Assert.Equal(IconStateColors.Default, settings.IconColors);
    }

    [Fact]
    public void CustomColorsDoNotChangeClassificationOrNotificationThresholds()
    {
        var settings = LoadJson("""
            {"iconStates":{
              "normal":{"color":"#CC3030"},
              "elevated":{"thresholdPercent":50,"color":"#663399"},
              "high":{"thresholdPercent":80,"color":"#0077FF"},
              "critical":{"thresholdPercent":95,"color":"#168C4C"}},
             "notificationThresholdsPercent":[20,60,90]}
            """);
        var snapshot = new UsageSnapshot(DateTimeOffset.UtcNow, new(new(
            Primary: new(85, 300), Secondary: new(65, 10080))));
        var appearance = UsageIconClassifier.Describe(snapshot, settings.IconThresholdsPercent);

        Assert.Equal(new UsageWindowAppearance(UsageIconState.High, 15), appearance.Primary);
        Assert.Equal(new UsageWindowAppearance(UsageIconState.Elevated, 35), appearance.Secondary);
        var alerts = new UsageThresholdMonitor(settings.NotificationThresholdsPercent).Observe(snapshot);
        Assert.Equal(2, alerts.Length);
        Assert.All(alerts, alert => Assert.Equal([20, 60], alert.ThresholdsPercent));
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
