using System.Diagnostics;
using System.Runtime.InteropServices;
using TaskbarUtil.Models;

namespace TaskbarUtil.Core;

public class AppResolver
{
    readonly bool _verbose;

    public AppResolver(bool verbose = false)
    {
        _verbose = verbose;
    }

    public List<ResolvedApp> Search(string query)
    {
        var results = new List<ResolvedApp>();

        // 1. Check known apps first (instant, no I/O)
        var known = KnownApps.Search(query);
        results.AddRange(known);
        if (_verbose && known.Count > 0)
            Console.Error.WriteLine($"  [known] Found {known.Count} directly resolvable match(es)");

        // 2. Check if a known app needs dynamic Start Menu resolution
        var knownEntry = KnownApps.FindByName(query);
        if (knownEntry != null && knownEntry.AppId == null && knownEntry.Aumid == null)
        {
            // This known app needs a Start Menu .lnk lookup
            var lnk = FindStartMenuShortcut(knownEntry.DisplayName, knownEntry.Aliases);
            if (lnk != null)
            {
                results.Add(lnk);
                if (_verbose)
                    Console.Error.WriteLine($"  [start-menu] Resolved '{knownEntry.DisplayName}' -> {lnk.LinkPath}");
            }
        }

        // 3. Scan Start Menu shortcuts (broader search)
        var startMenu = SearchStartMenu(query);
        foreach (var app in startMenu)
        {
            if (!results.Any(r => r.DisplayName.Equals(app.DisplayName, StringComparison.OrdinalIgnoreCase)))
                results.Add(app);
        }
        if (_verbose && startMenu.Count > 0)
            Console.Error.WriteLine($"  [start-menu] Found {startMenu.Count} shortcut match(es)");

        // 4. Query AppxPackages for UWP/MSIX apps (with proper AUMID resolution)
        var appx = SearchAppxPackages(query);
        foreach (var app in appx)
        {
            if (!results.Any(r =>
                r.AppUserModelID != null &&
                r.AppUserModelID.Equals(app.AppUserModelID, StringComparison.OrdinalIgnoreCase)))
                results.Add(app);
        }
        if (_verbose && appx.Count > 0)
            Console.Error.WriteLine($"  [appx] Found {appx.Count} package match(es)");

        return results
            .OrderByDescending(r => r.Confidence)
            .ToList();
    }

    public ResolvedApp? Resolve(string query)
    {
        return Search(query).FirstOrDefault();
    }

    ResolvedApp? FindStartMenuShortcut(string displayName, string[]? aliases)
    {
        var dirs = GetStartMenuDirs();
        var searchNames = new List<string> { displayName };
        if (aliases != null)
            searchNames.AddRange(aliases);

        foreach (var dir in dirs)
        {
            if (!Directory.Exists(dir)) continue;

            foreach (var lnk in Directory.EnumerateFiles(dir, "*.lnk", SearchOption.AllDirectories))
            {
                var name = Path.GetFileNameWithoutExtension(lnk);
                foreach (var search in searchNames)
                {
                    if (name.Equals(search, StringComparison.OrdinalIgnoreCase) ||
                        name.Contains(search, StringComparison.OrdinalIgnoreCase))
                    {
                        var linkPath = ToPortableLinkPath(lnk);
                        var target = ResolveShortcutTarget(lnk);
                        return new ResolvedApp(displayName, PinType.DesktopApp, linkPath, null, null, target, 95);
                    }
                }
            }
        }

        return null;
    }

    List<ResolvedApp> SearchStartMenu(string query)
    {
        var results = new List<ResolvedApp>();
        var dirs = GetStartMenuDirs();

        foreach (var dir in dirs)
        {
            if (!Directory.Exists(dir)) continue;

            foreach (var lnk in Directory.EnumerateFiles(dir, "*.lnk", SearchOption.AllDirectories))
            {
                var name = Path.GetFileNameWithoutExtension(lnk);
                if (!name.Contains(query, StringComparison.OrdinalIgnoreCase))
                    continue;

                var target = ResolveShortcutTarget(lnk);
                var linkPath = ToPortableLinkPath(lnk);
                int confidence = name.Equals(query, StringComparison.OrdinalIgnoreCase) ? 95 : 70;

                results.Add(new ResolvedApp(name, PinType.DesktopApp, linkPath, null, null, target, confidence));
            }
        }

        return results;
    }

    List<ResolvedApp> SearchAppxPackages(string query)
    {
        var results = new List<ResolvedApp>();

        try
        {
            // Use PowerShell to get packages AND their manifest Application IDs for correct AUMIDs
            var script = $@"
                Get-AppxPackage -Name '*{EscapePowerShellString(query)}*' | ForEach-Object {{
                    $pkg = $_
                    try {{
                        $manifest = Get-AppxPackageManifest $_
                        $manifest.Package.Applications.Application | ForEach-Object {{
                            [PSCustomObject]@{{
                                Name = $pkg.Name
                                PFN = $pkg.PackageFamilyName
                                AppId = $_.Id
                                DisplayName = $pkg.Name.Split('.')[-1]
                            }}
                        }}
                    }} catch {{ }}
                }} | ConvertTo-Json -Compress
            ";

            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -NonInteractive -Command \"{script}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null) return results;

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(15000);

            if (string.IsNullOrWhiteSpace(output)) return results;

            ParseAppxOutput(output, results);
        }
        catch
        {
            if (_verbose)
                Console.Error.WriteLine("  [appx] Failed to query AppxPackages");
        }

        return results;
    }

    static void ParseAppxOutput(string json, List<ResolvedApp> results)
    {
        json = json.Trim();
        if (string.IsNullOrEmpty(json)) return;

        try
        {
            var doc = System.Text.Json.JsonDocument.Parse(json);

            if (doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                foreach (var item in doc.RootElement.EnumerateArray())
                    AddAppxResult(item, results);
            }
            else if (doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object)
            {
                AddAppxResult(doc.RootElement, results);
            }
        }
        catch { }
    }

    static void AddAppxResult(System.Text.Json.JsonElement item, List<ResolvedApp> results)
    {
        var pfn = item.GetProperty("PFN").GetString();
        var appId = item.GetProperty("AppId").GetString();
        var displayName = item.GetProperty("DisplayName").GetString();
        if (pfn == null || appId == null) return;

        var aumid = $"{pfn}!{appId}";
        displayName ??= pfn.Split('_')[0];

        results.Add(new ResolvedApp(displayName, PinType.UWA, null, null, aumid, null, 60));
    }

    static string[] GetStartMenuDirs()
    {
        return new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu), "Programs"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs")
        };
    }

    static string ToPortableLinkPath(string lnk)
    {
        var commonStart = Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu);
        var userStart = Environment.GetFolderPath(Environment.SpecialFolder.StartMenu);

        if (lnk.StartsWith(commonStart, StringComparison.OrdinalIgnoreCase))
            return "%ProgramData%\\Microsoft\\Windows\\Start Menu" + lnk[commonStart.Length..];
        if (lnk.StartsWith(userStart, StringComparison.OrdinalIgnoreCase))
            return "%APPDATA%\\Microsoft\\Windows\\Start Menu" + lnk[userStart.Length..];

        return lnk;
    }

    static string? ResolveShortcutTarget(string lnkPath)
    {
        try
        {
            dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
            var shortcut = shell.CreateShortcut(lnkPath);
            string target = shortcut.TargetPath;
            Marshal.ReleaseComObject(shortcut);
            Marshal.ReleaseComObject(shell);
            return string.IsNullOrEmpty(target) ? null : target;
        }
        catch
        {
            return null;
        }
    }

    static string EscapePowerShellString(string input)
    {
        return input.Replace("'", "''").Replace("\"", "`\"");
    }
}
