using TaskbarUtil.Models;

namespace TaskbarUtil.Core;

/// <summary>
/// Built-in registry of common Windows apps with their correct pin identifiers.
/// Desktop apps use DesktopApplicationID or Start Menu .lnk paths.
/// UWP/MSIX apps use AppUserModelID (AUMID).
/// Link paths are NOT hardcoded -- they're resolved dynamically by AppResolver.
/// </summary>
public static class KnownApps
{
    static readonly List<KnownApp> Apps = new()
    {
        // --- Desktop apps with DesktopApplicationID ---
        new("File Explorer", PinType.DesktopApp, AppId: "Microsoft.Windows.Explorer"),
        new("Microsoft Edge", PinType.DesktopApp, AppId: "MSEdge"),

        // --- UWP / MSIX apps with verified AUMIDs ---
        new("Windows Terminal", PinType.UWA, Aumid: "Microsoft.WindowsTerminal_8wekyb3d8bbwe!App"),
        new("Notepad", PinType.UWA, Aumid: "Microsoft.WindowsNotepad_8wekyb3d8bbwe!App"),
        new("Calculator", PinType.UWA, Aumid: "Microsoft.WindowsCalculator_8wekyb3d8bbwe!App"),
        new("Microsoft Store", PinType.UWA, Aumid: "Microsoft.WindowsStore_8wekyb3d8bbwe!App"),
        new("Settings", PinType.UWA, Aumid: "windows.immersivecontrolpanel_cw5n1h2txyewy!microsoft.windows.immersivecontrolpanel"),
        new("Photos", PinType.UWA, Aumid: "Microsoft.Windows.Photos_8wekyb3d8bbwe!App"),
        new("Snipping Tool", PinType.UWA, Aumid: "Microsoft.ScreenSketch_8wekyb3d8bbwe!App"),
        new("Clock", PinType.UWA, Aumid: "Microsoft.WindowsAlarms_8wekyb3d8bbwe!App"),
        new("Outlook", PinType.UWA, Aumid: "Microsoft.OutlookForWindows_8wekyb3d8bbwe!Microsoft.OutlookforWindows"),
        new("Microsoft Teams", PinType.UWA, Aumid: "MSTeams_8wekyb3d8bbwe!MSTeams"),

        // --- Desktop apps resolved dynamically via Start Menu scan ---
        // These have Aliases that match common Start Menu shortcut names
        new("Google Chrome", PinType.DesktopApp, Aliases: ["Chrome"]),
        new("Mozilla Firefox", PinType.DesktopApp, Aliases: ["Firefox"]),
        new("Visual Studio Code", PinType.DesktopApp, Aliases: ["VS Code", "Code"]),
        new("Visual Studio Code - Insiders", PinType.DesktopApp, Aliases: ["Code - Insiders", "Code Insiders"]),
        new("Slack", PinType.DesktopApp, Aliases: []),
        new("Zoom", PinType.DesktopApp, Aliases: ["Zoom Workplace"]),
        new("7-Zip", PinType.DesktopApp, Aliases: ["7-Zip File Manager"]),
        new("Command Prompt", PinType.DesktopApp, Aliases: ["cmd"]),
        new("PowerShell", PinType.DesktopApp, Aliases: ["PowerShell 7", "Windows PowerShell"]),
        new("Remote Desktop", PinType.DesktopApp, Aliases: ["Remote Desktop Connection"]),
        new("Docker Desktop", PinType.DesktopApp, Aliases: ["Docker"]),
    };

    public record KnownApp(
        string DisplayName,
        PinType PinType,
        string? AppId = null,
        string? Aumid = null,
        string[]? Aliases = null
    );

    public static IReadOnlyList<KnownApp> GetAll() => Apps;

    /// <summary>
    /// Search known apps by friendly name. Returns apps that directly resolve
    /// to a pin identifier (AppID or AUMID) without needing further lookup.
    /// </summary>
    public static IReadOnlyList<ResolvedApp> Search(string query)
    {
        var q = query.Trim();
        return Apps
            .Where(a => Matches(a, q))
            .Where(a => a.AppId != null || a.Aumid != null) // Only return directly resolvable apps
            .Select(a => ToResolvedApp(a))
            .OrderByDescending(a => a.Confidence)
            .ToList();
    }

    /// <summary>
    /// Find a known app entry (including those that need dynamic resolution).
    /// </summary>
    public static KnownApp? FindByName(string query)
    {
        var q = query.Trim();
        return Apps.FirstOrDefault(a => Matches(a, q));
    }

    static bool Matches(KnownApp app, string query)
    {
        if (app.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase))
            return true;
        if (app.AppId != null && app.AppId.Contains(query, StringComparison.OrdinalIgnoreCase))
            return true;
        if (app.Aumid != null && app.Aumid.Contains(query, StringComparison.OrdinalIgnoreCase))
            return true;
        if (app.Aliases != null)
        {
            foreach (var alias in app.Aliases)
            {
                if (alias.Contains(query, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }
        return false;
    }

    static ResolvedApp ToResolvedApp(KnownApp app)
    {
        int confidence = 100;

        if (app.Aumid != null)
            return new ResolvedApp(app.DisplayName, PinType.UWA, null, null, app.Aumid, null, confidence);

        if (app.AppId != null)
            return new ResolvedApp(app.DisplayName, PinType.DesktopApp, null, app.AppId, null, null, confidence);

        // Should not happen for directly resolvable apps
        return new ResolvedApp(app.DisplayName, app.PinType, null, null, null, null, 0);
    }
}
