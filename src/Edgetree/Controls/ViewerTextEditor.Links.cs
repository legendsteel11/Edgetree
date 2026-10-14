using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Input;
using ICSharpCode.AvalonEdit.Rendering;
using Cursors = System.Windows.Input.Cursors;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;

namespace SidebarExplorer.App;

// MARKDOWN LINKS THAT GO SOMEWHERE (2026-10-13, on request): Ctrl+click a
// [text](target) - or an ![image](target) - in a Markdown file, as in VS
// Code's editor, and the target opens: a file in this app (MainWindow takes
// it from MarkdownLinkInvoked), a web or mail address outside it, a #heading
// in this same text. Without it, a document that links to its neighbours
// sent the reader to another program to follow them.
//
// AvalonEdit's own link support finds bare URLs only and starts them itself
// (Process.Start inside VisualLineLinkText), with no event to take the click
// - so the Markdown form is found here, by an element generator like its
// LinkElementGenerator, and the click is ours. A bare https:// in the text
// is still AvalonEdit's, as before. Ctrl as the modifier for both, as
// AvalonEdit's option already has it (RequireControlModifierForHyperlinkClick):
// a plain click is how a caret or a selection starts.
//
// The element is the whole link, text and target, so either half can be
// Ctrl+clicked. It changes nothing about how the link is drawn: the
// highlighting's Link colour still paints it.
public sealed partial class ViewerTextEditor
{
    // A Markdown link's target, raw as written ("README.md", "../a b.md#x",
    // "https://..."), Ctrl+clicked. MainWindow resolves it.
    public event Action<string>? MarkdownLinkInvoked;

    // [text](target) or ![alt](target), the target bare or in <...>, with an
    // optional "title" after it. Brackets nested inside the text are not
    // followed - as rare in practice as they are costly to match.
    private static readonly Regex MarkdownLink = new(
        @"!?\[[^\]\r\n]*\]\((?:<(?<target>[^>\r\n]+)>|(?<target>[^)\s]+))(?:[ \t]+(?:""[^""\r\n]*""|'[^'\r\n]*'))?\)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private void AttachMarkdownLinks()
        => TextArea.TextView.ElementGenerators.Add(new MarkdownLinkGenerator(this));

    private sealed class MarkdownLinkGenerator : VisualLineElementGenerator
    {
        private readonly ViewerTextEditor _owner;

        public MarkdownLinkGenerator(ViewerTextEditor owner) => _owner = owner;

        // The next link at or after startOffset on this visual line - the
        // same search LinkElementGenerator makes for URLs.
        private Match? Find(int startOffset, out int matchOffset)
        {
            matchOffset = -1;
            if (!_owner._markdown)
            {
                return null;
            }

            int end = CurrentContext.VisualLine.LastDocumentLine.EndOffset;
            var text = CurrentContext.GetText(startOffset, end - startOffset);
            var match = MarkdownLink.Match(text.Text, text.Offset, text.Count);
            if (!match.Success)
            {
                return null;
            }

            matchOffset = match.Index - text.Offset + startOffset;
            return match;
        }

        public override int GetFirstInterestedOffset(int startOffset)
        {
            Find(startOffset, out int offset);
            return offset;
        }

        public override VisualLineElement? ConstructElement(int offset)
        {
            var match = Find(offset, out int matchOffset);
            return match is not null && matchOffset == offset
                ? new MarkdownLinkElement(CurrentContext.VisualLine, match.Length, match.Groups["target"].Value, _owner)
                : null;
        }
    }

    private sealed class MarkdownLinkElement : VisualLineText
    {
        private readonly string _target;
        private readonly ViewerTextEditor _owner;

        public MarkdownLinkElement(VisualLine parent, int length, string target, ViewerTextEditor owner)
            : base(parent, length)
        {
            _target = target;
            _owner = owner;
        }

        // The hand with Ctrl held, as AvalonEdit's own links show it.
        protected override void OnQueryCursor(QueryCursorEventArgs e)
        {
            if (Keyboard.Modifiers == ModifierKeys.Control)
            {
                e.Cursor = Cursors.Hand;
                e.Handled = true;
            }
        }

        protected override void OnMouseDown(MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left && !e.Handled &&
                Keyboard.Modifiers == ModifierKeys.Control)
            {
                _owner.MarkdownLinkInvoked?.Invoke(_target);
                e.Handled = true;
            }
        }

        // A colourizer splitting the element keeps each part a link.
        protected override VisualLineText CreateInstance(int length)
            => new MarkdownLinkElement(ParentVisualLine, length, _target, _owner);
    }

    // ----- #heading -----------------------------------------------------------

    private static readonly Regex HeadingLine = new(
        @"^[ ]{0,3}#{1,6}[ \t]+(?<title>.*?)[ \t]*#*[ \t]*$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // Puts the caret on the heading a #anchor names, the way GitHub (and VS
    // Code's preview) name them: lower case, punctuation dropped, spaces as
    // hyphens, and a repeated heading numbered -1, -2 on. The caret's own
    // reveal brings the line to the top (RevealCaret). False when no heading
    // matches; the text then stays where it was.
    public bool JumpToHeading(string anchor)
    {
        if (_session is null)
        {
            return false;
        }

        string wanted;
        try
        {
            wanted = HeadingSlug(Uri.UnescapeDataString(anchor.TrimStart('#')));
        }
        catch (UriFormatException)
        {
            return false;
        }

        // Only the lines the highlighting draws as headings (ScanHeadings,
        // shared with the sticky headings): a "# comment" inside a fenced
        // code block counted too until 2026-10-14 (pre-release review), which
        // took the jump into the code and numbered the real heading after it
        // as a repeat - GitHub counts neither.
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (int number in ScanHeadings(Document.Text).Lines)
        {
            var line = Document.GetLineByNumber(number);
            var match = HeadingLine.Match(Document.GetText(line));
            if (!match.Success)
            {
                continue;
            }

            string slug = HeadingSlug(match.Groups["title"].Value);
            if (seen.TryGetValue(slug, out int count))
            {
                seen[slug] = count + 1;
                slug = $"{slug}-{count + 1}";
            }
            else
            {
                seen[slug] = 0;
            }

            if (string.Equals(slug, wanted, StringComparison.Ordinal))
            {
                TextArea.ClearSelection();
                TextArea.Caret.Offset = line.Offset;
                return true;
            }
        }
        return false;
    }

    // GitHub's: letters, marks, numbers and connector punctuation (the _)
    // kept, spaces and hyphens as hyphens, everything else - the ** and `
    // of inline formatting with it - dropped.
    private static string HeadingSlug(string heading)
    {
        var slug = new StringBuilder(heading.Length);
        foreach (char c in heading.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(c) ||
                char.GetUnicodeCategory(c) is System.Globalization.UnicodeCategory.NonSpacingMark
                    or System.Globalization.UnicodeCategory.SpacingCombiningMark
                    or System.Globalization.UnicodeCategory.ConnectorPunctuation)
            {
                slug.Append(c);
            }
            else if (c is ' ' or '-')
            {
                slug.Append('-');
            }
        }
        return slug.ToString();
    }
}
