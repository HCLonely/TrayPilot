using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace TrayPilot;
internal static class AppIcon
{
    internal static Bitmap Draw(int size)
    {
        var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bitmap);
        g.SmoothingMode = SmoothingMode.AntiAlias; g.ScaleTransform(size / 64f, size / 64f);
        using var shape = new GraphicsPath();
        shape.AddArc(2, 2, 22, 22, 180, 90); shape.AddArc(40, 2, 22, 22, 270, 90);
        shape.AddArc(40, 40, 22, 22, 0, 90); shape.AddArc(2, 40, 22, 22, 90, 90); shape.CloseFigure();
        using var fill = new LinearGradientBrush(new Point(0, 0), new Point(64, 64), Color.FromArgb(62, 133, 255), Color.FromArgb(45, 62, 175));
        g.FillPath(fill, shape);
        using var white = new Pen(Color.White, 5) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        g.DrawLines(white, new[] { new Point(14, 39), new Point(18, 49), new Point(46, 49), new Point(50, 39) });
        g.DrawLine(white, 21, 22, 21, 31); g.DrawLine(white, 32, 17, 32, 31); g.DrawLine(white, 43, 24, 43, 31);
        return bitmap;
    }
    internal static Icon Create()
    {
        using var bitmap = Draw(32); var handle = bitmap.GetHicon();
        try { return (Icon)Icon.FromHandle(handle).Clone(); } finally { Native.DestroyIcon(handle); }
    }
    internal static void Save(string path)
    {
        var sizes = new[] { 16, 20, 24, 32, 48, 64, 128, 256 };
        var data = sizes.Select(size => { using var bitmap = Draw(size); using var stream = new MemoryStream(); bitmap.Save(stream, ImageFormat.Png); return stream.ToArray(); }).ToArray();
        using var writer = new BinaryWriter(File.Create(path));
        writer.Write((ushort)0); writer.Write((ushort)1); writer.Write((ushort)sizes.Length);
        int offset = 6 + sizes.Length * 16;
        for (int i = 0; i < sizes.Length; i++)
        {
            writer.Write((byte)(sizes[i] == 256 ? 0 : sizes[i])); writer.Write((byte)(sizes[i] == 256 ? 0 : sizes[i]));
            writer.Write((byte)0); writer.Write((byte)0); writer.Write((ushort)1); writer.Write((ushort)32);
            writer.Write(data[i].Length); writer.Write(offset); offset += data[i].Length;
        }
        foreach (var bytes in data) writer.Write(bytes);
    }
}
