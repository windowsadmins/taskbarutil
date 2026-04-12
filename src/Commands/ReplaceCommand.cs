using System.CommandLine;
using TaskbarUtil.Core;
using TaskbarUtil.Models;

namespace TaskbarUtil.Commands;

public static class ReplaceCommand
{
    public static Command Create(Option<bool> verboseOption, Option<bool> dryRunOption)
    {
        var oldArg = new Argument<string>("old", "App name to replace");
        var newArg = new Argument<string>("new", "App name to replace with");
        var uwpOption = new Option<bool>("--uwp", "Treat <new> as a UWP AppUserModelID");
        var appIdOption = new Option<bool>("--app-id", "Treat <new> as a DesktopApplicationID");

        var command = new Command("replace", "Replace a pinned app with another, keeping position")
        {
            oldArg,
            newArg,
            uwpOption,
            appIdOption
        };

        command.SetHandler((oldApp, newApp, uwp, appId, verbose, dryRun) =>
        {
            var layout = LayoutXmlParser.TryLoadFromFile(EnvironmentInfo.ConfigFilePath);
            if (layout == null || layout.Pins.Count == 0)
            {
                Console.Error.WriteLine("No layout config found.");
                Environment.ExitCode = 1;
                return;
            }

            var existing = layout.FindPin(oldApp);
            if (existing == null)
            {
                Console.Error.WriteLine($"'{oldApp}' not found in layout config.");
                Environment.ExitCode = 3;
                return;
            }

            TaskbarPin replacement;
            if (uwp)
            {
                replacement = TaskbarPin.FromAppUserModelID(newApp, newApp);
            }
            else if (appId)
            {
                replacement = TaskbarPin.FromApplicationID(newApp, newApp);
            }
            else
            {
                var resolver = new AppResolver(verbose);
                var resolved = resolver.Resolve(newApp);
                if (resolved == null)
                {
                    Console.Error.WriteLine($"Could not find '{newApp}'. Try 'taskbarutil find {newApp}' to search.");
                    Environment.ExitCode = 3;
                    return;
                }
                replacement = TaskbarPin.FromResolvedApp(resolved);
            }

            if (dryRun)
            {
                Console.WriteLine($"[dry-run] Would replace '{existing.DisplayName}' with '{replacement.DisplayName}'");
                return;
            }

            layout.ReplacePin(oldApp, replacement);

            var xml = LayoutXmlGenerator.Generate(layout);
            File.WriteAllText(EnvironmentInfo.ConfigFilePath, xml);

            Console.WriteLine($"Replaced '{existing.DisplayName}' with '{replacement.DisplayName}'.");
            Console.WriteLine($"Run 'taskbarutil apply' to deploy the configuration.");

        }, oldArg, newArg, uwpOption, appIdOption, verboseOption, dryRunOption);

        return command;
    }
}
