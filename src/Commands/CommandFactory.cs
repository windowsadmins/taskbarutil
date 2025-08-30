using System.CommandLine;
using TaskbarUtil.Core;

namespace TaskbarUtil.Commands;

public class CommandFactory
{
    public Command CreateAddCommand()
    {
        var pathArgument = new Argument<string>(
            name: "path",
            description: "Path to item to add to taskbar (application, file, folder, or URL)"
        );

        var labelOption = new Option<string?>(
            aliases: ["--label", "-l"],
            description: "Custom label for the taskbar item"
        );

        var replacingOption = new Option<string?>(
            aliases: ["--replacing", "-r"],
            description: "Replace an existing item with this label"
        );

        var positionOption = new Option<string?>(
            aliases: ["--position", "-p"],
            description: "Position: beginning, end, or index number"
        );

        var afterOption = new Option<string?>(
            aliases: ["--after", "-a"],
            description: "Place after this item"
        );

        var beforeOption = new Option<string?>(
            aliases: ["--before", "-b"],
            description: "Place before this item"
        );

        var addCommand = new Command("--add", "Add an item to the taskbar")
        {
            pathArgument,
            labelOption,
            replacingOption,
            positionOption,
            afterOption,
            beforeOption
        };

        addCommand.SetHandler((string path, string? label, string? replacing, 
            string? position, string? after, string? before, bool verbose, bool noRestart) =>
        {
            if (verbose)
                TaskbarManager.SetVerbose(true);

            var options = new TaskbarItemOptions
            {
                Path = path,
                Label = label,
                Replacing = replacing,
                Before = before,
                After = after
            };

            // Parse position
            if (!string.IsNullOrEmpty(position))
            {
                if (int.TryParse(position, out var index))
                {
                    options.PositionType = Position.Index;
                    options.Index = index;
                }
                else
                {
                    options.PositionType = position.ToLowerInvariant() switch
                    {
                        "beginning" or "begin" or "first" or "start" => Position.Beginning,
                        "end" or "last" => Position.End,
                        _ => Position.End
                    };
                }
            }

            var success = TaskbarManager.PinToTaskbar(options.Path, options.ItemType);
            if (success)
            {
                Console.WriteLine($"Successfully added '{path}' to taskbar");
                if (!noRestart)
                {
                    TaskbarManager.RestartExplorer();
                }
            }
            else
            {
                Console.WriteLine($"Failed to add '{path}' to taskbar");
                Environment.Exit(1);
            }

        }, pathArgument, labelOption, replacingOption, positionOption, afterOption, beforeOption,
           new Option<bool>("--verbose"), new Option<bool>("--no-restart"));

        return addCommand;
    }

    public Command CreateRemoveCommand()
    {
        var itemArgument = new Argument<string>(
            name: "item",
            description: "Item to remove from taskbar (name, path, or 'all')"
        );

        var removeCommand = new Command("--remove", "Remove an item from the taskbar")
        {
            itemArgument
        };

        removeCommand.SetHandler((string item, bool verbose, bool noRestart) =>
        {
            if (verbose)
                TaskbarManager.SetVerbose(true);

            bool success;
            if (item.Equals("all", StringComparison.OrdinalIgnoreCase))
            {
                var items = TaskbarManager.GetPinnedItems();
                success = true;
                foreach (var pinnedItem in items)
                {
                    if (!TaskbarManager.UnpinFromTaskbar(pinnedItem.Name))
                    {
                        success = false;
                        Console.WriteLine($"Failed to remove '{pinnedItem.Name}'");
                    }
                }
                if (success)
                {
                    Console.WriteLine("Successfully removed all items from taskbar");
                }
            }
            else
            {
                success = TaskbarManager.UnpinFromTaskbar(item);
                if (success)
                {
                    Console.WriteLine($"Successfully removed '{item}' from taskbar");
                }
                else
                {
                    Console.WriteLine($"Failed to remove '{item}' from taskbar");
                }
            }

            if (success && !noRestart)
            {
                TaskbarManager.RestartExplorer();
            }

            if (!success)
            {
                Environment.Exit(1);
            }

        }, itemArgument, new Option<bool>("--verbose"), new Option<bool>("--no-restart"));

        return removeCommand;
    }

    public Command CreateListCommand()
    {
        var listCommand = new Command("--list", "List all items in the taskbar");

        listCommand.SetHandler((bool verbose) =>
        {
            if (verbose)
                TaskbarManager.SetVerbose(true);

            var items = TaskbarManager.GetPinnedItems();
            TaskbarManager.PrintItems(items);

        }, new Option<bool>("--verbose"));

        return listCommand;
    }

    public Command CreateFindCommand()
    {
        var itemArgument = new Argument<string>(
            name: "item",
            description: "Item to find in the taskbar"
        );

        var findCommand = new Command("--find", "Find an item in the taskbar")
        {
            itemArgument
        };

        findCommand.SetHandler((string item, bool verbose) =>
        {
            if (verbose)
                TaskbarManager.SetVerbose(true);

            var items = TaskbarManager.GetPinnedItems();
            var foundItem = TaskbarManager.FindItem(items, item);

            if (foundItem != null)
            {
                Console.WriteLine($"Found: {foundItem.Name} -> {foundItem.Path} (Type: {foundItem.Type})");
            }
            else
            {
                Console.WriteLine($"Item not found: {item}");
                Environment.Exit(1);
            }

        }, itemArgument, new Option<bool>("--verbose"));

        return findCommand;
    }

    public Command CreateMoveCommand()
    {
        var itemArgument = new Argument<string>(
            name: "item",
            description: "Item to move in the taskbar"
        );

        var positionOption = new Option<string?>(
            aliases: ["--position", "-p"],
            description: "New position: beginning, end, or index number"
        );

        var afterOption = new Option<string?>(
            aliases: ["--after", "-a"],
            description: "Place after this item"
        );

        var beforeOption = new Option<string?>(
            aliases: ["--before", "-b"],
            description: "Place before this item"
        );

        var moveCommand = new Command("--move", "Move an item in the taskbar")
        {
            itemArgument,
            positionOption,
            afterOption,
            beforeOption
        };

        moveCommand.SetHandler((string item, string? position, string? after, 
            string? before, bool verbose, bool noRestart) =>
        {
            if (verbose)
                TaskbarManager.SetVerbose(true);

            Console.WriteLine("Move functionality not yet implemented - Windows taskbar doesn't support programmatic reordering easily.");
            Console.WriteLine("You may need to manually drag items to reorder them in the taskbar.");

        }, itemArgument, positionOption, afterOption, beforeOption,
           new Option<bool>("--verbose"), new Option<bool>("--no-restart"));

        return moveCommand;
    }
}
