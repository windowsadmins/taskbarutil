using System.CommandLine;
using TaskbarUtil.Commands;

namespace TaskbarUtil;

class Program
{
    static async Task<int> Main(string[] args)
    {
        var rootCommand = new RootCommand("TaskbarUtil - Windows 11 taskbar pin management via local policy")
        {
            TreatUnmatchedTokensAsErrors = true
        };

        var verboseOption = new Option<bool>(
            aliases: ["--verbose", "-v"],
            description: "Enable verbose output");
        rootCommand.AddGlobalOption(verboseOption);

        var dryRunOption = new Option<bool>(
            aliases: ["--dry-run"],
            description: "Show what would be done without making changes");
        rootCommand.AddGlobalOption(dryRunOption);

        rootCommand.AddCommand(ListCommand.Create());
        rootCommand.AddCommand(FindCommand.Create(verboseOption));
        rootCommand.AddCommand(AddCommand.Create(verboseOption, dryRunOption));
        rootCommand.AddCommand(RemoveCommand.Create(dryRunOption));
        rootCommand.AddCommand(ShowCommand.Create());
        rootCommand.AddCommand(ApplyCommand.Create(verboseOption, dryRunOption));
        rootCommand.AddCommand(ResetCommand.Create(verboseOption, dryRunOption));

        return await rootCommand.InvokeAsync(args);
    }
}
