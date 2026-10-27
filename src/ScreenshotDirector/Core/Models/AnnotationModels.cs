using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ScreenshotDirector.Core.Models;

public enum SnippingToolMode
{
    Select,     // Snip / Crop selection
    Pen,        // Freehand pen
    Highlighter,// Translucent highlighter
    Arrow,      // Straight arrow
    Rectangle,  // Outline rectangle
    Ellipse,    // Outline ellipse
    Text,       // Text annotation
    Pixelate    // Mosaic / blur redaction box
}

public interface IAnnotationItem
{
    void Draw(DrawingContext dc);
}

public class PenStrokeAnnotation : IAnnotationItem
{
    public List<Point> Points { get; set; } = new();
    public Color StrokeColor { get; set; }
    public double Thickness { get; set; }
    public bool IsHighlighter { get; set; }

    public void Draw(DrawingContext dc)
    {
        if (Points.Count < 2) return;

        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(Points[0], false, false);
            ctx.PolyLineTo(Points.Skip(1).ToList(), true, true);
        }
        geometry.Freeze();

        if (IsHighlighter)
        {
            DrawHighlighter(dc, geometry);
            return;
        }

        var brush = new SolidColorBrush(StrokeColor);
        brush.Freeze();

        var pen = new Pen(brush, Thickness)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round
        };
        pen.Freeze();

        dc.DrawGeometry(null, pen, geometry);
    }

    private void DrawHighlighter(DrawingContext dc, Geometry geometry)
    {
        // A highlighter should read as a wide marker stroke rather than a thin
        // pen line, so its width is scaled up from the shared stroke setting.
        double width = Math.Clamp(Thickness * 4.0, 14.0, 34.0);
        Color markerColor = Lighten(StrokeColor, 0.12);

        // Two translucent passes: a soft outer bleed plus a brighter core.
        // Keeping both semi-transparent preserves text/UI under the stroke.
        var bleedBrush = new SolidColorBrush(Color.FromArgb(30, markerColor.R, markerColor.G, markerColor.B));
        var coreBrush = new SolidColorBrush(Color.FromArgb(100, markerColor.R, markerColor.G, markerColor.B));
        bleedBrush.Freeze();
        coreBrush.Freeze();

        var bleedPen = new Pen(bleedBrush, width * 1.18)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round
        };
        var corePen = new Pen(coreBrush, width)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round
        };
        bleedPen.Freeze();
        corePen.Freeze();

        dc.DrawGeometry(null, bleedPen, geometry);
        dc.DrawGeometry(null, corePen, geometry);
    }

    private static Color Lighten(Color color, double amount)
    {
        amount = Math.Clamp(amount, 0.0, 1.0);
        return Color.FromRgb(
            (byte)(color.R + (255 - color.R) * amount),
            (byte)(color.G + (255 - color.G) * amount),
            (byte)(color.B + (255 - color.B) * amount));
    }
}

public class ShapeAnnotation : IAnnotationItem
{
    public SnippingToolMode ShapeType { get; set; }
    public Point StartPoint { get; set; }
    public Point EndPoint { get; set; }
    public Color StrokeColor { get; set; }
    public double Thickness { get; set; }

    public void Draw(DrawingContext dc)
    {
        var brush = new SolidColorBrush(StrokeColor);
        brush.Freeze();

        var pen = new Pen(brush, Thickness)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round
        };
        pen.Freeze();

        var rect = new Rect(StartPoint, EndPoint);

        switch (ShapeType)
        {
            case SnippingToolMode.Rectangle:
                dc.DrawRectangle(null, pen, rect);
                break;

            case SnippingToolMode.Ellipse:
                Point center = new Point(rect.X + rect.Width / 2.0, rect.Y + rect.Height / 2.0);
                dc.DrawEllipse(null, pen, center, rect.Width / 2.0, rect.Height / 2.0);
                break;

            case SnippingToolMode.Arrow:
                Vector dir = EndPoint - StartPoint;
                if (dir.Length > 2)
                {
                    dc.DrawLine(pen, StartPoint, EndPoint);

                    dir.Normalize();
                    Vector perp = new Vector(-dir.Y, dir.X);
                    double arrowHeadLength = Math.Max(12, Thickness * 3.5);
                    double arrowHeadWidth = arrowHeadLength * 0.55;

                    Point p1 = EndPoint - dir * arrowHeadLength + perp * arrowHeadWidth;
                    Point p2 = EndPoint - dir * arrowHeadLength - perp * arrowHeadWidth;

                    var arrowGeom = new StreamGeometry();
                    using (var ctx = arrowGeom.Open())
                    {
                        ctx.BeginFigure(EndPoint, true, true);
                        ctx.LineTo(p1, true, false);
                        ctx.LineTo(p2, true, false);
                    }
                    arrowGeom.Freeze();

                    dc.DrawGeometry(brush, null, arrowGeom);
                }
                break;
        }
    }
}

public class TextAnnotation : IAnnotationItem
{
    public Point Position { get; set; }
    public string Text { get; set; } = string.Empty;
    public Color TextColor { get; set; }
    public double FontSize { get; set; } = 18;

    public void Draw(DrawingContext dc)
    {
        if (string.IsNullOrWhiteSpace(Text)) return;

        var textBrush = new SolidColorBrush(TextColor);
        textBrush.Freeze();

        var formattedText = new FormattedText(
            Text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal),
            FontSize,
            textBrush,
            96.0);

        // Background pill for contrast
        var bgRect = new Rect(Position.X - 5, Position.Y - 2, formattedText.Width + 10, formattedText.Height + 4);
        var bgBrush = new SolidColorBrush(Color.FromArgb(190, 18, 16, 26));
        var borderPen = new Pen(new SolidColorBrush(Color.FromArgb(120, 255, 255, 255)), 1);
        bgBrush.Freeze();
        borderPen.Freeze();

        dc.DrawRoundedRectangle(bgBrush, borderPen, bgRect, 4, 4);
        dc.DrawText(formattedText, Position);
    }
}

public class PixelateAnnotation : IAnnotationItem
{
    public Rect Bounds { get; set; }
    public BitmapSource? PixelatedBitmap { get; set; }

    public void Draw(DrawingContext dc)
    {
        if (PixelatedBitmap != null && Bounds.Width > 0 && Bounds.Height > 0)
        {
            dc.DrawImage(PixelatedBitmap, Bounds);
        }
    }
}

public class SnippingStateSnapshot
{
    public BitmapSource BaseImage { get; }
    public List<IAnnotationItem> Annotations { get; }

    public SnippingStateSnapshot(BitmapSource baseImage, IEnumerable<IAnnotationItem> annotations)
    {
        BaseImage = baseImage;
        Annotations = new List<IAnnotationItem>(annotations);
    }
}

public static class PixelateHelper
{
    public static BitmapSource? CreatePixelatedPatch(BitmapSource source, Rect bounds, int targetBlockSize = 10)
    {
        try
        {
            int x = Math.Max(0, (int)bounds.X);
            int y = Math.Max(0, (int)bounds.Y);
            int w = Math.Min((int)bounds.Width, source.PixelWidth - x);
            int h = Math.Min((int)bounds.Height, source.PixelHeight - y);

            if (w <= 0 || h <= 0) return null;

            var cropped = new CroppedBitmap(source, new Int32Rect(x, y, w, h));
            var formatted = new FormatConvertedBitmap(cropped, PixelFormats.Bgra32, null, 0);

            int width = formatted.PixelWidth;
            int height = formatted.PixelHeight;
            int stride = width * 4;
            byte[] pixels = new byte[height * stride];
            formatted.CopyPixels(pixels, stride, 0);

            int blockSize = Math.Max(6, Math.Min(targetBlockSize, Math.Min(width, height) / 3));
            if (blockSize < 2) blockSize = 2;

            for (int by = 0; by < height; by += blockSize)
            {
                for (int bx = 0; bx < width; bx += blockSize)
                {
                    int bw = Math.Min(blockSize, width - bx);
                    int bh = Math.Min(blockSize, height - by);

                    long sumB = 0, sumG = 0, sumR = 0, sumA = 0;
                    int count = bw * bh;

                    for (int py = 0; py < bh; py++)
                    {
                        for (int px = 0; px < bw; px++)
                        {
                            int idx = ((by + py) * width + (bx + px)) * 4;
                            sumB += pixels[idx];
                            sumG += pixels[idx + 1];
                            sumR += pixels[idx + 2];
                            sumA += pixels[idx + 3];
                        }
                    }

                    byte avgB = (byte)(sumB / count);
                    byte avgG = (byte)(sumG / count);
                    byte avgR = (byte)(sumR / count);
                    byte avgA = (byte)(sumA / count);

                    for (int py = 0; py < bh; py++)
                    {
                        for (int px = 0; px < bw; px++)
                        {
                            int idx = ((by + py) * width + (bx + px)) * 4;
                            pixels[idx] = avgB;
                            pixels[idx + 1] = avgG;
                            pixels[idx + 2] = avgR;
                            pixels[idx + 3] = avgA;
                        }
                    }
                }
            }

            double dpiX = source.DpiX > 0 && !double.IsNaN(source.DpiX) && !double.IsInfinity(source.DpiX)
                ? source.DpiX
                : 96.0;
            double dpiY = source.DpiY > 0 && !double.IsNaN(source.DpiY) && !double.IsInfinity(source.DpiY)
                ? source.DpiY
                : 96.0;

            var result = BitmapSource.Create(width, height, dpiX, dpiY, PixelFormats.Bgra32, null, pixels, stride);
            result.Freeze();
            return result;
        }
        catch
        {
            return null;
        }
    }
}
