using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace RevitCortex.Plugin.UI;

/// <summary>
/// Generates ribbon icons programmatically with vector symbols.
///
/// One palette for the whole panel:
/// <list type="bullet">
/// <item>Slate (#3F4A55) for plain command buttons (settings, export, support, license).</item>
/// <item>Toggle buttons (server, Autopilot) are light gray (#9AA0A6) when off and
/// Claude orange (#D97757) when on, so state is readable at a glance.</item>
/// </list>
/// </summary>
public static class IconFactory
{
    /// <summary>Accent used for every "on" state (Claude orange).</summary>
    public static readonly Color ClaudeOrange = Color.FromRgb(217, 119, 87);  // #D97757
    private static readonly Color Slate = Color.FromRgb(63, 74, 85);           // #3F4A55
    private static readonly Color OffGray = Color.FromRgb(154, 160, 166);      // #9AA0A6
    private static readonly Color InactiveGray = Color.FromRgb(97, 97, 97);   // #616161
    private static readonly Color TealDark = Slate;
    private static readonly Color IndigoAccent = Slate;

    /// <summary>Connection icon: lightning bolt. Orange when running, gray when stopped.</summary>
    public static BitmapSource CreateConnectionIcon(int size, bool isActive = false)
    {
        var bg = isActive ? ClaudeOrange : OffGray;
        return CreateIconWithDrawing(size, bg, (dc, s) =>
        {
            // Lightning bolt
            double m = s * 0.2; // margin
            var pen = new Pen(Brushes.White, s * 0.08) { LineJoin = PenLineJoin.Round };
            pen.Freeze();
            var bolt = new StreamGeometry();
            using (var ctx = bolt.Open())
            {
                ctx.BeginFigure(new Point(s * 0.55, m), false, false);
                ctx.LineTo(new Point(s * 0.35, s * 0.48), true, true);
                ctx.LineTo(new Point(s * 0.55, s * 0.48), true, true);
                ctx.LineTo(new Point(s * 0.40, s - m), true, true);
            }
            bolt.Freeze();
            dc.DrawGeometry(null, pen, bolt);

            // Small dot at bottom-right corner (status indicator)
            double dotR = s * 0.1;
            dc.DrawEllipse(Brushes.White, null,
                new Point(s * 0.75, s * 0.75), dotR, dotR);
        });
    }

    /// <summary>Panel icon: chat bubble on indigo background</summary>
    public static BitmapSource CreatePanelIcon(int size)
    {
        return CreateIconWithDrawing(size, IndigoAccent, (dc, s) =>
        {
            double m = s * 0.18;
            var pen = new Pen(Brushes.White, s * 0.07) { LineJoin = PenLineJoin.Round };
            pen.Freeze();

            // Rounded chat bubble
            var bubble = new StreamGeometry();
            using (var ctx = bubble.Open())
            {
                double l = m, t = m, r = s - m, b = s * 0.65;
                double cr = s * 0.08;
                ctx.BeginFigure(new Point(l + cr, t), true, true);
                ctx.LineTo(new Point(r - cr, t), true, true);
                ctx.ArcTo(new Point(r, t + cr), new Size(cr, cr), 0, false, SweepDirection.Clockwise, true, true);
                ctx.LineTo(new Point(r, b - cr), true, true);
                ctx.ArcTo(new Point(r - cr, b), new Size(cr, cr), 0, false, SweepDirection.Clockwise, true, true);
                // Tail
                ctx.LineTo(new Point(s * 0.45, b), true, true);
                ctx.LineTo(new Point(s * 0.30, s - m), true, true);
                ctx.LineTo(new Point(s * 0.35, b), true, true);
                ctx.LineTo(new Point(l + cr, b), true, true);
                ctx.ArcTo(new Point(l, b - cr), new Size(cr, cr), 0, false, SweepDirection.Clockwise, true, true);
                ctx.LineTo(new Point(l, t + cr), true, true);
                ctx.ArcTo(new Point(l + cr, t), new Size(cr, cr), 0, false, SweepDirection.Clockwise, true, true);
            }
            bubble.Freeze();
            dc.DrawGeometry(null, pen, bubble);

            // Three dots inside bubble
            double dotY = (m + s * 0.65) / 2;
            double dotR = s * 0.04;
            for (int i = 0; i < 3; i++)
            {
                double dotX = s * 0.35 + i * s * 0.13;
                dc.DrawEllipse(Brushes.White, null, new Point(dotX, dotY), dotR, dotR);
            }
        });
    }

    /// <summary>Settings icon: gear on dark teal background</summary>
    public static BitmapSource CreateSettingsIcon(int size)
    {
        return CreateIconWithDrawing(size, TealDark, (dc, s) =>
        {
            var center = new Point(s / 2.0, s / 2.0);
            double outerR = s * 0.35;
            double innerR = s * 0.18;
            int teeth = 8;
            var pen = new Pen(Brushes.White, s * 0.06);
            pen.Freeze();

            // Gear outline
            var gear = new StreamGeometry();
            using (var ctx = gear.Open())
            {
                bool first = true;
                for (int i = 0; i < teeth; i++)
                {
                    double angle1 = (2 * Math.PI * i / teeth) - Math.PI / 2;
                    double angle2 = angle1 + Math.PI / teeth * 0.5;
                    double angle3 = angle1 + Math.PI / teeth * 0.8;
                    double angle4 = angle1 + Math.PI / teeth;

                    var p1 = PointOnCircle(center, outerR, angle1);
                    var p2 = PointOnCircle(center, outerR, angle2);
                    var p3 = PointOnCircle(center, innerR, angle3);
                    var p4 = PointOnCircle(center, innerR, angle4);

                    if (first) { ctx.BeginFigure(p1, false, true); first = false; }
                    else ctx.LineTo(p1, true, true);
                    ctx.LineTo(p2, true, true);
                    ctx.LineTo(p3, true, true);
                    ctx.LineTo(p4, true, true);
                }
            }
            gear.Freeze();
            dc.DrawGeometry(null, pen, gear);

            // Center circle
            double centerR = s * 0.08;
            dc.DrawEllipse(Brushes.White, null, center, centerR, centerR);
        });
    }

    /// <summary>
    /// Power BI icon: four bars of increasing height (analytics/dashboard motif)
    /// on a yellow-amber background to evoke the Power BI brand without using
    /// the trademarked logo.
    /// </summary>
    public static BitmapSource CreatePowerBiIcon(int size)
    {
        return CreateIconWithDrawing(size, Slate, (dc, s) =>
        {
            double margin = s * 0.18;
            double baseY = s - margin;
            double barW = (s - 2 * margin) / 4 * 0.7;
            double gap = (s - 2 * margin) / 4 * 0.3;
            var fill = Brushes.White;

            for (int i = 0; i < 4; i++)
            {
                double h = (i + 1) * (s - 2 * margin) / 5;
                double x = margin + i * (barW + gap);
                double y = baseY - h;
                dc.DrawRectangle(fill, null, new Rect(x, y, barW, h));
            }
        });
    }

    /// <summary>License icon: shield with a checkmark, on a violet background
    /// (distinct from the teal-gear Settings icon it used to borrow).</summary>
    public static BitmapSource CreateLicenseIcon(int size)
    {
        return CreateIconWithDrawing(size, Slate, (dc, s) =>
        {
            double m = s * 0.22;
            var pen = new Pen(Brushes.White, s * 0.07) { LineJoin = PenLineJoin.Round };
            pen.Freeze();

            // Shield outline
            var shield = new StreamGeometry();
            using (var ctx = shield.Open())
            {
                double l = m, r = s - m, t = m * 0.9, mid = s * 0.55, b = s - m * 0.7;
                ctx.BeginFigure(new Point(s / 2.0, t), false, true);
                ctx.LineTo(new Point(r, t + (mid - t) * 0.35), true, true);
                ctx.LineTo(new Point(r, mid), true, true);
                ctx.LineTo(new Point(s / 2.0, b), true, true);
                ctx.LineTo(new Point(l, mid), true, true);
                ctx.LineTo(new Point(l, t + (mid - t) * 0.35), true, true);
            }
            shield.Freeze();
            dc.DrawGeometry(null, pen, shield);

            // Checkmark inside the shield
            var check = new StreamGeometry();
            using (var ctx = check.Open())
            {
                ctx.BeginFigure(new Point(s * 0.36, s * 0.5), false, false);
                ctx.LineTo(new Point(s * 0.46, s * 0.6), true, true);
                ctx.LineTo(new Point(s * 0.66, s * 0.38), true, true);
            }
            check.Freeze();
            var checkPen = new Pen(Brushes.White, s * 0.08) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            checkPen.Freeze();
            dc.DrawGeometry(null, checkPen, check);
        });
    }

    /// <summary>Support icon: stylized envelope on indigo background.</summary>
    public static BitmapSource CreateSupportIcon(int size)
    {
        return CreateIconWithDrawing(size, IndigoAccent, (dc, s) =>
        {
            // Envelope body (rounded rectangle)
            double mx = s * 0.15;
            double my = s * 0.25;
            double w = s - 2 * mx;
            double h = s - 2 * my;
            var rect = new Rect(mx, my, w, h);
            var pen = new Pen(Brushes.White, s * 0.06) { LineJoin = PenLineJoin.Round };
            pen.Freeze();
            dc.DrawRoundedRectangle(null, pen, rect, s * 0.05, s * 0.05);

            // Envelope flap (triangle apex pointing down to center)
            var flap = new StreamGeometry();
            using (var ctx = flap.Open())
            {
                ctx.BeginFigure(new Point(mx, my), false, false);
                ctx.LineTo(new Point(s / 2.0, my + h * 0.55), true, true);
                ctx.LineTo(new Point(mx + w, my), true, true);
            }
            flap.Freeze();
            dc.DrawGeometry(null, pen, flap);
        });
    }

    /// <summary>
    /// Stop Auto icon: red square (stop symbol) on a dark-red background when active,
    /// gray when inactive.
    /// </summary>
    public static BitmapSource CreateStopAutoIcon(int size, bool isActive = true)
    {
        var bg = isActive
            ? Color.FromRgb(183, 28, 28)   // #B71C1C deep red
            : InactiveGray;
        return CreateIconWithDrawing(size, bg, (dc, s) =>
        {
            double m = s * 0.28;
            dc.DrawRectangle(Brushes.White, null, new Rect(m, m, s - 2 * m, s - 2 * m));
        });
    }

    /// <summary>
    /// Autopilot icon: a ring with a "play" triangle (work keeps going) and a
    /// small status dot. Light gray when off, Claude orange when on.
    /// </summary>
    public static BitmapSource CreateAutopilotIcon(int size, bool isActive = false)
    {
        var bg = isActive ? ClaudeOrange : OffGray;
        return CreateIconWithDrawing(size, bg, (dc, s) =>
        {
            var center = new Point(s / 2.0, s / 2.0);
            var ring = new Pen(Brushes.White, s * 0.07);
            ring.Freeze();
            dc.DrawEllipse(null, ring, center, s * 0.32, s * 0.32);

            var play = new StreamGeometry();
            using (var ctx = play.Open())
            {
                ctx.BeginFigure(new Point(s * 0.42, s * 0.34), true, true);
                ctx.LineTo(new Point(s * 0.66, s * 0.50), true, true);
                ctx.LineTo(new Point(s * 0.42, s * 0.66), true, true);
            }
            play.Freeze();
            dc.DrawGeometry(Brushes.White, null, play);

            if (isActive)
            {
                // "Live" dot in the corner, like a recording indicator.
                dc.DrawEllipse(Brushes.White, null, new Point(s * 0.82, s * 0.18), s * 0.09, s * 0.09);
            }
        });
    }

    private static Point PointOnCircle(Point center, double radius, double angle)
    {
        return new Point(
            center.X + radius * Math.Cos(angle),
            center.Y + radius * Math.Sin(angle));
    }

    private static BitmapSource CreateIconWithDrawing(int size, Color background,
        Action<DrawingContext, double> drawAction)
    {
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            var brush = new SolidColorBrush(background);
            brush.Freeze();

            double radius = size * 0.2;
            dc.DrawRoundedRectangle(brush, null,
                new Rect(0, 0, size, size), radius, radius);

            drawAction(dc, size);
        }

        var rtb = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        rtb.Freeze();
        return rtb;
    }
}
