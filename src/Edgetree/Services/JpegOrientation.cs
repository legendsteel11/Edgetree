namespace SidebarExplorer.App.Services;

// TURNING A JPEG WITHOUT TOUCHING ITS PICTURE (2026-11-05): the EXIF
// orientation value is rewritten - two bytes - and every other byte of the
// file is copied as it was. Turned back, the file is the original again, byte
// for byte.
//
// Measured before choosing, on four files (two phone photos at 4000x1848 with
// orientation 6, a 3840x2160 baseline and a 7680x2160 progressive JPEG), each
// turned four times and compared pixel for pixel with the original:
//  - JpegBitmapEncoder.Rotation is lossless only for a baseline JPEG whose
//    sides are multiples of 16. The phone photo - the thing most often turned -
//    was re-encoded without a word: 2.3MB down to 1.4MB, about 100,000 pixels
//    off by more than 8, and its orientation 6 kept, so it came out turned
//    twice. The progressive file was re-encoded as baseline. Nothing in the
//    API says which of the two happened.
//  - Decoding, turning and encoding at quality 95 loses a little more on
//    every turn (pixels off by more than 8: 13,884 after one, 84,327 after
//    four).
//  - Writing only the metadata through WPF leaves the pixels exact, but the
//    file changes around them: the EXIF block re-serialised (32,937 bytes to
//    20,266, byte order swapped), the colour profile replaced, and the 68KB a
//    Samsung camera appends after the picture dropped.
// The tag is also what the phone relies on in the first place: its photos are
// stored on their side with 6 in this field.
public static class JpegOrientation
{
    // The orientation after the picture as shown is turned by quarter turns
    // clockwise (negative: counter-clockwise). The eight values are the four
    // turns, each with and without a mirror; the mirror stays and the turns
    // add up - read as MainWindow.ApplyExifOrientation reads them, mirrored
    // first and then turned.
    public static int Turned(int orientation, int quarterTurns)
    {
        var (mirrored, turn) = orientation switch
        {
            2 => (true, 0),
            3 => (false, 2),
            4 => (true, 2),
            5 => (true, 3),
            6 => (false, 1),
            7 => (true, 1),
            8 => (false, 3),
            _ => (false, 0),
        };

        turn = ((turn + quarterTurns) % 4 + 4) % 4;
        return (mirrored, turn) switch
        {
            (false, 0) => 1,
            (false, 1) => 6,
            (false, 2) => 3,
            (false, 3) => 8,
            (true, 0) => 2,
            (true, 1) => 7,
            (true, 2) => 4,
            _ => 5,
        };
    }

    private static readonly byte[] ExifId = "Exif\0\0"u8.ToArray();
    private static readonly byte[] XmpId = "http://ns.adobe.com/xap/1.0/\0"u8.ToArray();
    private const int OrientationTag = 0x0112;

    // The file turned, or null where its tag cannot carry the turn: not a
    // JPEG, a segment that runs past the end of the file, an orientation
    // stored as anything but one SHORT, or an EXIF block too full to take one
    // more entry. `written` is the orientation the result carries.
    //
    // Three shapes of file:
    //  - EXIF with an orientation entry (every phone photo): its value is
    //    overwritten where it stands.
    //  - EXIF without one: the first directory is written again one entry
    //    longer, after everything else in the segment, and the header pointed
    //    at it. Nothing already there moves, so every offset in the block -
    //    the camera's own notes, the thumbnail - still points where it did;
    //    the old directory stays behind, unreferenced.
    //  - No EXIF at all (a wallpaper, a screenshot): a 36-byte segment holding
    //    only the orientation goes in after the start of the file, or after
    //    the JFIF header where there is one, since JFIF must come first.
    // An XMP packet's tiff:Orientation, where there is one, is given the same
    // value, so a program that reads XMP first does not see the old one.
    public static byte[]? Turn(byte[] jpeg, int quarterTurns, out int written)
    {
        written = 0;
        if (jpeg.Length < 4 || jpeg[0] != 0xFF || jpeg[1] != 0xD8)
        {
            return null;
        }

        int exif = -1, xmp = -1, afterJfif = 2;
        int pos = 2;
        while (true)
        {
            if (pos + 2 > jpeg.Length || jpeg[pos] != 0xFF)
            {
                return null;
            }

            byte marker = jpeg[pos + 1];
            if (marker == 0xFF)
            {
                // A fill byte before the marker.
                pos++;
                continue;
            }

            // The metadata segments all come before the first scan.
            if (marker is 0xDA or 0xD9)
            {
                break;
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

            if (marker == 0xE0 && pos == 2)
            {
                afterJfif = pos + 2 + length;
            }
            else if (marker == 0xE1)
            {
                if (exif < 0 && StartsWith(jpeg, pos + 4, length - 2, ExifId))
                {
                    exif = pos;
                }
                else if (xmp < 0 && StartsWith(jpeg, pos + 4, length - 2, XmpId))
                {
                    xmp = pos;
                }
            }

            pos += 2 + length;
        }

        if (exif < 0)
        {
            written = Turned(1, quarterTurns);
            byte[] segment =
            {
                0xFF, 0xE1, 0x00, 0x22,
                (byte)'E', (byte)'x', (byte)'i', (byte)'f', 0x00, 0x00,
                (byte)'M', (byte)'M', 0x00, 0x2A, 0x00, 0x00, 0x00, 0x08,
                0x00, 0x01,
                0x01, 0x12, 0x00, 0x03, 0x00, 0x00, 0x00, 0x01, 0x00, (byte)written, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x00,
            };
            var inserted = new byte[jpeg.Length + segment.Length];
            Array.Copy(jpeg, 0, inserted, 0, afterJfif);
            Array.Copy(segment, 0, inserted, afterJfif, segment.Length);
            Array.Copy(jpeg, afterJfif, inserted, afterJfif + segment.Length, jpeg.Length - afterJfif);
            return PatchXmp(inserted, xmp < 0 ? -1 : xmp >= afterJfif ? xmp + segment.Length : xmp, written);
        }

        int segmentEnd = exif + 2 + ((jpeg[exif + 2] << 8) | jpeg[exif + 3]);
        int tiff = exif + 10;
        if (tiff + 8 > segmentEnd)
        {
            return null;
        }

        bool little;
        if (jpeg[tiff] == 'I' && jpeg[tiff + 1] == 'I')
        {
            little = true;
        }
        else if (jpeg[tiff] == 'M' && jpeg[tiff + 1] == 'M')
        {
            little = false;
        }
        else
        {
            return null;
        }

        long ifdOffset = Read32(jpeg, tiff + 4, little);
        if (ifdOffset < 8 || tiff + ifdOffset + 2 > segmentEnd)
        {
            return null;
        }

        int ifd = tiff + (int)ifdOffset;
        int count = Read16(jpeg, ifd, little);
        int entries = ifd + 2;
        if (entries + count * 12 + 4 > segmentEnd)
        {
            return null;
        }

        for (int i = 0; i < count; i++)
        {
            int entry = entries + i * 12;
            if (Read16(jpeg, entry, little) != OrientationTag)
            {
                continue;
            }

            if (Read16(jpeg, entry + 2, little) != 3 || Read32(jpeg, entry + 4, little) != 1)
            {
                return null;
            }

            int current = Read16(jpeg, entry + 8, little);
            written = Turned(current is >= 1 and <= 8 ? current : 1, quarterTurns);
            var patched = (byte[])jpeg.Clone();
            Write16(patched, entry + 8, written, little);
            return PatchXmp(patched, xmp, written);
        }

        // No orientation entry: the directory again, one entry longer, in tag
        // order as TIFF requires, starting on an even offset.
        written = Turned(1, quarterTurns);
        int pad = (segmentEnd - tiff) % 2;
        int size = 2 + (count + 1) * 12 + 4;
        int newLength = segmentEnd - exif - 2 + pad + size;
        if (newLength > 0xFFFF)
        {
            return null;
        }

        var directory = new byte[size];
        Write16(directory, 0, count + 1, little);
        int at = 2;
        bool placed = false;
        for (int i = 0; i < count; i++)
        {
            int entry = entries + i * 12;
            if (!placed && Read16(jpeg, entry, little) > OrientationTag)
            {
                WriteOrientationEntry(directory, at, written, little);
                at += 12;
                placed = true;
            }

            Array.Copy(jpeg, entry, directory, at, 12);
            at += 12;
        }

        if (!placed)
        {
            WriteOrientationEntry(directory, at, written, little);
            at += 12;
        }

        // Where the next directory (the thumbnail's) starts, as it was.
        Array.Copy(jpeg, entries + count * 12, directory, at, 4);

        var grown = new byte[jpeg.Length + pad + size];
        Array.Copy(jpeg, 0, grown, 0, segmentEnd);
        Array.Copy(directory, 0, grown, segmentEnd + pad, size);
        Array.Copy(jpeg, segmentEnd, grown, segmentEnd + pad + size, jpeg.Length - segmentEnd);
        grown[exif + 2] = (byte)(newLength >> 8);
        grown[exif + 3] = (byte)newLength;
        Write32(grown, tiff + 4, segmentEnd + pad - tiff, little);
        return PatchXmp(grown, xmp < 0 ? -1 : xmp > exif ? xmp + pad + size : xmp, written);
    }

    // tiff:Orientation="6" as an attribute or <tiff:Orientation>6</...> as an
    // element; the digit is replaced where it stands, so the packet keeps its
    // length. Anything else is left as it is.
    private static byte[] PatchXmp(byte[] jpeg, int xmp, int value)
    {
        if (xmp < 0)
        {
            return jpeg;
        }

        int end = xmp + 2 + ((jpeg[xmp + 2] << 8) | jpeg[xmp + 3]);
        var name = "tiff:Orientation"u8;
        for (int at = xmp + 4; at + name.Length + 3 <= end; at++)
        {
            if (!jpeg.AsSpan(at, name.Length).SequenceEqual(name))
            {
                continue;
            }

            int digit = at + name.Length;
            if (jpeg[digit] == '=')
            {
                digit++;
            }

            if (jpeg[digit] is not ((byte)'"' or (byte)'\'' or (byte)'>'))
            {
                continue;
            }

            digit++;
            if (digit + 1 < end && jpeg[digit] is >= (byte)'1' and <= (byte)'8' &&
                jpeg[digit + 1] is not (>= (byte)'0' and <= (byte)'9'))
            {
                jpeg[digit] = (byte)('0' + value);
            }
        }

        return jpeg;
    }

    private static void WriteOrientationEntry(byte[] target, int at, int value, bool little)
    {
        Write16(target, at, OrientationTag, little);
        Write16(target, at + 2, 3, little);
        Write32(target, at + 4, 1, little);
        Write16(target, at + 8, value, little);
        target[at + 10] = 0;
        target[at + 11] = 0;
    }

    private static bool StartsWith(byte[] data, int at, int available, byte[] prefix)
        => available >= prefix.Length && data.AsSpan(at, prefix.Length).SequenceEqual(prefix);

    private static int Read16(byte[] data, int at, bool little)
        => little ? data[at] | (data[at + 1] << 8) : (data[at] << 8) | data[at + 1];

    private static long Read32(byte[] data, int at, bool little)
        => little
            ? (uint)(data[at] | (data[at + 1] << 8) | (data[at + 2] << 16) | (data[at + 3] << 24))
            : (uint)((data[at] << 24) | (data[at + 1] << 16) | (data[at + 2] << 8) | data[at + 3]);

    private static void Write16(byte[] data, int at, int value, bool little)
    {
        if (little)
        {
            data[at] = (byte)value;
            data[at + 1] = (byte)(value >> 8);
        }
        else
        {
            data[at] = (byte)(value >> 8);
            data[at + 1] = (byte)value;
        }
    }

    private static void Write32(byte[] data, int at, long value, bool little)
    {
        if (little)
        {
            data[at] = (byte)value;
            data[at + 1] = (byte)(value >> 8);
            data[at + 2] = (byte)(value >> 16);
            data[at + 3] = (byte)(value >> 24);
        }
        else
        {
            data[at] = (byte)(value >> 24);
            data[at + 1] = (byte)(value >> 16);
            data[at + 2] = (byte)(value >> 8);
            data[at + 3] = (byte)value;
        }
    }
}
