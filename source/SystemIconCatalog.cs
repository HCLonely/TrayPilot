namespace TrayPilot;

internal static class SystemIconCatalog
{
    internal sealed record Item(int Mask, string Name);
    internal const int Microphone = 16, Location = 32, All = 4095;
    internal const int Language = 256 | 512;
    internal static void ValidateVisibility(int hidden, int forcedShown)
    {
        if ((hidden & ~All) != 0) throw new ArgumentOutOfRangeException(nameof(hidden));
        if ((forcedShown & ~Language) != 0 || (hidden & forcedShown) != 0)
            throw new ArgumentOutOfRangeException(nameof(forcedShown));
    }
    internal static readonly Item[] Items =
    {
        new(1, "systemLiveVolume"), new(2, "systemLiveNetwork"), new(4, "systemLiveBattery"), new(8, "systemClock"),
        new(Microphone, "systemMicrophone"), new(Location, "systemLocation"), new(64, "systemStudioEffects"),
        new(128, "systemRecall"), new(256, "systemLanguage"), new(512, "systemLanguageSupplementary"),
        new(1024, "systemBell"), new(2048, "systemShowDesktop")
    };
}
