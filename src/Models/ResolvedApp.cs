namespace TaskbarUtil.Models;

public record ResolvedApp(
    string DisplayName,
    PinType PinType,
    string? LinkPath,
    string? ApplicationID,
    string? AppUserModelID,
    string? ExePath,
    int Confidence
);
