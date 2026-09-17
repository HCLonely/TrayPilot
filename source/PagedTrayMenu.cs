namespace TrayPilot;

internal sealed class PagedTrayMenu : ContextMenuStrip
{
    int wheelDelta;
    internal event Action<int>? PageRequested;

    protected override void OnOpening(System.ComponentModel.CancelEventArgs e)
    {
        wheelDelta = 0;
        base.OnOpening(e);
    }

    protected override void WndProc(ref Message message)
    {
        // Consume WM_MOUSEWHEEL before ToolStrip's built-in item scrolling.
        if (message.Msg == 0x020A)
        {
            wheelDelta += unchecked((short)((long)message.WParam >> 16));
            int steps = wheelDelta / 120;
            wheelDelta %= 120;
            if (steps != 0) PageRequested?.Invoke(-steps);
            return;
        }
        base.WndProc(ref message);
    }
}
