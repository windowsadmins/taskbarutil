using System.CommandLine;
using TaskbarUtil.Core;
using TaskbarUtil.Models;

namespace TaskbarUtil.Commands;

public static class AddCommand
{
    public static Command Create(Option<bool> verboseOption, Option<bool> dryRunOption)
    {
        var appArg = new Argument<string>("app", "App name, .lnk path, .exe path, or AppUserModelID");
        var positionOption = new Option<int?>("--position", "Position (1-based index)");
        positionOption.AddAlias("-p");
        var uwpOption = new Option<bool>("--uwp", "Treat <app> as a UWP AppUserModelID");
        var appIdOption = new Option<bool>("--app-id", "Treat <app> as a DesktopApplicationID");

        var command = new Command("add", "Add an app to the taskbar layout config")
        {
            appArg,
            positionOption,
            uwpOption,
            appIdOption
        };

        command.SetHandler((app, position, uwp, appId, verbose, dryRun) =>
        {
            TaskbarPin pin;

            if (uwp)
            {
                pin = TaskbarPin.FromAppUserModelID(app, app);
            }
            else if (appId)
            {
                pin = TaskbarPin.FromApplicationID(app, app);
            }
            else
            {
                // Resolve the app name
                var resolver = new AppResolver(verbose);
                var resolved = resolver.Resolve(app);

                if (resolved == null)
                {
                    Console.Error.WriteLine($"Could not find '{app}'. Try 'taskbarutil find {app}' to search.");
                    Environment.ExitCode = 3;
                    return;
                }

                if (verbose)
                    Console.Error.WriteLine($"Resolved '{app}' -> {resolved.DisplayName} ({resolved.PinType})");

                pin = TaskbarPin.FromResolvedApp(resolved);
            }

            // Load existing layout or create new one
            var layout = LayoutXmlParser.TryLoadFromFile(EnvironmentInfo.ConfigFilePath) ?? new TaskbarLayout();

            // Check for duplicates
            var existing = layout.Pins.FirstOrDefault(p =>
                p.DisplayName.Equals(pin.DisplayName, StringComparison.OrdinalIgnoreCase) ||
                p.GetXmlIdentifier().Equals(pin.GetXmlIdentifier(), StringComparison.OrdinalIgnoreCase));

            if (existing != null)
            {
                Console.WriteLine($"'{pin.DisplayName}' is already in the layout config.");
                return;
            }

            // Add at position (convert 1-based to 0-based)
            int? idx = position.HasValue ? position.Value - 1 : null;
            layout.AddPin(pin, idx);

            if (dryRun)
            {
                Console.WriteLine($"[dry-run] Would add '{pin.DisplayName}' to layout config");
                Console.WriteLine($"  Type: {pin.Type}");
                Console.WriteLine($"  Identifier: {pin.GetXmlIdentifier()}");
                if (position.HasValue)
                    Console.WriteLine($"  Position: {position.Value}");
                return;
            }

            // Save
            EnvironmentInfo.EnsureConfigDirectory();
            var xml = LayoutXmlGenerator.Generate(layout);
            File.WriteAllText(EnvironmentInfo.ConfigFilePath, xml);

            Console.WriteLine($"Added '{pin.DisplayName}' to layout config.");
            Console.WriteLine($"Run 'taskbarutil apply' to deploy the configuration.");

        }, appArg, positionOption, uwpOption, appIdOption, verboseOption, dryRunOption);

        return command;
    }
}
