using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace TaskbarUtil.Core;

public static class ExplorerHelper
{
    [DllImport("user32.dll", SetLastError = true)]
    static extern IntPtr FindWindow(string lpClassName, string? lpWindowName);

    static readonly string Start2BinPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        @"Packages\Microsoft.Windows.StartMenuExperienceHost_cw5n1h2txyewy\LocalState\start2.bin");

    const string TaskbandKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Taskband";

    static readonly TimeSpan ShellWait = TimeSpan.FromSeconds(15);

    /// <summary>
    /// Live-apply taskbar policy by clearing all caches and restarting the shell.
    /// Clears three caches: start2.bin (pin data), Taskband registry (pin order),
    /// then kills shell processes and explorer for a full rebuild from policy XML.
    /// </summary>
    public static void RestartExplorer(bool verbose = false)
    {
        // Step 1: Kill shell processes (releases locks on start2.bin)
        KillProcess("StartMenuExperienceHost", verbose);
        KillProcess("ShellExperienceHost", verbose);
        Thread.Sleep(2000);

        // Step 2: Delete start2.bin (pin data cache)
        DeleteStart2Bin(verbose);

        // Step 3: Delete Taskband registry key (pin order cache)
        DeleteTaskband(verbose);

        // Step 4: Kill explorer
        if (verbose)
            Console.Error.WriteLine("  [explorer] Stopping explorer.exe...");

        foreach (var proc in Process.GetProcessesByName("explorer"))
        {
            try
            {
                proc.Kill();
                proc.WaitForExit(3000);
            }
            catch { }
        }

        // Step 5: Get the shell back.
        //
        // Windows relaunches the shell on its own only when Winlogon's
        // AutoRestartShell watchdog is armed and owns the process we just killed.
        // Neither holds reliably when this runs from a login script: the shell may
        // still be coming up, the watchdog may not be watching that instance, and
        // policy can disable the restart outright. When the relaunch does not
        // happen the session is left with a desktop, a cursor and no shell at all,
        // which is unrecoverable without a reboot.
        //
        // So waiting is not enough. If the taskbar does not come back on its own,
        // start explorer.exe here and wait again. A taskbar that is missing pins is
        // a cosmetic problem; a session with no shell is not.
        if (WaitForShell(ShellWait))
        {
            if (verbose)
                Console.Error.WriteLine("  [explorer] Taskbar is back");
            return;
        }

        if (verbose)
            Console.Error.WriteLine("  [explorer] Taskbar did not reappear - starting explorer.exe");
        Log.Warn($"explorer: taskbar did not reappear within {ShellWait.TotalSeconds:0} seconds; starting explorer.exe");

        if (!StartExplorer(verbose))
            return;

        if (WaitForShell(ShellWait))
        {
            if (verbose)
                Console.Error.WriteLine("  [explorer] Taskbar is back");
            Log.Info("explorer: taskbar came back after starting explorer.exe");
            return;
        }

        if (verbose)
            Console.Error.WriteLine("  [explorer] Warning: Taskbar still missing after starting explorer.exe");
        Log.Error($"explorer: taskbar still missing {ShellWait.TotalSeconds:0} seconds after starting explorer.exe");
    }

    /// <summary>
    /// Waits for the taskbar window to exist, polling twice a second.
    /// Returns false if it has not appeared within <paramref name="timeout"/>.
    /// </summary>
    static bool WaitForShell(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        do
        {
            Thread.Sleep(500);
            if (FindWindow("Shell_TrayWnd", null) != IntPtr.Zero)
                return true;
        }
        while (DateTime.UtcNow < deadline);

        return false;
    }

    /// <summary>
    /// Launches explorer.exe in this user's session. Used only as the recovery path
    /// when Windows has not relaunched the shell itself.
    /// </summary>
    static bool StartExplorer(bool verbose)
    {
        var explorerPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");

        try
        {
            Process.Start(new ProcessStartInfo(explorerPath) { UseShellExecute = false });
            return true;
        }
        catch (Exception ex)
        {
            if (verbose)
                Console.Error.WriteLine($"  [explorer] Could not start explorer.exe: {ex.Message}");
            Log.Error($"explorer: could not start {explorerPath}", ex);
            return false;
        }
    }

    static void DeleteStart2Bin(bool verbose)
    {
        try
        {
            if (File.Exists(Start2BinPath))
            {
                File.Delete(Start2BinPath);
                if (verbose)
                    Console.Error.WriteLine($"  [explorer] Deleted start2.bin");
            }
        }
        catch (Exception ex)
        {
            if (verbose)
                Console.Error.WriteLine($"  [explorer] Could not delete start2.bin: {ex.Message}");
        }
    }

    static void DeleteTaskband(bool verbose)
    {
        try
        {
            Registry.CurrentUser.DeleteSubKeyTree(TaskbandKeyPath, throwOnMissingSubKey: false);
            if (verbose)
                Console.Error.WriteLine("  [explorer] Cleared Taskband registry");
        }
        catch (Exception ex)
        {
            if (verbose)
                Console.Error.WriteLine($"  [explorer] Could not clear Taskband: {ex.Message}");
        }
    }

    static void KillProcess(string name, bool verbose)
    {
        foreach (var proc in Process.GetProcessesByName(name))
        {
            try
            {
                if (verbose)
                    Console.Error.WriteLine($"  [explorer] Killing {name} (PID {proc.Id})...");
                proc.Kill();
                proc.WaitForExit(3000);
            }
            catch { }
        }
    }
}
