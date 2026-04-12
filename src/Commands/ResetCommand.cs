using System.CommandLine;
using TaskbarUtil.Core;

namespace TaskbarUtil.Commands;

public static class ResetCommand
{
    public static Command Create(Option<bool> verboseOption, Option<bool> dryRunOption)
    {
        var noRestartOption = new Option<bool>("--no-restart", "Do not restart explorer after resetting");

        var command = new Command("reset", "Remove policy and restore default taskbar")
        {
            noRestartOption
        };

        command.SetHandler((noRestart, verbose, dryRun) =>
        {
            if (dryRun)
            {
                Console.WriteLine("[dry-run] Would reset taskbar policy:");
                Console.WriteLine("  - Remove policy registry keys / layout XML");
                Console.WriteLine($"  - Delete config file: {EnvironmentInfo.ConfigFilePath}");
                Console.WriteLine("  - Clear Taskband Favorites (force rebuild)");
                Console.WriteLine($"  - Restart explorer: {!noRestart}");
                return;
            }

            // Remove policy
            PolicyManager.Reset(verbose);
            Console.WriteLine("Policy / layout files cleaned up.");

            // Delete config file
            if (File.Exists(EnvironmentInfo.ConfigFilePath))
            {
                File.Delete(EnvironmentInfo.ConfigFilePath);
                Console.WriteLine("Config file deleted.");
            }

            // Clear Taskband to force rebuild from remaining shortcuts
            TaskbarWriter.Restore(verbose);
            Console.WriteLine("Taskband registry cleared (will rebuild on explorer restart).");

            if (!noRestart)
            {
                Console.WriteLine("Restarting explorer...");
                ExplorerHelper.RestartExplorer(verbose);
            }

            Console.WriteLine();
            Console.WriteLine("Taskbar reset. Default pins should be restored.");

        }, noRestartOption, verboseOption, dryRunOption);

        return command;
    }
}
