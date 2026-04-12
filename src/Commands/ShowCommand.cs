using System.CommandLine;
using TaskbarUtil.Core;

namespace TaskbarUtil.Commands;

public static class ShowCommand
{
    public static Command Create()
    {
        var command = new Command("show", "Display the current layout config (XML)");

        command.SetHandler(() =>
        {
            var path = EnvironmentInfo.ConfigFilePath;

            if (!File.Exists(path))
            {
                Console.WriteLine("No layout config exists yet.");
                Console.WriteLine($"Use 'taskbarutil add <app>' to start building one.");
                Console.WriteLine($"Config path: {path}");
                return;
            }

            var layout = LayoutXmlParser.TryLoadFromFile(path);
            if (layout == null || layout.Pins.Count == 0)
            {
                Console.WriteLine("Layout config is empty.");
                return;
            }

            Console.WriteLine($"# Layout config: {path}");
            Console.WriteLine($"# Pins: {layout.Pins.Count}");
            Console.WriteLine($"# AllowUserUnpin: {layout.AllowUserUnpin}");
            Console.WriteLine();

            // Show the pins summary
            for (int i = 0; i < layout.Pins.Count; i++)
            {
                var pin = layout.Pins[i];
                Console.WriteLine($"  {i + 1}. [{pin.Type}] {pin.DisplayName}");
                Console.WriteLine($"     {pin.GetXmlIdentifier()}");
            }

            Console.WriteLine();
            Console.WriteLine("--- XML ---");
            Console.WriteLine(File.ReadAllText(path));
        });

        return command;
    }
}
