using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Application = System.Windows.Application;

namespace SidebarExplorer.App.Services;

// Fetches the same thumbnail Windows Explorer's "큰 아이콘" view shows for a
// file, via IShellItemImageFactory - which also means the OS thumbnail cache
// (thumbcache) does the caching: a file thumbnailed once by anything on the
// system comes back instantly, and this app deliberately keeps no cache of its
// own (the context menu shows one image at a time; it's not an image viewer).
//
// Always asynchronous: first-time generation can take real time (a large image
// on a NAS), so the menu opens immediately with a reserved empty slot and the
// image lands via the callback when ready - same two-stage pattern as
// ShellIconService's per-file icons.
//
// GDI/COM discipline, same containment rule as the icon service: the HBITMAP
// and the COM factory both live and die inside Extract; only a frozen WPF
// ImageSource ever leaves this class.
public static class ShellThumbnailService
{
    // onCompleted is invoked on the UI thread, with a null image when the
    // file has no thumbnail to give (unsupported/corrupted/unreadable) - the
    // caller collapses its slot rather than showing an empty box forever.
    // pixelWidth/Height are the ORIGINAL image's dimensions (the thumbnail is
    // scaled, so they can't be read off it), 0 when they couldn't be read -
    // decoded header-only, on the same background hop as the thumbnail.
    public static void GetThumbnail(string path, int pixelSize, Action<ImageSource?, int, int> onCompleted)
        => GetPreview(path, pixelSize, thumbnailOnly: true, onCompleted);

    // For callers that only want the picture. The dimensions below cost a
    // SECOND open of the same file - cheap locally, a second network round trip
    // over SMB - and the filmstrip throws them away: it asks for one thumbnail
    // per realised cell, so on a NAS folder that was one wasted open per cell
    // scrolled past (found 2026-08-10, chasing a stutter that grew while
    // browsing 1329 photos).
    //
    // embeddedOnly is what makes fetching AHEAD of the strip affordable. The
    // header a JPEG carries costs tens of KB; the shell's answer costs the whole
    // file, and sustained 2-5MB reads over SMB dropped a network drive off the
    // network while the strip was scrolled (2026-08-11). A speculative fetch
    // takes the cheap answer or no answer at all; a file that is actually
    // arrived at is still asked properly.
    public static void GetThumbnailOnly(string path, int pixelSize, Action<ImageSource?> onCompleted,
        bool embeddedOnly = false)
        => GetPreview(path, pixelSize, thumbnailOnly: true,
            (image, _, _) => onCompleted(image), readDimensions: false, embeddedOnly: embeddedOnly);

    // The picture a JPEG already carries inside itself.
    //
    // This is the difference between us and the viewers that open a folder of
    // 1359 NAS photos in seconds. The measurement that made sense of it: 869ms
    // average per shell thumbnail on a cold NAS folder, worst 2784ms. The shell builds its thumbnail by reading
    // and decoding the WHOLE file - 2-5MB each over SMB. Almost every JPEG a
    // phone or camera writes already holds a small one in its EXIF header, and
    // reading that touches only the first few tens of KB.
    //
    // DelayCreation + CacheOption.None is what keeps it to the header: the
    // decoder is asked for the thumbnail and nothing else, so the pixels of the
    // full image are never fetched.
    //
    // Returns null when there is no embedded thumbnail (PNG, most screenshots,
    // some editors' output) - the caller falls back to the shell, which is
    // still the only answer for those.
    public static ImageSource? TryReadEmbedded(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            var frame = BitmapDecoder.Create(stream,
                BitmapCreateOptions.DelayCreation, BitmapCacheOption.None).Frames[0];
            if (frame.Thumbnail is not { } embedded)
            {
                return null;
            }

            // CachedBitmap, and it has to be INSIDE the using: what the decoder
            // hands back is delay-created and still tied to this stream, so
            // freezing it and returning it produced cells that simply never
            // drew - a strip with gaps scattered through it rather than a slow
            // one (2026-08-11). The pixels are read here, while the file is
            // still open, and what leaves this method owns them.
            var cached = new CachedBitmap(embedded, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);

            // The header's orientation applies to the embedded picture exactly
            // as it does to the full one, and the shell already honours it - so
            // without this the strip would disagree with itself depending on
            // which path answered.
            var oriented = ApplyOrientation(cached, ReadOrientation(frame));
            oriented.Freeze();
            return oriented;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException
                                      or FileFormatException or ArgumentException or OverflowException)
        {
            return null;
        }
    }

    private static int ReadOrientation(BitmapFrame frame)
    {
        try
        {
            if (frame.Metadata is not BitmapMetadata metadata)
            {
                return 1;
            }

            object? value = metadata.Format switch
            {
                "jpg" or "jpeg" => metadata.GetQuery("/app1/ifd/{ushort=274}"),
                "tiff" => metadata.GetQuery("/ifd/{ushort=274}"),
                _ => null,
            };

            return value is ushort tag && tag is >= 1 and <= 8 ? tag : 1;
        }
        catch (Exception e) when (e is NotSupportedException or ArgumentException
                                     or InvalidOperationException or IOException)
        {
            return 1;
        }
    }

    private static BitmapSource ApplyOrientation(BitmapSource source, int orientation)
    {
        if (orientation <= 1)
        {
            return source;
        }

        BitmapSource result = orientation is 2 or 4 or 5 or 7
            ? new TransformedBitmap(source, new ScaleTransform(-1, 1))
            : source;

        double angle = orientation switch
        {
            3 or 4 => 180,
            5 or 8 => 270,
            6 or 7 => 90,
            _ => 0,
        };

        return angle == 0 ? result : new TransformedBitmap(result, new RotateTransform(angle));
    }

    // thumbnailOnly:false lets the shell fall back to the file-type icon -
    // what the viewer panel shows for a non-image selection. The context
    // menu's thumbnail slot keeps thumbnailOnly:true (see the flag note in
    // Extract).
    // How many of these are running right now. Nothing here throttles - every
    // call is its own Task.Run - so on a folder of 1329 files the filmstrip can
    // put one in flight per cell it realises, all on the same SMB connection.
    // Read by the viewer's load instrument; if this number climbs while
    // browsing, the strip is the thing to bound (2026-08-10).
    private static int _inFlight;

    public static int InFlight => Volatile.Read(ref _inFlight);

    // Set by the viewer's instrument in Debug builds. What it is looking for:
    // this Task.Run is an MTA thread pool thread, and a shell COM object created
    // there can come back as a PROXY whose calls marshal onto the app's STA
    // thread - i.e. onto the UI thread. If that is what is happening, an
    // Extract that takes 1.5s on a cold NAS freezes the window for 1.5s, which
    // matches both the size and the timing of the stalls left unexplained after
    // everything else was timed and cleared (2026-08-10).
    public static Action<string>? Trace;

    // A few at a time, NEWEST FIRST, because both halves were measured on a
    // cold NAS folder (2026-08-10):
    //
    //   314 extractions, average 869ms each, worst 2784ms
    //
    // Every one of those used to be its own Task.Run, fired the moment a cell
    // was realised, all onto one SMB connection. Three things came of it: the
    // NAS itself stopped answering twice, each call got slower because they
    // were competing, and - the part that makes it feel broken rather than
    // slow - the queue was FIFO, so the cells actually on screen waited behind
    // hundreds for cells scrolled past long ago.
    //
    // A STACK fixes the last one for free: the newest request is the one the
    // eye is on. Old entries still run, just last, and the OS thumbnail cache
    // makes the second visit to any folder cheap regardless.
    //
    // NOT a semaphore around Task.Run: that would hold a pool thread per
    // waiting job, and 300 blocked threads is its own outage.
    // The bound exists for the NAS and is priced for the NAS: two, because
    // three dropped it off the network again while a cold folder was being
    // browsed (2026-08-10). Local disks were paying it too, and a local folder
    // of 170 photos filled its strip at a visibly unhurried pace as a result
    // (2026-08-11).
    //
    // So the caller sets it per folder. Everything in this class is a queue in
    // front of one resource; how much of that resource there is depends on
    // where the files are, and only the caller knows that.
    private static int _maxWorkers = 2;

    public static int MaxWorkers
    {
        get => Volatile.Read(ref _maxWorkers);
        set => Volatile.Write(ref _maxWorkers, Math.Clamp(value, 1, 8));
    }
    // Per-source totals, so the flush can say what the two paths actually cost
    // instead of listing the slow ones. "misses" is the count that came back
    // with nothing - for the embedded row that is files with no header
    // thumbnail, which is the number that decides whether fetching ahead is
    // worth doing at all on a network folder.
    private sealed class CostTally
    {
        public int Count;
        public int Misses;
        public double TotalMs;
        public double MaxMs;
    }

    private static readonly object CostGate = new();
    private static readonly Dictionary<string, CostTally> Costs = new();

    private static void RecordCost(string source, bool missed, double ms)
    {
        lock (CostGate)
        {
            if (!Costs.TryGetValue(source, out var tally))
            {
                Costs[source] = tally = new CostTally();
            }

            tally.Count++;
            if (missed)
            {
                tally.Misses++;
            }

            tally.TotalMs += ms;
            tally.MaxMs = Math.Max(tally.MaxMs, ms);
        }
    }

    public static int PendingCostCount
    {
        get
        {
            lock (CostGate)
            {
                return Costs.Values.Sum(tally => tally.Count);
            }
        }
    }

    // Read and reset together: each flush describes the run it belongs to, so
    // two runs can be compared without subtracting one from the other by hand.
    public static List<string> DrainCostSummary()
    {
        lock (CostGate)
        {
            var lines = Costs
                .Where(pair => pair.Value.Count > 0)
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair =>
                    $"  {pair.Key} x{pair.Value.Count,5}   avg {pair.Value.TotalMs / pair.Value.Count,6:F0} ms" +
                    $"   max {pair.Value.MaxMs,6:F0} ms   none {pair.Value.Misses}")
                .ToList();
            Costs.Clear();
            return lines;
        }
    }

    private static readonly object PendingGate = new();
    private static readonly Stack<(string Path, int PixelSize, bool ThumbnailOnly,
        bool ReadDimensions, bool EmbeddedOnly, Action<ImageSource?, int, int> OnCompleted)> Pending = new();
    private static int _workers;

    // Everything QUEUED BUT NOT STARTED, thrown away. This queue is
    // speculative - a strip asking ahead for pictures the eye may never reach -
    // and it outlived the thing that asked for it. Closing the viewer left over
    // a thousand requests standing, and that cost twice: each request holds a
    // callback, each callback holds the cell it was going to fill, and each
    // cell holds a bitmap whose pixels are UNMANAGED, so emptying the strip
    // freed nothing and a 1.3GB process stayed at 1.3GB; then the workers went
    // on reading files and filling cells nobody could see, putting new bitmaps
    // behind the same roots (2026-08-12, memory.log).
    //
    // Only the queue - a job already running finishes and answers into a
    // callback that finds nothing to do, which is one wasted read at most.
    // _inFlight is corrected by hand here because it is incremented on the
    // PUSH, not on the start, and the instrument reading it would otherwise
    // count these forever.
    public static void DropPending()
    {
        lock (PendingGate)
        {
            int dropped = Pending.Count;
            if (dropped == 0)
            {
                return;
            }

            Pending.Clear();
            Interlocked.Add(ref _inFlight, -dropped);
        }
    }

    public static void GetPreview(string path, int pixelSize, bool thumbnailOnly,
        Action<ImageSource?, int, int> onCompleted, bool readDimensions = true,
        bool embeddedOnly = false)
    {
        Interlocked.Increment(ref _inFlight);
        lock (PendingGate)
        {
            Pending.Push((path, pixelSize, thumbnailOnly, readDimensions, embeddedOnly, onCompleted));
            if (_workers >= MaxWorkers)
            {
                return;
            }

            _workers++;
        }

        Task.Run(() =>
        {
            while (true)
            {
                (string Path, int PixelSize, bool ThumbnailOnly, bool ReadDimensions,
                    bool EmbeddedOnly, Action<ImageSource?, int, int> OnCompleted) job;
                lock (PendingGate)
                {
                    if (!Pending.TryPop(out job))
                    {
                        _workers--;
                        return;
                    }
                }

                RunPreview(job.Path, job.PixelSize, job.ThumbnailOnly, job.ReadDimensions,
                    job.EmbeddedOnly, job.OnCompleted);
            }
        });
    }

    private static void RunPreview(string path, int pixelSize, bool thumbnailOnly,
        bool readDimensions, bool embeddedOnly, Action<ImageSource?, int, int> onCompleted)
    {
        try
        {
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();

            // Our own cache first, and it answers from LOCAL disk however far
            // away the file itself is. That ordering is the point: everything
            // below reads the original, and for a folder on a NAS every one of
            // those reads is network traffic paid again.
            ImageSource? thumbnail = null;
            string source = "cache   ";
            bool fromCache = false;

            // AN SVG IS DRAWN HERE FIRST when it is simple enough (2026-10-06,
            // see SvgRenderer): ahead of the cache, which may hold an empty
            // picture an installed handler gave for it, and never written to
            // it - an icon is a few KB of shapes, cheaper to draw than to store.
            bool isSvg = IsSvg(path);
            bool selfDrawn = false;
            if (isSvg && thumbnailOnly)
            {
                thumbnail = SvgRenderer.TryRender(path, pixelSize);
                selfDrawn = thumbnail is not null;
                if (selfDrawn)
                {
                    source = "svg     ";
                }
            }

            if (thumbnail is null && thumbnailOnly)
            {
                thumbnail = ThumbnailCacheService.TryRead(path, pixelSize);
                if (isSvg && IsFlat(thumbnail))
                {
                    thumbnail = null;
                }
                fromCache = thumbnail is not null;
            }

            // Then the file's own header, and the shell only if it has no
            // thumbnail of its own. Traced separately so the three costs stay
            // comparable - the whole point of this path is that they should not
            // be.
            if (thumbnail is null && thumbnailOnly)
            {
                source = "embedded";
                thumbnail = TryReadEmbedded(path);
            }

            if (thumbnail is null && !embeddedOnly)
            {
                source = "shell   ";
                thumbnail = Extract(path, pixelSize, thumbnailOnly);

                // An SVG handler's answer that is ONE value all over - fully
                // transparent, or solid black - is no picture (measured
                // 2026-10-06: PowerToys' handler gave exactly those two for
                // Material Symbols, at 128 and at 801, every time). Treated as
                // no thumbnail, so the caller shows the type icon, and nothing
                // empty is stored. Only for SVG: a solid frame is a real
                // thumbnail for a film that opens on black.
                if (isSvg && IsFlat(thumbnail))
                {
                    thumbnail = null;
                }
            }

            // Only what was just made, and only for the thumbnail-only callers:
            // the viewer's non-image mode falls back to a file-type ICON, which
            // is not a picture of this file and must never be stored as one.
            if (thumbnail is not null && thumbnailOnly && !fromCache && !selfDrawn)
            {
                ThumbnailCacheService.Write(path, thumbnail, pixelSize);
            }

            LogThumb(path, source.Trim(), fromCache, thumbnail, pixelSize);

            {
                double ms = (System.Diagnostics.Stopwatch.GetTimestamp() - t0)
                    * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
                // Counted whatever it cost, because the number this path was
                // built for is SMALL and a "log the slow ones" threshold is
                // exactly what hid it: 100ms of per-file lines and not one of
                // them embedded, which reads as "the header path never runs"
                // when it may equally mean "it always beat the threshold".
                RecordCost(source, thumbnail is null, ms);
                if (Trace is { } trace && ms >= 100)
                {
                    trace($"  thumb {source} {ms,7:F0} ms  {System.IO.Path.GetFileName(path)}");
                }
            }

            int pixelWidth = 0, pixelHeight = 0;
            if (readDimensions)
            {
                try
                {
                    using var stream = File.OpenRead(path);
                    var frame = BitmapDecoder.Create(stream,
                        BitmapCreateOptions.DelayCreation, BitmapCacheOption.None).Frames[0];
                    pixelWidth = frame.PixelWidth;
                    pixelHeight = frame.PixelHeight;
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException or FileFormatException or ArgumentException)
                {
                    // No WIC codec / unreadable header - the info line just omits
                    // the dimensions.
                }
            }

            // BROUGHT DOWN TO THE SIZE ASKED FOR HERE, where every source
            // meets. Doing it inside the shell call covered a third of the
            // answers and left the number unmoved: most of a filmstrip comes
            // back from this app's OWN cache or from a JPEG's embedded
            // thumbnail, neither of which passes through Extract, and both of
            // which hand over whatever size they happen to hold - measured
            // 256x193 against a 96px request, for 2,402 files, twice over
            // (2026-08-12). A caller that asks for 96 should be given 96
            // whatever answered it.
            thumbnail = ShrinkToAsked(thumbnail, pixelSize);

            Application.Current?.Dispatcher.BeginInvoke(() => onCompleted(thumbnail, pixelWidth, pixelHeight));
        }
        finally
        {
            Interlocked.Decrement(ref _inFlight);
        }
    }

    private static bool IsSvg(string path)
        => path.EndsWith(".svg", StringComparison.OrdinalIgnoreCase);

    // Every pixel the same value, transparency included. An icon on a
    // transparent ground is never this - its shape differs from its ground -
    // however few colours it uses.
    private static bool IsFlat(ImageSource? image)
    {
        if (image is not BitmapSource bitmap || bitmap.PixelWidth == 0 || bitmap.PixelHeight == 0)
        {
            return false;
        }

        var bgra = bitmap.Format == PixelFormats.Bgra32 || bitmap.Format == PixelFormats.Pbgra32
            ? bitmap
            : new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
        int stride = bgra.PixelWidth * 4;
        var pixels = new byte[stride * bgra.PixelHeight];
        bgra.CopyPixels(pixels, stride, 0);
        uint first = BitConverter.ToUInt32(pixels, 0);
        for (int i = 4; i < pixels.Length; i += 4)
        {
            if (BitConverter.ToUInt32(pixels, i) != first)
            {
                return false;
            }
        }
        return true;
    }

    private static ImageSource? Extract(string path, int pixelSize, bool thumbnailOnly)
    {
        IShellItemImageFactory? factory = null;
        IntPtr hBitmap = IntPtr.Zero;
        try
        {
            var factoryIid = typeof(IShellItemImageFactory).GUID;
            if (SHCreateItemFromParsingName(path, IntPtr.Zero, ref factoryIid, out factory) != 0 ||
                factory is null)
            {
                return null;
            }

            // THUMBNAILONLY (the thumbnail slot's mode): fail rather than fall
            // back to a file-type icon - the menu slot is for an actual
            // preview, and the row already has its icon. Without it (the
            // viewer's non-image mode) the shell answers with the icon
            // instead. BIGGERSIZEOK: a cached larger image is fine, the Image
            // control scales it down.
            var size = new SIZE { cx = pixelSize, cy = pixelSize };
            int flags = SIIGBF_BIGGERSIZEOK | (thumbnailOnly ? SIIGBF_THUMBNAILONLY : 0);
            if (factory.GetImage(size, flags, out hBitmap) != 0 ||
                hBitmap == IntPtr.Zero)
            {
                return null;
            }

            return ShrinkToAsked(ToBitmapSource(hBitmap), pixelSize);
        }
        catch (Exception e) when (e is COMException or ArgumentException or InvalidCastException)
        {
            return null;
        }
        finally
        {
            if (hBitmap != IntPtr.Zero)
            {
                DeleteObject(hBitmap);
            }
            if (factory is not null)
            {
                Marshal.ReleaseComObject(factory);
            }
        }
    }

    // BIGGERSIZEOK means the shell may answer with whatever it already has, and
    // it does: a 96px request came back 256x193, because 256 is the size its
    // own cache keeps (measured 2026-08-12). Receiving that is exactly what the
    // flag is for - it is the fast answer, with no rescale at the shell's end -
    // but KEEPING it is a different question. A filmstrip of 2,400 frames held
    // 600MB of 256px pictures to draw 86MB of 96px ones.
    //
    // So the picture is brought down to the size that was asked for, once, here
    // where it arrives. The pixels are COPIED rather than wrapped:
    // TransformedBitmap is a lazy view that keeps its source alive, so wrapping
    // would hold both the big one and the small one and save nothing at all.
    private static ImageSource? ShrinkToAsked(ImageSource? source, int pixelSize)
    {
        // Only a real pixel buffer can be shrunk, and only one that is bigger
        // than the ask needs to be.
        if (source is not BitmapSource image ||
            (image.PixelWidth <= pixelSize && image.PixelHeight <= pixelSize))
        {
            return source;
        }

        try
        {
            double factor = pixelSize / (double)Math.Max(image.PixelWidth, image.PixelHeight);
            var scaled = new TransformedBitmap(image, new ScaleTransform(factor, factor));
            var copy = new WriteableBitmap(scaled);
            copy.Freeze();
            return copy;
        }
        catch (Exception e) when (e is COMException or ArgumentException or InvalidOperationException)
        {
            // Better the oversized picture than none.
            return image;
        }
    }

    // 한 줄에 셋을 담는다: 누가 만들었는가(캐시·헤더·셸), DIB 헤더가 뭐라고
    // 했는가, 그리고 나온 그림의 크기. **뒤집힌 것들이 biHeight 부호 하나로
    // 갈리면 검사를 되살리면 되고, 안 갈리면 헤더는 정말 못 믿는 것이라 다른
    // 길을 봐야 한다.** 셸이 아닌 경로에서는 biHeight 가 의미 없으므로 - 로 찍는다.
    [System.Diagnostics.Conditional("DEBUG")]
    private static void LogThumb(string path, string source, bool fromCache, ImageSource? image,
        int asked)
    {
        try
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Edgetree");
            Directory.CreateDirectory(dir);
            string bih = source == "shell"
                ? (_lastDibHeight > 0 ? $"+{_lastDibHeight} (bottom-up)"
                    : _lastDibHeight < 0 ? $"{_lastDibHeight} (top-down)" : "0 (no dib)")
                : "-";
            string size = image is BitmapSource bs ? $"{bs.PixelWidth}x{bs.PixelHeight}" : "-";
            File.AppendAllText(
                Path.Combine(dir, "thumb.log"),
                $"{DateTime.Now:HH:mm:ss.fff}  src={source,-8} cache={(fromCache ? "yes" : "no ")} " +
                $"asked={asked,-4} biHeight={bih,-18} size={size,-9} " +
                $"{Orientation(path, image)} {Path.GetFileName(path)}{Environment.NewLine}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    // 썸네일 로그에 한 줄 끼워 넣는 자리. 뒤집힘을 재는 동안에는 그림이 언제
    // 다시 요청됐는지가 같은 시간축에 있어야 읽히므로, 바깥에서 일어난 일도
    // 이 파일에 적는다.
    [System.Diagnostics.Conditional("DEBUG")]
    public static void LogNote(string line)
    {
        try
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Edgetree");
            Directory.CreateDirectory(dir);
            File.AppendAllText(
                Path.Combine(dir, "thumb.log"),
                $"{DateTime.Now:HH:mm:ss.fff}  {line}{Environment.NewLine}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    // ----- 뒤집힘을 눈이 아니라 파일 자신에게 물어보는 계측기 (2026-08-21) -----
    //
    // 지금까지 판정은 사람 눈이었고, 그래서 표를 만드는 데 재현 절차 한 벌이
    // 통째로 들었다. **기준은 파일 자신의 디코드로 잡으면 된다** - 앱이 직접
    // 디코딩하는 경로는 한 번도 뒤집힌 적이 없으므로, 같은 파일을 작게 디코딩해
    // 위·아래 밝기 차이의 부호를 셸이 준 그림과 견주면 뒤집힘이 객관적으로
    // 갈린다. 대칭이라 부호가 안 잡히는 그림은 flat 으로 찍고 판정에서 뺀다.
    //
    // **같은 줄에 `conv=` 를 함께 찍는 것이 이번 계측의 핵심이다.**
    // `CreateBitmapSourceFromHBitmap` 은 DIB 헤더를 믿고 줄 순서를 맞추므로,
    // 헤더가 거짓말을 하는 비트맵에서는 그것도 같이 틀린다 - 즉 2026-08-09에
    // 죽은 높이 부호 검사와 **같은 이론**일 수 있다. 그것이 사실이면 `conv=` 가
    // 지금 바로 서 있는 것들에서 down 으로 찍힌다. 한 번 돌리면 이 길이 살아
    // 있는지 죽었는지가 확정된다.
    //
    // 비용이 큰 계측기라 환경 변수로만 켠다(파일마다 디코드 한 번 더). DEBUG
    // 빌드에서 `EDGETREE_THUMBPROBE=1` 일 때만 돈다.
    private static readonly bool ProbeOrientation =
        Environment.GetEnvironmentVariable("EDGETREE_THUMBPROBE") == "1";

    [ThreadStatic]
    private static double? _lastConvSignature;

    private static string Orientation(string path, ImageSource? image)
    {
        if (!ProbeOrientation)
        {
            return string.Empty;
        }

        double? reference = ReferenceSignature(path);
        string raw = Verdict(reference, (image as BitmapSource) is { } bs ? Signature(bs) : null);
        string conv = Verdict(reference, _lastConvSignature);
        _lastConvSignature = null;
        return $"raw={raw,-5} conv={conv,-5}";
    }

    private static string Verdict(double? reference, double? candidate)
    {
        if (reference is not { } r || candidate is not { } c)
        {
            return "-";
        }

        // 위아래가 비슷한 그림은 어느 쪽으로 놓아도 같은 부호가 나오므로 판정에
        // 쓰면 안 된다. 이 문턱을 넘는 그림만 답으로 친다.
        if (Math.Abs(r) < 0.02 || Math.Abs(c) < 0.02)
        {
            return "flat";
        }

        return Math.Sign(r) == Math.Sign(c) ? "up" : "DOWN";
    }

    // 파일 자신을 작게 디코딩한 기준. 셸을 거치지 않는 유일한 길이다.
    private static double? ReferenceSignature(string path)
    {
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.UriSource = new Uri(path);
            image.DecodePixelWidth = 32;
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            image.EndInit();
            image.Freeze();
            return Signature(image);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or
                                      NotSupportedException or FileFormatException or
                                      ArgumentException or UriFormatException or
                                      InvalidOperationException or OverflowException)
        {
            // 영상·문서처럼 WIC 가 못 여는 것은 기준이 없다.
            return null;
        }
    }

    // 위 4분의 1과 아래 4분의 1의 평균 밝기 차이. 부호만 쓰므로 크기·화질과
    // 무관하고, 두 그림을 같은 형식(Pbgra32)으로 맞춰 재기 때문에 투명한 부분이
    // 한쪽에서만 검게 나오는 일도 없다.
    private static double? Signature(BitmapSource source)
    {
        try
        {
            if (source.PixelWidth < 4 || source.PixelHeight < 4)
            {
                return null;
            }

            var small = new TransformedBitmap(source,
                new ScaleTransform(16.0 / source.PixelWidth, 16.0 / source.PixelHeight));
            var converted = new FormatConvertedBitmap(small, PixelFormats.Pbgra32, null, 0);
            int w = converted.PixelWidth, h = converted.PixelHeight;
            if (w < 2 || h < 4)
            {
                return null;
            }

            int stride = w * 4;
            var pixels = new byte[stride * h];
            converted.CopyPixels(pixels, stride, 0);

            int band = Math.Max(1, h / 4);
            double top = 0, bottom = 0;
            for (int y = 0; y < band; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int i = y * stride + x * 4;
                    top += pixels[i] + pixels[i + 1] + pixels[i + 2];
                }
            }

            for (int y = h - band; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int i = y * stride + x * 4;
                    bottom += pixels[i] + pixels[i + 1] + pixels[i + 2];
                }
            }

            return (top - bottom) / (band * w * 3 * 255.0);
        }
        catch (Exception e) when (e is COMException or ArgumentException or
                                      InvalidOperationException or NotSupportedException)
        {
            return null;
        }
    }

    // 헤더를 믿는 변환이 같은 비트맵을 어느 쪽으로 세우는지. 이것 하나 때문에
    // 썸네일마다 변환이 한 번 더 도므로 계측기가 켜졌을 때만 부른다.
    [System.Diagnostics.Conditional("DEBUG")]
    private static void ProbeHeaderConversion(IntPtr hBitmap)
    {
        if (!ProbeOrientation)
        {
            return;
        }

        try
        {
            var converted = Imaging.CreateBitmapSourceFromHBitmap(
                hBitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            converted.Freeze();
            _lastConvSignature = Signature(converted);
        }
        catch (Exception e) when (e is COMException or ArgumentException or
                                      InvalidOperationException)
        {
            _lastConvSignature = null;
        }
    }

    private static BitmapSource? ToBitmapSource(IntPtr hBitmap)
    {
        // Reading the DIB's bits directly preserves the (premultiplied) alpha
        // channel that CreateBitmapSourceFromHBitmap throws away - without
        // this, a transparent PNG's thumbnail lands on a solid black square.
        // The buffer is read as top-down, and WHETHER THAT IS RIGHT IS ASKED
        // PER BITMAP rather than assumed - see RowsAreReversed.
        //
        // A HEIGHT-SIGN CHECK WAS TRIED HERE AND REMOVED (2026-08-09). The
        // theory was that bottom-up DIBs (biHeight > 0) needed their rows
        // reversed, which would explain the upside-down folder icons. It
        // never demonstrated a fix - the icons kept flipping with it in
        // place, and what actually solved them was moving the ICON path off
        // GetImage entirely and onto an HICON from the system image list
        // (ShellIconService.GetViewerIcon), which carries no orientation
        // header to be wrong about. Meanwhile a video's thumbnail came back
        // upside down WITH the check in place, i.e. the header said bottom-up
        // for a bitmap whose rows were not. The lesson is that these headers
        // cannot be trusted in either direction, so the code does not consult
        // them: thumbnails are read the way they were read before the theory,
        // which is the way that worked.
        _lastDibHeight = GetObject(hBitmap, Marshal.SizeOf<DIBSECTION>(), out DIBSECTION dib) != 0
            ? dib.dsBmih.biHeight
            : 0;
        ProbeHeaderConversion(hBitmap);

        if (GetObject(hBitmap, Marshal.SizeOf<BITMAP>(), out BITMAP bmp) != 0 &&
            bmp.bmBitsPixel == 32 && bmp.bmBits != IntPtr.Zero)
        {
            var source = RowsAreReversed(hBitmap, bmp)
                ? FromReversedRows(bmp)
                : BitmapSource.Create(
                    bmp.bmWidth, bmp.bmHeight, 96, 96, PixelFormats.Pbgra32, null,
                    bmp.bmBits, bmp.bmWidthBytes * bmp.bmHeight, bmp.bmWidthBytes);
            source.Freeze();
            return source;
        }

        // Not a 32bpp DIB (rare) - the alpha-less conversion is still a
        // correct picture for opaque formats.
        var fallback = Imaging.CreateBitmapSourceFromHBitmap(
            hBitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
        fallback.Freeze();
        return fallback;
    }

    // ----- 줄 순서를 짐작하지 않고 물어본다 (2026-08-21, 신고 · 측정) ---------
    //
    // 셸이 주는 32bpp 비트맵의 실제 줄 순서는 **요청 크기에 따라 달라진다.**
    // 2026-08-21 측정(`thumb.log`, 이미지 20여 건 전부 일치):
    //
    //   asked=96   biHeight=+96 (bottom-up)   → 메모리 첫 줄이 그림의 **아래쪽**
    //   asked=128  biHeight=+256 (bottom-up)  → 메모리 첫 줄이 그림의 **위쪽**
    //
    // 96은 셸이 그 크기로 새로 만들어 주는 답이고, 128은 자기 256 캐시로 주는
    // 답이다. **헤더는 둘 다 bottom-up 이라 여전히 못 믿는다** - 2026-08-09에
    // 높이 부호 검사를 뺀 판단은 그대로 유효하다. 썸네일 바 높이를 조금 키우면
    // 바로 서던 것이 이것이었다: 단계를 넘으면서 크기가 바뀌어 다른 쪽 답을
    // 받았을 뿐이다.
    //
    // 그래서 헤더 대신 **같은 비트맵을 두 방식으로 읽어 맞대 본다.**
    // CreateBitmapSourceFromHBitmap 은 방향을 제대로 세워 주지만 투명도를
    // 버리므로(투명 PNG가 검은 사각형이 되는 것이 그것이라 지금 raw 읽기를
    // 쓴다) 그림 자체로는 못 쓴다. 대신 **줄 순서를 물어보는 데는 쓸 수 있다.**
    // 그쪽의 첫 줄이 이쪽의 첫 줄과 맞으면 그대로, 마지막 줄과 맞으면 뒤집힌
    // 것이다.
    //
    // 비교는 **완전히 불투명한 화소(알파 255)에서만** 한다. 거기서는 미리 곱한
    // 값과 그냥 값이 같으므로 두 읽기가 바이트까지 일치해야 하고, 투명한 자리의
    // 값 차이가 판정을 흐리지 않는다. 맞아떨어지는 화소가 너무 적으면(거의 다
    // 투명한 아이콘) 판정을 포기하고 지금까지의 동작 그대로 둔다.
    //
    // 비용은 셸 경로마다 변환 한 번이다. 캐시·헤더 경로는 여기를 지나지 않는다.
    private static bool RowsAreReversed(IntPtr hBitmap, BITMAP bmp)
    {
        if (bmp.bmHeight < 2 || bmp.bmWidth < 1)
        {
            return false;
        }

        try
        {
            var upright = Imaging.CreateBitmapSourceFromHBitmap(
                hBitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            if (upright.PixelWidth != bmp.bmWidth || upright.PixelHeight != bmp.bmHeight)
            {
                return false;
            }

            int stride = bmp.bmWidthBytes;
            int enough = Math.Max(4, bmp.bmWidth / 8);
            var top = new byte[stride];
            var bottom = new byte[stride];
            var reference = new byte[stride];

            // 맨 윗줄부터 보되 **위아래가 서로 다른 첫 줄**을 골라 판정한다. 첫
            // 줄과 끝 줄이 같은 그림이 실제로 많고(가장자리가 단색인 스크린샷,
            // 위아래로 여백이 있는 그림), 그런 줄로는 어느 쪽에 맞는지가 갈리지
            // 않아 판정을 포기하게 된다 - 2026-08-21 측정에서 남아 있던 뒤집힘
            // 열 건 중 여덟 건이 이것이었다. 줄 몇 개만 보고 끝내므로 비용은
            // 그대로다.
            int scan = Math.Min(bmp.bmHeight / 2, 24);
            for (int k = 0; k <= scan; k++)
            {
                Marshal.Copy(IntPtr.Add(bmp.bmBits, k * stride), top, 0, stride);
                Marshal.Copy(IntPtr.Add(bmp.bmBits, (bmp.bmHeight - 1 - k) * stride),
                    bottom, 0, stride);
                if (OpaqueMatches(top, bottom, bmp.bmWidth) >= bmp.bmWidth - enough)
                {
                    // 위아래가 사실상 같은 줄. 이걸로는 못 가른다.
                    continue;
                }

                upright.CopyPixels(new Int32Rect(0, k, bmp.bmWidth, 1), reference, stride, 0);
                int matchesTop = OpaqueMatches(reference, top, bmp.bmWidth);
                int matchesBottom = OpaqueMatches(reference, bottom, bmp.bmWidth);
                if (matchesTop >= enough && matchesTop > matchesBottom)
                {
                    return false;
                }

                if (matchesBottom >= enough && matchesBottom > matchesTop)
                {
                    return true;
                }
            }

            // 어느 쪽으로도 충분히 안 맞으면(거의 투명한 그림) 건드리지 않는다.
            // 이 안전장치가 하는 일은 "모르겠으면 예전 그대로"다.
            return false;
        }
        catch (Exception e) when (e is COMException or ArgumentException or
                                      InvalidOperationException or NotSupportedException)
        {
            return false;
        }
    }

    private static int OpaqueMatches(byte[] reference, byte[] candidate, int width)
    {
        int matches = 0;
        for (int x = 0; x < width; x++)
        {
            int i = x * 4;
            if (candidate[i + 3] != 255)
            {
                continue;
            }

            if (candidate[i] == reference[i] &&
                candidate[i + 1] == reference[i + 1] &&
                candidate[i + 2] == reference[i + 2])
            {
                matches++;
            }
        }

        return matches;
    }

    // 줄 순서를 뒤집어 담은 사본. 알파를 그대로 들고 오므로 투명 PNG가 검은
    // 사각형이 되지 않는다.
    private static BitmapSource FromReversedRows(BITMAP bmp)
    {
        int stride = bmp.bmWidthBytes;
        var buffer = new byte[stride * bmp.bmHeight];
        for (int y = 0; y < bmp.bmHeight; y++)
        {
            Marshal.Copy(
                IntPtr.Add(bmp.bmBits, (bmp.bmHeight - 1 - y) * stride),
                buffer, y * stride, stride);
        }

        return BitmapSource.Create(
            bmp.bmWidth, bmp.bmHeight, 96, 96, PixelFormats.Pbgra32, null, buffer, stride);
    }

    private const int SIIGBF_BIGGERSIZEOK = 0x00000001;
    private const int SIIGBF_THUMBNAILONLY = 0x00000008;

    [StructLayout(LayoutKind.Sequential)]
    private struct SIZE
    {
        public int cx;
        public int cy;
    }

    // ----- 뒤집힌 썸네일을 재기 위한 것 (2026-08-19, 신고) -------------------
    //
    // 신고: 썸네일 바의 그림이 종종 거꾸로 뜬다. **다시 가면 바로 서고, 갓 저장한
    // 파일에서 잘 난다.** 그 두 가지가 진단을 좁힌다 - 파일에 박힌 문제라면 같은
    // 파일이 항상 뒤집혀야 하므로, 처음 만든 그림과 나중에 캐시에서 온 그림이
    // 서로 다르다는 뜻이다.
    //
    // **높이 부호 검사는 2026-08-09에 한 번 넣었다가 뺐다**(아래 ToBitmapSource의
    // 주석). 그때 판정은 아이콘으로 했고 아이콘은 결국 다른 경로로 옮겨서
    // 해결됐으므로, 그 실험이 이 문제를 판정한 것이라고 보기 어렵다. 그래서
    // 되살리기 전에 잰다.
    //
    // GetObject 에 BITMAP 을 주면 bmHeight 는 **항상 양수**라 방향이 안 담긴다.
    // 방향은 DIB 섹션 헤더의 biHeight 부호에만 있다(양수면 상향식).
    [ThreadStatic]
    private static int _lastDibHeight;

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DIBSECTION
    {
        public BITMAP dsBm;
        public BITMAPINFOHEADER dsBmih;
        public uint dsBitfields0;
        public uint dsBitfields1;
        public uint dsBitfields2;
        public IntPtr dshSection;
        public uint dsOffset;
    }

    [DllImport("gdi32.dll", EntryPoint = "GetObjectW")]
    private static extern int GetObject(IntPtr h, int c, out DIBSECTION pv);

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAP
    {
        public int bmType;
        public int bmWidth;
        public int bmHeight;
        public int bmWidthBytes;
        public ushort bmPlanes;
        public ushort bmBitsPixel;
        public IntPtr bmBits;
    }

    [ComImport]
    [Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        [PreserveSig]
        int GetImage(SIZE size, int flags, out IntPtr phbm);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int SHCreateItemFromParsingName(
        string pszPath, IntPtr pbc, ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory? ppv);

    [DllImport("gdi32.dll")]
    private static extern int GetObject(IntPtr h, int c, out BITMAP pv);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);
}
