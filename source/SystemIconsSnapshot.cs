namespace TrayPilot;

internal sealed record SystemIconsSnapshot(bool Connected, bool Connecting, int Requested, int Found, int Hidden,
    bool Shared, bool Legacy, string Error, string Diagnostic, (string Text, string Font)[] Appearances)
{
    internal int ForcedShown { get; init; }
    internal ExplorerIconImage?[] Images { get; init; } = new ExplorerIconImage?[12];
    internal string StateKey(int bit) => Connecting ? "systemPageIdentifying" : !Connected ? "systemNativeUnavailable"
        : (Found & bit) == 0 ? (Requested & bit) != 0 ? "systemNativeWaiting" : (ForcedShown & bit) != 0 ? "systemForceShowWaiting" : "systemNativeNotFound"
        : (Hidden & bit) != 0 ? (ForcedShown & bit) != 0 ? "systemPagePending" : "systemNativeHidden"
        : (ForcedShown & bit) != 0 ? "systemForceShowActive"
        : Shared && (bit & 48) != 0 && (Requested & bit) != 0 && (Requested & 48) != 48 ? "systemSharedIndicator"
        : (Requested & bit) != 0 ? "systemPagePending" : "systemNativeVisible";
}
