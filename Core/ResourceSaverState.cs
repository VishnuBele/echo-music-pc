namespace EchoMusic.Core;

/// <summary>Runtime resource-saving state shared by UI image and stream-buffer policies.</summary>
public static class ResourceSaverState
{
    public static volatile bool IsActive;
}
