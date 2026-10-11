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
    // Whole file up to here; past it, only the head. PROVISIONAL numbers, to be
    // settled by measuring a read over the NAS - the panel's job is "what is
    // this file", and the first megabyte answers that for a log of any size,
    // while reading a whole multi-gigabyte one into memory answers nothing the
    // head did not. A head-only result is read-only by design: saving it would
    // write the head back as the whole file.
    public const long WholeFileLimit = 10L * 1024 * 1024;
    public const int HeadBytes = 1024 * 1024;

    public sealed record Result(
        string Text,
        Encoding Encoding,
        bool HasBom,
        bool Truncated,
        long FileLength);

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
        return new Result(text, encoding, bomLength > 0, truncated, length);
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
