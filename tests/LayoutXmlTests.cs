using Xunit;
using TaskbarUtil.Core;
using TaskbarUtil.Models;

namespace TaskbarUtil.Tests;

public class LayoutXmlTests
{
    [Fact]
    public void Generate_EmptyLayout_ProducesValidXml()
    {
        var layout = new TaskbarLayout();
        var xml = LayoutXmlGenerator.Generate(layout);

        Assert.Contains("LayoutModificationTemplate", xml);
        Assert.Contains("TaskbarPinList", xml);
        Assert.Contains("PinListPlacement=\"Replace\"", xml);
        Assert.Contains("utf-8", xml);
    }

    [Fact]
    public void Generate_DesktopAppWithLinkPath()
    {
        var layout = new TaskbarLayout();
        layout.AddPin(TaskbarPin.FromLinkPath("Chrome", @"%ProgramData%\Microsoft\Windows\Start Menu\Programs\Google Chrome.lnk"));

        var xml = LayoutXmlGenerator.Generate(layout);

        Assert.Contains("DesktopApp", xml);
        Assert.Contains("DesktopApplicationLinkPath", xml);
        Assert.Contains("Google Chrome.lnk", xml);
    }

    [Fact]
    public void Generate_DesktopAppWithApplicationID()
    {
        var layout = new TaskbarLayout();
        layout.AddPin(TaskbarPin.FromApplicationID("File Explorer", "Microsoft.Windows.Explorer"));

        var xml = LayoutXmlGenerator.Generate(layout);

        Assert.Contains("DesktopApp", xml);
        Assert.Contains("DesktopApplicationID=\"Microsoft.Windows.Explorer\"", xml);
    }

    [Fact]
    public void Generate_UWAApp()
    {
        var layout = new TaskbarLayout();
        layout.AddPin(TaskbarPin.FromAppUserModelID("Terminal", "Microsoft.WindowsTerminal_8wekyb3d8bbwe!App"));

        var xml = LayoutXmlGenerator.Generate(layout);

        Assert.Contains("<taskbar:UWA", xml);
        Assert.Contains("AppUserModelID=\"Microsoft.WindowsTerminal_8wekyb3d8bbwe!App\"", xml);
    }

    [Fact]
    public void Generate_AllowUserUnpin_AddsPinGeneration()
    {
        var layout = new TaskbarLayout { AllowUserUnpin = true };
        layout.AddPin(TaskbarPin.FromApplicationID("Explorer", "Microsoft.Windows.Explorer"));

        var xml = LayoutXmlGenerator.Generate(layout);

        Assert.Contains("PinGeneration=\"1\"", xml);
    }

    [Fact]
    public void RoundTrip_PreservesAllPins()
    {
        var layout = new TaskbarLayout();
        layout.AddPin(TaskbarPin.FromLinkPath("Chrome", @"%ProgramData%\Microsoft\Windows\Start Menu\Programs\Google Chrome.lnk"));
        layout.AddPin(TaskbarPin.FromApplicationID("File Explorer", "Microsoft.Windows.Explorer"));
        layout.AddPin(TaskbarPin.FromAppUserModelID("Terminal", "Microsoft.WindowsTerminal_8wekyb3d8bbwe!App"));

        var xml = LayoutXmlGenerator.Generate(layout);
        var parsed = LayoutXmlParser.Parse(xml);

        Assert.Equal(3, parsed.Pins.Count);
        Assert.Equal(PinType.DesktopApp, parsed.Pins[0].Type);
        Assert.Equal(@"%ProgramData%\Microsoft\Windows\Start Menu\Programs\Google Chrome.lnk", parsed.Pins[0].DesktopApplicationLinkPath);
        Assert.Equal(PinType.DesktopApp, parsed.Pins[1].Type);
        Assert.Equal("Microsoft.Windows.Explorer", parsed.Pins[1].DesktopApplicationID);
        Assert.Equal(PinType.UWA, parsed.Pins[2].Type);
        Assert.Equal("Microsoft.WindowsTerminal_8wekyb3d8bbwe!App", parsed.Pins[2].AppUserModelID);
    }

    [Fact]
    public void RoundTrip_PreservesAllowUserUnpin()
    {
        var layout = new TaskbarLayout { AllowUserUnpin = true };
        layout.AddPin(TaskbarPin.FromApplicationID("Explorer", "Microsoft.Windows.Explorer"));

        var xml = LayoutXmlGenerator.Generate(layout);
        var parsed = LayoutXmlParser.Parse(xml);

        Assert.True(parsed.AllowUserUnpin);
    }

    [Fact]
    public void Layout_AddPin_AtPosition()
    {
        var layout = new TaskbarLayout();
        layout.AddPin(TaskbarPin.FromApplicationID("First", "first"));
        layout.AddPin(TaskbarPin.FromApplicationID("Third", "third"));
        layout.AddPin(TaskbarPin.FromApplicationID("Second", "second"), 1);

        Assert.Equal("First", layout.Pins[0].DisplayName);
        Assert.Equal("Second", layout.Pins[1].DisplayName);
        Assert.Equal("Third", layout.Pins[2].DisplayName);
    }

    [Fact]
    public void Layout_RemovePin_ByName()
    {
        var layout = new TaskbarLayout();
        layout.AddPin(TaskbarPin.FromApplicationID("Explorer", "Microsoft.Windows.Explorer"));
        layout.AddPin(TaskbarPin.FromLinkPath("Chrome", "chrome.lnk"));

        Assert.True(layout.RemovePin("Explorer"));
        Assert.Single(layout.Pins);
        Assert.Equal("Chrome", layout.Pins[0].DisplayName);
    }

    [Fact]
    public void Layout_RemovePin_NotFound_ReturnsFalse()
    {
        var layout = new TaskbarLayout();
        Assert.False(layout.RemovePin("nonexistent"));
    }
}

public class KnownAppsTests
{
    [Theory]
    [InlineData("edge", "Microsoft Edge")]
    [InlineData("explorer", "File Explorer")]
    [InlineData("terminal", "Windows Terminal")]
    [InlineData("notepad", "Notepad")]
    [InlineData("calculator", "Calculator")]
    public void Search_FindsDirectlyResolvableApps(string query, string expectedName)
    {
        var results = KnownApps.Search(query);
        Assert.Contains(results, r => r.DisplayName == expectedName);
    }

    [Theory]
    [InlineData("chrome", "Google Chrome")]
    [InlineData("firefox", "Mozilla Firefox")]
    [InlineData("VS Code", "Visual Studio Code")]
    public void FindByName_FindsAppsNeedingDynamicResolution(string query, string expectedName)
    {
        var entry = KnownApps.FindByName(query);
        Assert.NotNull(entry);
        Assert.Equal(expectedName, entry.DisplayName);
    }

    [Fact]
    public void Search_NoMatch_ReturnsEmpty()
    {
        var results = KnownApps.Search("zzzznonexistentapp");
        Assert.Empty(results);
    }
}

public class TaskbarPinTests
{
    [Fact]
    public void FromResolvedApp_Desktop_WithLinkPath()
    {
        var app = new ResolvedApp("Chrome", PinType.DesktopApp, "chrome.lnk", null, null, "chrome.exe", 100);
        var pin = TaskbarPin.FromResolvedApp(app);

        Assert.Equal(PinType.DesktopApp, pin.Type);
        Assert.Equal("chrome.lnk", pin.DesktopApplicationLinkPath);
        Assert.Equal("Chrome", pin.DisplayName);
    }

    [Fact]
    public void FromResolvedApp_Desktop_WithApplicationID()
    {
        var app = new ResolvedApp("Explorer", PinType.DesktopApp, null, "Microsoft.Windows.Explorer", null, null, 100);
        var pin = TaskbarPin.FromResolvedApp(app);

        Assert.Equal(PinType.DesktopApp, pin.Type);
        Assert.Equal("Microsoft.Windows.Explorer", pin.DesktopApplicationID);
    }

    [Fact]
    public void FromResolvedApp_UWA()
    {
        var app = new ResolvedApp("Terminal", PinType.UWA, null, null, "Microsoft.WindowsTerminal_8wekyb3d8bbwe!App", null, 100);
        var pin = TaskbarPin.FromResolvedApp(app);

        Assert.Equal(PinType.UWA, pin.Type);
        Assert.Equal("Microsoft.WindowsTerminal_8wekyb3d8bbwe!App", pin.AppUserModelID);
    }
}
