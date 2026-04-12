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

        // Wait for explorer to auto-relaunch and taskbar to reappear
        for (int i = 0; i < 30; i++)
        {
            Thread.Sleep(500);
            if (FindWindow("Shell_TrayWnd", null) != IntPtr.Zero)
            {
                if (verbose)
                    Console.Error.WriteLine("  [explorer] Taskbar is back");
                return;
            }
        }

        if (verbose)
            Console.Error.WriteLine("  [explorer] Warning: Taskbar did not reappear within 15 seconds");
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
