namespace TaskbarUtil.Core;

public class TaskbarItem
{
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public TaskbarItemType Type { get; set; }
    public int Position { get; set; }
}

public enum TaskbarItemType
{
    Unknown,
    Application,
    File,
    Folder,
    Url,
    Shortcut
}

public enum Position
{
    Beginning,
    End,
    Before,
    After,
    Index
}

public class TaskbarItemOptions
{
    public string Path { get; set; } = string.Empty;
    public string? Label { get; set; }
    public string? Replacing { get; set; }
    public Position PositionType { get; set; } = Position.End;
    public int? Index { get; set; }
    public string? Before { get; set; }
    public string? After { get; set; }
    public TaskbarItemType ItemType { get; set; } = TaskbarItemType.Application;
}
