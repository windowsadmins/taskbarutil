using System.Xml.Linq;
using TaskbarUtil.Models;

namespace TaskbarUtil.Core;

public static class LayoutXmlParser
{
    static readonly XNamespace Ns = "http://schemas.microsoft.com/Start/2014/LayoutModification";
    static readonly XNamespace NsDefault = "http://schemas.microsoft.com/Start/2014/FullDefaultLayout";
    static readonly XNamespace NsTaskbar = "http://schemas.microsoft.com/Start/2014/TaskbarLayout";

    public static TaskbarLayout Parse(string xml)
    {
        var doc = XDocument.Parse(xml);
        var layout = new TaskbarLayout();

        var collection = doc.Descendants(Ns + "CustomTaskbarLayoutCollection").FirstOrDefault();
        if (collection != null)
        {
            var pinGen = collection.Attribute("PinGeneration")?.Value;
            layout.AllowUserUnpin = pinGen == "1";
        }

        var pinList = doc.Descendants(NsTaskbar + "TaskbarPinList").FirstOrDefault();
        if (pinList == null)
            return layout;

        foreach (var element in pinList.Elements())
        {
            var pin = ParsePinElement(element);
            if (pin != null)
                layout.Pins.Add(pin);
        }

        return layout;
    }

    public static TaskbarLayout? TryLoadFromFile(string path)
    {
        if (!File.Exists(path))
            return null;

        var xml = File.ReadAllText(path);
        return Parse(xml);
    }

    static TaskbarPin? ParsePinElement(XElement element)
    {
        var localName = element.Name.LocalName;

        if (localName == "UWA")
        {
            var aumid = element.Attribute("AppUserModelID")?.Value;
            if (aumid == null) return null;
            var name = LookupFriendlyName(null, null, aumid) ?? ExtractDisplayName(aumid);
            return TaskbarPin.FromAppUserModelID(name, aumid);
        }

        if (localName == "DesktopApp")
        {
            var linkPath = element.Attribute("DesktopApplicationLinkPath")?.Value;
            if (linkPath != null)
            {
                var name = LookupFriendlyName(linkPath, null, null) ?? ExtractDisplayNameFromPath(linkPath);
                return TaskbarPin.FromLinkPath(name, linkPath);
            }

            var appId = element.Attribute("DesktopApplicationID")?.Value;
            if (appId != null)
            {
                var name = LookupFriendlyName(null, appId, null) ?? ExtractDisplayName(appId);
                return TaskbarPin.FromApplicationID(name, appId);
            }
        }

        return null;
    }

    static string? LookupFriendlyName(string? linkPath, string? appId, string? aumid)
    {
        // Try to match against KnownApps for friendly display names
        var known = KnownApps.Search(linkPath ?? appId ?? aumid ?? "");
        var match = known.FirstOrDefault(a =>
            (linkPath != null && a.LinkPath != null &&
             a.LinkPath.Equals(linkPath, StringComparison.OrdinalIgnoreCase)) ||
            (appId != null && a.ApplicationID != null &&
             a.ApplicationID.Equals(appId, StringComparison.OrdinalIgnoreCase)) ||
            (aumid != null && a.AppUserModelID != null &&
             a.AppUserModelID.Equals(aumid, StringComparison.OrdinalIgnoreCase)));
        return match?.DisplayName;
    }

    static string ExtractDisplayNameFromPath(string path)
    {
        var fileName = Path.GetFileNameWithoutExtension(path);
        return string.IsNullOrEmpty(fileName) ? path : fileName;
    }

    static string ExtractDisplayName(string id)
    {
        // e.g. "Microsoft.WindowsTerminal_8wekyb3d8bbwe!App" -> "WindowsTerminal"
        var name = id;
        if (name.Contains('.'))
            name = name.Split('.').Last();
        if (name.Contains('_'))
            name = name.Split('_').First();
        if (name.Contains('!'))
            name = name.Split('!').First();
        return name;
    }
}
