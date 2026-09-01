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
                Log.Debug($"reset: dry run, would remove policy, delete {EnvironmentInfo.ConfigFilePath} and clear Taskband (restart={!noRestart})");
                return;
            }

            Log.Info($"reset: removing taskbar policy and layout config (restart={!noRestart})");

            // Remove policy
            PolicyManager.Reset(verbose);
            Console.WriteLine("Policy / layout files cleaned up.");
            Log.Info("reset: policy registry keys and layout files removed");

            // Delete config file
            if (File.Exists(EnvironmentInfo.ConfigFilePath))
            {
                File.Delete(EnvironmentInfo.ConfigFilePath);
                Console.WriteLine("Config file deleted.");
                Log.Info($"reset: deleted {EnvironmentInfo.ConfigFilePath}");
            }

            // Clear Taskband to force rebuild from remaining shortcuts
            if (TaskbarWriter.Restore(verbose))
                Log.Info("reset: Taskband registry cleared");
            else
                Log.Warn("reset: Taskband registry could not be fully restored");
            Console.WriteLine("Taskband registry cleared (will rebuild on explorer restart).");

            if (!noRestart)
            {
                Console.WriteLine("Restarting explorer...");
                Log.Info("reset: restarting explorer");
                ExplorerHelper.RestartExplorer(verbose);
            }

            Console.WriteLine();
            Console.WriteLine("Taskbar reset. Default pins should be restored.");
            Log.Info("reset: completed");

        }, noRestartOption, verboseOption, dryRunOption);

        return command;
    }
}
