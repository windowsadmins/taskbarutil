using System.CommandLine;
using System.CommandLine.Invocation;
using TaskbarUtil.Core;
using TaskbarUtil.Models;

namespace TaskbarUtil.Commands;

public static class FindCommand
{
    public static Command Create(Option<bool> verboseOption)
    {
        var queryArg = new Argument<string>("query", "App name to search for");
        var typeOption = new Option<string?>("--type", "Filter by type: desktop, uwp, all")
        {
            Arity = ArgumentArity.ZeroOrOne
        };
        typeOption.SetDefaultValue("all");

        var command = new Command("find", "Search for installed apps by name")
        {
            queryArg,
            typeOption
        };

        command.SetHandler((query, type, verbose) =>
        {
            var resolver = new AppResolver(verbose);
            var results = resolver.Search(query);

            if (type != "all")
            {
                var pinType = type == "uwp" ? PinType.UWA : PinType.DesktopApp;
                results = results.Where(r => r.PinType == pinType).ToList();
            }

            if (results.Count == 0)
            {
                Console.WriteLine($"No apps found matching '{query}'.");
                return;
            }

            Console.WriteLine($"# Apps matching '{query}'");
            Console.WriteLine();

            foreach (var app in results)
            {
                var typeLabel = app.PinType == PinType.UWA ? "UWP" : "Desktop";
                Console.WriteLine($"  [{typeLabel}] {app.DisplayName}");

                if (app.LinkPath != null)
                    Console.WriteLine($"         Link: {app.LinkPath}");
                if (app.ApplicationID != null)
                    Console.WriteLine($"         AppID: {app.ApplicationID}");
                if (app.AppUserModelID != null)
                    Console.WriteLine($"         AUMID: {app.AppUserModelID}");
                if (app.ExePath != null)
                    Console.WriteLine($"         Exe: {app.ExePath}");

                Console.WriteLine();
            }

            Console.WriteLine($"{results.Count} result(s)");
        }, queryArg, typeOption, verboseOption);

        return command;
    }
}
