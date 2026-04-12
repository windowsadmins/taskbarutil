using System.CommandLine;
using TaskbarUtil.Core;

namespace TaskbarUtil.Commands;

public static class ApplyCommand
{
    public static Command Create(Option<bool> verboseOption, Option<bool> dryRunOption)
    {
        var noRestartOption = new Option<bool>("--no-restart", "Do not restart explorer after applying");

        var command = new Command("apply", "Apply the layout config via local policy")
        {
            noRestartOption
        };

        command.SetHandler((noRestart, verbose, dryRun) =>
        {
            var configPath = EnvironmentInfo.ConfigFilePath;

            if (!File.Exists(configPath))
            {
                Console.Error.WriteLine("No layout config found.");
                Console.Error.WriteLine($"Use 'taskbarutil add <app>' to build a config first.");
                Console.Error.WriteLine($"Expected path: {configPath}");
                Environment.ExitCode = 1;
                return;
            }

            var layout = LayoutXmlParser.TryLoadFromFile(configPath);
            if (layout == null || layout.Pins.Count == 0)
            {
                Console.Error.WriteLine("Layout config is empty. Add some apps first.");
                Environment.ExitCode = 1;
                return;
            }

            if (dryRun)
            {
                Console.WriteLine("[dry-run] Would apply layout config:");
                Console.WriteLine($"  Config: {configPath}");
                Console.WriteLine($"  Pins: {layout.Pins.Count}");
                foreach (var pin in layout.Pins)
                    Console.WriteLine($"    - {pin.DisplayName} ({pin.Type})");
                Console.WriteLine($"  Restart explorer: {!noRestart}");
                return;
            }

            // Step 1: Deploy layout XML via policy (registry or file copy)
            var policyResult = PolicyManager.Apply(configPath, verbose);
            Console.WriteLine($"Layout XML deployed via {policyResult.Method}.");
            if (policyResult.Message != null)
                Console.WriteLine($"  {policyResult.Message}");

            // Step 2: Write shortcuts directly to User Pinned folder for immediate effect
            Console.WriteLine("Writing taskbar shortcuts...");
            var writerOk = TaskbarWriter.Apply(layout, verbose);
            if (!writerOk)
                Console.Error.WriteLine("Warning: Shortcut write had issues. Taskbar may not update fully.");

            // Step 3: Restart explorer
            if (!noRestart)
            {
                Console.WriteLine("Restarting explorer...");
                ExplorerHelper.RestartExplorer(verbose);
            }

            Console.WriteLine();
            Console.WriteLine($"Taskbar layout applied with {layout.Pins.Count} pin(s).");

        }, noRestartOption, verboseOption, dryRunOption);

        return command;
    }
}
