using System.Drawing.Drawing2D;

namespace TrayPilot;

internal static class DashboardGlyph
{
    internal static void Draw(Graphics graphics, string name, Rectangle bounds, Color color)
    {
        var state = graphics.Save();
        try
        {
            graphics.TranslateTransform(bounds.X, bounds.Y); graphics.ScaleTransform(bounds.Width / 24f, bounds.Height / 24f);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var pen = new Pen(color, 1.7f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
            switch (name)
            {
                case "iconManagement":
                    graphics.DrawRectangle(pen, 3, 5, 18, 14);
                    graphics.DrawLines(pen, new PointF[] { new(3, 13), new(8, 13), new(10, 16), new(14, 16), new(16, 13), new(21, 13) }); break;
                case "hideRules":
                    foreach (int y in new[] { 5, 12, 19 }) { graphics.DrawLine(pen, 8, y, 21, y); graphics.DrawLine(pen, 3, y, 4, y); } break;
                case "systemIcons":
                    graphics.DrawRectangle(pen, 3, 4, 18, 12); graphics.DrawLine(pen, 12, 16, 12, 21); graphics.DrawLine(pen, 8, 21, 16, 21); break;
                case "settings":
                    graphics.DrawLine(pen, 3, 7, 21, 7); graphics.DrawLine(pen, 3, 17, 21, 17); graphics.DrawLine(pen, 8, 3, 8, 11); graphics.DrawLine(pen, 16, 13, 16, 21); break;
                case "about":
                    graphics.DrawEllipse(pen, 3, 3, 18, 18); graphics.DrawLine(pen, 12, 11, 12, 17); graphics.DrawLine(pen, 12, 7, 12, 7.1f); break;
            }
        }
        finally { graphics.Restore(state); }
    }
}
