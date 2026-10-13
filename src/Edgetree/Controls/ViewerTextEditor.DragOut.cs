using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using ICSharpCode.AvalonEdit;
// The project also references WinForms, whose namespaces name the same
// types; these are the WPF ones.
using Cursors = System.Windows.Input.Cursors;
using DataFormats = System.Windows.DataFormats;
using DataObject = System.Windows.DataObject;
using DragDropEffects = System.Windows.DragDropEffects;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Point = System.Windows.Point;

namespace SidebarExplorer.App;

// DRAGGING A SELECTION OUT (2026-10-13, on request): press inside the
// selection, move, and the text goes to whatever accepts it - another
// editor, a chat box, a browser field - in the reading view and the editing
// one alike.
//
// OURS, NOT AVALONEDIT'S. Its own text drag (Options.EnableTextDragDrop,
// left off) offers the drop target a MOVE while the text is editable, and a
// target that takes it cuts the text out of this document; it also moves
// text within the document, where a press inside the selection that slips a
// few pixels shifts it a character or two (the tree logs such micro-drags
// from ordinary clicks - selfdrop.log). Neither can be switched off on its
// own, so this replaces it with the one thing asked for: a COPY, out.
// Dropped back on the text it does nothing (the editor has AllowDrop off),
// and moving text inside the document by drag is not offered (2026-10-13):
// it is hard to aim in any editor, and it is the half that can damage text.
//
// The press is taken from AvalonEdit only when it lands on a selected
// character with no modifier - Shift extends a selection, Ctrl opens a link,
// Alt starts a column selection, and those stay AvalonEdit's. "Inside" is the
// right-click's own test (GetPositionFromPoint + Selection.Contains), so the
// two buttons agree about where the selection is. A press that never becomes
// a drag is the click it would have been: caret there, selection gone.
public sealed partial class ViewerTextEditor
{
    // Where a press that may become a drag landed (TextArea coordinates, for
    // the threshold), and the caret position it means if it stays a click.
    private Point? _dragOutPressPoint;
    private TextViewPosition? _dragOutClickPosition;

    private void AttachDragOut()
    {
        TextArea.PreviewMouseLeftButtonDown += TextArea_DragOutPress;
        TextArea.PreviewMouseMove += TextArea_DragOutMove;
        TextArea.PreviewMouseLeftButtonUp += TextArea_DragOutRelease;
        // The capture is the press's life: taken away (the window losing
        // activation, say), the press is over.
        TextArea.LostMouseCapture += (_, _) => ForgetDragOutPress();
        // Notepad's and VS Code's sign that a selection can be picked up: the
        // arrow over it instead of the I-beam. On the TextView, which the
        // event bubbles through before it reaches the TextArea, where
        // AvalonEdit's own handler would draw the I-beam (its drag being off)
        // and skips an event already handled. A link under Ctrl is decided
        // further down still, and Ctrl keeps this out of the way anyway.
        TextArea.TextView.QueryCursor += (_, e) =>
        {
            if (Keyboard.Modifiers == ModifierKeys.None && PressablePositionInSelection(e) is not null)
            {
                e.Cursor = Cursors.Arrow;
                e.Handled = true;
            }
        };
    }

    // The caret position under the pointer when it is over a selected
    // character of the text itself - not the line numbers, not past the
    // view's edge. Null otherwise.
    private TextViewPosition? PressablePositionInSelection(MouseEventArgs e)
    {
        if (TextArea.Selection.IsEmpty)
        {
            return null;
        }

        var inView = e.GetPosition(TextArea.TextView);
        if (inView.X < 0 || inView.Y < 0 ||
            inView.X >= TextArea.TextView.ActualWidth || inView.Y >= TextArea.TextView.ActualHeight)
        {
            return null;
        }

        return GetPositionFromPoint(e.GetPosition(this)) is { } position &&
               TextArea.Selection.Contains(Document.GetOffset(position.Location))
            ? position
            : null;
    }

    private void TextArea_DragOutPress(object sender, MouseButtonEventArgs e)
    {
        ForgetDragOutPress();
        if (e.ClickCount != 1 || Keyboard.Modifiers != ModifierKeys.None ||
            PressablePositionInSelection(e) is not { } position)
        {
            return;
        }

        // Kept from AvalonEdit, whose press would start a new selection right
        // here and lose the one about to be dragged. Its press also took the
        // keyboard, so that is done here.
        e.Handled = true;
        TextArea.Focus();
        if (TextArea.CaptureMouse())
        {
            _dragOutPressPoint = e.GetPosition(TextArea);
            _dragOutClickPosition = position;
        }
    }

    private void TextArea_DragOutMove(object sender, MouseEventArgs e)
    {
        if (_dragOutPressPoint is not { } start)
        {
            return;
        }

        // A release this never saw (the capture holds it, but a press can
        // outlive its button in odd input paths): nothing to drag.
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            ForgetDragOutPress();
            return;
        }

        var now = e.GetPosition(TextArea);
        if (Math.Abs(now.X - start.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(now.Y - start.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        var clickPosition = _dragOutClickPosition;
        // The capture goes first: a capture and an OLE drag do not mix.
        ForgetDragOutPress();
        e.Handled = true;

        var data = new DataObject(DataFormats.UnicodeText, TextArea.Selection.GetText(), autoConvert: true);
        var result = DragDropEffects.None;
        try
        {
            result = DragDrop.DoDragDrop(TextArea, data, DragDropEffects.Copy);
        }
        catch (COMException)
        {
            // The drop target's own failure, delivered here over OLE
            // (RPC_E_SERVERFAULT) - the tree's drag-out met it from Explorer
            // (MainWindow.DragOutFiles). Nothing was delivered; the selection
            // stays as it was.
        }

        // Taken nowhere and let go over the text: a press that wandered past
        // the threshold, meant as the click it started as.
        if (result == DragDropEffects.None && clickPosition is { } position && PointerOverTextView())
        {
            PlaceCaretAsClick(position);
        }
    }

    private void TextArea_DragOutRelease(object sender, MouseButtonEventArgs e)
    {
        if (_dragOutPressPoint is null)
        {
            return;
        }

        var clickPosition = _dragOutClickPosition;
        ForgetDragOutPress();
        e.Handled = true;
        if (clickPosition is { } position)
        {
            PlaceCaretAsClick(position);
        }
    }

    private void ForgetDragOutPress()
    {
        if (_dragOutPressPoint is null)
        {
            return;
        }

        // Cleared before the release, whose LostMouseCapture comes back here.
        _dragOutPressPoint = null;
        _dragOutClickPosition = null;
        if (TextArea.IsMouseCaptured)
        {
            TextArea.ReleaseMouseCapture();
        }
    }

    // What AvalonEdit's own plain click does: the selection goes, the caret
    // lands, and up/down keep to the new column.
    private void PlaceCaretAsClick(TextViewPosition position)
    {
        TextArea.ClearSelection();
        TextArea.Caret.Position = position;
        TextArea.Caret.DesiredXPos = double.NaN;
    }

    private bool PointerOverTextView()
    {
        var p = Mouse.GetPosition(TextArea.TextView);
        return p.X >= 0 && p.Y >= 0 &&
               p.X < TextArea.TextView.ActualWidth && p.Y < TextArea.TextView.ActualHeight;
    }
}
