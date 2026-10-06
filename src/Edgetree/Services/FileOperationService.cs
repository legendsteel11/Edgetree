using System.IO;
using System.Runtime.InteropServices;
using Microsoft.VisualBasic.FileIO;
using Clipboard = System.Windows.Clipboard;

namespace SidebarExplorer.App.Services;

public static class FileOperationService
{
    // THE CLIPBOARD CAN REFUSE, AND REFUSING MUST NOT END THE APP (issue #3,
    // 2026-09-06). Windows lets one process hold the clipboard open at a time,
    // so any other program touching it at that instant makes this throw
    // CLIPBRD_E_CANT_OPEN. Nothing here caught it, and the only thing the
    // application-level handler recovers is the virtualization layout race - so
    // a path copy that lost a race took the window with it.
    //
    // The three clipboard calls below have caught this since they were written;
    // this one was the odd one out rather than a case anybody judged
    // differently. Silent, like theirs: the path is simply not copied, and a
    // dialog about a clipboard someone else is using is noise at the moment the
    // user is reaching for Ctrl+V.
    public static void CopyPathToClipboard(string path)
    {
        try
        {
            Clipboard.SetText(path);
        }
        catch (ExternalException)
        {
        }
    }

    // Where Windows keeps "was this a copy or a cut" - the file list alone
    // doesn't say. Writing it is what makes Ctrl+X here paste as a MOVE in
    // Explorer, and reading it is how a cut made in Explorer arrives here as
    // one. (DROPEFFECT_COPY = 1, DROPEFFECT_MOVE = 2; Explorer writes 5 =
    // COPY|LINK for a copy, hence the bit test rather than an equality one.)
    private const string PreferredDropEffect = "Preferred DropEffect";
    private const int DropEffectCopy = 1;
    private const int DropEffectMove = 2;

    public static void CopyToClipboard(string path)
        => CopyToClipboard(new[] { path });

    public static void CopyToClipboard(IEnumerable<string> paths)
        => SetClipboardFiles(paths, move: false);

    // Ctrl+X. Nothing is moved here - the clipboard just records the intent,
    // and the paste is what acts on it (same as Explorer, so a cut left
    // unpasted costs nothing).
    public static bool CutToClipboard(string path)
        => CutToClipboard(new[] { path });

    public static bool CutToClipboard(IEnumerable<string> paths)
        => SetClipboardFiles(paths, move: true);

    private static bool SetClipboardFiles(IEnumerable<string> paths, bool move)
    {
        var files = new System.Collections.Specialized.StringCollection();
        foreach (string path in paths)
        {
            files.Add(path);
        }
        if (files.Count == 0)
        {
            return false;
        }

        var data = new System.Windows.DataObject();
        data.SetFileDropList(files);
        data.SetData(PreferredDropEffect,
            new MemoryStream(BitConverter.GetBytes(move ? DropEffectMove : DropEffectCopy)));

        try
        {
            // copy: true so the list outlives this process - a cut made here
            // and pasted in Explorer after the sidebar is closed still works.
            Clipboard.SetDataObject(data, copy: true);
            return true;
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            // Another process had the clipboard open. Nothing was placed, so
            // the caller must not mark anything as cut.
            return false;
        }
    }

    // What a paste turned out to be, so the caller knows whether the source
    // folders need refreshing too (a move empties them) and can drop its cut
    // markers once they've been acted on.
    // CreatedPaths is where things actually LANDED, which the caller cannot work
    // out from the sources: a collision numbers up to " (2)", and a cut pasted
    // back into its own folder lands nowhere at all.
    public sealed record PasteOutcome(
        bool WasMove, IReadOnlyList<string> SourcePaths, IReadOnlyList<string> CreatedPaths)
    {
        public static readonly PasteOutcome None =
            new(false, Array.Empty<string>(), Array.Empty<string>());
    }

    // Returns false only when the clipboard has nothing pasteable, so the
    // caller can distinguish "nothing to do" from "tried and failed".
    public static bool TryPaste(string destinationFolder, out PasteOutcome outcome, out string? error)
    {
        error = null;
        outcome = PasteOutcome.None;

        string[] sourcePaths;
        bool move;
        try
        {
            var data = Clipboard.GetDataObject();
            if (data is null || !data.GetDataPresent(System.Windows.DataFormats.FileDrop) ||
                data.GetData(System.Windows.DataFormats.FileDrop) is not string[] paths || paths.Length == 0)
            {
                return false;
            }
            sourcePaths = paths;
            move = IsMoveRequested(data);
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            return false;
        }

        var created = new List<string>();
        outcome = new PasteOutcome(move, sourcePaths, created);

        foreach (string sourcePath in sourcePaths)
        {
            try
            {
                string? landed = move
                    ? MoveEntry(sourcePath, destinationFolder)
                    : CopyEntry(sourcePath, destinationFolder);
                if (landed is not null)
                {
                    created.Add(landed);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                error = ex.Message;
            }
        }

        return true;
    }

    // Is the clipboard STILL holding exactly the cut these paths were marked
    // for? Anything else - a copy somewhere, another app's cut, a paste that
    // consumed ours (Explorer empties the clipboard after a move) - means the
    // markers are stale. null = couldn't tell, because another process had the
    // clipboard open at that instant; the caller must leave the markers alone
    // rather than clear them on a failed read (our own cut raises the change
    // notification too, and clearing there would erase what was just set).
    public static bool? ClipboardStillHoldsCut(IReadOnlyCollection<string> paths)
    {
        try
        {
            var data = Clipboard.GetDataObject();
            if (data is null || !data.GetDataPresent(System.Windows.DataFormats.FileDrop) ||
                !IsMoveRequested(data) ||
                data.GetData(System.Windows.DataFormats.FileDrop) is not string[] onClipboard ||
                onClipboard.Length != paths.Count)
            {
                return false;
            }

            foreach (string path in onClipboard)
            {
                if (!paths.Contains(path, StringComparer.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            return true;
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            return null;
        }
    }

    private static bool IsMoveRequested(System.Windows.IDataObject data)
    {
        if (data.GetData(PreferredDropEffect) is MemoryStream stream && stream.Length >= 4)
        {
            var bytes = new byte[4];
            stream.Position = 0;
            if (stream.Read(bytes, 0, 4) == 4)
            {
                return (BitConverter.ToInt32(bytes, 0) & DropEffectMove) == DropEffectMove;
            }
        }

        return false;
    }

    // Returns the path it actually wrote, which is not always the one the name
    // suggests: a collision numbers up to " (2)". The caller uses it to show what
    // arrived, and deriving the name a second time at the call site would get the
    // numbered cases wrong - it would point at the file that was already there.
    private static string? CopyEntry(string sourcePath, string destinationFolder)
    {
        string trimmedSource = sourcePath.TrimEnd(Path.DirectorySeparatorChar);
        string name = Path.GetFileName(trimmedSource);

        // COPIED, THEN PASTED ONTO ITSELF - which is what Ctrl+C, Ctrl+V on one
        // folder means in a TREE, where the thing selected IS the destination.
        // In a file list the same two presses paste into the folder being
        // LOOKED AT, and Explorer answers with a numbered duplicate beside the
        // original. This used to answer "폴더를 자기 자신이나 그 하위 폴더로
        // 복사할 수 없습니다", which is true of the letter of the request and
        // not of any of its meaning.
        //
        // So the copy lands in the PARENT and numbers up, exactly as the same
        // paste into any other folder would. Only the exact-self case: pasting
        // into something further down inside the source really is the endless
        // walk, and stays refused below.
        if (Directory.Exists(sourcePath) && IsSamePath(trimmedSource, destinationFolder))
        {
            if (Path.GetDirectoryName(trimmedSource) is not { } parent)
            {
                // A drive root has nowhere to be a sibling of.
                throw new IOException(Strings.CopyIntoSelfError);
            }

            string selfCopy = GetUniqueDestination(Path.Combine(parent, name));
            CopyDirectoryRecursive(sourcePath, selfCopy, overwrite: false);
            return selfCopy;
        }

        if (Directory.Exists(sourcePath))
        {
            // Into itself or into its own subtree. MoveEntry has refused this
            // from the start; copy never did, and the difference was not
            // harmless - copying a folder into ITSELF walks into the copy it
            // just made and does it again, forever, writing the whole time.
            // Reported 2026-08-02 as the app hanging (Windows killed it as
            // "not responding") after a Ctrl+C, Ctrl+V on one selected folder,
            // which is as ordinary a sequence as this app has: paste targets
            // the selected folder, so the source IS the destination. It left
            // behind a folder nested into itself dozens of levels deep.
            if (IsSameOrBeneath(destinationFolder, trimmedSource))
            {
                throw new IOException(Strings.CopyIntoSelfError);
            }

            string folderCopy = GetUniqueDestination(Path.Combine(destinationFolder, name));
            CopyDirectoryRecursive(sourcePath, folderCopy, overwrite: false);
            return folderCopy;
        }

        if (File.Exists(sourcePath))
        {
            string fileCopy = GetUniqueDestination(Path.Combine(destinationFolder, name));
            File.Copy(sourcePath, fileCopy);
            return fileCopy;
        }

        return null;
    }

    // The cut half of paste. Name conflicts number up to " (2)" exactly like a
    // copied paste does - the app has never asked before writing, and a move
    // is not the place to start.
    // Returns where it put the item, or null when it did nothing (cut and pasted
    // back into its own folder). Same reason as CopyEntry above.
    private static string? MoveEntry(string sourcePath, string destinationFolder)
    {
        string trimmedSource = sourcePath.TrimEnd(Path.DirectorySeparatorChar);
        string name = Path.GetFileName(trimmedSource);

        // Cut and pasted back into the folder it came from: Explorer does
        // nothing here, and the alternative - numbering it up to " (2)" - would
        // silently turn a move into a copy.
        if (Path.GetDirectoryName(trimmedSource) is { } sourceParent && IsSamePath(sourceParent, destinationFolder))
        {
            return null;
        }

        if (Directory.Exists(sourcePath))
        {
            // Into itself or into its own subtree: refuse before anything is
            // written, since carrying it out would consume the source.
            if (IsSameOrBeneath(destinationFolder, trimmedSource))
            {
                throw new IOException(Strings.MoveIntoSelfError);
            }

            string destPath = GetUniqueDestination(Path.Combine(destinationFolder, name));
            if (IsSameVolume(trimmedSource, destPath))
            {
                Directory.Move(sourcePath, destPath);
            }
            else
            {
                // Directory.Move can't cross volumes. Copy the whole tree
                // FIRST and delete the source only once that has succeeded -
                // a failure halfway leaves the original untouched, so there is
                // never a half-moved folder to roll back.
                CopyDirectoryRecursive(sourcePath, destPath, overwrite: false);
                Directory.Delete(sourcePath, recursive: true);
            }

            return destPath;
        }

        if (File.Exists(sourcePath))
        {
            // File.Move handles crossing volumes on its own (copy + delete,
            // with the delete only after the copy landed).
            string moved = GetUniqueDestination(Path.Combine(destinationFolder, name));
            File.Move(sourcePath, moved);
            return moved;
        }

        return null;
    }

    private static bool IsSamePath(string a, string b)
        => string.Equals(a.TrimEnd(Path.DirectorySeparatorChar), b.TrimEnd(Path.DirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);

    private static bool IsSameOrBeneath(string candidate, string root)
    {
        string trimmedCandidate = candidate.TrimEnd(Path.DirectorySeparatorChar);
        string trimmedRoot = root.TrimEnd(Path.DirectorySeparatorChar);
        return string.Equals(trimmedCandidate, trimmedRoot, StringComparison.OrdinalIgnoreCase) ||
            trimmedCandidate.StartsWith(trimmedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    // Public since 2026-08-13: the drag decides what it means with it. Same
    // volume is a rename and costs nothing; across volumes a "move" is a full
    // copy followed by a delete, which on a big file over a network is a long
    // silent operation nobody asked for by dragging.
    public static bool IsSameVolumePair(string a, string b)
    {
        try
        {
            return IsSameVolume(a, b);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            // Unanswerable means "not the same", which falls to a copy - the
            // safe half of the pair.
            return false;
        }
    }

    private static bool IsSameVolume(string a, string b)
        => string.Equals(Path.GetPathRoot(Path.GetFullPath(a)), Path.GetPathRoot(Path.GetFullPath(b)),
            StringComparison.OrdinalIgnoreCase);

    private static void CopyDirectoryRecursive(string sourceDir, string destDir, bool overwrite)
    {
        // Both listings are taken BEFORE anything is written. EnumerateFiles/
        // Directories stream lazily, so a folder created inside the source
        // while the walk is still running gets picked up by that same walk and
        // copied in turn - which is how a folder pasted into itself produced an
        // endless chain of copies. The caller now refuses that case outright;
        // this is the second lock on the same door, and it also covers the
        // cross-volume move path that comes through here.
        var files = Directory.EnumerateFiles(sourceDir).ToList();
        var dirs = Directory.EnumerateDirectories(sourceDir).ToList();

        Directory.CreateDirectory(destDir);
        foreach (var file in files)
        {
            string destFile = Path.Combine(destDir, Path.GetFileName(file));
            File.Copy(file, overwrite ? destFile : GetUniqueDestination(destFile), overwrite);
        }
        foreach (var dir in dirs)
        {
            CopyDirectoryRecursive(dir, Path.Combine(destDir, Path.GetFileName(dir)), overwrite);
        }
    }

    // Dragging a file in from outside the app always copies (never removes
    // the external original - the safer default for a sidebar utility, not a
    // full file manager) and asks per top-level dropped item before
    // overwriting anything that already exists at the destination; declining
    // just skips that item and moves on to the next. A name the SAME drop has
    // already put there is numbered instead - see below.
    public static bool TryImportDroppedPaths(IReadOnlyList<string> sourcePaths, string destinationFolder,
        Func<string, bool> confirmOverwrite, out IReadOnlyList<string> createdPaths, out string? error)
    {
        error = null;
        var created = new List<string>();
        createdPaths = created;
        foreach (string sourcePath in sourcePaths)
        {
            try
            {
                string name = Path.GetFileName(sourcePath.TrimEnd(Path.DirectorySeparatorChar));

                // A SOURCE WITH NO LEAF NAME IS A WHOLE VOLUME, and it is put
                // down again untouched (2026-08-27, on report).
                //
                // Path.GetFileName("G:\") is the empty string, so everything
                // below it went wrong quietly rather than throwing: Path.Combine
                // returned the DESTINATION FOLDER unchanged, that folder exists
                // by definition, and the question that reached the screen read
                // '' 이(가) 이미 있습니다. 덮어쓸까요? with nothing named in it.
                // Answering 예 would have copied the entire drive over the
                // target folder with overwrite: true.
                //
                // WHAT THIS DEVICE HIDES: one dropped path, silently. The tree
                // no longer offers a volume to a drag at all (see
                // TreeViewItem_PreviewMouseMove), so the only way to arrive
                // here is a drive letter dragged in from Explorer - which this
                // app does not do; it is a sidebar, and copying a volume is not
                // a thing it should be asked to start. The rest of the drop is
                // carried out normally.
                if (name.Length == 0)
                {
                    continue;
                }

                string destPath = Path.Combine(destinationFolder, name);
                bool exists = File.Exists(destPath) || Directory.Exists(destPath);

                // A NAME THIS DROP HAS ALREADY USED IS NUMBERED, NOT ASKED
                // ABOUT (2026-10-06). Files gathered from several folders - a
                // multi-selection made in search results, or by Ctrl+click
                // across the tree - can share a name, and the second one used
                // to meet the overwrite question for the copy of the first,
                // made a moment earlier by this same drop. The question cannot
                // say that is what it means, and answering yes lost a file.
                // Numbered up to " (2)" the way a paste numbers every
                // collision; the question is kept for what was in the folder
                // before the drop.
                if (exists && created.Contains(destPath, StringComparer.OrdinalIgnoreCase))
                {
                    destPath = GetUniqueDestination(destPath);
                    exists = false;
                }
                else if (exists && !confirmOverwrite(name))
                {
                    continue;
                }

                if (Directory.Exists(sourcePath))
                {
                    // Same guard as CopyEntry's, for the drag-in route: an
                    // outside folder dropped onto something inside itself is
                    // the same endless walk.
                    if (IsSameOrBeneath(destinationFolder, sourcePath.TrimEnd(Path.DirectorySeparatorChar)))
                    {
                        throw new IOException(Strings.CopyIntoSelfError);
                    }

                    CopyDirectoryRecursive(sourcePath, destPath, overwrite: exists);
                    created.Add(destPath);
                }
                else if (File.Exists(sourcePath))
                {
                    File.Copy(sourcePath, destPath, overwrite: exists);
                    created.Add(destPath);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                error = ex.Message;
            }
        }

        return true;
    }

    // Shift+드래그. The MOVE half of the drop above, and deliberately NOT a
    // second idea of what moving means: it goes through MoveEntry, the same
    // call 잘라내기+붙여넣기 has always used. So a name that already exists
    // numbers up to " (2)" rather than asking, a folder dropped into its own
    // subtree is refused before anything is written, and a move across volumes
    // copies in full BEFORE the source is removed.
    //
    // Only ever reached for a drag that started inside this app (see
    // MainWindow's InternalDragFormat). A file dragged in from Explorer still
    // copies whatever keys are held - taking someone's file out of a folder
    // this app does not own is not a sidebar's business, and Explorer's own
    // idea of who deletes the source in a cross-application move is not
    // something to be guessing at.
    //
    // The source FOLDERS come back because a move empties them, and the tree
    // may well have them open: refreshing only the destination would leave the
    // rows that just left still sitting there.
    public static bool TryMoveDroppedPaths(IReadOnlyList<string> sourcePaths, string destinationFolder,
        out IReadOnlyList<string> emptiedFolders, out IReadOnlyList<string> createdPaths, out string? error)
    {
        error = null;
        var folders = new List<string>();
        var created = new List<string>();
        createdPaths = created;
        foreach (string sourcePath in sourcePaths)
        {
            try
            {
                string? parent = Path.GetDirectoryName(sourcePath.TrimEnd(Path.DirectorySeparatorChar));
                if (MoveEntry(sourcePath, destinationFolder) is { } landed)
                {
                    created.Add(landed);
                }
                if (parent is not null && !folders.Contains(parent, StringComparer.OrdinalIgnoreCase))
                {
                    folders.Add(parent);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // One item's failure is not the other five's - same rule the
                // copy path beside this one follows.
                error = ex.Message;
            }
        }

        emptiedFolders = folders;
        return true;
    }

    // Pasting into a folder that already has an item with the same name
    // appends " (2)", " (3)", ... instead of overwriting. Shared with
    // ArchiveService so a second 압축 of the same row numbers up the same way
    // the rest of the app does.
    internal static string GetUniqueDestination(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path))
        {
            return path;
        }

        string dir = Path.GetDirectoryName(path) ?? string.Empty;
        string name = Path.GetFileNameWithoutExtension(path);
        string ext = Path.GetExtension(path);

        for (int i = 2; ; i++)
        {
            string candidate = Path.Combine(dir, $"{name} ({i}){ext}");
            if (!File.Exists(candidate) && !Directory.Exists(candidate))
            {
                return candidate;
            }
        }
    }

    // Auto-numbered on conflict ("새 폴더 (2)", ...), same convention as paste,
    // so calling this repeatedly in the same folder never overwrites/fails.
    public static bool TryCreateFolder(string parentDirectory, out string? createdPath, out string? error)
    {
        createdPath = null;
        error = null;
        try
        {
            string path = GetUniqueDestination(Path.Combine(parentDirectory, Strings.NewFolderDefaultName));
            Directory.CreateDirectory(path);
            createdPath = path;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            error = ex.Message;
            return false;
        }
    }

    public static bool TryRename(string path, string newName, out string? error)
    {
        error = null;
        try
        {
            string? dir = Path.GetDirectoryName(path);
            if (dir is null)
            {
                error = Strings.RenameFailedBody;
                return false;
            }
            string newPath = Path.Combine(dir, newName);

            if (Directory.Exists(path))
            {
                Directory.Move(path, newPath);
            }
            else if (File.Exists(path))
            {
                File.Move(path, newPath);
            }

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            error = ex.Message;
            return false;
        }
    }

    // ----- Shift+Delete ------------------------------------------------------
    //
    // WINDOWS ASKS, NOT US. A MessageBox of our own was written first and taken
    // out again: the shell's own confirmation names the file, its type, its size
    // and when it was last changed, is the dialog everyone already knows for
    // this exact gesture, and is translated into every language this app is not.
    // Ours could only ever have been a worse copy of it.
    //
    // THE WHOLE SELECTION GOES IN ONE CALL, which is the other half of the
    // reason. SHFileOperation takes a double-null-terminated LIST, so deleting
    // twelve files asks "이 항목 12개를..." once - deleting them one at a time
    // would have asked twelve times, and that is the shape that trains a hand to
    // click 예 without reading.
    //
    // No FOF_ALLOWUNDO, so this really is permanent; and deliberately NO
    // FOF_NOCONFIRMATION, since the confirmation is the point.
    //
    // THE RECYCLED DELETE GOES THROUGH THE SAME CALL with different flags, and
    // the combination is what Explorer itself does by default:
    //
    //   FOF_ALLOWUNDO       - to the Recycle Bin, where it can be fetched back
    //   FOF_NOCONFIRMATION  - so an ordinary delete asks nothing at all, which
    //                         is what Windows has done for years and what a box
    //                         of ours was only repeating
    //   FOF_WANTNUKEWARNING - EXCEPT when the file cannot be recycled, and this
    //                         is the whole reason the flag exists. A network
    //                         share has no Recycle Bin, so a delete there is
    //                         permanent whatever was asked for; without this,
    //                         dropping our own question would have made files on
    //                         a NAS disappear in silence. It partially overrides
    //                         FOF_NOCONFIRMATION, and only for that case.
    private const int FO_DELETE = 0x0003;
    private const ushort FOF_NOCONFIRMATION = 0x0010;
    private const ushort FOF_ALLOWUNDO = 0x0040;
    private const ushort FOF_NOCONFIRMMKDIR = 0x0200;
    private const ushort FOF_WANTNUKEWARNING = 0x4000;
    private const int ERROR_CANCELLED_SHELL = 0x4C7;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode, Pack = 8)]
    private struct SHFILEOPSTRUCT
    {
        public IntPtr hwnd;
        public uint wFunc;
        public string pFrom;
        public string? pTo;
        public ushort fFlags;
        public int fAnyOperationsAborted;
        public IntPtr hNameMappings;
        public string? lpszProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHFileOperation(ref SHFILEOPSTRUCT lpFileOp);

    // cancelled is TRUE when the person said no - which is not a failure and
    // must not be reported as one.
    public static bool TryShellDelete(IReadOnlyList<string> paths, IntPtr owner, bool permanent,
        out bool cancelled, out string? error)
    {
        cancelled = false;
        error = null;

        var existing = paths.Where(p => File.Exists(p) || Directory.Exists(p)).ToList();
        if (existing.Count == 0)
        {
            return true;
        }

        var op = new SHFILEOPSTRUCT
        {
            hwnd = owner,
            wFunc = FO_DELETE,
            // Double-null-terminated: one trailing \0 per entry from the join,
            // and one more to close the list.
            pFrom = string.Join('\0', existing) + "\0\0",
            fFlags = permanent
                ? FOF_NOCONFIRMMKDIR
                : (ushort)(FOF_NOCONFIRMMKDIR | FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_WANTNUKEWARNING),
        };

        int result;
        try
        {
            result = SHFileOperation(ref op);
        }
        catch (Exception ex) when (ex is EntryPointNotFoundException or DllNotFoundException)
        {
            error = ex.Message;
            return false;
        }

        if (op.fAnyOperationsAborted != 0 || result == ERROR_CANCELLED_SHELL)
        {
            cancelled = true;
            return true;
        }

        if (result != 0)
        {
            // The shell has already shown whatever went wrong in its own dialog,
            // so this is for the log and for the caller's own summary rather
            // than a second box saying the same thing in worse words.
            error = string.Format(Strings.DeleteFailedShellBody, result);
            return false;
        }

        return true;
    }

    // The VB FileSystem delete that used to live here is GONE (2026-08-13).
    // Every delete in the app now goes through TryShellDelete above, and a
    // second way to remove a file is a second set of rules about recycling,
    // confirming and reporting that can drift from the first without anyone
    // noticing which one they were looking at.
}
