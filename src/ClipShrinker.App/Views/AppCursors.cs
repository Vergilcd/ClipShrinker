using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ClipShrinker.Core.Project;

namespace ClipShrinker.App.Views;

/// <summary>2.7.5: the mouse pointers of the timeline, the Sounds lane and the draggable buttons. Edges (trim handles,
/// section and sound ends) show the left-right arrow; what moves as a whole shows the pointing hand on hover and a
/// closed hand while it is dragged. Windows has no closed-hand pointer, so it is drawn here once.</summary>
public static class AppCursors
{
    public static readonly Cursor Grabbing = MakeGrabbing();

    /// <summary>The pointer over a timeline (hover) or during its drag.</summary>
    public static Cursor? ForTimeline(HoverKind k, bool dragging) => k switch
    {
        HoverKind.Edge => Cursors.SizeWE,
        HoverKind.Move => dragging ? Grabbing : Cursors.Hand,
        _ => null,
    };

    /// <summary>While a button is dragged with DoDragDrop: the closed hand instead of Windows' drag pointers.</summary>
    public static void OnGiveFeedback(object sender, GiveFeedbackEventArgs e)
    {
        e.UseDefaultCursors = false;
        Mouse.SetCursor(Grabbing);
        e.Handled = true;
    }

    /// <summary>A closed hand (fist seen from the back, four knuckles on top), white with a black outline like the
    /// Windows pointers, as a 32x32 .cur made in memory.</summary>
    static Cursor MakeGrabbing()
    {
        const int S = 32;
        try
        {
            var g = new GeometryGroup { FillRule = FillRule.Nonzero };
            g.Children.Add(new RectangleGeometry(new Rect(8, 12, 17, 13), 4, 4));          // the back of the hand
            for (int i = 0; i < 4; i++)                                                      // folded fingers
                g.Children.Add(new RectangleGeometry(new Rect(9 + i * 4, 9, 4, 8), 2, 2));
            g.Children.Add(new RectangleGeometry(new Rect(5, 15, 6, 5), 2.5, 2.5));          // thumb
            Geometry shape = g.Children[0];                                                  // one outline: the union
            foreach (var c in g.Children.Skip(1)) shape = Geometry.Combine(shape, c, GeometryCombineMode.Union, null);
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                dc.DrawGeometry(Brushes.White, new Pen(Brushes.Black, 1.2), shape);
                var knuckle = new Pen(Brushes.Black, 0.9);
                for (int i = 1; i < 4; i++) dc.DrawLine(knuckle, new Point(9 + i * 4, 10), new Point(9 + i * 4, 15));
            }
            var bmp = new RenderTargetBitmap(S, S, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(dv);
            var px = new byte[S * S * 4];
            bmp.CopyPixels(px, S * 4, 0);
            return new Cursor(new MemoryStream(Cur(px, S, 16, 16)), true);
        }
        catch (Exception e) when (e is IOException or ArgumentException or NotSupportedException)
        {
            return Cursors.SizeAll;
        }
    }

    /// <summary>A .cur file with one 32-bit image (BGRA rows bottom-up, then an empty AND mask).</summary>
    static byte[] Cur(byte[] bgra, int s, int hotX, int hotY)
    {
        int maskRow = (s + 31) / 32 * 4, imgSize = 40 + s * s * 4 + maskRow * s;
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write((short)0); w.Write((short)2); w.Write((short)1);                           // ICONDIR: cursor, 1 image
        w.Write((byte)s); w.Write((byte)s); w.Write((byte)0); w.Write((byte)0);
        w.Write((short)hotX); w.Write((short)hotY); w.Write(imgSize); w.Write(6 + 16);
        w.Write(40); w.Write(s); w.Write(s * 2); w.Write((short)1); w.Write((short)32);  // BITMAPINFOHEADER
        w.Write(0); w.Write(s * s * 4); w.Write(0); w.Write(0); w.Write(0); w.Write(0);
        for (int y = s - 1; y >= 0; y--)
            for (int x = 0; x < s; x++)
            {
                int i = (y * s + x) * 4;
                byte a = bgra[i + 3];
                // un-premultiply (RenderTargetBitmap gives premultiplied colours)
                byte U(byte c) => a == 0 ? (byte)0 : (byte)Math.Min(255, c * 255 / a);
                w.Write(U(bgra[i])); w.Write(U(bgra[i + 1])); w.Write(U(bgra[i + 2])); w.Write(a);
            }
        w.Write(new byte[maskRow * s]);
        w.Flush();
        return ms.ToArray();
    }
}
