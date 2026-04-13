using System.CommandLine;
using Microsoft.Win32;

namespace TaskbarUtil.Commands;

public static class SettingsCommand
{
    const string AdvancedKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";

    static readonly (string Name, string RegValue, string[] Options)[] Settings =
    [
        ("search",   "SearchboxTaskbarMode", ["hidden", "icon", "icon-label", "box"]),
        ("taskview", "ShowTaskViewButton",   ["off", "on"]),
        ("widgets",  "TaskbarDa",            ["off", "on"]),
        ("resume",   "ShowResumeButton",     ["off", "on"]),
        ("copilot",  "ShowCopilotButton",    ["off", "on"]),
        ("chat",     "TaskbarMn",            ["off", "on"]),
    ];

    public static Command Create()
    {
        var nameArg = new Argument<string?>("setting", () => null,
            "Setting name: search, taskview, widgets, resume, copilot, chat");
        var valueArg = new Argument<string?>("value", () => null,
            "New value (on/off, or for search: hidden/icon/icon-label/box)");

        var command = new Command("settings", "Show or change taskbar item visibility")
        {
            nameArg,
            valueArg
        };

        command.SetHandler((name, value) =>
        {
            if (name == null)
            {
                ShowAll();
                return;
            }

            var setting = Settings.FirstOrDefault(s =>
                s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

            if (setting.Name == null)
            {
                Console.Error.WriteLine($"Unknown setting '{name}'.");
                Console.Error.WriteLine($"Available: {string.Join(", ", Settings.Select(s => s.Name))}");
                Environment.ExitCode = 1;
                return;
            }

            if (value == null)
            {
                ShowOne(setting);
                return;
            }

            SetValue(setting, value);

        }, nameArg, valueArg);

        return command;
    }

    static void ShowAll()
    {
        Console.WriteLine("# Taskbar items");
        Console.WriteLine();
        foreach (var s in Settings)
            ShowOne(s);
    }

    static void ShowOne((string Name, string RegValue, string[] Options) setting)
    {
        var current = GetRegValue(setting.RegValue);
        var display = setting.Name == "search"
            ? MapSearchValue(current)
            : (current == 0 ? "off" : "on");

        var pad = setting.Name.PadRight(10);
        Console.WriteLine($"  {pad} {display,-12} ({string.Join("|", setting.Options)})");
    }

    static void SetValue((string Name, string RegValue, string[] Options) setting, string value)
    {
        int regVal;

        if (setting.Name == "search")
        {
            regVal = value.ToLowerInvariant() switch
            {
                "hidden" or "off" or "0" => 0,
                "icon" or "1" => 1,
                "icon-label" or "2" => 2,
                "box" or "3" => 3,
                _ => -1
            };
        }
        else
        {
            regVal = value.ToLowerInvariant() switch
            {
                "off" or "false" or "0" or "hide" or "no" => 0,
                "on" or "true" or "1" or "show" or "yes" => 1,
                _ => -1
            };
        }

        if (regVal == -1)
        {
            Console.Error.WriteLine($"Invalid value '{value}' for {setting.Name}.");
            Console.Error.WriteLine($"Options: {string.Join(", ", setting.Options)}");
            Environment.ExitCode = 1;
            return;
        }

        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(AdvancedKeyPath);
            key.SetValue(setting.RegValue, regVal, RegistryValueKind.DWord);
            var display = setting.Name == "search" ? MapSearchValue(regVal) : (regVal == 0 ? "off" : "on");
            Console.WriteLine($"{setting.Name} = {display}");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to set {setting.Name}: {ex.Message}");
            Environment.ExitCode = 1;
        }
    }

    static int GetRegValue(string name)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(AdvancedKeyPath);
            var val = key?.GetValue(name);
            return val is int i ? i : 0;
        }
        catch { return 0; }
    }

    static string MapSearchValue(int val) => val switch
    {
        0 => "hidden",
        1 => "icon",
        2 => "icon-label",
        3 => "box",
        _ => $"unknown ({val})"
    };
}
