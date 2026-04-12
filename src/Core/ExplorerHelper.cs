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

    public static void RestartExplorer(bool verbose = false)
    {
        if (verbose)
            Console.Error.WriteLine("  [explorer] Stopping explorer.exe...");

        // Try graceful shutdown first
        var hwnd = FindWindow("Shell_TrayWnd", null);
        if (hwnd != IntPtr.Zero)
        {
            PostMessage(hwnd, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
            Thread.Sleep(2000);
        }

        // Kill any remaining explorer processes
        foreach (var proc in Process.GetProcessesByName("explorer"))
        {
            try
            {
                proc.Kill();
                proc.WaitForExit(3000);
            }
            catch
            {
                // May already be exiting
            }
        }

        if (verbose)
            Console.Error.WriteLine("  [explorer] Starting explorer.exe...");

        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            UseShellExecute = true
        });

        // Wait for taskbar to reappear
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
}
