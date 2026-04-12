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
        var pin = Pins.FirstOrDefault(p =>
            p.DisplayName.Equals(nameOrPath, StringComparison.OrdinalIgnoreCase) ||
            (p.DesktopApplicationLinkPath != null && p.DesktopApplicationLinkPath.Contains(nameOrPath, StringComparison.OrdinalIgnoreCase)) ||
            (p.DesktopApplicationID != null && p.DesktopApplicationID.Equals(nameOrPath, StringComparison.OrdinalIgnoreCase)) ||
            (p.AppUserModelID != null && p.AppUserModelID.Contains(nameOrPath, StringComparison.OrdinalIgnoreCase)));

        if (pin != null)
        {
            Pins.Remove(pin);
            return true;
        }
        return false;
    }
}
