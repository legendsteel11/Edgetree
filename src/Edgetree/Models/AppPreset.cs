using System.Reflection;
using System.Text.Json;

namespace SidebarExplorer.App.Models;

// A named snapshot of the app's SHAPE, swapped in from the header's right-click
// menu - "이것들을 이 모양으로 놓고 쓰다가, 저 모양으로 바꾼다".
//
// WHAT IS IN ONE IS DECIDED IN EXACTLY ONE PLACE: the Fields list below. That
// is the whole design. The alternative - saving everything except a list of
// exclusions - looks tidier until a setting that turns out to be DATA is added
// without anyone noticing, and then switching presets deletes someone's
// favorites. An allow-list fails the other way: a new option simply is not in
// presets until it is added here, which is a missing feature rather than a
// loss.
//
// The values are held by NAME rather than as typed properties, so that adding a
// field to a preset is one line here instead of one line here plus two
// assignments elsewhere that can disagree with it. Reflection is the price, and
// it is paid twice per preset press.
public class AppPreset
{
    // Five, and the number is not arbitrary: it is what Ctrl+Alt+1~5 can
    // address, which is where this is going.
    public const int MaxPresets = 5;

    public string Name { get; set; } = string.Empty;

    // Property name -> value as it was when the preset was taken. A JsonElement
    // rather than object so a preset survives the round trip through
    // settings.json with its type intact.
    public Dictionary<string, JsonElement> Values { get; set; } = new();

    // ----- 프리셋에 들어가는 것 -------------------------------------------
    //
    // Grouped by what they are, in the order they matter: SHAPE first, because
    // that is what a preset is for. Everything absent from this list stays
    // whatever it was when the preset was applied - see the note at the bottom.
    //
    // THE GROUPS ARE NOT DECORATION. Each one costs a different amount to put
    // on a running window - repainting every colour in the tree, or re-reading
    // every open folder off disk - so the apply path asks each group whether it
    // changed at all before paying for it. Without that, switching between two
    // presets that share a palette still repainted the whole tree, and one that
    // shared a filter still went back to disk for every open folder.

    // 창의 모양과 자리. DockOnRight picks the screen edge, the two ratios are
    // the band's height and where it sits in the leftover space - together they
    // are the whole of "좌측 하단에 작게" or "우측에 쭉".
    internal static readonly string[] WindowFields =
    {
        nameof(AppSettings.DockOnRight),
        nameof(AppSettings.DockedTopRatio),
        nameof(AppSettings.DockedHeightRatio),
        nameof(AppSettings.IsAutoHidden),
        nameof(AppSettings.AlwaysOnTop),

        // 창 모드와 그때의 기하 (2026-08-17). Reported as "the preset does not
        // carry 창 모드", and it was not a missing line here - there was no
        // field to name, and the apply path opens by docking. The four bounds
        // travel with the flag because a floating window without them is a
        // window at whatever size the last float happened to leave, which is
        // not a SHAPE and so not a preset.
        //
        // 도킹된 띠의 기하는 위의 두 ratio가 말하고, 이 넷은 창 모드일 때만
        // 쓰인다. 둘이 같은 창을 서로 다른 말로 적는 것이라 섞이지 않는다.
        nameof(AppSettings.IsFloating),
        nameof(AppSettings.FloatingLeft),
        nameof(AppSettings.FloatingTop),
        nameof(AppSettings.FloatingWidth),
        nameof(AppSettings.FloatingHeight),

        // 자동 숨김이 어떻게 동작하는지. The state above is which one it is in;
        // these are the terms it is in it on.
        nameof(AppSettings.AutoHideSliverWidth),
        nameof(AppSettings.AutoHideHandleWidth),
        nameof(AppSettings.AutoHideUseHandle),
        nameof(AppSettings.AutoHideSlide),
        nameof(AppSettings.AutoHideCloseOnMouseLeave),
    };

    // The three that decide how WIDE the window is, kept apart from the rest of
    // the shape because they are the ones that make the viewer panel have to be
    // closed and reopened. If none of them moved, the panel can be left exactly
    // as it is - which is the difference between a preset press that flickers
    // and one that does not.
    internal static readonly string[] SizeFields =
    {
        nameof(AppSettings.ExpandedWidth),
        nameof(AppSettings.ViewerOpen),
        nameof(AppSettings.ViewerWidth),
    };

    // 전체 덮기 IS NOT IN THE LIST, and was for about an hour on 2026-08-16.
    // Kept as a note because the idea is a good one and the reason it came out
    // is not "it does not work" - it is WHEN it can be applied.
    //
    // The mode collapses the tree's viewport to nothing, and a preset also
    // walks the tree to the folder it was saved in. That walk is asynchronous
    // (NavigateToPath retries across dispatcher turns and confirms a second
    // later), so applying the cover on the line after it dropped the viewport
    // to 0 while containers were still being regenerated - and WPF's own scroll
    // anchor was left pointing at one that had gone. ArgumentNullException in
    // VirtualizingStackPanel.FindScrollOffset, four in a burst, the recovery in
    // App.xaml.cs stopping by design on the fifth.
    //
    // Going back means hanging the cover off the END of that walk, which ends
    // three different ways (settled, dropped by user input, never quiet) and so
    // needs an answer for each plus a last-resort. Start there, not at the
    // apply order.

    // What the panel looks like once it is open, which costs nothing to change.
    internal static readonly string[] ViewerLookFields =
    {
        nameof(AppSettings.ViewerFilmstrip),
        nameof(AppSettings.ViewerFilmstripCellHeight),
        nameof(AppSettings.ViewerFilmstripGrid),
        nameof(AppSettings.ViewerFilmstripGridCellSize),
        nameof(AppSettings.ViewerFilmstripGridHeight),
        nameof(AppSettings.ViewerFilmstripNames),
        nameof(AppSettings.ViewerNavigator),
        nameof(AppSettings.ViewerSideSwapped),

        // 시계. Missed when it was built (2026-08-16) rather than left out: it
        // is a display switch on the panel exactly as the two above it are, it
        // costs nothing to apply, and a preset named for looking at pictures is
        // the obvious place to want it. The full cover is the one panel state
        // that is NOT here, and for a reason of its own - see the note under
        // SizeFields.
        nameof(AppSettings.ViewerClock),
        nameof(AppSettings.ViewerClockScale),

        // 그림이 도착할 때의 크기 (2026-08-17). 여기 오는 이유는 위의 셋과 같다 -
        // 패널의 표시 스위치이고 적용에 값이 안 들고, 사진을 보려고 만든 프리셋이
        // 자름맞춤으로 열리는 것은 그 프리셋이 하려던 말 그 자체다.
        nameof(AppSettings.ViewerRest),
    };

    internal static readonly string[] LookFields =
    {
        // 트리의 생김새.
        nameof(AppSettings.TreeFontSize),
        nameof(AppSettings.TreeFontWeight),
        nameof(AppSettings.TabSpacing),
        nameof(AppSettings.RowSpacing),
        nameof(AppSettings.ScrollBarThickness),
        nameof(AppSettings.ShowFolderIcons),
        nameof(AppSettings.ShowFileIcons),
        nameof(AppSettings.ShowDriveIcons),
        nameof(AppSettings.UseShellIcons),
        // ShowPathBar is deliberately absent since 2026-08-11: the strip is
        // always on, so a preset carrying it would be storing an answer to a
        // question nobody asks.
        nameof(AppSettings.HideTitleBarTitle),
        nameof(AppSettings.UseMyComputerHeaderIcon),
        nameof(AppSettings.TreeEdgeShades),

        // 옆 패널들.
        nameof(AppSettings.FavoritesAtBottom),
        // In, where ShowPathBar is out. That one is a whether and it is always
        // yes; this is a WHERE, and where things sit is what a preset is for.
        nameof(AppSettings.PathBarAtTop),
        nameof(AppSettings.FavoritesPanelHeight),
        nameof(AppSettings.SidePanelMode),
    };

    // 색. The theme flag and both themes' palettes together, so a preset named
    // 다크 and one named 알록달록 differ by everything they should. Applying
    // these recolours every row in the tree, which is why they are their own
    // group and are skipped whole when two presets share a palette.
    internal static readonly string[] ColorFields =
    {
        nameof(AppSettings.IsLightMode),
        nameof(AppSettings.BackgroundColorHex),
        nameof(AppSettings.FolderNameColorHex),
        nameof(AppSettings.FolderNameHighlightColorHex),
        nameof(AppSettings.FileNameColorHex),
        nameof(AppSettings.FileNameHighlightColorHex),
        nameof(AppSettings.SelectionColorHex),
        nameof(AppSettings.HistoryBackgroundColorHex),
        nameof(AppSettings.HoverBackgroundColorHex),
        nameof(AppSettings.FolderNameHoverColorHex),
        nameof(AppSettings.FileNameHoverColorHex),
        nameof(AppSettings.ShowMoreColorHex),
        // 즐겨찾기·북마크 패널의 이름 셋 (2026-08-17).
        nameof(AppSettings.PanelNameColorHex),
        nameof(AppSettings.PanelNameHighlightColorHex),
        nameof(AppSettings.PanelNameHoverColorHex),
        nameof(AppSettings.GuideLineColorHex),
        nameof(AppSettings.GuideLineActiveColorHex),
        nameof(AppSettings.ExpanderColorHex),
        nameof(AppSettings.FilterChipCheckedBackgroundColorHex),
        nameof(AppSettings.FilterChipCheckedForegroundColorHex),
        nameof(AppSettings.FilterChipExcludeColorHex),
        nameof(AppSettings.FilterChipExcludeCheckedBackgroundColorHex),
        nameof(AppSettings.PanelDividerColorHex),
        // In the COLOUR group, not the look one: it is applied by the same pass
        // that repaints the palette (LookFields' apply path never calls
        // ApplyColorSettings), so a preset that turned the lines off through any
        // other group would store the answer and never draw it.
        nameof(AppSettings.ShowPanelDividers),
        nameof(AppSettings.ViewerBackgroundColorHex),
        nameof(AppSettings.HeaderBackgroundColorHex),
        nameof(AppSettings.StoredAutoHideHandleColor),
        nameof(AppSettings.LightBackgroundColorHex),
        nameof(AppSettings.LightFolderNameColorHex),
        nameof(AppSettings.LightFolderNameHighlightColorHex),
        nameof(AppSettings.LightFileNameColorHex),
        nameof(AppSettings.LightFileNameHighlightColorHex),
        nameof(AppSettings.LightSelectionColorHex),
        nameof(AppSettings.LightHistoryBackgroundColorHex),
        nameof(AppSettings.LightHoverBackgroundColorHex),
        nameof(AppSettings.LightFolderNameHoverColorHex),
        nameof(AppSettings.LightFileNameHoverColorHex),
        nameof(AppSettings.LightShowMoreColorHex),
        nameof(AppSettings.LightPanelNameColorHex),
        nameof(AppSettings.LightPanelNameHighlightColorHex),
        nameof(AppSettings.LightPanelNameHoverColorHex),
        nameof(AppSettings.LightGuideLineColorHex),
        nameof(AppSettings.LightGuideLineActiveColorHex),
        nameof(AppSettings.LightExpanderColorHex),
        nameof(AppSettings.LightFilterChipCheckedBackgroundColorHex),
        nameof(AppSettings.LightFilterChipCheckedForegroundColorHex),
        nameof(AppSettings.LightFilterChipExcludeColorHex),
        nameof(AppSettings.LightFilterChipExcludeCheckedBackgroundColorHex),
        nameof(AppSettings.LightPanelDividerColorHex),
        nameof(AppSettings.LightViewerBackgroundColorHex),
        nameof(AppSettings.LightHeaderBackgroundColorHex),
        nameof(AppSettings.StoredLightAutoHideHandleColor),
    };

    // What the tree is SHOWING, as opposed to how it looks. The expensive group:
    // changing any of these sends every folder already on screen back to disk to
    // be read again, so a preset that shares them with the current state must
    // not pay for it.
    internal static readonly string[] ContentFields =
    {
        // 표시할 파일 종류 - which KINDS are shown, and whether the exclusion
        // list is in force. The two extension LISTS themselves are not here:
        // those are something the user wrote once, and a preset that rewrote
        // them would be editing content rather than changing shape.
        nameof(AppSettings.FileFilterCategories),
        nameof(AppSettings.FileFilterExcludeEnabled),

        // 정렬 기본값. The per-folder overrides are not here for the same
        // reason the extension lists are not.
        nameof(AppSettings.SortField),
        nameof(AppSettings.SortByDate),
        nameof(AppSettings.SortDescending),
    };

    // 작업중이던 자리. Added on request (2026-08-11) after being left out on the
    // first pass: a preset named for a job is expected to open where that job
    // is, and the shape alone does not do that.
    //
    // ONE PATH, not the whole expanded tree. Restoring an expansion SET would
    // only mean something if it also collapsed whatever is open now, which is
    // both destructive and a folder read per row; walking to one path expands
    // the folders on the way and leaves everything else where it was. It is
    // also the only field in a preset that costs disk to apply, which is why it
    // is asked about separately and goes last.
    internal static readonly string[] PlaceFields =
    {
        nameof(AppSettings.LastSelectedPath),
    };

    private static readonly string[] Fields = WindowFields
        .Concat(SizeFields)
        .Concat(PlaceFields)
        .Concat(ViewerLookFields)
        .Concat(LookFields)
        .Concat(ColorFields)
        .Concat(ContentFields)
        .ToArray();

    // NOT in a preset, and each for a reason worth stating once:
    //
    //   Favorites, BookmarkPaths, VideoMarks, HiddenFolderPaths,
    //   FolderSortOverrides, FileFilterCustomExtensions,
    //   FileFilterExcludeExtensions, SearchHistory, LastSearchFolder,
    //   NetworkLocations
    //     - DATA. The user made these, one at a time, and no change of shape
    //       should be able to take them away.
    //
    //   ExpandedFolderPaths, LastSelectedPath
    //     - where you ARE, not what the app looks like. A preset that moved the
    //       tree would also have to read every folder in it off disk.
    //
    //   StartWithWindows, AlwaysShowTrayIcon, Language
    //     - about the machine and the install, not about this window.
    //
    //   ViewerHdr*, ViewerSubtitle*, ViewerRepeat, ViewerPrecacheThumbnails,
    //   OpenMediaInViewer, ViewerFollowsSelection, HelpWindow*, MaxItemsPerFolder, AutoCollapseFolders,
    //   SearchSortMode, SearchSortField, SearchSortDescending,
    //   SearchGroupByFolder, DragMovesInsideTree, ExpandFolderOnSingleClick
    //     - preferences about how a job is done, which nobody expects to change
    //       when they move the window to the other edge.

    private static PropertyInfo[] SettingsProperties { get; } =
        typeof(AppSettings).GetProperties(BindingFlags.Public | BindingFlags.Instance);

    private static PropertyInfo? Find(string name)
        => SettingsProperties.FirstOrDefault(p => p.Name == name && p.CanRead && p.CanWrite);

    public static AppPreset Capture(AppSettings settings, string name)
    {
        var preset = new AppPreset { Name = name };
        preset.Overwrite(settings);
        return preset;
    }

    public void Overwrite(AppSettings settings)
    {
        Values.Clear();
        foreach (string field in Fields)
        {
            if (Find(field) is { } property)
            {
                Values[field] = JsonSerializer.SerializeToElement(
                    property.GetValue(settings), property.PropertyType);
            }
        }
    }

    // "Would applying this preset actually change any of these?" Asked BEFORE
    // ApplyTo, so the caller can skip the work each group costs.
    //
    // Compared as serialized text rather than by value, which is what makes one
    // line here cover a bool, a double and a List<string> alike. A field the
    // preset does not carry counts as unchanged - ApplyTo would leave it alone
    // too, so the two answers agree.
    public bool Differs(AppSettings settings, string[] fields)
    {
        foreach (string field in fields)
        {
            if (!Values.TryGetValue(field, out var stored) || Find(field) is not { } property)
            {
                continue;
            }

            string current = JsonSerializer.Serialize(
                property.GetValue(settings), property.PropertyType);
            if (current != stored.GetRawText())
            {
                return true;
            }
        }

        return false;
    }

    // One stored answer, read BEFORE the preset is applied. The apply path has
    // to know two of these up front - whether the window ends up hidden, and
    // whether the viewer ends up open - because the order it does things in
    // depends on both, and by the time the values are written it is too late to
    // ask. Falls back to what the app is doing now for a preset that predates
    // the field.
    public bool ValueOr(string field, bool fallback)
        => Values.TryGetValue(field, out var value)
            && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : fallback;

    // The same question for a number. Asked for the side panel's height, which
    // the apply path has to read back AFTER the look pass rather than before -
    // see ApplyPreset, where the font size overwrites it on the way through.
    public double ValueOr(string field, double fallback)
        => Values.TryGetValue(field, out var value)
            && value.ValueKind is JsonValueKind.Number
            && value.TryGetDouble(out double number)
            && double.IsFinite(number)
            ? number
            : fallback;

    // Copies this preset's values onto the live settings. Anything the preset
    // does not carry is LEFT ALONE rather than reset to a default: a preset
    // written by an older build is missing whatever was added since, and
    // "apply what I know" keeps it usable where "all or nothing" would make
    // every upgrade break every preset.
    public void ApplyTo(AppSettings settings)
    {
        foreach (var (field, value) in Values)
        {
            if (Find(field) is not { } property)
            {
                continue;
            }

            try
            {
                property.SetValue(settings, value.Deserialize(property.PropertyType));
            }
            catch (Exception ex) when (
                ex is JsonException or ArgumentException or NotSupportedException or InvalidCastException)
            {
                // A value whose type has changed since it was saved. One field
                // skipped is a preset that is slightly out of date; a throw
                // here would be a preset that cannot be applied at all.
                //
                // JsonException alone was too narrow: a stored null for a field
                // that is now a value type reaches SetValue as null and comes
                // back as ArgumentException, and a type Deserialize has no
                // converter for raises NotSupportedException. All three mean
                // the same thing here - this one field cannot be carried over -
                // and none of them is a reason to abandon the other forty.
            }
        }

        // A preset is a file like any other: written by a build that may not be
        // this one, and editable by anyone who finds it. The same pass the
        // settings file gets on the way in - see AppSettings.Normalize.
        settings.Normalize();
    }
}
