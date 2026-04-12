namespace TaskbarUtil.Core;

public class TaskbarLayout
{
    public bool AllowUserUnpin { get; set; }
    public List<TaskbarPin> Pins { get; } = new();

    public void AddPin(TaskbarPin pin, int? position = null)
    {
        if (position.HasValue && position.Value >= 0 && position.Value < Pins.Count)
            Pins.Insert(position.Value, pin);
        else
            Pins.Add(pin);
    }

    public bool RemovePin(string nameOrPath)
    {
        var pin = FindPin(nameOrPath);
        if (pin != null)
        {
            Pins.Remove(pin);
            return true;
        }
        return false;
    }

    public bool MovePin(string nameOrPath, int newPosition)
    {
        var pin = FindPin(nameOrPath);
        if (pin == null) return false;

        Pins.Remove(pin);
        var idx = Math.Clamp(newPosition, 0, Pins.Count);
        Pins.Insert(idx, pin);
        return true;
    }

    public bool ReplacePin(string nameOrPath, TaskbarPin replacement)
    {
        var pin = FindPin(nameOrPath);
        if (pin == null) return false;

        var idx = Pins.IndexOf(pin);
        Pins[idx] = replacement;
        return true;
    }

    public TaskbarPin? FindPin(string nameOrPath)
    {
        return Pins.FirstOrDefault(p =>
            p.DisplayName.Equals(nameOrPath, StringComparison.OrdinalIgnoreCase) ||
            p.DisplayName.Contains(nameOrPath, StringComparison.OrdinalIgnoreCase) ||
            (p.DesktopApplicationLinkPath != null && p.DesktopApplicationLinkPath.Contains(nameOrPath, StringComparison.OrdinalIgnoreCase)) ||
            (p.DesktopApplicationID != null && p.DesktopApplicationID.Equals(nameOrPath, StringComparison.OrdinalIgnoreCase)) ||
            (p.AppUserModelID != null && p.AppUserModelID.Contains(nameOrPath, StringComparison.OrdinalIgnoreCase)));
    }
}
