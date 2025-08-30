using System.CommandLine;
using TaskbarUtil.Core;
using TaskbarUtil.Commands;

namespace TaskbarUtil;

class Program
{
    static async Task<int> Main(string[] args)
    {
        var rootCommand = new RootCommand("TaskbarUtil - A command line utility for managing Windows taskbar pins")
        {
            TreatUnmatchedTokensAsErrors = true
        };

        // Add verbose option
        var verboseOption = new Option<bool>(
            aliases: ["--verbose", "-v"],
            description: "Enable verbose output"
        );
        rootCommand.AddGlobalOption(verboseOption);

        // Add no-restart option
        var noRestartOption = new Option<bool>(
            aliases: ["--no-restart"],
            description: "Do not restart explorer.exe to refresh taskbar (explorer restarts by default)"
        );
        rootCommand.AddGlobalOption(noRestartOption);

        // Create command handlers
        var commandFactory = new CommandFactory();
        
        // Add commands
        rootCommand.Add(commandFactory.CreateAddCommand());
        rootCommand.Add(commandFactory.CreateRemoveCommand());
        rootCommand.Add(commandFactory.CreateListCommand());
        rootCommand.Add(commandFactory.CreateFindCommand());
        rootCommand.Add(commandFactory.CreateMoveCommand());

        return await rootCommand.InvokeAsync(args);
    }
}
