using System.CommandLine;
using TaskbarUtil.Core;

namespace TaskbarUtil.Commands;

public static class MoveCommand
{
    public static Command Create(Option<bool> dryRunOption)
    {
        var appArg = new Argument<string>("app", "App name to move");
        var positionOption = new Option<int?>("--position", "Move to position (1-based index)");
        positionOption.AddAlias("-p");
        var beforeOption = new Option<string?>("--before", "Move before this app");
        var afterOption = new Option<string?>("--after", "Move after this app");

        var command = new Command("move", "Move a pinned app to a new position")
        {
            appArg,
            positionOption,
            beforeOption,
            afterOption
        };

        command.SetHandler((app, position, before, after, dryRun) =>
        {
            var layout = LayoutXmlParser.TryLoadFromFile(EnvironmentInfo.ConfigFilePath);
            if (layout == null || layout.Pins.Count == 0)
            {
                Console.Error.WriteLine("No layout config found.");
                Environment.ExitCode = 1;
                return;
            }

            var pin = layout.FindPin(app);
            if (pin == null)
            {
                Console.Error.WriteLine($"'{app}' not found in layout config.");
                Environment.ExitCode = 3;
                return;
            }

            int newIndex;
            if (position.HasValue)
            {
                newIndex = position.Value - 1;
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
                newIndex = layout.Pins.IndexOf(target);
                // If moving from before the target, the index shifts after removal
                if (layout.Pins.IndexOf(pin) < newIndex) newIndex--;
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
                newIndex = layout.Pins.IndexOf(target) + 1;
                if (layout.Pins.IndexOf(pin) < newIndex) newIndex--;
            }
            else
            {
                Console.Error.WriteLine("Specify --position, --before, or --after.");
                Environment.ExitCode = 1;
                return;
            }

            if (dryRun)
            {
                Console.WriteLine($"[dry-run] Would move '{pin.DisplayName}' to position {newIndex + 1}");
                return;
            }

            layout.MovePin(app, newIndex);

            var xml = LayoutXmlGenerator.Generate(layout);
            File.WriteAllText(EnvironmentInfo.ConfigFilePath, xml);

            Console.WriteLine($"Moved '{pin.DisplayName}' to position {newIndex + 1}.");
            Console.WriteLine($"Run 'taskbarutil apply' to deploy the configuration.");

        }, appArg, positionOption, beforeOption, afterOption, dryRunOption);

        return command;
    }
}
