namespace LightBulb.PlatformInterop;

public sealed record DisplayInfo(
    string Id,
    string DeviceName,
    string Name,
    bool IsPrimary,
    Rect Bounds
)
{
    public string ConnectionId { get; init; } = Id;
}
