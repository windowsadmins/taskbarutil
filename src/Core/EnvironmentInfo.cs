namespace TaskbarUtil.Core;

public static class EnvironmentInfo
{
    public static string ConfigDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TaskbarUtil");

    public static string ConfigFilePath =>
        Path.Combine(ConfigDirectory, "LayoutModification.xml");

    public static string PinnedItemsDirectory =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            @"Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar");

    public static string DefaultProfileShellPath =>
        @"C:\Users\Default\AppData\Local\Microsoft\Windows\Shell";

    public static int WindowsBuild
    {
        get
        {
            try
            {
                return Environment.OSVersion.Version.Build;
            }
            catch
            {
                return 0;
            }
        }
    }

    public static bool IsWindows11 => WindowsBuild >= 22000;

    // 24H2 starts at build 26100
    public static bool Is24H2OrLater => WindowsBuild >= 26100;

    public static void EnsureConfigDirectory()
    {
        Directory.CreateDirectory(ConfigDirectory);
    }

    public static string ExpandEnvironmentPath(string path)
    {
        return Environment.ExpandEnvironmentVariables(path);
    }
}
