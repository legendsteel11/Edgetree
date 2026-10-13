using System.IO;
using System.Windows.Media;
using System.Windows.Threading;
using ICSharpCode.AvalonEdit.Document;
using SidebarExplorer.App.Services;
// The project also references WinForms, whose namespaces name the same
// types; these are the WPF ones.
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;

namespace SidebarExplorer.App;

// CHANGED LINES SHOWN IN THE LINE NUMBERS (2026-10-13, on request): a line
// edited since the file was last saved has its number in one colour, a line
// added in another. What VS Code's gutter says, said by the numbers this view
// already draws (SmallLineNumberMargin), so it shows only while they are on.
//
// AGAINST THE SAVED TEXT, NOT A LOG OF EDITS (decided with the author, the
// same day). Each line of the saved text is kept as a hash (TextSession
// .Baseline), and after each pause in typing the document is compared with it
// line by line. So a line changed and changed back is unmarked again, undoing
// to the saved text clears everything, and a file restored after a crash is
// compared with the file on disk. Recording edits as they happen would be
// simpler, and wrong in all three. The baseline is taken where the text and
// the file are the same: read (Load), saved (CompleteSave), reloaded
// (Reload); a restored file reads its disk copy for it.
//
// DELETED LINES ARE NOT SHOWN: there is no number left to colour. VS Code
// draws a mark between lines for them; left for later, if it is missed.
//
// THE COST. Hashing a ten-megabyte text and comparing it run on a worker,
// after a 300ms pause, never per keystroke; most edits are settled by
// trimming the lines both ends share, and Myers' comparison runs only on what
// is left, with a bound on the differences it will align (MaxAlignedEdits).
// Past that bound - a replace-all over a large file - the whole changed span
// is marked rather than aligned. A text equal to the saved one (the undo
// stack's original-file marker) is cleared at once without comparing. What
// runs per frame is one dictionary lookup per visible line, in the margin.
public sealed partial class ViewerTextEditor
{
    internal enum LineChange : byte
    {
        None,
        Modified,
        Added,
    }

    // Up to this many lines inserted or deleted in all are aligned exactly;
    // Myers' trace for it is about 2·D² ints (8 MB at this bound), held only
    // while the comparison runs.
    private const int MaxAlignedEdits = 1000;

    private DispatcherTimer? _changeMarkTimer;
    private int _changeMarkRun;

    // Amber for edited, green for added (VS Code's gutter: blue and green) -
    // a first pick, put up to be judged on screen. Darker in the light theme
    // so the numbers stay legible on a pale ground.
    private static readonly Brush ModifiedDark = Frozen(Color.FromRgb(0xE0, 0xA1, 0x4A));
    private static readonly Brush AddedDark = Frozen(Color.FromRgb(0x6C, 0xC0, 0x7E));
    private static readonly Brush ModifiedLight = Frozen(Color.FromRgb(0xB0, 0x66, 0x0E));
    private static readonly Brush AddedLight = Frozen(Color.FromRgb(0x2E, 0x8B, 0x45));

    private static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    // What the margin draws a line's number in.
    private Brush LineNumberBrush(DocumentLine line, Brush plain)
    {
        if (_session is not { } session || !session.LineChanges.TryGetValue(line, out var change))
        {
            return plain;
        }

        return change switch
        {
            LineChange.Modified => _light ? ModifiedLight : ModifiedDark,
            LineChange.Added => _light ? AddedLight : AddedDark,
            _ => plain,
        };
    }

    // Any change to the text on screen, a new text on screen, the numbers
    // turned on, the saved state reached or left. Cheap: a text equal to the
    // saved one is cleared here and now; anything else waits for a pause.
    private void ScheduleChangeMarks()
    {
        if (_session is not { } session || _lineNumbers is null)
        {
            return;
        }

        if (!session.IsModified)
        {
            _changeMarkTimer?.Stop();
            ++_changeMarkRun;
            if (session.LineChanges.Count > 0)
            {
                session.LineChanges.Clear();
                _lineNumbers.InvalidateVisual();
            }
            return;
        }

        _changeMarkTimer ??= new DispatcherTimer(
            TimeSpan.FromMilliseconds(300), DispatcherPriority.Background,
            (_, _) => RunChangeMarks(), Dispatcher);
        _changeMarkTimer.Stop();
        _changeMarkTimer.Start();
    }

    private async void RunChangeMarks()
    {
        _changeMarkTimer?.Stop();
        if (_session is not { } session || _lineNumbers is null || !session.IsModified ||
            session.Baseline is not { } baseline)
        {
            return;
        }

        int run = ++_changeMarkRun;
        var document = session.Document;
        var version = document.Version;
        var snapshot = document.CreateSnapshot();

        // Caught whole: this is an async void off a timer, and anything
        // escaping it would end the process over a colour. WHAT THIS HIDES: a
        // fault in the comparison itself shows only as marks that stop
        // following the text - the harness in the scratchpad (changemarks)
        // is where it was checked.
        List<(int Line, LineChange Change)> marks;
        try
        {
            marks = await Task.Run(() => CompareLines(baseline, HashLines(snapshot.Text)));
        }
        catch (Exception)
        {
            return;
        }

        if (run != _changeMarkRun || !ReferenceEquals(_session, session))
        {
            return;
        }

        // Typed while the comparison ran: its line numbers belong to a text
        // that is no longer there, so it runs again on the next pause.
        if (version is null || document.Version is not { } now ||
            !now.BelongsToSameDocumentAs(version) || now.CompareAge(version) != 0)
        {
            ScheduleChangeMarks();
            return;
        }

        // Keyed by the line OBJECT, not its number: an edit before the next
        // comparison moves the lines under it, and the marks go with them.
        session.LineChanges.Clear();
        foreach (var (line, change) in marks)
        {
            session.LineChanges[document.GetLineByNumber(line)] = change;
        }
        _lineNumbers?.InvalidateVisual();
    }

    // A baseline taken from text handed over as a string - the text a save
    // has just written. Hashed on a worker; the generation keeps an older
    // hash from landing after a newer baseline.
    private void SetBaseline(TextSession session, string text)
    {
        int generation = ++session.BaselineGeneration;
        _ = SetBaselineAsync(session, generation, () => HashLines(text));
    }

    // A file restored from the disk copy: its baseline is the file as it is
    // on disk now. A file that is gone is compared with nothing, so every line
    // reads as added; one that can no longer be read whole, or at all, gets
    // no marks rather than wrong ones.
    private void SetBaselineFromDisk(TextSession session)
    {
        int generation = ++session.BaselineGeneration;
        string path = session.Path;
        _ = SetBaselineAsync(session, generation, () =>
        {
            if (!File.Exists(path))
            {
                return Array.Empty<long>();
            }

            var file = TextFileReader.Read(path, CancellationToken.None);
            return file.Truncated || file.LongLinesSplit ? null : HashLines(file.Text);
        });
    }

    private async Task SetBaselineAsync(TextSession session, int generation, Func<long[]?> hash)
    {
        session.Baseline = null;
        long[]? baseline;
        try
        {
            baseline = await Task.Run(hash);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
                                       or NotSupportedException or OutOfMemoryException)
        {
            return;
        }

        if (session.BaselineGeneration != generation)
        {
            return;
        }

        session.Baseline = baseline;
        if (ReferenceEquals(_session, session))
        {
            ScheduleChangeMarks();
        }
    }

    // ----- The comparison (worker thread) ---------------------------------------

    // One 64-bit FNV-1a hash per line, split where AvalonEdit splits lines -
    // at \r\n, \n or \r - so hash N is document line N+1. A string ending in
    // a line break has an empty last line, as the document does.
    internal static long[] HashLines(string text)
    {
        const ulong Basis = 14695981039346656037UL;
        const ulong Prime = 1099511628211UL;
        var hashes = new List<long>(text.Length / 32 + 1);
        ulong hash = Basis;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c is '\r' or '\n')
            {
                hashes.Add(unchecked((long)hash));
                hash = Basis;
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                {
                    i++;
                }
                continue;
            }

            hash = unchecked((hash ^ c) * Prime);
        }
        hashes.Add(unchecked((long)hash));
        return hashes.ToArray();
    }

    // The current text's lines (b) against the saved text's (a): every line of
    // b that differs, 1-based, as edited or added. Within one run of
    // differences, as many new lines as old ones went are counted edited and
    // the rest added - one line changed and two typed after it read as
    // exactly that.
    internal static List<(int Line, LineChange Change)> CompareLines(long[] a, long[] b)
    {
        var marks = new List<(int Line, LineChange Change)>();
        int start = 0;
        while (start < a.Length && start < b.Length && a[start] == b[start])
        {
            start++;
        }
        int endA = a.Length;
        int endB = b.Length;
        while (endA > start && endB > start && a[endA - 1] == b[endB - 1])
        {
            endA--;
            endB--;
        }

        int lengthA = endA - start;
        int lengthB = endB - start;
        if (lengthB == 0)
        {
            return marks;
        }
        if (lengthA == 0)
        {
            for (int j = 0; j < lengthB; j++)
            {
                marks.Add((start + j + 1, LineChange.Added));
            }
            return marks;
        }

        if (Align(a, start, lengthA, b, start, lengthB) is not { } aligned)
        {
            // Too different to align within the bound: the span is marked as
            // a whole, edited as far as the old one reached and added past it.
            for (int j = 0; j < lengthB; j++)
            {
                marks.Add((start + j + 1, j < lengthA ? LineChange.Modified : LineChange.Added));
            }
            return marks;
        }

        // Kept lines pair up in order, so between two pairs lies one run of
        // old lines gone and new lines come.
        var (keptA, keptB) = aligned;
        int i = 0;
        int k = 0;
        while (i < lengthA || k < lengthB)
        {
            if (i < lengthA && k < lengthB && keptA[i] && keptB[k])
            {
                i++;
                k++;
                continue;
            }

            int gone = 0;
            while (i < lengthA && !keptA[i])
            {
                gone++;
                i++;
            }
            int firstNew = k;
            while (k < lengthB && !keptB[k])
            {
                k++;
            }
            for (int j = firstNew; j < k; j++)
            {
                marks.Add((start + j + 1, j - firstNew < gone ? LineChange.Modified : LineChange.Added));
            }
        }
        return marks;
    }

    // Myers' O(ND) comparison of a[a0, a0+n) with b[b0, b0+m): which lines of
    // each are kept by a shortest edit script. Null past MaxAlignedEdits.
    private static (bool[] KeptA, bool[] KeptB)? Align(long[] a, int a0, int n, long[] b, int b0, int m)
    {
        int max = Math.Min(n + m, MaxAlignedEdits);
        int offset = max + 1;
        var v = new int[2 * max + 3];
        var trace = new List<int[]>();
        for (int d = 0; d <= max; d++)
        {
            trace.Add((int[])v.Clone());
            for (int k = -d; k <= d; k += 2)
            {
                int x = k == -d || (k != d && v[offset + k - 1] < v[offset + k + 1])
                    ? v[offset + k + 1]
                    : v[offset + k - 1] + 1;
                int y = x - k;
                while (x < n && y < m && a[a0 + x] == b[b0 + y])
                {
                    x++;
                    y++;
                }
                v[offset + k] = x;
                if (x >= n && y >= m)
                {
                    return Backtrack(trace, d, n, m, offset);
                }
            }
        }
        return null;
    }

    private static (bool[] KeptA, bool[] KeptB) Backtrack(List<int[]> trace, int depth, int n, int m, int offset)
    {
        var keptA = new bool[n];
        var keptB = new bool[m];
        int x = n;
        int y = m;
        for (int d = depth; d > 0; d--)
        {
            var v = trace[d];
            int k = x - y;
            int previousK = k == -d || (k != d && v[offset + k - 1] < v[offset + k + 1]) ? k + 1 : k - 1;
            int previousX = v[offset + previousK];
            int previousY = previousX - previousK;
            while (x > previousX && y > previousY)
            {
                x--;
                y--;
                keptA[x] = true;
                keptB[y] = true;
            }
            x = previousX;
            y = previousY;
        }
        while (x > 0 && y > 0)
        {
            x--;
            y--;
            keptA[x] = true;
            keptB[y] = true;
        }
        return (keptA, keptB);
    }
}
