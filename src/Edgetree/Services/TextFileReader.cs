using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace SidebarExplorer.App.Services;

// Reads a text file for the panel's text view (2026-10-11): the bytes, which
// encoding they are in, and whether the file was cut short.
//
// Kept free of the editor on purpose. The panel creates its editor only when a
// text file is actually opened (see ViewerTextEditor), and this half is plain
// .NET - so it can be reasoned about, and replaced, without touching the
// control that draws the result.
public static class TextFileReader
{
    // Whole file up to here; past it, only the head. The panel's job is "what
    // is this file", and the first megabyte answers that for a log of any
    // size, while reading a whole multi-gigabyte one into memory answers
    // nothing the head did not. A head-only result is read-only by design:
    // saving it would write the head back as the whole file.
    //
    // SETTLED BY MEASUREMENT (2026-10-11, a harness driving the built editor,
    // ordinary many-line files, read + document + first layout): 1MB 44ms,
    // 5MB 86ms, 10MB 153ms, 20MB 297ms, 50MB 501ms; scrolling to either end
    // stays under 20ms at every size. What grows is MEMORY, about five times
    // the file - 53MB held at 10MB, 105MB at 20MB, 262MB at 50MB - and that is
    // what keeps the line at 10MB. The share adds little: the NAS read 161MB/s
    // when measured for films (2026-08), about 60ms per 10MB.
    public const long WholeFileLimit = 10L * 1024 * 1024;
    public const int HeadBytes = 1024 * 1024;

    // ONE LONG LINE IS WHAT ACTUALLY STOPS THE APP, not a big file (measured
    // the same day). AvalonEdit lays out a whole document line at once when it
    // wraps, so the cost of showing a line grows with its length - faster
    // than linearly past a point: 10K characters 22ms, 20K 37ms, 100K 165ms,
    // 500K 1.2s, 1M 4.6s with the UI frozen, 5M not done after a minute. A
    // hundred 20K lines cost 37ms, so it is the length of a line, never the
    // number of them. The head limit does not help - the first megabyte of a
    // one-line file is a one-megabyte line.
    //
    // So a line past LongLineLimit is shown in SplitLength pieces, each its
    // own display line (the view says so, and it is read-only: the text on
    // screen is no longer the file's, so it must never be saved back). 20K
    // keeps every ordinary text untouched; 10K pieces cost about 22ms each,
    // and only the ones on screen are ever laid out.
    public const int LongLineLimit = 20_000;
    public const int SplitLength = 10_000;

    // The kinds the text view opens - one list, read by the panel and by the
    // tree row's 보기 mark alike, so the mark can never offer a file the panel
    // would then refuse. .log, .ini, .json and the like are one line each here.
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".md", ".markdown"
    };

    public static bool IsTextFile(string path) => Extensions.Contains(Path.GetExtension(path));

    public sealed record Result(
        string Text,
        Encoding Encoding,
        bool HasBom,
        bool Truncated,
        long FileLength,
        bool LongLinesSplit);

    static TextFileReader()
    {
        // .NET (Core) knows only the Unicode encodings and Latin-1 until this
        // provider is registered; without it code page 949 does not exist and
        // every BOM-less Korean file would come out as UTF-8 noise. It ships
        // in the shared framework, so this costs no package.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public static Result Read(string path, CancellationToken cancellation)
    {
        // ReadWrite | Delete sharing so a log another program is still writing
        // to - the common case for exactly the files that hit the head limit -
        // opens instead of failing with a sharing violation.
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 1,
            FileOptions.SequentialScan);

        long length = stream.Length;
        bool truncated = length > WholeFileLimit;
        int toRead = truncated ? HeadBytes : (int)length;

        byte[] bytes = new byte[toRead];
        int read = 0;
        // In chunks rather than one call, so a cancel (the selection moved on)
        // is noticed between them on a slow share instead of after the whole
        // read. 256KB is a few round trips' worth of SMB payload.
        while (read < toRead)
        {
            cancellation.ThrowIfCancellationRequested();
            int got = stream.Read(bytes, read, Math.Min(256 * 1024, toRead - read));
            if (got == 0)
            {
                // The file shrank while it was being read (a log rotated).
                break;
            }
            read += got;
        }

        var span = new ReadOnlySpan<byte>(bytes, 0, read);
        var (encoding, bomLength) = Detect(span, truncated);
        string text = encoding.GetString(TrimIncompleteTail(span[bomLength..], encoding, truncated));
        cancellation.ThrowIfCancellationRequested();

        string? split = SplitLongLines(text);
        return new Result(split ?? text, encoding, bomLength > 0, truncated, length, split is not null);
    }

    // Null when no line is past LongLineLimit, which is nearly every file:
    // those pay one scan for line breaks and nothing else. The scan is
    // IndexOfAny over a span, which .NET vectorises, so it reads the text at
    // memory speed rather than a character at a time.
    private static string? SplitLongLines(string text)
    {
        ReadOnlySpan<char> all = text;
        if (!HasLongLine(all))
        {
            return null;
        }

        var sb = new StringBuilder(text.Length + text.Length / SplitLength + 16);
        int start = 0;
        while (true)
        {
            int rel = all[start..].IndexOfAny('\r', '\n');
            int end = rel < 0 ? all.Length : start + rel;
            AppendSplit(sb, all[start..end]);
            if (rel < 0)
            {
                break;
            }
            // The break itself, as it was: a CR LF pair arrives as two steps,
            // the second an empty line between them, and is copied unchanged.
            sb.Append(all[end]);
            start = end + 1;
        }
        return sb.ToString();
    }

    private static bool HasLongLine(ReadOnlySpan<char> all)
    {
        int start = 0;
        while (true)
        {
            int rel = all[start..].IndexOfAny('\r', '\n');
            int end = rel < 0 ? all.Length : start + rel;
            if (end - start > LongLineLimit)
            {
                return true;
            }
            if (rel < 0)
            {
                return false;
            }
            start = end + 1;
        }
    }

    // A line within the limit goes through whole. A longer one is cut into
    // pieces of at most SplitLength: after a space or tab in the last 200
    // characters when there is one, so words are not cut where it can be
    // helped, and never between the two halves of a surrogate pair.
    private static void AppendSplit(StringBuilder sb, ReadOnlySpan<char> line)
    {
        if (line.Length <= LongLineLimit)
        {
            sb.Append(line);
            return;
        }

        const int Lookback = 200;
        while (line.Length > SplitLength)
        {
            int cut = SplitLength;
            int space = line[(SplitLength - Lookback)..SplitLength].LastIndexOfAny(' ', '\t');
            if (space >= 0)
            {
                cut = SplitLength - Lookback + space + 1;
            }
            else if (char.IsHighSurrogate(line[cut - 1]))
            {
                cut--;
            }
            sb.Append(line[..cut]).Append('\n');
            line = line[cut..];
        }
        sb.Append(line);
    }

    // BOM first, then "is this valid UTF-8", then the PC's own ANSI code page.
    // The order is the 2026-08-14 review's: a BOM is a statement, UTF-8
    // validity is very nearly one (legacy multi-byte text almost never happens
    // to form valid UTF-8 sequences), and what is left is the code page
    // Notepad would have called ANSI on this machine.
    private static (Encoding Encoding, int BomLength) Detect(ReadOnlySpan<byte> bytes, bool truncated)
    {
        if (bytes.StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }))
        {
            return (new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), 3);
        }
        // UTF-32 LE before UTF-16 LE: its BOM begins with the UTF-16 one.
        if (bytes.StartsWith(new byte[] { 0xFF, 0xFE, 0x00, 0x00 }))
        {
            return (new UTF32Encoding(bigEndian: false, byteOrderMark: true), 4);
        }
        if (bytes.StartsWith(new byte[] { 0xFF, 0xFE }))
        {
            return (new UnicodeEncoding(bigEndian: false, byteOrderMark: true), 2);
        }
        if (bytes.StartsWith(new byte[] { 0xFE, 0xFF }))
        {
            return (new UnicodeEncoding(bigEndian: true, byteOrderMark: true), 2);
        }

        if (IsValidUtf8(TrimIncompleteUtf8(bytes, truncated)))
        {
            return (new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), 0);
        }

        return (AnsiEncoding(), 0);
    }

    private static readonly UTF8Encoding StrictUtf8 =
        new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private static bool IsValidUtf8(ReadOnlySpan<byte> bytes)
    {
        try
        {
            StrictUtf8.GetCharCount(bytes);
            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }

    // A head cut at an arbitrary byte can end partway through a multi-byte
    // UTF-8 sequence. Left in, that partial character fails the validity test
    // above and a perfectly good UTF-8 file would be read as ANSI - so up to
    // three trailing bytes that only START a sequence are set aside first.
    // Only ever for a truncated read: a whole file that ends mid-sequence is
    // simply not valid UTF-8.
    private static ReadOnlySpan<byte> TrimIncompleteUtf8(ReadOnlySpan<byte> bytes, bool truncated)
    {
        if (!truncated)
        {
            return bytes;
        }

        int end = bytes.Length;
        for (int back = 1; back <= Math.Min(3, bytes.Length); back++)
        {
            byte b = bytes[end - back];
            if ((b & 0xC0) == 0x80)
            {
                // A continuation byte: keep looking for its lead.
                continue;
            }
            if ((b & 0x80) == 0)
            {
                // ASCII: nothing is cut.
                return bytes;
            }
            int needed = (b & 0xE0) == 0xC0 ? 2 : (b & 0xF0) == 0xE0 ? 3 : (b & 0xF8) == 0xF0 ? 4 : 1;
            return needed > back ? bytes[..(end - back)] : bytes;
        }
        return bytes;
    }

    private static ReadOnlySpan<byte> TrimIncompleteTail(ReadOnlySpan<byte> bytes, Encoding encoding, bool truncated)
        => encoding is UTF8Encoding ? TrimIncompleteUtf8(bytes, truncated) : bytes;

    [DllImport("kernel32.dll")]
    private static extern uint GetACP();

    // The system's ANSI code page - 949 on a Korean install, 1252 on a Western
    // one, 932 on a Japanese one - rather than 949 written in. The app is used
    // outside Korea, and a hard-coded 949 would turn every legacy Western
    // file's accented letters into Hangul.
    //
    // UNLESS the PC has "Beta: Use Unicode UTF-8 for worldwide language
    // support" on, where the ANSI code page IS UTF-8 (65001) - and that is the
    // one answer already ruled out by getting here. The display language's
    // own legacy code page is the next best guess at what such a file is in.
    private static Encoding AnsiEncoding()
    {
        int codePage = (int)GetACP();
        if (codePage == 65001)
        {
            codePage = CultureInfo.CurrentUICulture.TextInfo.ANSICodePage;
        }

        try
        {
            return Encoding.GetEncoding(codePage);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            return Encoding.Latin1;
        }
    }
}
