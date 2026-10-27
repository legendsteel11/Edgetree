using System.IO;

namespace SidebarExplorer.App.Services;

// Which FILES the tree lists, by kind. Folders are never filtered - they are
// the way through the tree, and a filter that could hide the road as well as
// the destination would be a filter you cannot navigate out of.
//
// The shape is a deliberate echo of 폴더 숨기기: it removes noise so the thing
// you are looking for is easier to see. Same rule follows from that - SEARCH
// IGNORES THIS ENTIRELY (see the scope scan), exactly as it looks inside hidden
// folders. A filter is about what the tree shows, not about what exists.
//
// Categories overlap on purpose. ".ts" is TypeScript and an MPEG transport
// stream; ".bat" is a script and an executable. Rather than pick a winner, an
// extension may sit in several sets and a file shows if ANY selected category
// claims it - which is what someone picking both 코드 and 미디어 means.
public static class FileTypeFilter
{
    public const string Code = "code";
    public const string Image = "image";
    public const string Document = "document";
    public const string Media = "media";
    public const string Archive = "archive";
    public const string Executable = "executable";

    // Everything none of the others claims, including files with no extension
    // at all. It exists because the lists below WILL miss something - no list
    // of extensions is ever finished - and without it those files would vanish
    // with nothing the user could do about it.
    public const string Other = "other";

    // The extensions the user typed in themselves. Offered only once it holds
    // something, and deliberately NOT part of what 기타 answers to: 기타 means
    // "none of the six lists claims this", and letting a custom entry change
    // that would make two rows move when one was edited.
    public const string Custom = "custom";

    public static readonly string[] AllCategories =
    {
        Code, Image, Document, Media, Archive, Executable, Other,
    };

    private static readonly HashSet<string> CodeExtensions = Build(
        "c h cpp cc cxx c++ hpp hh hxx cs csx java kt kts scala sc groovy go rs swift m mm",
        "py pyw pyi rb rbw php phtml pl pm t lua r rmd jl dart ts tsx js jsx mjs cjs vue svelte",
        "html htm xhtml shtml css scss sass less styl json jsonc json5 xml xsd xsl xslt yaml yml",
        "toml ini cfg conf config properties env sql psql sh bash zsh fish ksh ps1 psm1 psd1",
        "bat cmd vbs asm s S f f77 f90 f95 for pas dpr vb fs fsi fsx ex exs erl hrl clj cljs",
        "cljc edn hs lhs ml mli nim zig v sv svh vhd vhdl tcl awk sed mk cmake gradle sbt",
        "csproj vbproj fsproj sln vcxproj proj targets props nuspec resx xaml axaml razor",
        "cshtml vbhtml aspx ascx asmx jsp ejs hbs handlebars mustache twig liquid pug jade haml",
        "erb tf tfvars hcl proto graphql gql ipynb patch diff gitignore gitattributes editorconfig",
        "dockerfile makefile rakefile gemfile podfile cabal nix elm purs re rei coffee litcoffee");

    private static readonly HashSet<string> ImageExtensions = Build(
        "jpg jpeg jpe jfif jif png apng gif bmp dib webp tif tiff ico cur svg svgz",
        "heic heif hif avif jxl raw arw cr2 cr3 nef nrw orf rw2 raf sr2 srf pef dng x3f",
        "psd psb xcf ai eps epsf ps indd cdr tga icb vda vst pcx ppm pgm pbm pnm exr hdr",
        "jp2 j2k jpf jpx jpm mj2 wdp hdp emf wmf dds kra clip sai pdn afphoto afdesign");

    private static readonly HashSet<string> DocumentExtensions = Build(
        "txt text md markdown mdown mkd rst adoc asciidoc tex latex bib rtf log nfo readme",
        "doc docx docm dot dotx dotm odt ott fodt pdf",
        "xls xlsx xlsm xlsb xlt xltx ods ots fods csv tsv",
        "ppt pptx pptm pps ppsx pot potx odp otp fodp",
        "epub mobi azw azw3 kfx fb2 lit djvu djv chm one onepkg onetoc2",
        "pages numbers key wpd wps hwp hwpx hml xps oxps vsd vsdx odg otg mpp");

    private static readonly HashSet<string> MediaExtensions = Build(
        "mp3 wav wave flac alac aac m4a m4b m4r ogg oga opus wma aiff aif aifc ape wv",
        "dsf dff mpc tta shn ra amr au snd mid midi kar mka mod xm it s3m sid",
        "mp4 m4v mkv avi mov qt wmv flv f4v webm mpg mpeg mpe m1v m2v mts m2ts ts tp vob",
        "3gp 3g2 rm rmvb asf ogv ogm divx xvid mxf dv f4p m2p mpv",
        "m3u m3u8 pls cue srt ass ssa sub idx vtt smi sami lrc");

    private static readonly HashSet<string> ArchiveExtensions = Build(
        "zip zipx rar r00 r01 7z tar gz tgz bz2 tbz tbz2 xz txz lz lzma lz4 zst zstd",
        "cab arj lzh lha ace pea zpaq cpio z arc sit sitx sqx uha kgb",
        "iso img dmg toast vcd mdf nrg wim esd swm",
        "jar war ear par pak pk3 pk4 nupkg whl egg gem crate xapk obb");

    // Things a person LAUNCHES. Deliberately not everything that contains
    // machine code: dll, sys, ocx, drv, efi and bare bin are components, not
    // programs - Windows itself files a .dll as "응용 프로그램 확장" rather than
    // "응용 프로그램", and nobody double-clicks one. This category exists to
    // answer "what can I run here", and a system folder's few hundred DLLs are
    // the noise a filter is supposed to remove, not the result (2026-08-02).
    // They fall to 기타, which is reachable.
    private static readonly HashSet<string> ExecutableExtensions = Build(
        "exe com scr cpl run out",
        "msi msix msixbundle appx appxbundle msp mst appimage flatpakref snap deb rpm pkg",
        "apk aab ipa app gadget jnlp air",
        "lnk url website appref-ms desktop pif scf",
        "bat cmd ps1 vbs vbe wsf wsh hta reg inf msc job workflow ahk au3");

    private static HashSet<string> Build(params string[] lines)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string line in lines)
        {
            foreach (string extension in line.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                set.Add(extension);
            }
        }
        return set;
    }

    // The active selection, mirrored from AppSettings.FileFilterCategories so a
    // listing does not have to reach for settings. EMPTY MEANS EVERYTHING -
    // "전체" is the absence of a filter rather than a category of its own, which
    // is why a fresh install and a cleared filter are the same state.
    public static readonly HashSet<string> SelectedCategories =
        new(StringComparer.OrdinalIgnoreCase);

    public static bool IsFiltering => SelectedCategories.Count > 0;

    // The user's own extension list, mirrored from
    // AppSettings.FileFilterCustomExtensions the same way SelectedCategories is
    // mirrored from FileFilterCategories - a listing asks this class, never the
    // settings.
    private static readonly HashSet<string> CustomExtensions =
        new(StringComparer.OrdinalIgnoreCase);

    public static bool HasCustomExtensions => CustomExtensions.Count > 0;

    public static void SetCustomExtensions(string storedList)
    {
        CustomExtensions.Clear();
        foreach (string extension in Split(storedList))
        {
            CustomExtensions.Add(extension);
        }
    }

    // The exclusion list, and the switch that arms it. This is the ONE rule in
    // this class that takes files away rather than letting them through, and
    // it is answered before everything else - see ShouldShowFile.
    //
    // Kept out of SelectedCategories on purpose. An exclusion is not a kind:
    // put it in that set and the set stops being empty, "전체" goes out, and
    // ShouldShowFile starts asking which categories claim the file - which
    // would empty the tree, since "not .log" claims nothing. Held apart, 전체
    // and an exclusion can be true at the same time, which is the state most
    // people actually want ("everything except these").
    private static readonly HashSet<string> ExcludedExtensions =
        new(StringComparer.OrdinalIgnoreCase);

    // The list can be kept while the rule is switched off, the same way any
    // other chip in the footer can be switched off without being emptied.
    private static bool _excludeEnabled = true;

    public static bool HasExcludedExtensions => ExcludedExtensions.Count > 0;

    public static bool IsExcluding => _excludeEnabled && ExcludedExtensions.Count > 0;

    public static void SetExcludedExtensions(string storedList)
    {
        ExcludedExtensions.Clear();
        foreach (string extension in Split(storedList))
        {
            ExcludedExtensions.Add(extension);
        }
    }

    public static void SetExcludeEnabled(bool enabled) => _excludeEnabled = enabled;

    // Takes what someone actually types - ".PNG, jpg;webp" - and returns the
    // stored form: lower case, no leading dots, no blanks, no repeats, in the
    // order given. Commas are what the hint asks for; semicolons, spaces and
    // newlines are accepted too rather than silently dropping half the input
    // because of the separator someone happened to reach for.
    public static string NormalizeExtensions(string? input)
        => string.Join(",", Split(input));

    private static IEnumerable<string> Split(string? input)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string piece in (input ?? string.Empty)
            .Split(new[] { ',', ';', ' ', '\t', '\r', '\n', '|' }, StringSplitOptions.RemoveEmptyEntries))
        {
            // A whole name is accepted as well as a bare extension, so pasting
            // "report.docx" gives docx rather than nothing. TrimStart handles
            // ".docx"; the Path call handles the rest.
            string extension = piece.Trim().TrimStart('.');
            if (extension.Contains('.'))
            {
                extension = Path.GetExtension(extension).TrimStart('.');
            }

            // '*' is what a file dialog's "*.psd" would leave behind, and any
            // path separator means this was never an extension at all.
            extension = extension.Trim('*', '"', '\'');
            if (extension.Length == 0 || extension.IndexOfAny(new[] { '\\', '/', ':' }) >= 0)
            {
                continue;
            }

            extension = extension.ToLowerInvariant();
            if (seen.Add(extension))
            {
                yield return extension;
            }
        }
    }

    // For the row and chip that offer it: "psd, ai, fig" rather than the stored
    // "psd,ai,fig".
    public static string DescribeExtensions(string storedList)
        => string.Join(", ", Split(storedList));

    public static bool ShouldShowFile(string fileName)
    {
        // TrimStart('.'): GetExtension hands back ".png", the sets hold "png".
        // A file with no extension at all gives "", which no set claims - so it
        // lands in 기타, which is exactly where it belongs, and no exclusion
        // list can name it either.
        string extension = Path.GetExtension(fileName).TrimStart('.');

        // First, and above everything: an excluded extension is gone whatever
        // else is selected. It beats 전체 (which is the absence of a filter,
        // not an override) and it beats a category or a custom list that would
        // otherwise let the file through - including one holding the same
        // extension, where the exclusion simply wins.
        if (IsExcluding && ExcludedExtensions.Contains(extension))
        {
            return false;
        }

        if (SelectedCategories.Count == 0)
        {
            return true;
        }

        foreach (string category in SelectedCategories)
        {
            if (Matches(category, extension))
            {
                return true;
            }
        }

        return false;
    }

    // "Could this be plain text under a name the text view does not list?" -
    // for the row menu's 텍스트로 보기 (2026-10-27). Code, the text line of
    // the documents, and anything no category claims (a README or a LICENSE
    // with no extension lands there). Not pictures, media, archives, office
    // binaries or programs: the read would only refuse them, and a row that
    // can only refuse is noise on every such menu.
    public static bool MayBeText(string fileName)
    {
        string extension = Path.GetExtension(fileName).TrimStart('.');
        return CodeExtensions.Contains(extension)
               || TextDocumentExtensions.Contains(extension)
               || Matches(Other, extension);
    }

    private static readonly HashSet<string> TextDocumentExtensions = Build(
        "txt text md markdown mdown mkd rst adoc asciidoc tex latex bib rtf log nfo readme");

    private static bool Matches(string category, string extension) => category switch
    {
        Code => CodeExtensions.Contains(extension),
        Image => ImageExtensions.Contains(extension),
        Document => DocumentExtensions.Contains(extension),
        Media => MediaExtensions.Contains(extension),
        Archive => ArchiveExtensions.Contains(extension),
        Executable => ExecutableExtensions.Contains(extension),
        Custom => CustomExtensions.Contains(extension),
        Other => !CodeExtensions.Contains(extension)
            && !ImageExtensions.Contains(extension)
            && !DocumentExtensions.Contains(extension)
            && !MediaExtensions.Contains(extension)
            && !ArchiveExtensions.Contains(extension)
            && !ExecutableExtensions.Contains(extension),
        _ => false,
    };
}
