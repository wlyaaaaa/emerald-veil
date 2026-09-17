using System.Text.Json;
using EmeraldVeil.Core;
namespace EmeraldVeil.Core.Tests;

public sealed class WallpaperSelectionTests
{
    private static string Configuration(string monitor="//?/DISPLAY#MTT1337#example", string location="2") => """
    {"example-user":{"general":{"user":{"monitormap":{"@MONITOR@":{"location":@LOCATION@}}},
    "wallpaperconfig":{"selectedwallpapers":{"Monitor0":{"file":"D:/other/scene.pkg"},
    "Monitor2":{"file":"D:/selected/scene.pkg"}}}},"wproperties":{"D:/selected/scene.pkg":{
    "Monitor0":{"rate":10},"Monitor2":{"rate":200,"schemecolor":"0 1 0","volume":80}}}}}
    """.Replace("@MONITOR@", monitor, StringComparison.Ordinal).Replace("@LOCATION@", location, StringComparison.Ordinal);
    [Fact] public void ExactVddMappingWinsOverThePrimarySelection()
    {
        var selected=WallpaperSelection.Read(Configuration(),"example-user",@"\\?\DISPLAY#MTT1337#example");
        Assert.Equal("Monitor2",selected!.Location);
        Assert.Equal("D:/selected/scene.pkg",selected.File);
        using var p=JsonDocument.Parse(selected.Properties);
        Assert.Equal(200,p.RootElement.GetProperty("rate").GetInt32());
        Assert.Equal("0 1 0",p.RootElement.GetProperty("schemecolor").GetString());
        Assert.Equal(0,p.RootElement.GetProperty("volume").GetInt32());
    }
    [Fact] public void ReadDoesNotChangeTheVddOrAnyOriginalProperties()
    {
        string json=Configuration(); _=WallpaperSelection.Read(json,"example-user","//?/DISPLAY#MTT1337#example");
        Assert.Contains("\"volume\":80",json);
    }
    [Theory] [InlineData("other-user","//?/DISPLAY#MTT1337#example")]
    [InlineData("example-user","//?/DISPLAY#MTT1337#changed")]
    public void UnknownIdentityNeverGuessesAMonitor(string user,string monitor) =>
        Assert.Null(WallpaperSelection.Read(Configuration(),user,monitor));
    [Fact] public void InvalidLocationCannotTargetAnotherMonitor() =>
        Assert.Null(WallpaperSelection.Read(Configuration(location:"-1"),"example-user","//?/DISPLAY#MTT1337#example"));
    [Fact] public void MissingConfigurationIsAnExplicitStaticFallback() =>
        Assert.Null(WallpaperSelection.Read("{}","example-user","monitor"));
    [Fact] public void TruncatedConfigurationIsNotAPartialSelection() =>
        Assert.ThrowsAny<JsonException>(()=>WallpaperSelection.Read("{","example-user","monitor"));
}