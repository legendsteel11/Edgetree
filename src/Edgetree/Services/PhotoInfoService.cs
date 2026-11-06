using System.IO;
using System.Runtime.InteropServices;

namespace SidebarExplorer.App.Services;

// A picture's shooting details for the panel's EXIF view (2026-11-05), from
// the Windows property store Explorer's Details tab reads - the route
// ShellMediaInfoService already takes for a film. Whatever this machine has a
// property handler for is read the same way: JPEG, TIFF, PNG, and HEIC or a
// camera raw where Windows has their codecs.
//
// THE VALUES ARE WINDOWS' OWN TEXT (PSFormatForDisplay): "1/60초", "F/1.7",
// "ISO-800", "플래시 끔" on a Korean Windows - what the Details tab says, in
// Windows' language rather than the app's. The labels are the app's
// (Strings), named as that tab names them. Three are formatted here instead,
// measured on a phone photo before writing this:
//  - the date, in the caption's own pattern;
//  - 35mm focal length, which Windows gives as a bare "23";
//  - the location, which comes as degrees; minutes; seconds to sixteen
//    places ("37; 7; 38.0416789999871696") and is given as decimal degrees,
//    the form a map takes when it is pasted in.
// And Windows' text carries invisible direction marks (U+200E, U+200F) round
// its numbers, which are taken out.
//
// The keys are looked up by their canonical names once, rather than spelled
// out as ShellMediaInfoService's are: fifteen GUIDs written from memory are
// fifteen chances to show the wrong value under a right label.
//
// Always off the UI thread: on a network path the store is a read of the file.
public static class PhotoInfoService
{
    // In the order the panel lists them.
    public enum Field
    {
        CameraMaker,
        CameraModel,
        LensModel,
        DateTaken,
        ExposureTime,
        FNumber,
        IsoSpeed,
        ExposureBias,
        ExposureProgram,
        MeteringMode,
        Flash,
        FocalLength,
        FocalLength35mm,
        Program,
        Location,
    }

    private static readonly (Field Field, string Name)[] Shown =
    {
        (Field.CameraMaker, "System.Photo.CameraManufacturer"),
        (Field.CameraModel, "System.Photo.CameraModel"),
        (Field.LensModel, "System.Photo.LensModel"),
        (Field.DateTaken, "System.Photo.DateTaken"),
        (Field.ExposureTime, "System.Photo.ExposureTime"),
        (Field.FNumber, "System.Photo.FNumber"),
        (Field.IsoSpeed, "System.Photo.ISOSpeed"),
        (Field.ExposureBias, "System.Photo.ExposureBias"),
        (Field.ExposureProgram, "System.Photo.ExposureProgram"),
        (Field.MeteringMode, "System.Photo.MeteringMode"),
        (Field.Flash, "System.Photo.Flash"),
        (Field.FocalLength, "System.Photo.FocalLength"),
        (Field.FocalLength35mm, "System.Photo.FocalLengthInFilm"),
        (Field.Program, "System.ApplicationName"),
    };

    private static readonly Lazy<Keys> ResolvedKeys = new(Resolve);

    private sealed record Keys(
        (Field Field, PROPERTYKEY Key)[] Shown,
        PROPERTYKEY? Latitude, PROPERTYKEY? LatitudeRef,
        PROPERTYKEY? Longitude, PROPERTYKEY? LongitudeRef);

    private static Keys Resolve()
    {
        var shown = new List<(Field, PROPERTYKEY)>();
        foreach (var (field, name) in Shown)
        {
            if (Find(name) is { } key)
            {
                shown.Add((field, key));
            }
        }

        return new Keys(shown.ToArray(),
            Find("System.GPS.Latitude"), Find("System.GPS.LatitudeRef"),
            Find("System.GPS.Longitude"), Find("System.GPS.LongitudeRef"));

        static PROPERTYKEY? Find(string name)
            => PSGetPropertyKeyFromName(name, out var key) == 0 ? key : null;
    }

    // The fields the file has, in order, each with its text. Empty when there
    // is nothing - no EXIF, no handler, a file gone.
    public static IReadOnlyList<(Field Field, string Value)> Read(string path) => Read(path, out _);

    // And whether the file could be read at all, which an empty list cannot
    // say: a file still being copied or a share gone for a moment comes back
    // empty too, and that is not a picture with no details.
    public static IReadOnlyList<(Field Field, string Value)> Read(string path, out bool read)
    {
        read = false;
        var found = new List<(Field, string)>();
        IPropertyStore? store = null;
        try
        {
            var iid = typeof(IPropertyStore).GUID;
            if (SHGetPropertyStoreFromParsingName(path, IntPtr.Zero, GPS_DEFAULT, ref iid, out store) != 0 ||
                store is null)
            {
                return found;
            }

            var keys = ResolvedKeys.Value;
            foreach (var (field, key) in keys.Shown)
            {
                if (Format(store, field, key) is { Length: > 0 } text)
                {
                    found.Add((field, text));
                }
            }

            if (Location(store, keys) is { } location)
            {
                found.Add((Field.Location, location));
            }

            read = true;
            return found;
        }
        catch (Exception e) when (e is COMException or ArgumentException or FileNotFoundException
                                      or UnauthorizedAccessException or InvalidCastException)
        {
            return found;
        }
        finally
        {
            if (store is not null)
            {
                Marshal.ReleaseComObject(store);
            }
        }
    }

    private static string? Format(IPropertyStore store, Field field, PROPERTYKEY key)
    {
        var value = new PROPVARIANT();
        try
        {
            if (store.GetValue(ref key, out value) != 0 || value.VarType == VT_EMPTY)
            {
                return null;
            }

            switch (field)
            {
                case Field.DateTaken:
                    return PropVariantToFileTime(ref value, PSTF_UTC, out long fileTime) == 0
                        ? DateTime.FromFileTimeUtc(fileTime).ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")
                        : null;

                case Field.FocalLength35mm:
                    return PropVariantToUInt32(ref value, out uint millimetres) == 0 && millimetres > 0
                        ? $"{millimetres}mm"
                        : null;
            }

            var buffer = new char[256];
            if (PSFormatForDisplay(ref key, ref value, PDFF_DEFAULT, buffer, (uint)buffer.Length) != 0)
            {
                return null;
            }

            return Clean(new string(buffer, 0, Math.Max(0, Array.IndexOf(buffer, '\0'))));
        }
        finally
        {
            PropVariantClear(ref value);
        }
    }

    // "37.127234, 127.069260" - latitude first, south and west negative.
    private static string? Location(IPropertyStore store, Keys keys)
    {
        if (keys.Latitude is not { } latitudeKey || keys.Longitude is not { } longitudeKey ||
            Degrees(store, latitudeKey) is not { } latitude || Degrees(store, longitudeKey) is not { } longitude)
        {
            return null;
        }

        if (keys.LatitudeRef is { } latitudeRef && Text(store, latitudeRef) is "S" or "s")
        {
            latitude = -latitude;
        }
        if (keys.LongitudeRef is { } longitudeRef && Text(store, longitudeRef) is "W" or "w")
        {
            longitude = -longitude;
        }

        return string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"{latitude:0.000000}, {longitude:0.000000}");
    }

    // Degrees, minutes, seconds as three doubles, added up.
    private static double? Degrees(IPropertyStore store, PROPERTYKEY key)
    {
        var value = new PROPVARIANT();
        try
        {
            if (store.GetValue(ref key, out value) != 0 || value.VarType == VT_EMPTY)
            {
                return null;
            }

            var parts = new double[3];
            if (PropVariantToDoubleVector(ref value, parts, 3, out uint count) != 0 || count == 0)
            {
                return null;
            }

            double degrees = parts[0] + (count > 1 ? parts[1] / 60 : 0) + (count > 2 ? parts[2] / 3600 : 0);
            return double.IsFinite(degrees) ? degrees : null;
        }
        finally
        {
            PropVariantClear(ref value);
        }
    }

    private static string? Text(IPropertyStore store, PROPERTYKEY key)
    {
        var value = new PROPVARIANT();
        try
        {
            if (store.GetValue(ref key, out value) != 0 || value.VarType == VT_EMPTY)
            {
                return null;
            }

            var buffer = new char[16];
            return PropVariantToString(ref value, buffer, (uint)buffer.Length) == 0
                ? new string(buffer, 0, Math.Max(0, Array.IndexOf(buffer, '\0'))).Trim()
                : null;
        }
        finally
        {
            PropVariantClear(ref value);
        }
    }

    private static string Clean(string text)
    {
        var kept = new System.Text.StringBuilder(text.Length);
        foreach (char c in text)
        {
            if (c is not ('‎' or '‏' or (>= '‪' and <= '‮') or (>= '⁦' and <= '⁩')))
            {
                kept.Append(c);
            }
        }
        return kept.ToString().Trim();
    }

    private const uint GPS_DEFAULT = 0;
    private const ushort VT_EMPTY = 0;
    private const uint PDFF_DEFAULT = 0;
    private const int PSTF_UTC = 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct PROPERTYKEY
    {
        public Guid FormatId;
        public uint PropertyId;
    }

    // The right SIZE for the propsys helpers, as in ShellMediaInfoService.
    [StructLayout(LayoutKind.Sequential)]
    private struct PROPVARIANT
    {
        public ushort VarType;
        public ushort Reserved1;
        public ushort Reserved2;
        public ushort Reserved3;
        public IntPtr Value1;
        public IntPtr Value2;
    }

    [ComImport]
    [Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetAt(uint index, out PROPERTYKEY key);
        [PreserveSig] int GetValue(ref PROPERTYKEY key, out PROPVARIANT value);
        [PreserveSig] int SetValue(ref PROPERTYKEY key, ref PROPVARIANT value);
        [PreserveSig] int Commit();
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int SHGetPropertyStoreFromParsingName(
        string path, IntPtr bindContext, uint flags, ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IPropertyStore? propertyStore);

    [DllImport("propsys.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int PSGetPropertyKeyFromName(string name, out PROPERTYKEY key);

    [DllImport("propsys.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int PSFormatForDisplay(
        ref PROPERTYKEY key, ref PROPVARIANT value, uint flags, [Out] char[] text, uint length);

    [DllImport("propsys.dll", PreserveSig = true)]
    private static extern int PropVariantToFileTime(ref PROPVARIANT value, int flags, out long fileTime);

    [DllImport("propsys.dll", PreserveSig = true)]
    private static extern int PropVariantToUInt32(ref PROPVARIANT value, out uint result);

    [DllImport("propsys.dll", PreserveSig = true)]
    private static extern int PropVariantToDoubleVector(
        ref PROPVARIANT value, [Out] double[] values, uint capacity, out uint count);

    [DllImport("propsys.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int PropVariantToString(ref PROPVARIANT value, [Out] char[] text, uint length);

    [DllImport("ole32.dll", PreserveSig = true)]
    private static extern int PropVariantClear(ref PROPVARIANT value);
}
