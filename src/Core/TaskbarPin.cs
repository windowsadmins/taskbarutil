using TaskbarUtil.Models;

namespace TaskbarUtil.Core;

public class TaskbarPin
{
    public PinType Type { get; set; }
    public string? DesktopApplicationLinkPath { get; set; }
    public string? DesktopApplicationID { get; set; }
    public string? AppUserModelID { get; set; }
    public string DisplayName { get; set; } = "";

    public string GetXmlIdentifier()
    {
        return Type switch
        {
            PinType.DesktopApp => DesktopApplicationLinkPath ?? DesktopApplicationID ?? "",
            PinType.UWA => AppUserModelID ?? "",
            _ => ""
        };
    }

    public static TaskbarPin FromLinkPath(string displayName, string linkPath)
    {
        return new TaskbarPin
        {
            Type = PinType.DesktopApp,
            DisplayName = displayName,
            DesktopApplicationLinkPath = linkPath
        };
    }

    public static TaskbarPin FromApplicationID(string displayName, string appId)
    {
        return new TaskbarPin
        {
            Type = PinType.DesktopApp,
            DisplayName = displayName,
            DesktopApplicationID = appId
        };
    }

    public static TaskbarPin FromAppUserModelID(string displayName, string aumid)
    {
        return new TaskbarPin
        {
            Type = PinType.UWA,
            DisplayName = displayName,
            AppUserModelID = aumid
        };
    }

    public static TaskbarPin FromResolvedApp(ResolvedApp app)
    {
        if (app.PinType == PinType.UWA)
            return FromAppUserModelID(app.DisplayName, app.AppUserModelID!);

        if (app.LinkPath != null)
            return FromLinkPath(app.DisplayName, app.LinkPath);

        if (app.ApplicationID != null)
            return FromApplicationID(app.DisplayName, app.ApplicationID);

        return FromLinkPath(app.DisplayName, app.ExePath ?? "");
    }
}
