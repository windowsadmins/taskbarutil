using System.CommandLine;
using TaskbarUtil.Core;

namespace TaskbarUtil.Commands;

public static class RemoveCommand
{
    public static Command Create(Option<bool> dryRunOption)
    {
        var appArg = new Argument<string>("app", "App name or identifier to remove from layout config");

        var command = new Command("remove", "Remove an app from the taskbar layout config")
        {
            appArg
        };

        command.SetHandler((app, dryRun) =>
        {
            var layout = LayoutXmlParser.TryLoadFromFile(EnvironmentInfo.ConfigFilePath);
            if (layout == null || layout.Pins.Count == 0)
            {
                Console.Error.WriteLine("No layout config found. Nothing to remove.");
                Console.Error.WriteLine($"Config path: {EnvironmentInfo.ConfigFilePath}");
                Environment.ExitCode = 1;
                return;
            }

            if (dryRun)
            {
                var match = layout.Pins.FirstOrDefault(p =>
                    p.DisplayName.Contains(app, StringComparison.OrdinalIgnoreCase) ||
                    p.GetXmlIdentifier().Contains(app, StringComparison.OrdinalIgnoreCase));

                if (match != null)
                    Console.WriteLine($"[dry-run] Would remove '{match.DisplayName}' from layout config");
                else
                    Console.WriteLine($"[dry-run] No match for '{app}' in layout config");
                return;
            }

            if (layout.RemovePin(app))
            {
                var xml = LayoutXmlGenerator.Generate(layout);
                File.WriteAllText(EnvironmentInfo.ConfigFilePath, xml);
                Console.WriteLine($"Removed '{app}' from layout config.");
                Console.WriteLine($"Run 'taskbarutil apply' to deploy the configuration.");
            }
            else
            {
                Console.Error.WriteLine($"'{app}' not found in layout config.");
                Console.Error.WriteLine("Current pins:");
                foreach (var pin in layout.Pins)
                    Console.Error.WriteLine($"  - {pin.DisplayName}");
                Environment.ExitCode = 3;
            }
        }, appArg, dryRunOption);

        return command;
    }
}
