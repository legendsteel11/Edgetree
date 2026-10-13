using System.Text.RegularExpressions;
using ICSharpCode.AvalonEdit.Document;

namespace SidebarExplorer.App;

// The text view's 서식 commands (2026-10-13): the right-click menu's submenu,
// and Ctrl+B / Ctrl+I. A Markdown file is shown as its source, so a format
// IS its marks - these put them in or take them out, and the highlighting
// shows the result as soon as they land: a heading grows, bold turns bold,
// a strikethrough is struck. Only for a Markdown file being edited
// (CanFormat); the menu does not show the submenu otherwise.
//
// EVERY ROW IS A TOGGLE. Asked of text that already carries the format, it
// takes the marks away again, so the menu needs no "remove" twin of each row
// - the way the format buttons of Word and of every Markdown editor behave.
//
// ONE UNDO STEP EACH. Every edit a command makes runs inside one document
// update, which AvalonEdit's undo stack records as one group.
//
// NO COLOUR (decided with the author, 2026-10-13). Markdown has no syntax for
// it; the only way is inline HTML, which GitHub strips and which this view
// would show as tags, not as colour.
public sealed partial class ViewerTextEditor
{
    public enum LineFormat
    {
        Bullet,
        Numbered,
        Quote,
    }

    public bool CanFormat => IsEditing && _markdown;

    // ----- Marks around a run: bold, italic, strikethrough, inline code -------
    //
    // The run is the selection; with none, the word under the caret; with no
    // word there, nothing - and then a pair of marks goes in with the caret
    // between them, ready to type into. Spaces at the ends of a selection are
    // left outside the marks, because "** word **" is not bold in Markdown.
    //
    // WHETHER THE FORMAT IS ALREADY THERE is read from the runs of the mark's
    // character on each side, counted inside and just outside the run alike,
    // so a selection of "**word**" and one of "word" get the same answer. One
    // character serves two formats - * is italic, ** bold, *** both - so for
    // italic the run's length decides: an odd run carries it.
    public void ToggleWrap(string marker)
    {
        if (!CanFormat)
        {
            return;
        }

        var document = Document;
        char mark = marker[0];
        int width = marker.Length;
        bool hadSelection = !TextArea.Selection.IsEmpty;
        int caret = TextArea.Caret.Offset;

        int start, end;
        if (hadSelection)
        {
            (start, end) = TrimmedSelection();
        }
        else
        {
            (start, end) = WordAround(caret);
        }

        int innerLeft = CountRun(document, start, end, mark, forward: true);
        int innerRight = CountRun(document, end, start + innerLeft, mark, forward: false);
        int contentStart = start + innerLeft;
        int contentEnd = end - innerRight;
        int left = innerLeft + CountRun(document, start, 0, mark, forward: false);
        int right = innerRight + CountRun(document, end, document.TextLength, mark, forward: true);

        int run = Math.Min(left, right);
        bool present = mark == '*' && width == 1 ? run % 2 == 1 : run >= width;

        using (document.RunUpdate())
        {
            // The far side first, so the near side's offset still holds.
            if (present)
            {
                document.Remove(contentEnd, width);
                document.Remove(contentStart - width, width);
            }
            else
            {
                document.Insert(contentEnd, marker);
                document.Insert(contentStart, marker);
            }
        }

        int newStart = contentStart + (present ? -width : width);
        int length = contentEnd - contentStart;
        if (hadSelection && length > 0)
        {
            Select(newStart, length);
        }
        else
        {
            // Where it was inside the word, or between the new marks.
            TextArea.ClearSelection();
            TextArea.Caret.Offset = newStart + Math.Clamp(caret - contentStart, 0, length);
        }
    }

    // ----- Marks at the head of lines: headings, lists, quotes ----------------

    private static readonly Regex HeadingMark = new(@"^[ ]{0,3}(#{1,6})(?=[ \t]|$)[ \t]*");
    private static readonly Regex BulletMark = new(@"^([ \t]*)[-*+][ \t]+");
    private static readonly Regex NumberMark = new(@"^([ \t]*)\d{1,9}[.)][ \t]+");
    private static readonly Regex QuoteMark = new(@"^[ ]{0,3}>[ \t]?");

    // 제목 1-3, and 본문 (level 0) taking the heading away. Asked for the level
    // every line already has, it takes it away too.
    public void SetHeading(int level)
    {
        if (!CanFormat || FormatLines() is not { Count: > 0 } lines)
        {
            return;
        }

        bool off = level == 0 || lines.All(line => HeadingLevelOf(Document.GetText(line)) == level);
        string heading = off ? string.Empty : new string('#', level) + " ";
        RewriteLineHeads(lines, (text, _) => (HeadingMark.Match(text).Length, heading));
    }

    // A list mark of either kind gives way to the other, so the one row turns
    // a bulleted list numbered and the other turns it back. Numbers run 1, 2,
    // 3 down the lines it is given.
    public void ToggleLineFormat(LineFormat format)
    {
        if (!CanFormat || FormatLines() is not { Count: > 0 } lines)
        {
            return;
        }

        bool off = lines.All(line => LineHas(Document.GetText(line), format));
        RewriteLineHeads(lines, (text, index) =>
        {
            if (format == LineFormat.Quote)
            {
                var quote = QuoteMark.Match(text);
                return off ? (quote.Length, string.Empty)
                    : quote.Success ? (0, string.Empty)
                    : (0, "> ");
            }

            var list = BulletMark.Match(text);
            if (!list.Success)
            {
                list = NumberMark.Match(text);
            }
            string indent = list.Success ? list.Groups[1].Value : LeadingWhitespace(text);
            int remove = list.Success ? list.Length : indent.Length;
            if (off)
            {
                return (remove, indent);
            }
            return (remove, indent + (format == LineFormat.Bullet ? "- " : $"{index + 1}. "));
        });
    }

    // What the caret's line carries, for the ticks in the 서식 submenu.
    public int CaretHeadingLevel => HeadingLevelOf(CaretLineText());

    public bool CaretLineHas(LineFormat format) => LineHas(CaretLineText(), format);

    // ----- Link --------------------------------------------------------------
    //
    // [text](address) around the selection, or the word under the caret, with
    // the caret left in the empty () for the address. A selection that IS an
    // address goes inside the () instead, with the caret in [] for the text.
    // With nothing to wrap, an empty pair with the caret in [].
    public void InsertLink()
    {
        if (!CanFormat)
        {
            return;
        }

        var (start, end) = TextArea.Selection.IsEmpty
            ? WordAround(TextArea.Caret.Offset)
            : TrimmedSelection();
        string text = Document.GetText(start, end - start);
        bool address = LooksLikeAddress(text);

        Document.Replace(start, end - start, address ? $"[]({text})" : $"[{text}]()");
        TextArea.ClearSelection();
        TextArea.Caret.Offset = address || text.Length == 0 ? start + 1 : start + text.Length + 3;
    }

    private static bool LooksLikeAddress(string text)
        => text.Length > 0 && !text.Any(char.IsWhiteSpace) &&
           (text.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ||
            Uri.TryCreate(text, UriKind.Absolute, out var uri) &&
            uri.Scheme is "http" or "https" or "ftp" or "mailto");

    // ----- Shared ------------------------------------------------------------

    private (int Start, int End) TrimmedSelection()
    {
        var range = TextArea.Selection.SurroundingSegment;
        int start = range.Offset;
        int end = range.EndOffset;
        while (start < end && char.IsWhiteSpace(Document.GetCharAt(start)))
        {
            start++;
        }
        while (end > start && char.IsWhiteSpace(Document.GetCharAt(end - 1)))
        {
            end--;
        }
        return (start, end);
    }

    // Letters (Hangul included), digits and _, so a snake_case name is one
    // word. An empty range at the offset when it touches no word.
    private (int Start, int End) WordAround(int offset)
    {
        var document = Document;
        int start = offset;
        int end = offset;
        while (start > 0 && IsWordChar(document.GetCharAt(start - 1)))
        {
            start--;
        }
        while (end < document.TextLength && IsWordChar(document.GetCharAt(end)))
        {
            end++;
        }
        return (start, end);
    }

    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    // How many of `mark` sit side by side from `from` toward `limit` - forward
    // reads from, from+1, ...; backward reads from-1, from-2, ....
    private static int CountRun(TextDocument document, int from, int limit, char mark, bool forward)
    {
        int at = from;
        if (forward)
        {
            while (at < limit && document.GetCharAt(at) == mark)
            {
                at++;
            }
            return at - from;
        }

        while (at > limit && document.GetCharAt(at - 1) == mark)
        {
            at--;
        }
        return from - at;
    }

    // The lines a line format applies to: every line the selection touches,
    // except a last one it only reaches the very start of - a drag down to
    // the start of the next line means the lines above it. With several,
    // blank ones are passed over (a blank line given "- " is an empty list
    // item, not a gap); a single one is used even when blank, which is how a
    // list is started on an empty line.
    private List<DocumentLine> FormatLines()
    {
        var document = Document;
        int from = TextArea.Caret.Offset;
        int to = from;
        if (!TextArea.Selection.IsEmpty)
        {
            var range = TextArea.Selection.SurroundingSegment;
            from = range.Offset;
            to = range.EndOffset;
        }

        var first = document.GetLineByOffset(from);
        var last = document.GetLineByOffset(to);
        if (last != first && to == last.Offset)
        {
            last = last.PreviousLine;
        }

        var lines = new List<DocumentLine>();
        for (var line = first; line is not null; line = line.NextLine)
        {
            lines.Add(line);
            if (line == last)
            {
                break;
            }
        }

        if (lines.Count > 1)
        {
            lines.RemoveAll(line => string.IsNullOrWhiteSpace(document.GetText(line)));
        }
        return lines;
    }

    // Replaces the head of each line - `rewrite` answers how many characters
    // to take off the start and what to put there - as one undo step. A head
    // that would come out the same is left alone, so the undo step holds only
    // real changes.
    //
    // The caret needs no help: AvalonEdit carries one standing at, or inside,
    // the replaced head to the end of the new one - measured 2026-10-13, so 제목
    // 2 on an empty line leaves the caret after "## ", ready for the heading.
    private void RewriteLineHeads(List<DocumentLine> lines, Func<string, int, (int Remove, string Insert)> rewrite)
    {
        var document = Document;
        using (document.RunUpdate())
        {
            for (int i = 0; i < lines.Count; i++)
            {
                var line = lines[i];
                var (remove, insert) = rewrite(document.GetText(line), i);
                if (!string.Equals(document.GetText(line.Offset, remove), insert, StringComparison.Ordinal))
                {
                    document.Replace(line.Offset, remove, insert);
                }
            }
        }
    }

    private string CaretLineText()
        => Document.GetText(Document.GetLineByOffset(TextArea.Caret.Offset));

    private static int HeadingLevelOf(string text)
        => HeadingMark.Match(text) is { Success: true } heading ? heading.Groups[1].Length : 0;

    private static bool LineHas(string text, LineFormat format) => format switch
    {
        LineFormat.Bullet => BulletMark.IsMatch(text),
        LineFormat.Numbered => NumberMark.IsMatch(text),
        _ => QuoteMark.IsMatch(text),
    };

    private static string LeadingWhitespace(string text)
        => text[..(text.Length - text.TrimStart(' ', '\t').Length)];
}
