using System.Diagnostics;
using System.Runtime.InteropServices;

namespace TaskbarUtil.Core;

public static class ExplorerHelper
{
    [DllImport("user32.dll", SetLastError = true)]
    static extern IntPtr FindWindow(string lpClassName, string? lpWindowName);

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    const uint WM_QUIT = 0x0012;

    static readonly string Start2BinPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        @"Packages\Microsoft.Windows.StartMenuExperienceHost_cw5n1h2txyewy\LocalState\start2.bin");

    /// <summary>
    /// Live-apply taskbar policy: delete start2.bin cache, kill StartMenuExperienceHost,
    /// then restart explorer. This forces a full re-read of the policy XML.
    /// </summary>
    public static void RestartExplorer(bool verbose = false)
    {
        // Step 1: Delete start2.bin so explorer rebuilds from policy XML
        DeleteStart2Bin(verbose);

        // Step 2: Kill StartMenuExperienceHost first (it manages taskbar pins)
        KillProcess("StartMenuExperienceHost", verbose);
        Thread.Sleep(1000);

        // Step 3: Kill explorer
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
        for (int i = 0; i < 20; i++)
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
            Console.Error.WriteLine("  [explorer] Warning: Taskbar did not reappear within 10 seconds");
    }

    static void DeleteStart2Bin(bool verbose)
    {
        try
        {
            if (File.Exists(Start2BinPath))
            {
                File.Delete(Start2BinPath);
                if (verbose)
                    Console.Error.WriteLine($"  [explorer] Deleted {Start2BinPath}");
            }
        }
        catch (Exception ex)
        {
            if (verbose)
                Console.Error.WriteLine($"  [explorer] Could not delete start2.bin: {ex.Message}");
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
