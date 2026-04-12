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
        var beforeOption = new Option<string?>("--before", "Insert before this app");
        var afterOption = new Option<string?>("--after", "Insert after this app");
        var uwpOption = new Option<bool>("--uwp", "Treat <app> as a UWP AppUserModelID");
        var appIdOption = new Option<bool>("--app-id", "Treat <app> as a DesktopApplicationID");

        var command = new Command("add", "Add an app to the taskbar layout config")
        {
            appArg,
            positionOption,
            beforeOption,
            afterOption,
            uwpOption,
            appIdOption
        };

        command.SetHandler((app, position, before, after, uwp, appId, verbose, dryRun) =>
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

            // Resolve insertion index from --position, --before, or --after
            int? idx = null;
            if (position.HasValue)
            {
                idx = position.Value - 1;
            }
            else if (before != null)
            {
                var target = layout.FindPin(before);
                if (target == null)
                {
                    Console.Error.WriteLine($"Cannot find '{before}' in layout config for --before.");
                    Environment.ExitCode = 3;
                    return;
                }
                idx = layout.Pins.IndexOf(target);
            }
            else if (after != null)
            {
                var target = layout.FindPin(after);
                if (target == null)
                {
                    Console.Error.WriteLine($"Cannot find '{after}' in layout config for --after.");
                    Environment.ExitCode = 3;
                    return;
                }
                idx = layout.Pins.IndexOf(target) + 1;
            }

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

        }, appArg, positionOption, beforeOption, afterOption, uwpOption, appIdOption, verboseOption, dryRunOption);

        return command;
    }
}
