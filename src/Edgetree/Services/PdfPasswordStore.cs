using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace SidebarExplorer.App.Services;

// Passwords the PDF view was asked to remember, one per file (2026-10-27, the
// author's design: a 암호 기억 box under the password; a remembered file is
// still NOT opened by selection - the panel shows 열기 with no password box,
// and the press opens it; unticking forgets it and the box comes back).
//
// ENCRYPTED WITH DPAPI for the current Windows user (CryptProtectData), in a
// file of its own - %APPDATA%\Edgetree\pdf-passwords.dat - never in
// settings.json, so presets and a copied settings file carry none of it. The
// file opens only under the same Windows account on the same machine; anyone
// signed in as that account can open a remembered PDF without the password,
// which is what remembering means and the same as a browser's saved
// passwords. P/Invoke rather than System.Security.Cryptography.ProtectedData,
// which on .NET 8 is a separate package for two calls.
//
// KEYED BY FULL PATH. A file the app renames or moves takes its password
// along (FileOperationService.PathMoved, as the unsaved texts do); one
// renamed outside the app is asked for again.
//
// Never used for thumbnails: a locked PDF stays its icon in the thumbnail bar,
// or its first page would land unencrypted in the thumbnail cache.
internal static class PdfPasswordStore
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Edgetree", "pdf-passwords.dat");

    private static readonly object Gate = new();
    // Loaded on first use; a locked PDF is rare, so a session that never
    // meets one never reads the file.
    private static Dictionary<string, string>? _entries;

    public static string? Get(string path)
    {
        lock (Gate)
        {
            return Entries().TryGetValue(Key(path), out var password) ? password : null;
        }
    }

    public static bool Has(string path) => Get(path) is not null;

    public static void Set(string path, string password)
    {
        lock (Gate)
        {
            Entries()[Key(path)] = password;
            Save();
        }
    }

    public static void Remove(string path)
    {
        lock (Gate)
        {
            if (Entries().Remove(Key(path)))
            {
                Save();
            }
        }
    }

    // FileOperationService.PathMoved, on whatever thread the move ran on. A
    // folder's move carries every remembered file under it.
    public static void OnPathMoved(string oldPath, string newPath)
    {
        lock (Gate)
        {
            if (!File.Exists(FilePath) && _entries is null)
            {
                return;
            }
            var entries = Entries();
            var moves = entries.Keys
                .Select(key => (key, moved: UnsavedTextStore.MovedPath(key, oldPath, newPath)))
                .Where(pair => pair.moved is not null)
                .ToList();
            if (moves.Count == 0)
            {
                return;
            }
            foreach (var (key, moved) in moves)
            {
                string password = entries[key];
                entries.Remove(key);
                entries[Key(moved!)] = password;
            }
            Save();
        }
    }

    private static string Key(string path)
    {
        try
        {
            return Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return path;
        }
    }

    // Gate held.
    private static Dictionary<string, string> Entries()
    {
        if (_entries is not null)
        {
            return _entries;
        }
        _entries = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (File.Exists(FilePath) &&
                Unprotect(File.ReadAllBytes(FilePath)) is { } json &&
                JsonSerializer.Deserialize<Dictionary<string, string>>(json) is { } loaded)
            {
                foreach (var (path, password) in loaded)
                {
                    _entries[path] = password;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // Unreadable or written under another account: nothing remembered,
            // and the next Set writes a fresh file over it.
            System.Diagnostics.Debug.WriteLine($"PDF passwords unreadable: {ex.Message}");
        }
        return _entries;
    }

    // Gate held. Written beside and moved into place, so a write cut short
    // never leaves half a file under the name Entries trusts.
    private static void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            if (_entries is not { Count: > 0 })
            {
                File.Delete(FilePath);
                return;
            }
            byte[] json = JsonSerializer.SerializeToUtf8Bytes(_entries);
            byte[]? sealedBytes = Protect(json);
            Array.Clear(json);
            if (sealedBytes is null)
            {
                return;
            }
            string temp = FilePath + ".tmp";
            File.WriteAllBytes(temp, sealedBytes);
            File.Move(temp, FilePath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Debug.WriteLine($"PDF passwords not saved: {ex.Message}");
        }
    }

    // ----- DPAPI -------------------------------------------------------------

    private static byte[]? Protect(byte[] plain) => Crypt(plain, protect: true);

    private static byte[]? Unprotect(byte[] sealedBytes) => Crypt(sealedBytes, protect: false);

    private static byte[]? Crypt(byte[] input, bool protect)
    {
        var handle = GCHandle.Alloc(input, GCHandleType.Pinned);
        var output = default(Native.DataBlob);
        try
        {
            var blob = new Native.DataBlob
            {
                Size = input.Length,
                Data = handle.AddrOfPinnedObject(),
            };
            bool ok = protect
                ? Native.CryptProtectData(ref blob, null, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero,
                    Native.UiForbidden, ref output)
                : Native.CryptUnprotectData(ref blob, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero,
                    Native.UiForbidden, ref output);
            if (!ok)
            {
                return null;
            }
            var result = new byte[output.Size];
            Marshal.Copy(output.Data, result, 0, output.Size);
            return result;
        }
        finally
        {
            handle.Free();
            if (output.Data != IntPtr.Zero)
            {
                Native.LocalFree(output.Data);
            }
        }
    }

    private static class Native
    {
        public const int UiForbidden = 0x1;

        [StructLayout(LayoutKind.Sequential)]
        public struct DataBlob
        {
            public int Size;
            public IntPtr Data;
        }

        [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CryptProtectData(ref DataBlob dataIn, string? description,
            IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, ref DataBlob dataOut);

        [DllImport("crypt32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CryptUnprotectData(ref DataBlob dataIn, IntPtr description,
            IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, ref DataBlob dataOut);

        [DllImport("kernel32.dll")]
        public static extern IntPtr LocalFree(IntPtr memory);
    }
}
