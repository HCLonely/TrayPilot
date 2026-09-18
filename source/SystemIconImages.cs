using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace TrayPilot;

internal static class SystemIconImages
{
    // White alpha masks are tinted by TrayListView for normal, hover and selected rows.
    internal static Bitmap Create(int mask, int size, (string Text, string Font) appearance = default)
    {
        var image = new Bitmap(size, size);
        using var graphics = Graphics.FromImage(image);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        if (!string.IsNullOrWhiteSpace(appearance.Text) && !string.IsNullOrWhiteSpace(appearance.Font))
        {
            try
            {
                using var family = new FontFamily(appearance.Font);
                // Reject substitutions or taskbar-private font URIs unavailable to GDI+.
                if (family.Name.Equals(appearance.Font, StringComparison.OrdinalIgnoreCase))
                {
                    bool layers = appearance.Text.All(c => c >= '\uE000' && c <= '\uF8FF');
                    using var font = new Font(family, size * (!layers && appearance.Text.Length > 1 ? .52f : .78f), FontStyle.Regular, GraphicsUnit.Pixel);
                    using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                    graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                    if (layers)
                        foreach (char glyph in appearance.Text) graphics.DrawString(glyph.ToString(), font, Brushes.White, new RectangleF(0, 0, size, size), format);
                    else graphics.DrawString(appearance.Text, font, Brushes.White, new RectangleF(0, 0, size, size), format);
                    return image;
                }
            }
            catch (ArgumentException) { }
        }
        graphics.ScaleTransform(size / 24f, size / 24f);
        using var pen = new Pen(Color.White, 1.7f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        void Lines(params PointF[] points) => graphics.DrawLines(pen, points);
        switch (mask)
        {
            case 1:
                graphics.DrawPolygon(pen, new PointF[] { new(3, 9), new(7, 9), new(12, 5), new(12, 19), new(7, 15), new(3, 15) });
                graphics.DrawArc(pen, 10, 7, 8, 10, -65, 130); graphics.DrawArc(pen, 10, 3, 13, 18, -60, 120); break;
            case 2:
                graphics.DrawArc(pen, 2, 5, 20, 19, 220, 100); graphics.DrawArc(pen, 6, 10, 12, 12, 220, 100);
                graphics.FillEllipse(Brushes.White, 10.5f, 17, 3, 3); break;
            case 4:
                graphics.DrawRectangle(pen, 2, 7, 18, 10); graphics.FillRectangle(Brushes.White, 21, 10, 2, 4);
                graphics.FillRectangle(Brushes.White, 5, 10, 10, 4); break;
            case 8:
                graphics.DrawEllipse(pen, 3, 3, 18, 18); Lines(new(12, 6), new(12, 12), new(16, 14)); break;
            case 16:
                graphics.DrawEllipse(pen, 9, 3, 6, 12); graphics.DrawArc(pen, 6, 7, 12, 12, 0, 180);
                Lines(new(12, 19), new(12, 22)); Lines(new(8, 22), new(16, 22)); break;
            case 32:
                using (var path = new GraphicsPath())
                {
                    path.AddArc(5, 2, 14, 14, 180, 180); path.AddBezier(19, 9, 19, 14, 12, 22, 12, 22);
                    path.AddBezier(12, 22, 12, 22, 5, 14, 5, 9); graphics.DrawPath(pen, path);
                }
                graphics.DrawEllipse(pen, 9, 6, 6, 6); break;
            case 64:
                graphics.DrawRectangle(pen, 2, 8, 13, 12); graphics.DrawPolygon(pen, new PointF[] { new(15, 12), new(21, 9), new(21, 19), new(15, 16) });
                Lines(new(9, 2), new(9, 6)); Lines(new(7, 4), new(11, 4)); break;
            case 128:
                graphics.DrawArc(pen, 3, 3, 18, 18, 210, 300); Lines(new(2, 4), new(2, 10), new(8, 10));
                Lines(new(12, 7), new(12, 12), new(16, 14)); break;
            case 256:
                graphics.DrawRectangle(pen, 2, 3, 20, 18); Lines(new(6, 17), new(10, 7), new(14, 17));
                Lines(new(8, 13), new(12, 13)); Lines(new(17, 8), new(19, 8)); Lines(new(17, 12), new(19, 12)); break;
            case 512:
                graphics.DrawRectangle(pen, 2, 6, 20, 13);
                for (int x = 5; x <= 18; x += 4) for (int y = 9; y <= 12; y += 3) graphics.FillRectangle(Brushes.White, x, y, 1.8f, 1.5f);
                Lines(new(7, 16), new(17, 16)); break;
            case 1024:
                graphics.DrawArc(pen, 6, 4, 12, 12, 180, 180); Lines(new(6, 10), new(6, 15), new(3, 18), new(21, 18), new(18, 15), new(18, 10));
                graphics.DrawArc(pen, 9, 17, 6, 5, 0, 180); break;
            case 2048:
                graphics.DrawRectangle(pen, 2, 4, 20, 14); Lines(new(9, 22), new(15, 22)); Lines(new(12, 18), new(12, 22));
                Lines(new(18, 5), new(18, 17)); break;
        }
        return image;
    }
}
