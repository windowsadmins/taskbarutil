using System.CommandLine;
using TaskbarUtil.Core;

namespace TaskbarUtil.Commands;

public static class ApplyCommand
{
    public static Command Create(Option<bool> verboseOption, Option<bool> dryRunOption)
    {
        var noRestartOption = new Option<bool>("--no-restart", "Do not restart explorer after applying");
        var allHomesOption = new Option<bool>("--allhomes", "Apply to all user profiles on this machine");

        var command = new Command("apply", "Apply the layout config via local policy")
        {
            noRestartOption,
            allHomesOption
        };

        command.SetHandler((noRestart, allHomes, verbose, dryRun) =>
        {
            var configPath = EnvironmentInfo.ConfigFilePath;

            if (!File.Exists(configPath))
            {
                Console.Error.WriteLine("No layout config found.");
                Console.Error.WriteLine($"Use 'taskbarutil add <app>' to build a config first.");
                Console.Error.WriteLine($"Expected path: {configPath}");
                Log.Error($"apply: no layout config at {configPath}");
                Environment.ExitCode = 1;
                return;
            }

            var layout = LayoutXmlParser.TryLoadFromFile(configPath);
            if (layout == null || layout.Pins.Count == 0)
            {
                Console.Error.WriteLine("Layout config is empty. Add some apps first.");
                Log.Error($"apply: layout config at {configPath} is empty or unreadable");
                Environment.ExitCode = 1;
                return;
            }

            var pinSummary = string.Join(", ", layout.Pins.Select(p => p.DisplayName));

            if (dryRun)
            {
                Console.WriteLine("[dry-run] Would apply layout config:");
                Console.WriteLine($"  Config: {configPath}");
                Console.WriteLine($"  Pins: {layout.Pins.Count}");
                foreach (var pin in layout.Pins)
                    Console.WriteLine($"    - {pin.DisplayName} ({pin.Type})");
                Console.WriteLine($"  Restart explorer: {!noRestart}");
                Log.Debug($"apply: dry run, would apply {layout.Pins.Count} pin(s) [{pinSummary}] from {configPath} (allhomes={allHomes}, restart={!noRestart})");
                return;
            }

            Log.Info($"apply: applying {layout.Pins.Count} pin(s) [{pinSummary}] from {configPath} (allhomes={allHomes}, restart={!noRestart})");

            // Step 1: Deploy layout XML via policy registry keys
            if (allHomes)
            {
                var count = PolicyManager.ApplyAllHomes(configPath, verbose);
                Console.WriteLine($"Policy set for {count} user profile(s).");
                Log.Info($"apply: policy set for {count} user profile(s)");
            }
            else
            {
                var policyResult = PolicyManager.Apply(configPath, verbose);
                Console.WriteLine($"Policy set via {policyResult.Method}.");
                if (policyResult.Message != null)
                    Console.WriteLine($"  {policyResult.Message}");
                if (policyResult.Success)
                    Log.Info($"apply: policy set via {policyResult.Method} ({policyResult.TargetPath}){(policyResult.Message != null ? " - " + policyResult.Message : "")}");
                else
                    Log.Error($"apply: policy via {policyResult.Method} ({policyResult.TargetPath}) failed: {policyResult.Message}");
            }

            // Step 2: Restart explorer (deletes start2.bin cache, kills
            // StartMenuExperienceHost, then restarts explorer for live apply)
            if (!noRestart)
            {
                Console.WriteLine("Applying live...");
                Log.Info("apply: restarting explorer for live apply");
                ExplorerHelper.RestartExplorer(verbose);
            }
            else
            {
                Console.WriteLine("Policy set. Sign out and back in to apply.");
                Log.Info("apply: explorer not restarted; layout takes effect at next sign-in");
            }

            Console.WriteLine($"Taskbar layout applied with {layout.Pins.Count} pin(s).");
            Log.Info($"apply: completed with {layout.Pins.Count} pin(s)");

        }, noRestartOption, allHomesOption, verboseOption, dryRunOption);

        return command;
    }
}
