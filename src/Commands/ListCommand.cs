using System.CommandLine;
using TaskbarUtil.Core;

namespace TaskbarUtil.Commands;

public static class ListCommand
{
    public static Command Create()
    {
        var command = new Command("list", "List items currently pinned to the taskbar");

        command.SetHandler(() =>
        {
            // Show shortcuts-based pins
            var items = PinnedItemsReader.GetCurrentPins();

            Console.WriteLine("# Taskbar Items (from shortcuts)");
            Console.WriteLine();

            if (items.Count == 0)
                Console.WriteLine("  (none)");
            else
            {
                for (int i = 0; i < items.Count; i++)
                {
                    var item = items[i];
                    var target = item.TargetPath ?? "(UWP/Store app)";
                    Console.WriteLine($"  {i + 1}. {item.DisplayName,-30} {target}");
                }
            }

            // Show policy-applied layout if active
            if (PolicyManager.IsApplied())
            {
                var layoutPath = PolicyManager.GetAppliedLayoutPath()
                    ?? EnvironmentInfo.ConfigFilePath;
                var layout = LayoutXmlParser.TryLoadFromFile(layoutPath);

                Console.WriteLine();
                Console.WriteLine("# Policy layout (active)");
                Console.WriteLine();

                if (layout != null && layout.Pins.Count > 0)
                {
                    for (int i = 0; i < layout.Pins.Count; i++)
                    {
                        var pin = layout.Pins[i];
                        var type = pin.Type == Models.PinType.UWA ? "UWP" : "Desktop";
                        Console.WriteLine($"  {i + 1}. [{type}] {pin.DisplayName,-25} {pin.GetXmlIdentifier()}");
                    }
                }
                else
                    Console.WriteLine("  (empty or unreadable)");
            }

            Console.WriteLine();
            var total = items.Count;
            if (PolicyManager.IsApplied())
            {
                var layout = LayoutXmlParser.TryLoadFromFile(
                    PolicyManager.GetAppliedLayoutPath() ?? EnvironmentInfo.ConfigFilePath);
                if (layout != null)
                    total = layout.Pins.Count;
                Console.WriteLine($"{total} pin(s) configured via policy");
            }
            else
                Console.WriteLine($"{total} item(s) pinned");
        });

        return command;
    }
}
