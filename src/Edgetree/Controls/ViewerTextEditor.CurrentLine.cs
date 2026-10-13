using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Rendering;
// The project also references WinForms, whose namespaces name the same
// types; these are the WPF ones.
using Brush = System.Windows.Media.Brush;

namespace SidebarExplorer.App;

// THE CARET'S LINE ON A FAINT BAND (2026-10-13, on request), as VS Code's
// editor marks it - so after a click, where the click landed stays visible.
//
// VS CODE'S RULES, READ FROM ITS CODE (the installed 2026 build): the band
// covers the whole line, every wrapped row of it, not just the row the caret
// is on; it goes away while anything is selected, so it never competes with
// a selection; and it stays when the keyboard leaves the editor, in the same
// colour (its inactive colour defaults to the active one, and the 2026 themes
// set neither apart). The colour is its 2026 themes' step from the editor's
// ground - see ViewerTextCurrentLineBackground in MainWindow's
// ApplyEdgeInkOverrides.
//
// WHILE READING, ONLY ONCE THE TEXT HAS BEEN CLICKED OR MOVED THROUGH (the
// author's choice, the same day). Reading shows no caret, so a band on the
// first line the moment a file opened would mark a place nobody chose.
// Editing shows it from the start, as VS Code does. Arrows and the page keys
// count as moving through - they move the hidden caret, and the band then
// says where it went.
//
// NOT AvalonEdit's own Options.HighlightCurrentLine: that one stays on
// through a selection, and has no notion of the reading rule.
public sealed partial class ViewerTextEditor
{
    // The text on screen has been clicked or moved through since it came up.
    private bool _caretPlaced;

    // The line the band was last asked for, or -1: what decides whether a
    // caret move has to redraw the layer at all.
    private int _bandLine = -1;

    private void AttachCurrentLine()
    {
        TextArea.TextView.BackgroundRenderers.Add(new CurrentLineRenderer(this));
        TextArea.Caret.PositionChanged += (_, _) => RefreshCurrentLine();
        TextArea.SelectionChanged += (_, _) => RefreshCurrentLine();
        TextArea.PreviewMouseDown += (_, _) => PlaceCurrentLine();
        TextArea.PreviewKeyDown += (_, e) =>
        {
            if (e.Key is Key.Up or Key.Down or Key.Left or Key.Right or
                         Key.PageUp or Key.PageDown or Key.Home or Key.End)
            {
                PlaceCurrentLine();
            }
        };
    }

    // The line to band, or -1 for none.
    private int CurrentLineToDraw
        => _session is not null && TextArea.Selection.IsEmpty && (!IsReadOnly || _caretPlaced)
            ? TextArea.Caret.Line
            : -1;

    private void PlaceCurrentLine()
    {
        if (!_caretPlaced)
        {
            _caretPlaced = true;
            RefreshCurrentLine();
        }
    }

    // Anything that can show, hide or move the band. The layer is drawn
    // again only when the answer changed; a scroll or a new layout redraws it
    // anyway.
    private void RefreshCurrentLine()
    {
        int line = CurrentLineToDraw;
        if (line != _bandLine)
        {
            _bandLine = line;
            TextArea.TextView.InvalidateLayer(KnownLayer.Background);
        }
    }

    // Under the selection and the find marks, over the ground. Squared to
    // whole pixels, as AvalonEdit draws its own current line.
    private sealed class CurrentLineRenderer : IBackgroundRenderer
    {
        private readonly ViewerTextEditor _owner;

        public CurrentLineRenderer(ViewerTextEditor owner) => _owner = owner;

        public KnownLayer Layer => KnownLayer.Background;

        public void Draw(TextView textView, DrawingContext drawingContext)
        {
            int line = _owner.CurrentLineToDraw;
            if (line < 1 ||
                textView.GetVisualLine(line) is not { } visual ||
                _owner.TryFindResource("ViewerTextCurrentLineBackground") is not Brush brush)
            {
                return;
            }

            var builder = new BackgroundGeometryBuilder { AlignToWholePixels = true };
            builder.AddRectangle(textView, new Rect(
                0, visual.VisualTop - textView.VerticalOffset, textView.ActualWidth, visual.Height));
            if (builder.CreateGeometry() is { } band)
            {
                drawingContext.DrawGeometry(brush, null, band);
            }
        }
    }
}
