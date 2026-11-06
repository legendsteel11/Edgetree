using System.IO;
using System.Text;
using System.Xml.Linq;

namespace SidebarExplorer.App.Services;

// 메타데이터 제거 for a JPEG (2026-11-05, the author's design): the rows ticked in
// the panel's EXIF view taken out of a COPY of the file, every byte of the
// picture left as it was.
//
// A ROW TAKES ITS COMPANIONS WITH IT. 위치 is the whole GPS directory -
// altitude, the satellite clock, the lot - not the two numbers shown; 찍은
// 날짜 every date tag; 렌즈 모델 the lens's make, specification and serial
// number. Left behind, the companion would say what the row was taken out to
// hide.
//
// AND WHAT THIS CANNOT READ GOES WITH THE ROWS THAT SAY WHEN, WHERE AND WITH
// WHAT (2026-11-06 review, measured on a Galaxy S24 Ultra's photo): with 찍은
// 날짜 and 위치 ticked, Windows read the copy clean while the bytes still held
// the exact shooting time twice - Samsung's trailer after the picture
// (Image_UTC_Data, to the millisecond) and an APP4 block of its own - and the
// country (MCC_Data). A Motion Photo's video or an MPF picture would carry the
// same. IPTC and XMP hold place names and dates under names no list can
// finish - a drone's position, an edit history's times - and a maker's note
// and the embedded thumbnail are read by nothing here at all. So
// PhotoInfoService's read-back proves nothing, and when any of OpaqueRows is
// ticked every one of those is dropped whole, the rule TrimXmp keeps for a
// packet it cannot read. What it costs: on that phone the trailer is also the
// HDR gain map, so the copy loses the HDR boost on an HDR screen, and a
// rating, keywords or caption held only in XMP or IPTC go too. The original
// keeps all of it.
//
// AND 그 밖의 정보 IS EVERYTHING THE VIEW DOES NOT SHOW that a person might
// mind: the maker's note (some carry the body's serial number), the serial
// numbers, the embedded thumbnail (which can still show the picture as it
// was before an edit), author, copyright, description and comments, the XMP
// packet (edit history), IPTC, and whatever a camera appends after the
// picture. On the phone measured here that last is an HDR gain map, so the
// copy shows the same on an ordinary screen and loses the HDR boost on an HDR
// one.
//
// TAKEN OUT IN PLACE, NOT REWRITTEN. A removed tag's value bytes are
// overwritten with zeros where they lie and its entry is taken out of its
// directory, the entries after it moving up by one; nothing else in the
// block moves. Every offset the file holds - and a maker's note is full of
// offsets of its own, which is why re-serialising EXIF breaks one - still
// points where it did, and a removed value is in the file nowhere at all. An
// unreferenced but intact block would show nothing and still hold
// everything. A segment that goes (XMP, IPTC, a comment) is simply not
// copied: the EXIF block's offsets are counted from its own start.
//
// EVERY ROW AND 그 밖의 정보 TOGETHER is the whole lot, and then the EXIF block
// is not edited but replaced by one holding only the orientation and, on a
// picture that is not sRGB, its colour space, so the copy carries no zeroed
// husk. Every row means all fifteen as the caller
// passes them: the panel asks for all of them when every row it SHOWS is
// ticked, since a file can hold a row it shows nothing for - a Canon writes a
// GPS directory with a version number in it and no position, and that alone
// kept this from being "everything" (measured).
//
// ALWAYS KEPT: the orientation (a phone photo is stored on its side and would
// lie down without it), the colour profile (colours shift without it), the
// JFIF and Adobe headers (the Adobe one says how the colours are coded), and
// the tags that describe the picture itself - sizes, colour space, versions.
// A camera set to Adobe RGB says so ONLY in those tags (ColorSpace 0xFFFF and
// InteropIndex R03, no profile in the file), so the whole lot carries them
// over: without them a colour-managed viewer reads the copy as sRGB and the
// colours shift (2026-11-06 review).
public static class JpegMetadataRemover
{
    // What one tag belongs to. A tag in two (a serial number) goes when either
    // is ticked.
    private static readonly Dictionary<int, PhotoInfoService.Field[]> Ifd0Tags = new()
    {
        [0x000B] = [PhotoInfoService.Field.Program],       // ProcessingSoftware
        [0x010F] = [PhotoInfoService.Field.CameraMaker],
        [0x0110] = [PhotoInfoService.Field.CameraModel],
        [0x0131] = [PhotoInfoService.Field.Program],
        [0x0132] = [PhotoInfoService.Field.DateTaken],
        // HostComputer: an iPhone writes its model here.
        [0x013C] = [PhotoInfoService.Field.CameraModel, PhotoInfoService.Field.Program],
    };

    // The rows that take along everything this cannot read item by item (see
    // the note above).
    private static readonly PhotoInfoService.Field[] OpaqueRows =
    [
        PhotoInfoService.Field.CameraMaker,
        PhotoInfoService.Field.CameraModel,
        PhotoInfoService.Field.LensModel,
        PhotoInfoService.Field.DateTaken,
        PhotoInfoService.Field.Location,
    ];

    // Taken with 그 밖의 정보 only.
    private static readonly HashSet<int> Ifd0OtherTags =
    [
        0x010E, // ImageDescription
        0x013B, // Artist
        0x8298, // Copyright
        0x9C9B, 0x9C9C, 0x9C9D, 0x9C9E, 0x9C9F, // XPTitle, XPComment, XPAuthor, XPKeywords, XPSubject
        0xC4A5, // PrintIM
    ];

    private static readonly Dictionary<int, PhotoInfoService.Field[]> ExifTags = new()
    {
        [0x829A] = [PhotoInfoService.Field.ExposureTime],
        [0x9201] = [PhotoInfoService.Field.ExposureTime],  // ShutterSpeedValue, the same in APEX
        [0x829D] = [PhotoInfoService.Field.FNumber],
        [0x9202] = [PhotoInfoService.Field.FNumber],       // ApertureValue, the same in APEX
        [0x8827] = [PhotoInfoService.Field.IsoSpeed],
        [0x8830] = [PhotoInfoService.Field.IsoSpeed],
        [0x8831] = [PhotoInfoService.Field.IsoSpeed],
        [0x8832] = [PhotoInfoService.Field.IsoSpeed],
        [0x8833] = [PhotoInfoService.Field.IsoSpeed],
        [0x8834] = [PhotoInfoService.Field.IsoSpeed],
        [0x8835] = [PhotoInfoService.Field.IsoSpeed],
        [0x9204] = [PhotoInfoService.Field.ExposureBias],
        [0x8822] = [PhotoInfoService.Field.ExposureProgram],
        [0x9207] = [PhotoInfoService.Field.MeteringMode],
        [0x9209] = [PhotoInfoService.Field.Flash],
        [0xA20B] = [PhotoInfoService.Field.Flash],         // FlashEnergy
        [0x920A] = [PhotoInfoService.Field.FocalLength],
        [0xA405] = [PhotoInfoService.Field.FocalLength35mm],
        [0x9003] = [PhotoInfoService.Field.DateTaken],
        [0x9004] = [PhotoInfoService.Field.DateTaken],
        [0x9010] = [PhotoInfoService.Field.DateTaken],
        [0x9011] = [PhotoInfoService.Field.DateTaken],
        [0x9012] = [PhotoInfoService.Field.DateTaken],
        [0x9290] = [PhotoInfoService.Field.DateTaken],
        [0x9291] = [PhotoInfoService.Field.DateTaken],
        [0x9292] = [PhotoInfoService.Field.DateTaken],
        [0xA432] = [PhotoInfoService.Field.LensModel],     // LensSpecification
        [0xA433] = [PhotoInfoService.Field.LensModel],     // LensMake
        [0xA434] = [PhotoInfoService.Field.LensModel],
        [0xA435] = [PhotoInfoService.Field.LensModel],     // LensSerialNumber, also with 그 밖의 정보
        [0xA431] = [PhotoInfoService.Field.CameraModel],   // BodySerialNumber, also with 그 밖의 정보
        // THE MAKER'S NOTE GOES WITH OpaqueRows, and with 그 밖의 정보. Measured
        // on a Canon EOS 70D's file: with Model taken out, "Canon EOS 70D" was
        // still in its maker's note, and the lens's name too - and a maker's
        // note is laid out the maker's own way, so it names the brand with no
        // text in it at all. The same goes for a time or a place: no textual
        // date showed in the S24's or the 70D's, but nothing here can read
        // one that is not text.
        [0x927C] = OpaqueRows,
    };

    private static readonly HashSet<int> ExifOtherTags =
    [
        0x927C, // MakerNote
        0x9286, // UserComment
        0xA420, // ImageUniqueID
        0xA430, // CameraOwnerName
        0xA431, // BodySerialNumber
        0xA435, // LensSerialNumber
    ];

    // The same facts as XMP properties, for a packet that repeats them (a file
    // through Lightroom or Photoshop does): namespace, then local name. Only
    // the rows that leave the packet standing: any of OpaqueRows takes all of it.
    private const string TiffNs = "http://ns.adobe.com/tiff/1.0/";
    private const string ExifNs = "http://ns.adobe.com/exif/1.0/";
    private const string ExifExNs = "http://cipa.jp/exif/1.0/";
    private const string XmpNs = "http://ns.adobe.com/xap/1.0/";

    private static PhotoInfoService.Field? XmpField(XName name) => (name.NamespaceName, name.LocalName) switch
    {
        (TiffNs, "Software") => PhotoInfoService.Field.Program,
        (ExifNs, "ExposureTime" or "ShutterSpeedValue") => PhotoInfoService.Field.ExposureTime,
        (ExifNs, "FNumber" or "ApertureValue") => PhotoInfoService.Field.FNumber,
        (ExifNs, "ISOSpeedRatings" or "PhotographicSensitivity") => PhotoInfoService.Field.IsoSpeed,
        (ExifNs, "ExposureBiasValue") => PhotoInfoService.Field.ExposureBias,
        (ExifNs, "ExposureProgram") => PhotoInfoService.Field.ExposureProgram,
        (ExifNs, "MeteringMode") => PhotoInfoService.Field.MeteringMode,
        (ExifNs, "Flash") => PhotoInfoService.Field.Flash,
        (ExifNs, "FocalLength") => PhotoInfoService.Field.FocalLength,
        (ExifNs, "FocalLengthIn35mmFilm") => PhotoInfoService.Field.FocalLength35mm,
        (ExifExNs, "PhotographicSensitivity" or "ISOSpeed") => PhotoInfoService.Field.IsoSpeed,
        (XmpNs, "CreatorTool") => PhotoInfoService.Field.Program,
        _ => null,
    };

    private static readonly byte[] ExifId = "Exif\0\0"u8.ToArray();
    private static readonly byte[] XmpId = "http://ns.adobe.com/xap/1.0/\0"u8.ToArray();
    private static readonly byte[] IccId = "ICC_PROFILE\0"u8.ToArray();
    private static readonly byte[] AdobeId = "Adobe"u8.ToArray();
    private static readonly byte[] JfifId = "JFIF\0"u8.ToArray();

    private const int GpsPointer = 0x8825;
    private const int ExifPointer = 0x8769;
    private const int OrientationTag = 0x0112;
    private const int ColourSpaceTag = 0xA001;
    private const int InteropPointer = 0xA005;
    private const int InteropIndexTag = 0x0001;

    private readonly record struct Segment(int Start, int End, byte Marker);

    // The copy's bytes, or null where this file cannot be read as a JPEG this
    // understands - a segment past the end, a directory pointing outside its
    // block. Nothing half done comes back.
    public static byte[]? Remove(byte[] jpeg, IReadOnlyCollection<PhotoInfoService.Field> fields, bool other)
    {
        if (jpeg.Length < 4 || jpeg[0] != 0xFF || jpeg[1] != 0xD8 ||
            Parse(jpeg) is not var (segments, scanStart, imageEnd))
        {
            return null;
        }

        var remove = new HashSet<PhotoInfoService.Field>(fields);
        var exif = segments.FirstOrDefault(s => s.Marker == 0xE1 && Is(jpeg, s, ExifId));
        bool hasExif = exif.End > 0;

        // What goes whole: 그 밖의 정보 takes it anyway, and these rows do too.
        bool opaqueGoes = other || OpaqueRows.Any(remove.Contains);

        byte[]? exifBlock = null;
        int orientation = 1;
        int? colourSpace = null;
        byte[]? interopIndex = null;
        if (hasExif)
        {
            // The TIFF block alone, edited on a copy: its offsets count from
            // its own first byte.
            int tiffStart = exif.Start + 4 + ExifId.Length;
            exifBlock = jpeg[tiffStart..exif.End];
            var tiff = new Tiff(exifBlock);
            if (!tiff.Valid)
            {
                return null;
            }

            orientation = tiff.Orientation;
            colourSpace = tiff.ColourSpace;
            interopIndex = tiff.InteropIndex;
            if (other && AllPresentRowsTicked(tiff, remove))
            {
                // Everything: no EXIF but what the picture needs to be shown
                // right (see the note above).
                exifBlock = null;
            }
            else if (!tiff.Remove(remove, other, opaqueGoes))
            {
                return null;
            }
        }

        var output = new MemoryStream(jpeg.Length);
        output.Write(jpeg, 0, 2);
        foreach (var segment in segments)
        {
            if (hasExif && segment.Start == exif.Start)
            {
                if (exifBlock is not null)
                {
                    WriteSegment(output, 0xE1, ExifId, exifBlock);
                }
                else
                {
                    WriteSmallestExif(output, orientation, colourSpace, interopIndex);
                }
                continue;
            }

            if (Kept(jpeg, segment, opaqueGoes) is not { } kept)
            {
                continue;
            }

            if (kept && segment.Marker == 0xE1 && Is(jpeg, segment, XmpId) && remove.Count > 0)
            {
                // A packet repeating a ticked row's facts loses them too.
                int bodyStart = segment.Start + 4 + XmpId.Length;
                if (TrimXmp(jpeg[bodyStart..segment.End], remove) is { } packet)
                {
                    if (packet.Length > 0 && packet.Length + XmpId.Length + 2 <= 0xFFFF)
                    {
                        WriteSegment(output, 0xE1, XmpId, packet);
                    }
                    continue;
                }
            }

            output.Write(jpeg, segment.Start, segment.End - segment.Start);
        }

        // The image itself, from the first scan to the end of the picture.
        output.Write(jpeg, scanStart, imageEnd - scanStart);

        // What follows the picture stays only while nothing that takes it is
        // ticked.
        if (!opaqueGoes && imageEnd < jpeg.Length)
        {
            output.Write(jpeg, imageEnd, jpeg.Length - imageEnd);
        }

        return output.ToArray();
    }

    // Every row the file has a value for is ticked (rows it has nothing for
    // cannot be, and do not count against "everything").
    private static bool AllPresentRowsTicked(Tiff tiff, HashSet<PhotoInfoService.Field> remove)
    {
        foreach (var field in Enum.GetValues<PhotoInfoService.Field>())
        {
            if (!remove.Contains(field) && tiff.Has(field))
            {
                return false;
            }
        }
        return true;
    }

    // Null: not copied. True: copied (an XMP packet perhaps trimmed). The
    // headers and the colour profile always stay; everything else that is
    // metadata goes with 그 밖의 정보 and with OpaqueRows.
    private static bool? Kept(byte[] jpeg, Segment segment, bool opaqueGoes)
    {
        byte marker = segment.Marker;
        if (marker is < 0xE0 or > 0xEF && marker != 0xFE)
        {
            return true; // tables, frame header, restart interval
        }

        if (marker == 0xE0 && Is(jpeg, segment, JfifId))
        {
            return true;
        }
        if (marker == 0xE2 && Is(jpeg, segment, IccId))
        {
            return true;
        }
        if (marker == 0xEE && Is(jpeg, segment, AdobeId))
        {
            return true;
        }

        return opaqueGoes ? null : true;
    }

    // tiff:Model="..." and its kind, as attributes or elements, for the ticked
    // rows. Null when nothing was taken (the original bytes stand); empty
    // when the packet is gone. A packet this cannot read is dropped whole
    // rather than kept: a ticked row's value may be in it.
    private static byte[]? TrimXmp(byte[] body, HashSet<PhotoInfoService.Field> remove)
    {
        string text = Encoding.UTF8.GetString(body);
        int start = text.IndexOf("<x:xmpmeta", StringComparison.Ordinal);
        if (start < 0)
        {
            start = text.IndexOf("<x:xapmeta", StringComparison.Ordinal);
        }
        int close = text.IndexOf("</x:xmpmeta>", StringComparison.Ordinal);
        int closeLength = "</x:xmpmeta>".Length;
        if (close < 0)
        {
            close = text.IndexOf("</x:xapmeta>", StringComparison.Ordinal);
        }
        if (start < 0 || close < start)
        {
            return [];
        }

        XElement root;
        try
        {
            root = XElement.Parse(text[start..(close + closeLength)], LoadOptions.PreserveWhitespace);
        }
        catch (System.Xml.XmlException)
        {
            return [];
        }

        bool changed = false;
        foreach (var element in root.DescendantsAndSelf().ToList())
        {
            foreach (var attribute in element.Attributes().ToList())
            {
                if (XmpField(attribute.Name) is { } field && remove.Contains(field))
                {
                    attribute.Remove();
                    changed = true;
                }
            }

            if (element.Parent is not null && XmpField(element.Name) is { } elementField && remove.Contains(elementField))
            {
                element.Remove();
                changed = true;
            }
        }

        if (!changed)
        {
            return null;
        }

        string trimmed = text[..start] + root.ToString(SaveOptions.DisableFormatting) + text[(close + closeLength)..];
        return Encoding.UTF8.GetBytes(trimmed);
    }

    private static void WriteSegment(MemoryStream output, byte marker, byte[] id, byte[] body)
    {
        int length = 2 + id.Length + body.Length;
        output.WriteByte(0xFF);
        output.WriteByte(marker);
        output.WriteByte((byte)(length >> 8));
        output.WriteByte((byte)length);
        output.Write(id);
        output.Write(body);
    }

    // What the whole lot leaves. With only an orientation this is the same
    // 36-byte block JpegOrientation inserts; nothing at all when the picture
    // stands as stored and is sRGB, which is what a reader assumes of a file
    // that says nothing. Reads back as itself, so a copy run through the
    // remover again comes out byte-identical and has nothing to remove.
    private static void WriteSmallestExif(MemoryStream output, int orientation, int? colourSpace, byte[]? interopIndex)
    {
        bool turned = orientation is >= 2 and <= 8;
        bool notSrgb = colourSpace is { } space && space != 1;
        if (!turned && !notSrgb)
        {
            return;
        }

        var tiff = new MemoryStream();
        void Write16(int value)
        {
            tiff.WriteByte((byte)(value >> 8));
            tiff.WriteByte((byte)value);
        }
        void Write32(int value)
        {
            Write16(value >> 16);
            Write16(value & 0xFFFF);
        }
        void Entry(int tag, int type, int value)
        {
            Write16(tag);
            Write16(type);
            Write32(1);
            if (type == 3)
            {
                // A short sits at the front of the value's four bytes.
                Write16(value);
                Write16(0);
            }
            else
            {
                Write32(value);
            }
        }

        tiff.Write("MM"u8);
        Write16(0x2A);
        Write32(8);

        int ifd0Entries = (turned ? 1 : 0) + (notSrgb ? 1 : 0);
        Write16(ifd0Entries);
        if (turned)
        {
            Entry(OrientationTag, 3, orientation);
        }

        int exifAt = 8 + 2 + ifd0Entries * 12 + 4;
        if (notSrgb)
        {
            Entry(ExifPointer, 4, exifAt);
        }
        Write32(0);

        if (notSrgb)
        {
            int exifEntries = interopIndex is null ? 1 : 2;
            Write16(exifEntries);
            Entry(ColourSpaceTag, 3, colourSpace!.Value);
            if (interopIndex is not null)
            {
                Entry(InteropPointer, 4, exifAt + 2 + exifEntries * 12 + 4);
            }
            Write32(0);

            if (interopIndex is not null)
            {
                Write16(1);
                Write16(InteropIndexTag);
                Write16(2);
                Write32(interopIndex.Length);
                tiff.Write(interopIndex);
                Write32(0);
            }
        }

        WriteSegment(output, 0xE1, ExifId, tiff.ToArray());
    }

    // The segments before the first scan, where that scan starts, and where
    // the picture ends (after its end marker) - the scans walked marker by
    // marker, a progressive file having several.
    private static (List<Segment> Segments, int ScanStart, int ImageEnd)? Parse(byte[] jpeg)
    {
        var segments = new List<Segment>();
        int pos = 2;
        int scanStart = -1;
        while (pos + 2 <= jpeg.Length)
        {
            if (jpeg[pos] != 0xFF)
            {
                return null;
            }

            byte marker = jpeg[pos + 1];
            if (marker == 0xFF)
            {
                pos++;
                continue;
            }

            if (marker == 0xD9)
            {
                return scanStart < 0 ? null : (segments, scanStart, pos + 2);
            }

            if (marker is 0x01 or >= 0xD0 and <= 0xD8)
            {
                pos += 2;
                continue;
            }

            if (pos + 4 > jpeg.Length)
            {
                return null;
            }

            int length = (jpeg[pos + 2] << 8) | jpeg[pos + 3];
            if (length < 2 || pos + 2 + length > jpeg.Length)
            {
                return null;
            }

            if (marker == 0xDA)
            {
                if (scanStart < 0)
                {
                    scanStart = pos;
                }

                // The entropy-coded data runs to the next marker that is not
                // a stuffed byte or a restart.
                int scan = pos + 2 + length;
                while (scan + 1 < jpeg.Length &&
                       !(jpeg[scan] == 0xFF && jpeg[scan + 1] != 0x00 && jpeg[scan + 1] is not (>= 0xD0 and <= 0xD7)))
                {
                    scan++;
                }
                pos = scan;
                continue;
            }

            if (scanStart < 0)
            {
                segments.Add(new Segment(pos, pos + 2 + length, marker));
            }
            pos += 2 + length;
        }

        return null;
    }

    private static bool Is(byte[] jpeg, Segment segment, byte[] id)
        => segment.End - segment.Start >= 4 + id.Length &&
           jpeg.AsSpan(segment.Start + 4, id.Length).SequenceEqual(id);

    // The TIFF block of an EXIF segment, edited in place.
    private sealed class Tiff
    {
        private readonly byte[] _data;
        private readonly bool _little;
        private readonly int _ifd0;

        public Tiff(byte[] data)
        {
            _data = data;
            if (data.Length < 8)
            {
                return;
            }

            if (data[0] == 'I' && data[1] == 'I')
            {
                _little = true;
            }
            else if (!(data[0] == 'M' && data[1] == 'M'))
            {
                return;
            }

            long ifd0 = U32(4);
            if (ifd0 < 8 || !DirectoryFits(ifd0))
            {
                return;
            }

            _ifd0 = (int)ifd0;
            Valid = true;
        }

        public bool Valid { get; }

        public int Orientation
            => Find(_ifd0, OrientationTag) is { } entry && U16(entry + 2) == 3
                ? U16(entry + 8) is var value and >= 1 and <= 8 ? value : 1
                : 1;

        public int? ColourSpace
            => ExifIfd() is { } exif && Find(exif, ColourSpaceTag) is { } entry && U16(entry + 2) == 3
                ? U16(entry + 8)
                : null;

        // R03 and its kind, as stored: three letters and a zero, which fit in
        // the entry itself. Anything longer is not one of the defined values.
        public byte[]? InteropIndex
        {
            get
            {
                if (ExifIfd() is not { } exif || Find(exif, InteropPointer) is not { } pointer ||
                    U32(pointer + 8) is var interop && !DirectoryFits(interop) ||
                    Find((int)interop, InteropIndexTag) is not { } entry ||
                    U16(entry + 2) != 2 || U32(entry + 4) != 4)
                {
                    return null;
                }
                return _data[(entry + 8)..(entry + 12)];
            }
        }

        // Whether the file holds anything for this row.
        public bool Has(PhotoInfoService.Field field)
        {
            if (field == PhotoInfoService.Field.Location)
            {
                return Find(_ifd0, GpsPointer) is not null;
            }

            if (Entries(_ifd0).Any(e => Ifd0Tags.TryGetValue(U16(e), out var f) && f.Contains(field)))
            {
                return true;
            }

            return ExifIfd() is { } exif &&
                   Entries(exif).Any(e => ExifTags.TryGetValue(U16(e), out var f) && f.Contains(field));
        }

        public bool Remove(HashSet<PhotoInfoService.Field> fields, bool other, bool opaqueGoes)
        {
            // The EXIF directory first: its place is read from IFD0, which is
            // compacted after.
            if (ExifIfd() is { } exif &&
                !RemoveEntries(exif, tag => ExifTags.TryGetValue(tag, out var f) && f.Any(fields.Contains) ||
                                            other && ExifOtherTags.Contains(tag)))
            {
                return false;
            }

            if (Find(_ifd0, GpsPointer) is { } gps)
            {
                long gpsIfd = U32(gps + 8);
                if (fields.Contains(PhotoInfoService.Field.Location))
                {
                    if (!DirectoryFits(gpsIfd) || !ClearDirectory((int)gpsIfd))
                    {
                        return false;
                    }
                }
                else if (fields.Contains(PhotoInfoService.Field.DateTaken))
                {
                    // GPSTimeStamp and GPSDateStamp: the shooting time again,
                    // in UTC, on a photo whose position is staying.
                    if (!DirectoryFits(gpsIfd) || !RemoveEntries((int)gpsIfd, tag => tag is 0x0007 or 0x001D))
                    {
                        return false;
                    }
                }
            }

            // The thumbnail's directory, read before IFD0 moves.
            int next = NextIfdPosition(_ifd0);
            long ifd1 = U32(next);
            if (opaqueGoes && ifd1 != 0)
            {
                if (!DirectoryFits(ifd1))
                {
                    return false;
                }

                // Its picture, then the directory itself.
                int ifd1Start = (int)ifd1;
                if (Find(ifd1Start, 0x0201) is { } at && Find(ifd1Start, 0x0202) is { } length)
                {
                    long offset = U32(at + 8), count = U32(length + 8);
                    if (offset + count <= _data.Length)
                    {
                        Array.Clear(_data, (int)offset, (int)count);
                    }
                }
                if (!ClearDirectory(ifd1Start))
                {
                    return false;
                }
            }

            if (!RemoveEntries(_ifd0, tag =>
                    Ifd0Tags.TryGetValue(tag, out var f) && f.Any(fields.Contains) ||
                    other && Ifd0OtherTags.Contains(tag) ||
                    tag == GpsPointer && fields.Contains(PhotoInfoService.Field.Location)))
            {
                return false;
            }

            if (opaqueGoes)
            {
                Write32(NextIfdPosition(_ifd0), 0);
            }

            return true;
        }

        private int? ExifIfd()
            => Find(_ifd0, ExifPointer) is { } entry && U32(entry + 8) is var offset && DirectoryFits(offset)
                ? (int)offset
                : null;

        private IEnumerable<int> Entries(int ifd)
        {
            int count = U16(ifd);
            for (int i = 0; i < count; i++)
            {
                yield return ifd + 2 + i * 12;
            }
        }

        private int? Find(int ifd, int tag)
        {
            foreach (int entry in Entries(ifd))
            {
                if (U16(entry) == tag)
                {
                    return entry;
                }
            }
            return null;
        }

        private int NextIfdPosition(int ifd) => ifd + 2 + U16(ifd) * 12;

        // Each matching entry's value zeroed where it lies, then the entry
        // taken out: the ones after it move up, the next-directory pointer
        // follows them, and the freed tail of the directory is zeroed too.
        private bool RemoveEntries(int ifd, Func<int, bool> matches)
        {
            int count = U16(ifd);
            var kept = new List<byte[]>(count);
            foreach (int entry in Entries(ifd))
            {
                if (matches(U16(entry)))
                {
                    if (!ClearValue(entry))
                    {
                        return false;
                    }
                }
                else
                {
                    kept.Add(_data[entry..(entry + 12)]);
                }
            }

            if (kept.Count == count)
            {
                return true;
            }

            long nextIfd = U32(NextIfdPosition(ifd));
            int end = NextIfdPosition(ifd) + 4;
            Write16(ifd, kept.Count);
            int at = ifd + 2;
            foreach (var entry in kept)
            {
                Array.Copy(entry, 0, _data, at, 12);
                at += 12;
            }
            Write32(at, nextIfd);
            Array.Clear(_data, at + 4, end - (at + 4));
            return true;
        }

        // A whole directory and everything its entries point at, zeroed.
        private bool ClearDirectory(int ifd)
        {
            foreach (int entry in Entries(ifd))
            {
                if (!ClearValue(entry))
                {
                    return false;
                }
            }
            Array.Clear(_data, ifd, 2 + U16(ifd) * 12 + 4);
            return true;
        }

        // The value bytes of one entry: in the entry itself when they fit in
        // four, else at the offset it holds.
        private bool ClearValue(int entry)
        {
            int size = TypeSize(U16(entry + 2));
            long count = U32(entry + 4);
            long bytes = size * count;
            if (size == 0 || bytes <= 4)
            {
                Array.Clear(_data, entry + 8, 4);
                return true;
            }

            long offset = U32(entry + 8);
            if (offset < 8 || offset + bytes > _data.Length)
            {
                return false;
            }

            Array.Clear(_data, (int)offset, (int)bytes);
            return true;
        }

        private static int TypeSize(int type) => type switch
        {
            1 or 2 or 6 or 7 => 1,
            3 or 8 => 2,
            4 or 9 or 11 or 13 => 4,
            5 or 10 or 12 => 8,
            _ => 0,
        };

        private bool DirectoryFits(long ifd)
            => ifd >= 8 && ifd + 2 <= _data.Length && ifd + 2 + U16((int)ifd) * 12L + 4 <= _data.Length;

        private int U16(int at)
            => _little ? _data[at] | (_data[at + 1] << 8) : (_data[at] << 8) | _data[at + 1];

        private long U32(int at)
            => _little
                ? (uint)(_data[at] | (_data[at + 1] << 8) | (_data[at + 2] << 16) | (_data[at + 3] << 24))
                : (uint)((_data[at] << 24) | (_data[at + 1] << 16) | (_data[at + 2] << 8) | _data[at + 3]);

        private void Write16(int at, int value)
        {
            if (_little)
            {
                _data[at] = (byte)value;
                _data[at + 1] = (byte)(value >> 8);
            }
            else
            {
                _data[at] = (byte)(value >> 8);
                _data[at + 1] = (byte)value;
            }
        }

        private void Write32(int at, long value)
        {
            if (_little)
            {
                _data[at] = (byte)value;
                _data[at + 1] = (byte)(value >> 8);
                _data[at + 2] = (byte)(value >> 16);
                _data[at + 3] = (byte)(value >> 24);
            }
            else
            {
                _data[at] = (byte)(value >> 24);
                _data[at + 1] = (byte)(value >> 16);
                _data[at + 2] = (byte)(value >> 8);
                _data[at + 3] = (byte)value;
            }
        }
    }
}
