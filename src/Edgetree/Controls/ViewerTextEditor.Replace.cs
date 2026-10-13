using System.Reflection;
using System.Windows;
using ICSharpCode.AvalonEdit.Search;
using SidebarExplorer.App.Services;
// The project also references WinForms, which has a ToolTip of its own.
using ToolTip = System.Windows.Controls.ToolTip;

namespace SidebarExplorer.App;

// The replace row's work (2026-10-13, edit step 3). AvalonEdit's find panel
// has no replace, so this does it with the panel's own search: the strategy
// is built from the same pattern and options the find row is bound to, by
// the same factory the panel uses - so 바꾸기 changes exactly what find has
// marked, no more and no less.
//
// 바꾸기 replaces the match that is SELECTED and moves on to the next one; with
// no match selected it only moves - Notepad's way, so the first press shows
// what the second will change. 모두 바꾸기 is ONE undo step (one document
// update), and says how many it changed where find says 일치하는 항목 없음.
public sealed partial class ViewerTextEditor
{
    // True when something was replaced.
    public bool ReplaceNext(string replacement)
    {
        if (!IsEditing || CurrentSearchStrategy() is not { } strategy)
        {
            return false;
        }

        if (TextArea.Selection.IsEmpty)
        {
            FindNextMatch();
            return false;
        }

        // The selection is a match only if the search finds exactly it.
        // Asked of the whole text, filtered to the selection, so a regex
        // lookbehind still sees what is before it.
        var selected = TextArea.Selection.SurroundingSegment;
        var match = strategy.FindAll(Document, selected.Offset, selected.Length).FirstOrDefault();
        if (match is null || match.Offset != selected.Offset || match.Length != selected.Length)
        {
            FindNextMatch();
            return false;
        }

        string text = ReplacementFor(match, replacement);
        Document.Replace(match.Offset, match.Length, text);

        // The next match is looked for from the END of the new text, not
        // through the panel's FindNext: that one starts a character past the
        // caret, and skips a match that begins right where this one ended
        // ("baa" with the caret at 1 went to the match at 2, measured
        // 2026-10-13) - "aaa", a to b, would change the first and third.
        // Wraps to the top like the panel does.
        int after = match.Offset + text.Length;
        var next = strategy.FindAll(Document, after, Document.TextLength - after).FirstOrDefault()
                   ?? strategy.FindAll(Document, 0, Document.TextLength).FirstOrDefault();
        if (next is null)
        {
            TextArea.ClearSelection();
            TextArea.Caret.Offset = after;
        }
        else
        {
            DriveFind(() => Select(next.Offset, next.Length));
        }
        return true;
    }

    // How many were replaced.
    public int ReplaceAll(string replacement)
    {
        if (!IsEditing || CurrentSearchStrategy() is not { } strategy)
        {
            return 0;
        }

        var matches = strategy.FindAll(Document, 0, Document.TextLength).ToList();
        if (matches.Count == 0)
        {
            return 0;
        }

        // Back to front, so every offset still holds when its turn comes.
        using (Document.RunUpdate())
        {
            for (int i = matches.Count - 1; i >= 0; i--)
            {
                Document.Replace(matches[i].Offset, matches[i].Length, ReplacementFor(matches[i], replacement));
            }
        }

        ShowFindMessage(string.Format(Strings.TextReplacedCount, matches.Count));
        return matches.Count;
    }

    // Null when find is closed or empty, or the pattern does not compile as a
    // regex - the find row is already showing that one in red.
    private ISearchStrategy? CurrentSearchStrategy()
    {
        if (!FindIsOpen || string.IsNullOrEmpty(_search.SearchPattern))
        {
            return null;
        }

        try
        {
            return SearchStrategyFactory.Create(
                _search.SearchPattern, !_search.MatchCase, _search.WholeWords,
                _search.UseRegex ? SearchMode.RegEx : SearchMode.Normal);
        }
        catch (SearchPatternException)
        {
            return null;
        }
    }

    // With regex on, $1 and the like are the match's groups. With it off the
    // text goes in as typed: AvalonEdit's ReplaceWith runs every replacement
    // through Match.Result, which reads a "$" in plain text as a group ("$0z"
    // for "a" came back "az", measured 2026-10-13).
    private string ReplacementFor(ISearchResult match, string replacement)
        => _search.UseRegex ? match.ReplaceWith(replacement) : replacement;

    // THE COUNT GOES IN THE PANEL'S OWN TOOLTIP, the one that says 일치하는
    // 항목 없음 - same look, same place, by being the same thing. A tooltip of
    // our own was the first idea and cannot work: the panel searches again
    // whenever the text changes, so a replace-all that leaves no match makes
    // it open its "no matches" at that very spot (measured 2026-10-13), and
    // the two would sit on top of each other. Writing into the panel's makes
    // the count the last word, and it goes the way the panel's message always
    // goes - when the next search finds something, or find closes.
    //
    // The field is private to AvalonEdit, read by reflection. The package is
    // pinned (6.3.1.120); a version that renamed it would simply show no
    // count, and replacing would work as before.
    private static readonly FieldInfo? FindMessageField =
        typeof(SearchPanel).GetField("messageView", BindingFlags.NonPublic | BindingFlags.Instance);

    private void ShowFindMessage(string text)
    {
        if (FindMessageField?.GetValue(_search) is ToolTip message &&
            _search.Template?.FindName("PART_searchTextBox", _search) is UIElement anchor)
        {
            message.PlacementTarget = anchor;
            message.Content = text;
            message.IsOpen = true;
        }
    }
}
