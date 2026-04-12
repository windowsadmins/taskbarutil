using System.Text;
using System.Xml.Linq;
using TaskbarUtil.Models;

namespace TaskbarUtil.Core;

public static class LayoutXmlGenerator
{
    static readonly XNamespace Ns = "http://schemas.microsoft.com/Start/2014/LayoutModification";
    static readonly XNamespace NsDefault = "http://schemas.microsoft.com/Start/2014/FullDefaultLayout";
    static readonly XNamespace NsStart = "http://schemas.microsoft.com/Start/2014/StartLayout";
    static readonly XNamespace NsTaskbar = "http://schemas.microsoft.com/Start/2014/TaskbarLayout";

    public static string Generate(TaskbarLayout layout)
    {
        var pinList = new XElement(NsTaskbar + "TaskbarPinList");

        foreach (var pin in layout.Pins)
        {
            pinList.Add(CreatePinElement(pin));
        }

        var collection = new XElement(Ns + "CustomTaskbarLayoutCollection",
            new XAttribute("PinListPlacement", "Replace"));

        if (layout.AllowUserUnpin)
            collection.Add(new XAttribute("PinGeneration", "1"));

        collection.Add(
            new XElement(NsDefault + "TaskbarLayout", pinList));

        var doc = new XDocument(
            new XDeclaration("1.0", "utf-8", null),
            new XElement(Ns + "LayoutModificationTemplate",
                new XAttribute("Version", "1"),
                new XAttribute(XNamespace.Xmlns + "defaultlayout", NsDefault),
                new XAttribute(XNamespace.Xmlns + "start", NsStart),
                new XAttribute(XNamespace.Xmlns + "taskbar", NsTaskbar),
                collection));

        using var writer = new Utf8StringWriter();
        doc.Save(writer);
        return writer.ToString();
    }

    sealed class Utf8StringWriter : StringWriter
    {
        public override Encoding Encoding => Encoding.UTF8;
    }

    static XElement CreatePinElement(TaskbarPin pin)
    {
        return pin.Type switch
        {
            PinType.UWA => new XElement(NsTaskbar + "UWA",
                new XAttribute("AppUserModelID", pin.AppUserModelID ?? "")),

            PinType.DesktopApp when pin.DesktopApplicationLinkPath != null =>
                new XElement(NsTaskbar + "DesktopApp",
                    new XAttribute("DesktopApplicationLinkPath", pin.DesktopApplicationLinkPath)),

            PinType.DesktopApp when pin.DesktopApplicationID != null =>
                new XElement(NsTaskbar + "DesktopApp",
                    new XAttribute("DesktopApplicationID", pin.DesktopApplicationID)),

            _ => new XElement(NsTaskbar + "DesktopApp",
                new XAttribute("DesktopApplicationLinkPath", pin.GetXmlIdentifier()))
        };
    }
}
