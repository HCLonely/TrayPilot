namespace TrayPilot;

internal sealed record SystemIconsSnapshot(bool Connected, bool Connecting, int Requested, int Found, int Hidden,
    bool Shared, bool Legacy, string Error, string Diagnostic, (string Text, string Font)[] Appearances)
{
    internal string StateKey(int bit) => Connecting ? "systemPageIdentifying" : !Connected ? "systemNativeUnavailable"
        : (Found & bit) == 0 ? (Requested & bit) != 0 ? "systemNativeWaiting" : "systemNativeNotFound"
        : (Hidden & bit) != 0 ? "systemNativeHidden"
        : Shared && (bit & 48) != 0 && (Requested & bit) != 0 && (Requested & 48) != 48 ? "systemSharedIndicator"
        : (Requested & bit) != 0 ? "systemPagePending" : "systemNativeVisible";
}
