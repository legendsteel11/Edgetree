using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml;
using System.Xml.Linq;
// The project also references WinForms, whose System.Drawing names the same
// types.
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using Pen = System.Windows.Media.Pen;
using Point = System.Windows.Point;

namespace SidebarExplorer.App.Services;

// Draws the SVGs simple enough to need no SVG engine (2026-10-06).
//
// Windows has no SVG decoder of its own, so an .svg was only ever as visible
// as whatever thumbnail handler the machine had installed. On the machine it
// was reported from, that handler is PowerToys', and for Material Symbols it
// answered every request - at every size, three times over - with a picture
// that was entirely transparent, or entirely black. The app then showed and
// cached that empty picture as the file's thumbnail, so the list stayed
// blank and the panel showed nothing or, when a request failed, the file
// type's icon instead - which read as "some appear, some do not".
//
// An icon set's SVG is a handful of shapes filled or stroked in one colour,
// and WPF already draws exactly that: its path markup is SVG's own path data
// (the app's icons are drawn from it - see FileSystemService). So those are
// drawn here, and anything beyond them - a transform, a gradient, CSS, text,
// a clip, an embedded image - is declined as a whole rather than drawn in
// part, and goes to the shell as before. A wrong picture is worse than the
// type icon.
public static class SvgRenderer
{
    // Icon files are a few KB. Past this it is a drawing, not an icon, and
    // drawings are what the declining rule below is for anyway.
    private const long MaxFileBytes = 512 * 1024;

    // Null whenever the file is not one this draws; the caller then asks the
    // shell. The longer side comes out pixelSize pixels, on a transparent
    // ground.
    public static BitmapSource? TryRender(string path, int pixelSize)
    {
        if (pixelSize <= 0)
        {
            return null;
        }

        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length == 0 || info.Length > MaxFileBytes)
            {
                return null;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException
                                     or NotSupportedException)
        {
            return null;
        }

        // On an STA thread of its own: drawing into a bitmap goes through a
        // Visual, and a Visual cannot be made on the thumbnail workers, which
        // are pool (MTA) threads. One short thread per icon is noise next to
        // the file read it follows.
        BitmapSource? result = null;
        var thread = new Thread(() => result = BuildAndRasterize(path, pixelSize))
        {
            IsBackground = true,
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        return result;
    }

    private static BitmapSource? BuildAndRasterize(string path, int pixelSize)
    {
        try
        {
            XDocument document;
            var settings = new XmlReaderSettings
            {
                // Editors write a DOCTYPE; nothing in it is needed, and nothing
                // in it may be fetched.
                DtdProcessing = DtdProcessing.Ignore,
                XmlResolver = null,
            };
            using (var stream = File.OpenRead(path))
            using (var reader = XmlReader.Create(stream, settings))
            {
                document = XDocument.Load(reader);
            }

            if (document.Root is not { } root || root.Name.LocalName != "svg" ||
                ViewBoxOf(root) is not { } viewBox || viewBox.Width <= 0 || viewBox.Height <= 0 ||
                root.Attributes().Any(a => DecliningAttributes.Contains(a.Name.LocalName)) ||
                Paint.Default.Inherit(root) is not { } rootPaint)
            {
                return null;
            }

            var group = new DrawingGroup();
            if (!AddChildren(root, rootPaint, group) || group.Children.Count == 0)
            {
                return null;
            }

            double scale = pixelSize / Math.Max(viewBox.Width, viewBox.Height);
            int width = Math.Max(1, (int)Math.Round(viewBox.Width * scale));
            int height = Math.Max(1, (int)Math.Round(viewBox.Height * scale));

            var visual = new DrawingVisual();
            using (var context = visual.RenderOpen())
            {
                // Pushed outermost first: the drawing is moved to the viewBox's
                // origin, then scaled - a Material Symbol's viewBox starts at
                // y = -960.
                context.PushTransform(new ScaleTransform(scale, scale));
                context.PushTransform(new TranslateTransform(-viewBox.X, -viewBox.Y));
                context.PushClip(new RectangleGeometry(viewBox));
                context.DrawDrawing(group);
            }

            var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or XmlException
                                     or FormatException or ArgumentException or InvalidOperationException
                                     or OverflowException or NotSupportedException)
        {
            // Unreadable, malformed, or path data WPF's parser does not take:
            // the shell's turn.
            return null;
        }
    }

    // viewBox, or failing that width and height in plain user units.
    private static Rect? ViewBoxOf(XElement root)
    {
        if ((string?)root.Attribute("viewBox") is { } box)
        {
            var numbers = Numbers(box);
            return numbers is { Count: 4 } ? new Rect(numbers[0], numbers[1], numbers[2], numbers[3]) : null;
        }

        return Length((string?)root.Attribute("width")) is { } width &&
               Length((string?)root.Attribute("height")) is { } height
            ? new Rect(0, 0, width, height)
            : null;
    }

    // What makes a file "not one this draws". Each of these changes what the
    // shapes look like, and drawing the shapes without it would be a picture
    // of something else.
    private static readonly HashSet<string> DecliningAttributes = new(StringComparer.Ordinal)
    {
        "transform", "style", "clip-path", "mask", "filter",
        "marker-start", "marker-mid", "marker-end", "stroke-dasharray",
    };

    private static bool AddChildren(XElement parent, Paint paint, DrawingGroup group)
    {
        foreach (var element in parent.Elements())
        {
            string name = element.Name.LocalName;

            // Editors' own bookkeeping (Inkscape's namedview, metadata, a
            // title) draws nothing. A <defs> can only be reached by url(#),
            // which is declined where it is used.
            if (element.Name.Namespace != parent.Name.Namespace ||
                name is "title" or "desc" or "metadata" or "defs")
            {
                continue;
            }

            if (element.Attributes().Any(a => DecliningAttributes.Contains(a.Name.LocalName)))
            {
                return false;
            }

            if ((string?)element.Attribute("display") == "none" ||
                (string?)element.Attribute("visibility") == "hidden")
            {
                continue;
            }

            var own = paint.Inherit(element);
            if (own is null)
            {
                return false;
            }

            if (name == "g")
            {
                if (!AddChildren(element, own, group))
                {
                    return false;
                }
                continue;
            }

            Geometry? geometry = name switch
            {
                "path" => PathGeometryOf(element, own),
                "rect" => RectOf(element),
                "circle" => Number(element, "r") is { } r
                    ? new EllipseGeometry(new Point(Number(element, "cx") ?? 0, Number(element, "cy") ?? 0), r, r)
                    : null,
                "ellipse" => Number(element, "rx") is { } rx && Number(element, "ry") is { } ry
                    ? new EllipseGeometry(new Point(Number(element, "cx") ?? 0, Number(element, "cy") ?? 0), rx, ry)
                    : null,
                "line" => new LineGeometry(
                    new Point(Number(element, "x1") ?? 0, Number(element, "y1") ?? 0),
                    new Point(Number(element, "x2") ?? 0, Number(element, "y2") ?? 0)),
                "polyline" => PolyOf(element, own, closed: false),
                "polygon" => PolyOf(element, own, closed: true),
                // <style>, <text>, <use>, <image>, gradients outside <defs>...
                _ => null,
            };
            if (geometry is null)
            {
                return false;
            }

            var drawing = own.Drawing(geometry);
            if (drawing is not null)
            {
                group.Children.Add(drawing);
            }
        }

        return true;
    }

    private static Geometry? PathGeometryOf(XElement element, Paint paint)
    {
        if ((string?)element.Attribute("d") is not { Length: > 0 } data)
        {
            return null;
        }

        // WPF's markup defaults to even-odd unless it opens with F1; SVG's
        // default is nonzero.
        return Geometry.Parse((paint.EvenOdd ? "F0 " : "F1 ") + data);
    }

    private static Geometry? RectOf(XElement element)
    {
        if (Number(element, "width") is not { } width || Number(element, "height") is not { } height ||
            width <= 0 || height <= 0)
        {
            return null;
        }

        // One corner radius given stands for both, as SVG reads it.
        double? rx = Number(element, "rx");
        double? ry = Number(element, "ry");
        return new RectangleGeometry(
            new Rect(Number(element, "x") ?? 0, Number(element, "y") ?? 0, width, height),
            rx ?? ry ?? 0, ry ?? rx ?? 0);
    }

    private static Geometry? PolyOf(XElement element, Paint paint, bool closed)
    {
        if (Numbers((string?)element.Attribute("points")) is not { Count: >= 4 } numbers || numbers.Count % 2 != 0)
        {
            return null;
        }

        var figure = new PathFigure { StartPoint = new Point(numbers[0], numbers[1]), IsClosed = closed };
        for (int i = 2; i < numbers.Count; i += 2)
        {
            figure.Segments.Add(new LineSegment(new Point(numbers[i], numbers[i + 1]), isStroked: true));
        }

        return new PathGeometry(new[] { figure })
        {
            FillRule = paint.EvenOdd ? FillRule.EvenOdd : FillRule.Nonzero,
        };
    }

    private static double? Number(XElement element, string attribute)
        => Length((string?)element.Attribute(attribute));

    // A plain number, or one in px - the unit an icon's width is written in.
    // Anything relative (%, em) has nothing here to be relative to.
    private static double? Length(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        text = text.Trim();
        if (text.EndsWith("px", StringComparison.OrdinalIgnoreCase))
        {
            text = text[..^2];
        }

        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
            ? value
            : null;
    }

    private static List<double>? Numbers(string? text)
    {
        if (text is null)
        {
            return null;
        }

        var numbers = new List<double>();
        foreach (string part in text.Split(new[] { ' ', ',', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (!double.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
            {
                return null;
            }
            numbers.Add(value);
        }
        return numbers;
    }

    // The presentation attributes, inherited down the tree the way SVG
    // inherits them.
    private sealed record Paint(
        Brush? Fill, Brush? Stroke, double StrokeWidth, PenLineCap Cap, PenLineJoin Join,
        double MiterLimit, bool EvenOdd, double FillOpacity, double StrokeOpacity)
    {
        // SVG's initial values: black fill, no stroke.
        public static readonly Paint Default = new(
            Brushes.Black, null, 1, PenLineCap.Flat, PenLineJoin.Miter, 4, false, 1, 1);

        // Null when the element asks for something not drawn here (a url()
        // paint - a gradient or a pattern).
        public Paint? Inherit(XElement element)
        {
            var next = this;
            if ((string?)element.Attribute("fill") is { } fill)
            {
                if (!TryBrush(fill, out var brush))
                {
                    return null;
                }
                next = next with { Fill = brush };
            }
            if ((string?)element.Attribute("stroke") is { } stroke)
            {
                if (!TryBrush(stroke, out var brush))
                {
                    return null;
                }
                next = next with { Stroke = brush };
            }
            if (Length((string?)element.Attribute("stroke-width")) is { } width)
            {
                next = next with { StrokeWidth = width };
            }
            if (Length((string?)element.Attribute("stroke-miterlimit")) is { } miter)
            {
                next = next with { MiterLimit = miter };
            }
            if (Length((string?)element.Attribute("fill-opacity")) is { } fillOpacity)
            {
                next = next with { FillOpacity = fillOpacity };
            }
            if (Length((string?)element.Attribute("stroke-opacity")) is { } strokeOpacity)
            {
                next = next with { StrokeOpacity = strokeOpacity };
            }
            switch ((string?)element.Attribute("stroke-linecap"))
            {
                case "round": next = next with { Cap = PenLineCap.Round }; break;
                case "square": next = next with { Cap = PenLineCap.Square }; break;
                case "butt": next = next with { Cap = PenLineCap.Flat }; break;
            }
            switch ((string?)element.Attribute("stroke-linejoin"))
            {
                case "round": next = next with { Join = PenLineJoin.Round }; break;
                case "bevel": next = next with { Join = PenLineJoin.Bevel }; break;
                case "miter": next = next with { Join = PenLineJoin.Miter }; break;
            }
            switch ((string?)element.Attribute("fill-rule"))
            {
                case "evenodd": next = next with { EvenOdd = true }; break;
                case "nonzero": next = next with { EvenOdd = false }; break;
            }
            return next;
        }

        public GeometryDrawing? Drawing(Geometry geometry)
        {
            Brush? fill = WithOpacity(Fill, FillOpacity);
            Brush? stroke = WithOpacity(Stroke, StrokeOpacity);
            if (fill is null && stroke is null)
            {
                return null;
            }

            Pen? pen = stroke is null || StrokeWidth <= 0
                ? null
                : new Pen(stroke, StrokeWidth)
                {
                    StartLineCap = Cap,
                    EndLineCap = Cap,
                    LineJoin = Join,
                    MiterLimit = MiterLimit,
                };
            return new GeometryDrawing(fill, pen, geometry);
        }

        private static Brush? WithOpacity(Brush? brush, double opacity)
        {
            if (brush is null || opacity >= 1)
            {
                return brush;
            }
            if (opacity <= 0)
            {
                return null;
            }

            var faded = brush.Clone();
            faded.Opacity = opacity;
            return faded;
        }

        // none, currentColor (black: nothing outside sets it), #rgb, #rrggbb,
        // rgb(r, g, b), and the named colours, which WPF knows by the same
        // names CSS does.
        private static bool TryBrush(string value, out Brush? brush)
        {
            brush = null;
            value = value.Trim();
            if (value is "none" or "transparent")
            {
                return true;
            }
            if (value == "currentColor")
            {
                brush = Brushes.Black;
                return true;
            }
            if (value.StartsWith("url(", StringComparison.Ordinal))
            {
                return false;
            }
            if (value.StartsWith('#') && value.Length == 4)
            {
                value = "#" + value[1] + value[1] + value[2] + value[2] + value[3] + value[3];
            }
            if (value.StartsWith("rgb(", StringComparison.OrdinalIgnoreCase) && value.EndsWith(')'))
            {
                var channels = Numbers(value[4..^1]);
                if (channels is not { Count: 3 })
                {
                    return false;
                }
                brush = new SolidColorBrush(Color.FromRgb(
                    (byte)Math.Clamp(channels[0], 0, 255),
                    (byte)Math.Clamp(channels[1], 0, 255),
                    (byte)Math.Clamp(channels[2], 0, 255)));
                return true;
            }

            try
            {
                if (ColorConverter.ConvertFromString(value) is Color color)
                {
                    brush = new SolidColorBrush(color);
                    return true;
                }
            }
            catch (FormatException)
            {
            }
            return false;
        }
    }
}
