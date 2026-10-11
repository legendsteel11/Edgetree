using System.IO;
using System.Text;

namespace SidebarExplorer.App.Services;

// Writes the text view's edits back to the file (2026-10-11, the second half
// of the TXT/MD round). Plain .NET for TextFileReader's reason: the editor
// hands over a string and an encoding, and everything about the file itself
// happens here, on a worker thread.
public static class TextFileWriter
{
    public enum Outcome
    {
        Saved,
        // The file on disk is not the one that was read: another program (or
        // another copy of this app) has written to it since.
        ChangedOnDisk,
        // Gone since it was read - deleted, renamed or moved away.
        Missing,
        // The text holds a character the file's code page has no byte for.
        Unencodable,
    }

    // FileLength and LastWriteUtc are the file as it stands after the write,
    // read back through a fresh handle - the stamp the NEXT save compares
    // against.
    public sealed record Result(Outcome Outcome, long FileLength = 0, DateTime LastWriteUtc = default);

    // expectedLength / expectedLastWriteUtc are what the file was when it was
    // read (or last saved). With overwrite false a file that no longer matches
    // them is left untouched and the caller asks; true is the answer to that
    // question, and writes whatever is there.
    public static Result Save(
        string path,
        string text,
        Encoding encoding,
        bool withBom,
        long expectedLength,
        DateTime expectedLastWriteUtc,
        bool overwrite)
    {
        // ENCODED BEFORE THE FILE IS TOUCHED. A character the code page cannot
        // hold is found here, while the file on disk is still whole - and the
        // write below then has nothing left that can fail halfway except the
        // disk itself.
        byte[] body;
        try
        {
            body = Strict(encoding).GetBytes(text);
        }
        catch (EncoderFallbackException)
        {
            return new Result(Outcome.Unencodable);
        }
        byte[] preamble = withBom ? encoding.GetPreamble() : Array.Empty<byte>();

        if (!overwrite)
        {
            if (!File.Exists(path))
            {
                return new Result(Outcome.Missing);
            }

            var (length, lastWrite) = Stamp(path);
            if (length != expectedLength || lastWrite != expectedLastWriteUtc)
            {
                return new Result(Outcome.ChangedOnDisk);
            }
        }

        // IN PLACE, the way Notepad saves, rather than a temporary file renamed
        // over the original. A rename makes a new file: it drops hard links,
        // can lose the original's ACL and alternate streams (the "downloaded
        // from the internet" mark among them), and on a NAS that indexes by
        // file it reads as a delete and a create. Writing into the existing
        // file keeps all of that, and the encode above keeps the window where
        // a failure could leave it short down to the write itself.
        //
        // OpenOrCreate and an explicit SetLength, not FileMode.Create: Create
        // is CREATE_ALWAYS, which Windows refuses with "access denied" on an
        // existing HIDDEN or SYSTEM file - so a hidden note could be opened,
        // edited and then not saved.
        using (var stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.Write, FileShare.Read))
        {
            stream.Write(preamble);
            stream.Write(body);
            stream.SetLength(stream.Position);
        }

        var (newLength, newLastWrite) = Stamp(path);
        return new Result(Outcome.Saved, newLength, newLastWrite);
    }

    // Through a handle, not FileInfo. FileInfo asks by path, and an SMB client
    // answers a path query from its file information cache - ten seconds by
    // default - so a write another machine made to a file on a share could go
    // unseen, and our own write could read back as the old stamp. Opening the
    // file goes to the server. Same source as TextFileReader's stamp, so the
    // two compare like for like.
    private static (long Length, DateTime LastWriteUtc) Stamp(string path)
    {
        using var handle = File.OpenHandle(
            path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return (RandomAccess.GetLength(handle), File.GetLastWriteTimeUtc(handle));
    }

    // The Unicode encodings can write every character there is, so they are
    // used as they come. A code page gets a copy that THROWS on a character it
    // has no byte for, where the default would quietly write "?" in its place
    // and the file would come back different from what was on screen.
    // The code-page provider is registered by TextFileReader, and nothing is
    // saved in this process that was not first read by it.
    private static Encoding Strict(Encoding encoding) => encoding switch
    {
        UTF8Encoding or UnicodeEncoding or UTF32Encoding => encoding,
        _ => Encoding.GetEncoding(encoding.CodePage, EncoderFallback.ExceptionFallback, DecoderFallback.ReplacementFallback),
    };
}
