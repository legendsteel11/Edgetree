using System.Windows;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Rendering;
using ICSharpCode.AvalonEdit.Utils;
// The project also references WinForms, whose namespaces name the same
// types; these are the WPF ones.
using Pen = System.Windows.Media.Pen;

namespace SidebarExplorer.App;

public sealed partial class ViewerTextEditor
{
    private FindOutlineLayer? _findOutlineLayer;

    private void InitFindOutline()
    {
        var textView = TextArea.TextView;
        _findOutlineLayer = new FindOutlineLayer(this);
        textView.InsertLayer(_findOutlineLayer, KnownLayer.Selection, LayerInsertionPosition.Above);
        // The layer is drawn once per change it is told about - a layer of the
        // text view is not redrawn with the selection's own.
        TextArea.SelectionChanged += (_, _) => _findOutlineLayer.Redraw();
        textView.ScrollOffsetChanged += (_, _) => _findOutlineLayer.Redraw();
        textView.VisualLinesChanged += (_, _) => _findOutlineLayer.Redraw();
    }

    private void SetFindOutlineShown(bool shown)
    {
        if (_findOutlineLayer is not null)
        {
            _findOutlineLayer.Shown = shown;
        }
    }

    // THE CURRENT MATCH'S OUTLINE, DRAWN INSIDE THE BOX (2026-11-27, on
    // report: its left side was cut on a match at a line's start, then its top
    // on line 1). It was AvalonEdit's SelectionBorder, a 1px pen centred on
    // the selection's edge, so half of it lay outside the box - and on the
    // text view's first column or first row, outside the text view, where two
    // clips cut it (the view's own, and the scroll viewer's at the padding).
    // Measured in a harness at 150%: the old outline's left column read 66 of
    // 255 and its top row did not show at all; widening the clips only moved
    // the cut, and letting the view paint past its top would have let a
    // half-scrolled line show through the padding too.
    //
    // So the outline is a layer of its own between the selection's fill and
    // the text (TextView.InsertLayer, AvalonEdit's own way in), drawn with a
    // pen two device pixels wide clipped to the selection's shape: the half
    // that shows is one pixel inside the box on every side, which no clip can
    // reach. Aliased, so that pixel is a crisp row rather than a pale pair
    // (152 beside 255 anti-aliased, 48 - the fill - aliased). Same harness:
    // left, top and right read 255 with line numbers on or off, scrolled, and
    // mid-line. In the text's own ink, as before.
    //
    // THE SIDES THEN WENT ONE PIXEL OUT (same day, on report): inside the box
    // they touched the first and last letters, the box being exactly the
    // letters' width; top and bottom have the line spacing and stay inside.
    // A side out past the view's left edge is what ClipToBounds off and the
    // room PlaceTextEdgeRoom keeps there are for.
    private sealed class FindOutlineLayer : FrameworkElement
    {
        private readonly ViewerTextEditor _owner;
        private bool _shown;

        public FindOutlineLayer(ViewerTextEditor owner)
        {
            _owner = owner;
            IsHitTestVisible = false;
            RenderOptions.SetEdgeMode(this, EdgeMode.Aliased);
        }

        public bool Shown
        {
            get => _shown;
            set
            {
                if (_shown != value)
                {
                    _shown = value;
                    InvalidateVisual();
                }
            }
        }

        public void Redraw()
        {
            if (_shown)
            {
                InvalidateVisual();
            }
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            var textView = _owner.TextArea.TextView;
            var selection = _owner.TextArea.Selection;
            if (!_shown || selection.IsEmpty || !textView.VisualLinesValid || _owner.Foreground is not { } ink)
            {
                return;
            }

            // The box is the match's own, one device pixel wider each side: a
            // match's box is exactly as wide as its letters, and a line drawn
            // inside it touched the first and last of them, where above and
            // below the line's spacing leaves room on its own.
            double pixel = PixelSnapHelpers.GetPixelSize(textView).Width;
            var builder = new BackgroundGeometryBuilder { AlignToWholePixels = true, CornerRadius = 0 };
            foreach (var segment in selection.Segments)
            {
                foreach (var row in BackgroundGeometryBuilder.GetRectsForSegment(textView, segment))
                {
                    builder.AddRectangle(textView, new Rect(row.X - pixel, row.Y, row.Width + 2 * pixel, row.Height));
                }
            }
            if (builder.CreateGeometry() is not { } shape)
            {
                return;
            }

            drawingContext.PushClip(shape);
            drawingContext.DrawGeometry(null, new Pen(ink, 2 * pixel), shape);
            drawingContext.Pop();
        }
    }
}
