using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using SidebarExplorer.App.Models;
using SidebarExplorer.App.Services;
using SidebarExplorer.App.Behaviors;
using MouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using Keyboard = System.Windows.Input.Keyboard;
using TextBox = System.Windows.Controls.TextBox;
using Key = System.Windows.Input.Key;
using Color = System.Windows.Media.Color;
using Cursors = System.Windows.Input.Cursors;
using ColorConverter = System.Windows.Media.ColorConverter;
// WinForms is referenced for the tray icon and Recycle Bin, and brings its own
// Point/Brush - the picker below is the first code in this file to use either.
using Point = System.Windows.Point;
using Brush = System.Windows.Media.Brush;
using Button = System.Windows.Controls.Button;

namespace SidebarExplorer.App;

public partial class ColorSettingsWindow : Window
{
    private readonly AppSettings _settings;
    private readonly Action _onChanged;

    public ColorSettingsWindow(AppSettings settings, Action onChanged)
    {
        InitializeComponent();
        ModalTitleFlash.Attach(this, HeaderGrid);
        _settings = settings;
        _onChanged = onChanged;
        WireLabelHoverPreviews();
        RefreshSwatches();
        Deactivated += Window_Deactivated;
        PreviewKeyDown += ColorSettingsWindow_PreviewKeyDown;
        PreviewMouseDown += Window_PreviewMouseDown;
        Closing += (_, _) => ClosePicker(keep: true);
    }

    // Clicking anywhere that isn't the hex box being edited commits it -
    // Keyboard.ClearFocus fires the box's LostKeyboardFocus, whose handler
    // already applies the value. Without this, the dialog's labels and
    // panels take no focus, so a click "away" left the box focused and only
    // Enter or Tab got the user out (reported 2026-08-08).
    private void Window_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (Keyboard.FocusedElement is TextBox focused &&
            _hexBoxes.Contains(focused) &&
            e.OriginalSource is DependencyObject origin &&
            !IsDescendantOf(origin, focused))
        {
            Keyboard.ClearFocus();
        }
    }

    // Visual-tree walk (falling back to logical for non-visuals like Run):
    // the click's OriginalSource inside a TextBox is its inner TextBoxView,
    // never the TextBox itself.
    private static bool IsDescendantOf(DependencyObject element, DependencyObject ancestor)
    {
        for (DependencyObject? node = element; node is not null;
             node = node is System.Windows.Media.Visual
                 ? System.Windows.Media.VisualTreeHelper.GetParent(node)
                 : LogicalTreeHelper.GetParent(node))
        {
            if (ReferenceEquals(node, ancestor))
            {
                return true;
            }
        }

        return false;
    }

    // Nudges attention back to this dialog if the user clicks outside the
    // whole app while it's open (ShowDialog only blocks its owner, not other
    // applications, so that's still possible). The picker is a layer inside
    // this window and takes no activation; the export/import file dialogs do,
    // and flashing at those would be scolding the user for a button this
    // window itself put there.
    private void Window_Deactivated(object? sender, EventArgs e)
    {
        if (_isFileDialogOpen)
        {
            return;
        }

        // A brush of its own to animate, because the one the border carries is
        // a shared theme resource and frozen. The cost of that is the line
        // below: assigning it REPLACES the resource reference, so from here on
        // the border no longer follows the theme - and the flash has to put
        // that back when it is done.
        //
        // Without the restore, one flash in the light theme left a #D4D4D4
        // border standing in the dark one for the rest of the session (seen
        // 2026-08-11: "테두리가 이렇게 밝지 않았던 것 같은데"). Nothing said it
        // had happened, because the flash itself ends on the right colour - it
        // is only the NEXT theme change that the border no longer hears.
        var flashBrush = new SolidColorBrush(((SolidColorBrush)RootBorder.BorderBrush).Color);
        RootBorder.BorderBrush = flashBrush;

        var flash = new ColorAnimation
        {
            To = Color.FromRgb(0x4F, 0xA8, 0xFF),
            Duration = TimeSpan.FromMilliseconds(150),
            AutoReverse = true,
            RepeatBehavior = new RepeatBehavior(2)
        };
        flash.Completed += (_, _) =>
            RootBorder.SetResourceReference(Border.BorderBrushProperty, "PanelBorder");
        flashBrush.BeginAnimation(SolidColorBrush.ColorProperty, flash);
    }

    private void RefreshSwatches()
    {
        BackgroundSwatch.Background = ParseBrush(CurrentBackgroundColorHex);
        FolderNameFontSwatch.Background = ParseBrush(CurrentFolderNameColorHex);
        FolderNameHighlightFontSwatch.Background = ParseBrush(CurrentFolderNameHighlightColorHex);
        FileNameFontSwatch.Background = ParseBrush(CurrentFileNameColorHex);
        FileNameHighlightFontSwatch.Background = ParseBrush(CurrentFileNameHighlightColorHex);
        SelectionSwatch.Background = ParseBrush(CurrentSelectionColorHex);
        SelectionZoneSwatch.Background = ParseBrush(CurrentSelectionZoneColorHex);
        HistorySwatch.Background = ParseBrush(CurrentHistoryBackgroundColorHex);
        HoverBackgroundSwatch.Background = ParseBrush(CurrentHoverBackgroundColorHex);
        FolderNameHoverFontSwatch.Background = ParseBrush(CurrentFolderNameHoverColorHex);
        FileNameHoverFontSwatch.Background = ParseBrush(CurrentFileNameHoverColorHex);
        ShowMoreFontSwatch.Background = ParseBrush(CurrentShowMoreColorHex);
        PanelNameFontSwatch.Background = ParseBrush(CurrentPanelNameColorHex);
        PanelNameHighlightFontSwatch.Background = ParseBrush(CurrentPanelNameHighlightColorHex);
        PanelNameHoverFontSwatch.Background = ParseBrush(CurrentPanelNameHoverColorHex);
        GuideLineSwatch.Background = ParseBrush(CurrentGuideLineColorHex);
        ExpanderSwatch.Background = ParseBrush(CurrentExpanderColorHex);
        FilterChipCheckedSwatch.Background = ParseBrush(CurrentFilterChipCheckedColorHex);
        FilterChipCheckedFontSwatch.Background = ParseBrush(CurrentFilterChipCheckedFontColorHex);
        FilterChipExcludeSwatch.Background = ParseBrush(CurrentFilterChipExcludeColorHex);
        FilterChipExcludeCheckedSwatch.Background = ParseBrush(CurrentFilterChipExcludeCheckedColorHex);
        GuideLineActiveSwatch.Background = ParseBrush(CurrentGuideLineActiveColorHex);
        HeaderSwatch.Background = ParseBrush(CurrentHeaderBackgroundColorHex);
        PanelDividerSwatch.Background = ParseBrush(CurrentPanelDividerColorHex);
        ViewerBackgroundSwatch.Background = ParseBrush(CurrentViewerBackgroundColorHex);
        AutoHideHandleSwatch.Background = ParseBrush(CurrentAutoHideHandleColorHex);

        // The codes beside them follow the same way - a theme flip, a reset to
        // defaults or a pick from the colour dialog all land here, so no caller
        // has to remember the boxes exist.
        foreach (var box in _hexBoxes)
        {
            RefreshHexBox(box);
        }

        RefreshThemeToggles();

        // The dice are simply live. They used to come in pairs, one per theme
        // zone, and the inactive theme's pair was disabled - which meant two
        // buttons in this row could never be pressed. With one pair there is
        // nothing to gate: it rolls the theme being looked at, which is the
        // only theme there has ever been to roll. They go dead for a moment
        // after a roll, and that is LockRollButtons, not this.
        RandomButton.IsEnabled = true;
        DaringButton.IsEnabled = true;
        MonoButton.IsEnabled = true;
        RefreshChainToggles();
        RefreshLabelPreviews();
        UpdateResetButtonEnabled();
    }

    // ----- 라벨이 자기 색을 입는다 -------------------------------------------
    //
    // Several of these cannot be checked against the app at all: 더 보기 only
    // appears once a folder runs past its display cap, 강조 needs a selected
    // row, and a hover colour needs the pointer to be somewhere other than
    // here. Setting one of them showed nothing until the situation that
    // reveals it happened to come round - so the label is drawn in the colour
    // it names, being the one thing certain to be on screen while that colour
    // is being chosen.
    //
    // Text colours only. A background colour painted onto letters says nothing
    // true about the pairing it will really form, and a guide line is a line
    // rather than a word - both stay with the swatch, which is what a swatch
    // is for.
    private void RefreshLabelPreviews()
    {
        LabelFolderName.Foreground = ParseBrush(CurrentFolderNameColorHex);
        LabelFolderNameHighlight.Foreground = ParseBrush(CurrentFolderNameHighlightColorHex);
        LabelFileName.Foreground = ParseBrush(CurrentFileNameColorHex);
        LabelFileNameHighlight.Foreground = ParseBrush(CurrentFileNameHighlightColorHex);
        LabelShowMore.Foreground = ParseBrush(CurrentShowMoreColorHex);
        LabelPanelName.Foreground = ParseBrush(CurrentPanelNameColorHex);
        LabelPanelNameHighlight.Foreground = ParseBrush(CurrentPanelNameHighlightColorHex);

        // The hover rows demonstrate rather than state, and they do it
        // TOGETHER: in the tree, pointing at a row changes its background and
        // its name colour in the same instant, so a label showing one third of
        // that would be honest about the setting and misleading about the
        // result. At rest they look ordinary - pointing at one is the whole of
        // the difference, which is what a hover colour is.
        //
        // While any of them is being picked they all hold the hovered look,
        // because a colour being dragged has to be visible without also
        // keeping the pointer somewhere else. The panel's hover row joins them
        // even though its own background is the favourites panel's rather than
        // the tree's - the row that PAINTS that ground is 마우스 오버 배경, one
        // colour shared by both lists, so the demonstration is the same one.
        bool pickingHover =
            ReferenceEquals(_pickerSwatch, FolderNameHoverFontSwatch) ||
            ReferenceEquals(_pickerSwatch, FileNameHoverFontSwatch) ||
            ReferenceEquals(_pickerSwatch, PanelNameHoverFontSwatch) ||
            ReferenceEquals(_pickerSwatch, HoverBackgroundSwatch);

        if (pickingHover)
        {
            ShowHovered(LabelFolderNameHover, CurrentFolderNameHoverColorHex);
            ShowHovered(LabelFileNameHover, CurrentFileNameHoverColorHex);
            ShowHovered(LabelPanelNameHover, CurrentPanelNameHoverColorHex);
            ShowHovered(LabelHoverBackground, CurrentFolderNameHoverColorHex);
            return;
        }

        ShowAtRest(LabelFolderNameHover, CurrentFolderNameColorHex);
        ShowAtRest(LabelFileNameHover, CurrentFileNameColorHex);
        ShowAtRest(LabelPanelNameHover, CurrentPanelNameColorHex);
        LabelHoverBackground.Background = System.Windows.Media.Brushes.Transparent;
        LabelHoverBackground.Foreground = (Brush)FindResource("DialogForeground");
    }

    private void ShowHovered(TextBlock label, string fontHex)
    {
        label.Background = ParseBrush(CurrentHoverBackgroundColorHex);
        label.Foreground = ParseBrush(fontHex);
    }

    private void ShowAtRest(TextBlock label, string fontHex)
    {
        label.Background = System.Windows.Media.Brushes.Transparent;
        label.Foreground = ParseBrush(fontHex);
    }

    // Transparent rather than unset, so the whole label cell answers the
    // pointer instead of only the glyphs it happens to contain.
    private void WireLabelHoverPreviews()
    {
        WireHoverPreview(LabelFolderNameHover, () => CurrentFolderNameHoverColorHex);
        WireHoverPreview(LabelFileNameHover, () => CurrentFileNameHoverColorHex);
        WireHoverPreview(LabelPanelNameHover, () => CurrentPanelNameHoverColorHex);
        WireHoverPreview(LabelHoverBackground, () => CurrentFolderNameHoverColorHex);
    }

    private void WireHoverPreview(TextBlock label, Func<string> fontHex)
    {
        label.Background = System.Windows.Media.Brushes.Transparent;
        label.MouseEnter += (_, _) => ShowHovered(label, fontHex());
        label.MouseLeave += (_, _) => RefreshLabelPreviews();
    }

    private static SolidColorBrush ParseBrush(string hex)
        => new((Color)ColorConverter.ConvertFromString(hex));

    // One pair of get/set properties per color row, each reading/writing
    // whichever of the dark/light fields IsLightMode currently points at -
    // every other place in this file (RefreshSwatches, the 16 PickColor
    // wirings below, ResetDefaults_Click) goes through these instead of the
    // raw AppSettings fields directly, so there's exactly one place that
    // knows the dark/light field-name mapping for each row.
    private string CurrentBackgroundColorHex
    {
        get => _settings.IsLightMode ? _settings.LightBackgroundColorHex : _settings.BackgroundColorHex;
        set { if (_settings.IsLightMode) _settings.LightBackgroundColorHex = value; else _settings.BackgroundColorHex = value; }
    }
    private string CurrentFolderNameColorHex
    {
        get => _settings.IsLightMode ? _settings.LightFolderNameColorHex : _settings.FolderNameColorHex;
        set { if (_settings.IsLightMode) _settings.LightFolderNameColorHex = value; else _settings.FolderNameColorHex = value; }
    }
    private string CurrentFolderNameHighlightColorHex
    {
        get => _settings.IsLightMode ? _settings.LightFolderNameHighlightColorHex : _settings.FolderNameHighlightColorHex;
        set { if (_settings.IsLightMode) _settings.LightFolderNameHighlightColorHex = value; else _settings.FolderNameHighlightColorHex = value; }
    }
    private string CurrentFileNameColorHex
    {
        get => _settings.IsLightMode ? _settings.LightFileNameColorHex : _settings.FileNameColorHex;
        set { if (_settings.IsLightMode) _settings.LightFileNameColorHex = value; else _settings.FileNameColorHex = value; }
    }
    private string CurrentFileNameHighlightColorHex
    {
        get => _settings.IsLightMode ? _settings.LightFileNameHighlightColorHex : _settings.FileNameHighlightColorHex;
        set { if (_settings.IsLightMode) _settings.LightFileNameHighlightColorHex = value; else _settings.FileNameHighlightColorHex = value; }
    }
    private string CurrentSelectionColorHex
    {
        get => _settings.IsLightMode ? _settings.LightSelectionColorHex : _settings.SelectionColorHex;
        set { if (_settings.IsLightMode) _settings.LightSelectionColorHex = value; else _settings.SelectionColorHex = value; }
    }
    private string CurrentHistoryBackgroundColorHex
    {
        get => _settings.IsLightMode ? _settings.LightHistoryBackgroundColorHex : _settings.HistoryBackgroundColorHex;
        set { if (_settings.IsLightMode) _settings.LightHistoryBackgroundColorHex = value; else _settings.HistoryBackgroundColorHex = value; }
    }
    private string CurrentHoverBackgroundColorHex
    {
        get => _settings.IsLightMode ? _settings.LightHoverBackgroundColorHex : _settings.HoverBackgroundColorHex;
        set { if (_settings.IsLightMode) _settings.LightHoverBackgroundColorHex = value; else _settings.HoverBackgroundColorHex = value; }
    }
    private string CurrentFolderNameHoverColorHex
    {
        get => _settings.IsLightMode ? _settings.LightFolderNameHoverColorHex : _settings.FolderNameHoverColorHex;
        set { if (_settings.IsLightMode) _settings.LightFolderNameHoverColorHex = value; else _settings.FolderNameHoverColorHex = value; }
    }
    private string CurrentFileNameHoverColorHex
    {
        get => _settings.IsLightMode ? _settings.LightFileNameHoverColorHex : _settings.FileNameHoverColorHex;
        set { if (_settings.IsLightMode) _settings.LightFileNameHoverColorHex = value; else _settings.FileNameHoverColorHex = value; }
    }
    private string CurrentShowMoreColorHex
    {
        get => _settings.IsLightMode ? _settings.LightShowMoreColorHex : _settings.ShowMoreColorHex;
        set { if (_settings.IsLightMode) _settings.LightShowMoreColorHex = value; else _settings.ShowMoreColorHex = value; }
    }
    private string CurrentPanelNameColorHex
    {
        get => _settings.IsLightMode ? _settings.LightPanelNameColorHex : _settings.PanelNameColorHex;
        set { if (_settings.IsLightMode) _settings.LightPanelNameColorHex = value; else _settings.PanelNameColorHex = value; }
    }
    private string CurrentPanelNameHighlightColorHex
    {
        get => _settings.IsLightMode ? _settings.LightPanelNameHighlightColorHex : _settings.PanelNameHighlightColorHex;
        set { if (_settings.IsLightMode) _settings.LightPanelNameHighlightColorHex = value; else _settings.PanelNameHighlightColorHex = value; }
    }
    private string CurrentPanelNameHoverColorHex
    {
        get => _settings.IsLightMode ? _settings.LightPanelNameHoverColorHex : _settings.PanelNameHoverColorHex;
        set { if (_settings.IsLightMode) _settings.LightPanelNameHoverColorHex = value; else _settings.PanelNameHoverColorHex = value; }
    }
    private string CurrentGuideLineColorHex
    {
        get => _settings.IsLightMode ? _settings.LightGuideLineColorHex : _settings.GuideLineColorHex;
        set { if (_settings.IsLightMode) _settings.LightGuideLineColorHex = value; else _settings.GuideLineColorHex = value; }
    }
    private string CurrentExpanderColorHex
    {
        get => _settings.IsLightMode ? _settings.LightExpanderColorHex : _settings.ExpanderColorHex;
        set { if (_settings.IsLightMode) _settings.LightExpanderColorHex = value; else _settings.ExpanderColorHex = value; }
    }
    private string CurrentFilterChipCheckedColorHex
    {
        get => _settings.IsLightMode ? _settings.LightFilterChipCheckedBackgroundColorHex : _settings.FilterChipCheckedBackgroundColorHex;
        set { if (_settings.IsLightMode) _settings.LightFilterChipCheckedBackgroundColorHex = value; else _settings.FilterChipCheckedBackgroundColorHex = value; }
    }
    private string CurrentFilterChipCheckedFontColorHex
    {
        get => _settings.IsLightMode ? _settings.LightFilterChipCheckedForegroundColorHex : _settings.FilterChipCheckedForegroundColorHex;
        set { if (_settings.IsLightMode) _settings.LightFilterChipCheckedForegroundColorHex = value; else _settings.FilterChipCheckedForegroundColorHex = value; }
    }
    private string CurrentFilterChipExcludeColorHex
    {
        get => _settings.IsLightMode ? _settings.LightFilterChipExcludeColorHex : _settings.FilterChipExcludeColorHex;
        set { if (_settings.IsLightMode) _settings.LightFilterChipExcludeColorHex = value; else _settings.FilterChipExcludeColorHex = value; }
    }
    private string CurrentFilterChipExcludeCheckedColorHex
    {
        get => _settings.IsLightMode ? _settings.LightFilterChipExcludeCheckedBackgroundColorHex : _settings.FilterChipExcludeCheckedBackgroundColorHex;
        set { if (_settings.IsLightMode) _settings.LightFilterChipExcludeCheckedBackgroundColorHex = value; else _settings.FilterChipExcludeCheckedBackgroundColorHex = value; }
    }
    private string CurrentGuideLineActiveColorHex
    {
        get => _settings.IsLightMode ? _settings.LightGuideLineActiveColorHex : _settings.GuideLineActiveColorHex;
        set { if (_settings.IsLightMode) _settings.LightGuideLineActiveColorHex = value; else _settings.GuideLineActiveColorHex = value; }
    }
    private string CurrentPanelDividerColorHex
    {
        get => _settings.IsLightMode ? _settings.LightPanelDividerColorHex : _settings.PanelDividerColorHex;
        set { if (_settings.IsLightMode) _settings.LightPanelDividerColorHex = value; else _settings.PanelDividerColorHex = value; }
    }

    private string CurrentViewerBackgroundColorHex
    {
        get => _settings.IsLightMode ? _settings.LightViewerBackgroundColorHex : _settings.ViewerBackgroundColorHex;
        set { if (_settings.IsLightMode) _settings.LightViewerBackgroundColorHex = value; else _settings.ViewerBackgroundColorHex = value; }
    }
    private string CurrentHeaderBackgroundColorHex
    {
        get => _settings.IsLightMode ? _settings.LightHeaderBackgroundColorHex : _settings.HeaderBackgroundColorHex;
        set { if (_settings.IsLightMode) _settings.LightHeaderBackgroundColorHex = value; else _settings.HeaderBackgroundColorHex = value; }
    }

    // Reading either of these resolves "unset" to the sidebar background of
    // the same theme (see AppSettings), so this row behaves like any other -
    // it always has a real colour to show, and writing one is what makes it
    // stop following.
    private string CurrentAutoHideHandleColorHex
    {
        get => _settings.IsLightMode ? _settings.LightAutoHideHandleColorHex : _settings.AutoHideHandleColorHex;
        set { if (_settings.IsLightMode) _settings.LightAutoHideHandleColorHex = value; else _settings.AutoHideHandleColorHex = value; }
    }

    // Reads the same as the rest even though nothing may be stored: the
    // property behind it answers with the value derived from the background
    // until someone picks one, so the swatch always shows the colour actually
    // on screen rather than an empty row.
    private string CurrentSelectionZoneColorHex
    {
        get => _settings.IsLightMode ? _settings.LightSelectionZoneColorHex : _settings.SelectionZoneColorHex;
        set { if (_settings.IsLightMode) _settings.LightSelectionZoneColorHex = value; else _settings.SelectionZoneColorHex = value; }
    }

    private void DarkMode_Click(object sender, RoutedEventArgs e) => SetThemeMode(light: false);

    private void LightMode_Click(object sender, RoutedEventArgs e) => SetThemeMode(light: true);

    private void SetThemeMode(bool light)
    {
        if (_settings.IsLightMode == light)
        {
            return;
        }

        _settings.IsLightMode = light;
        RefreshSwatches();
        _onChanged();
        // Again, and deliberately - the stamp is painted from AccentForeground,
        // which is a PER-THEME chrome brush that _onChanged is what swaps. The
        // call inside RefreshSwatches above still holds the outgoing theme's
        // blue, so without this the button that just lit up would wear the
        // colour of the theme it came from.
        RefreshThemeToggles();
    }

    // ----- 어느 쪽이 켜져 있는가 ---------------------------------------------
    //
    // 다크 모드 / 라이트 모드 are two ordinary buttons, and the one that is ON
    // is stamped with the app's own blue - AccentForeground, the same brush the
    // 앱 정보 and 도움말 windows use for their links. Reusing it means the app
    // has one accent rather than one more invented here.
    //
    // Two earlier answers were tried and dropped the day this was built
    // (2026-08-12): DISABLING the active button, where "you are here" was said
    // by the 0.4 opacity and read as "unavailable" instead; and INVERTING its
    // colours, which read as neither one thing nor the other.
    //
    // 선택 색 was the other candidate and is not used. It carries the right
    // meaning - it is literally the colour this app marks a chosen thing with -
    // but the user owns it, and its dark default #FF323438 is a near-black that
    // would vanish into the button it is meant to fill. A roll can take it
    // anywhere. The stamp has to be legible whatever the palette is doing.
    private void RefreshThemeToggles()
    {
        PaintThemeToggle(DarkModeButton, on: !_settings.IsLightMode);
        PaintThemeToggle(LightModeButton, on: _settings.IsLightMode);
        // Repainted with them because the stamp comes from AccentForeground,
        // which is a per-theme brush - the switch would otherwise keep the
        // outgoing theme's blue after a mode change.
        PaintToggleFill(EdgeShadesButton, on: _settings.TreeEdgeShades);
    }

    private void EdgeShades_Click(object sender, RoutedEventArgs e)
    {
        _settings.TreeEdgeShades = !_settings.TreeEdgeShades;
        PaintToggleFill(EdgeShadesButton, on: _settings.TreeEdgeShades);
        // The same callback every colour edit uses - it ends at the app's own
        // apply pass, which is where the shades are put up or taken down.
        _onChanged();
    }

    // The ink over the blue is CHOSEN rather than fixed, because the accent is
    // not the same blue in both themes: dark's #FF4FA8FF is light enough to
    // want dark letters and light's #FF0969DA deep enough to want white ones.
    // ContrastRatio already lives in this file for the random palettes, so the
    // pick costs one line and follows the accent if it is ever retuned.
    private static readonly Color ThemeToggleDarkInk = Color.FromRgb(0x1E, 0x1E, 0x1E);

    private void PaintThemeToggle(Button button, bool on)
    {
        // The ON button takes no mouse. It is already that theme so there is
        // nothing to press, and hover would paint its own fill over the fill
        // that is doing the talking. IsHitTestVisible does both without the
        // disabled look, which is the thing being got rid of here.
        button.IsHitTestVisible = !on;
        button.Cursor = on ? Cursors.Arrow : Cursors.Hand;
        PaintToggleFill(button, on);
    }

    // The stamp alone, for a button that is a SWITCH rather than one of a
    // pair. The theme buttons can afford to stop taking the mouse when lit
    // because pressing the lit one would do nothing anyway; a switch has to
    // stay pressable, since the press that turns it back off lands on the lit
    // state. The hover fill painting over the stamp is the price, and it is
    // the right way round here - it says the thing can still be pressed.
    private void PaintToggleFill(Button button, bool on)
    {
        // Read by the template's hover MultiTrigger - see DialogButtonStyle.
        button.Tag = on ? "lit" : null;

        if (!on)
        {
            button.ClearValue(BackgroundProperty);
            button.ClearValue(BorderBrushProperty);
            button.ClearValue(ForegroundProperty);
            return;
        }

        var accent = ((SolidColorBrush)FindResource("AccentForeground")).Color;
        var fill = new SolidColorBrush(accent);
        button.Background = fill;
        button.BorderBrush = fill;
        button.Foreground = new SolidColorBrush(
            ContrastRatio(Colors.White, accent) >= ContrastRatio(ThemeToggleDarkInk, accent)
                ? Colors.White
                : ThemeToggleDarkInk);
    }

    // True only if every one of the current theme's 16 colors already
    // matches that theme's own default (a fresh AppSettings() instance) -
    // ResetDefaultsButton is disabled in that case, since there'd be nothing
    // to actually reset.
    private bool IsCurrentThemeAtDefaults()
    {
        var defaults = new AppSettings { IsLightMode = _settings.IsLightMode };
        return CurrentBackgroundColorHex == GetDefault(defaults, s => s.BackgroundColorHex, s => s.LightBackgroundColorHex)
            && CurrentFolderNameColorHex == GetDefault(defaults, s => s.FolderNameColorHex, s => s.LightFolderNameColorHex)
            && CurrentFolderNameHighlightColorHex == GetDefault(defaults, s => s.FolderNameHighlightColorHex, s => s.LightFolderNameHighlightColorHex)
            && CurrentFileNameColorHex == GetDefault(defaults, s => s.FileNameColorHex, s => s.LightFileNameColorHex)
            && CurrentFileNameHighlightColorHex == GetDefault(defaults, s => s.FileNameHighlightColorHex, s => s.LightFileNameHighlightColorHex)
            && CurrentSelectionColorHex == GetDefault(defaults, s => s.SelectionColorHex, s => s.LightSelectionColorHex)
            && CurrentSelectionZoneColorHex == GetDefault(defaults, s => s.SelectionZoneColorHex, s => s.LightSelectionZoneColorHex)
            && CurrentHistoryBackgroundColorHex == GetDefault(defaults, s => s.HistoryBackgroundColorHex, s => s.LightHistoryBackgroundColorHex)
            && CurrentHoverBackgroundColorHex == GetDefault(defaults, s => s.HoverBackgroundColorHex, s => s.LightHoverBackgroundColorHex)
            && CurrentFolderNameHoverColorHex == GetDefault(defaults, s => s.FolderNameHoverColorHex, s => s.LightFolderNameHoverColorHex)
            && CurrentFileNameHoverColorHex == GetDefault(defaults, s => s.FileNameHoverColorHex, s => s.LightFileNameHoverColorHex)
            && CurrentShowMoreColorHex == GetDefault(defaults, s => s.ShowMoreColorHex, s => s.LightShowMoreColorHex)
            && CurrentPanelNameColorHex == GetDefault(defaults, s => s.PanelNameColorHex, s => s.LightPanelNameColorHex)
            && CurrentPanelNameHighlightColorHex == GetDefault(defaults, s => s.PanelNameHighlightColorHex, s => s.LightPanelNameHighlightColorHex)
            && CurrentPanelNameHoverColorHex == GetDefault(defaults, s => s.PanelNameHoverColorHex, s => s.LightPanelNameHoverColorHex)
            && CurrentGuideLineColorHex == GetDefault(defaults, s => s.GuideLineColorHex, s => s.LightGuideLineColorHex)
            && CurrentGuideLineActiveColorHex == GetDefault(defaults, s => s.GuideLineActiveColorHex, s => s.LightGuideLineActiveColorHex)
            && CurrentExpanderColorHex == GetDefault(defaults, s => s.ExpanderColorHex, s => s.LightExpanderColorHex)
            && CurrentFilterChipCheckedColorHex == GetDefault(defaults, s => s.FilterChipCheckedBackgroundColorHex, s => s.LightFilterChipCheckedBackgroundColorHex)
            && CurrentFilterChipCheckedFontColorHex == GetDefault(defaults, s => s.FilterChipCheckedForegroundColorHex, s => s.LightFilterChipCheckedForegroundColorHex)
            && CurrentFilterChipExcludeColorHex == GetDefault(defaults, s => s.FilterChipExcludeColorHex, s => s.LightFilterChipExcludeColorHex)
            && CurrentFilterChipExcludeCheckedColorHex == GetDefault(defaults, s => s.FilterChipExcludeCheckedBackgroundColorHex, s => s.LightFilterChipExcludeCheckedBackgroundColorHex)
            && CurrentPanelDividerColorHex == GetDefault(defaults, s => s.PanelDividerColorHex, s => s.LightPanelDividerColorHex)
            && CurrentViewerBackgroundColorHex == GetDefault(defaults, s => s.ViewerBackgroundColorHex, s => s.LightViewerBackgroundColorHex)
            && CurrentHeaderBackgroundColorHex == GetDefault(defaults, s => s.HeaderBackgroundColorHex, s => s.LightHeaderBackgroundColorHex)
            && CurrentAutoHideHandleColorHex == GetDefault(defaults, s => s.AutoHideHandleColorHex, s => s.LightAutoHideHandleColorHex);

        string GetDefault(AppSettings d, Func<AppSettings, string> dark, Func<AppSettings, string> light)
            => _settings.IsLightMode ? light(d) : dark(d);
    }

    private void UpdateResetButtonEnabled()
    {
        ResetDefaultsButton.IsEnabled = !IsCurrentThemeAtDefaults();
    }

    private void HeaderGrid_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        DragMove();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    // Only the currently active theme's 16 colors - the other theme's
    // customizations are left completely untouched (per explicit request).
    // ResetDefaultsButton is disabled whenever there'd be nothing to reset
    // (see UpdateResetButtonEnabled), so reaching this handler at all means a
    // real change is about to happen - hence the confirmation.
    // ----- 색상만 내보내기/불러오기 -------------------------------------------
    //
    // settings.json carries far more than colours - hidden folders, bookmarks,
    // favorites, the last selected path - and all of those name folders that
    // exist on one machine and not the next. Copying the file across is how
    // someone finds that out (2026-08-04). So the palette travels on its
    // own.
    //
    // Every colour, both themes, in one file: a theme is the pair, and
    // exporting only the half currently showing would leave the other half of
    // the destination untouched and mismatched. The properties are found by
    // name rather than listed, so a colour added later travels without anyone
    // remembering this code exists.
    private static IEnumerable<System.Reflection.PropertyInfo> ColorProperties()
        => typeof(AppSettings).GetProperties()
            .Where(p => p.PropertyType == typeof(string)
                && p.CanRead && p.CanWrite
                && p.Name.EndsWith("ColorHex", StringComparison.Ordinal));

    // The native file dialogs take activation the way the old colour dialog
    // did, and this window reads that as "the user left the app" (see
    // Window_Deactivated). True for exactly as long as one is up.
    private bool _isFileDialogOpen;

    private void ExportColors_Click(object sender, RoutedEventArgs e)
    {
        ClosePicker(keep: true);

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            FileName = Strings.ColorFileDefaultName,
            Filter = Strings.ColorFileFilter,
            AddExtension = true,
            DefaultExt = ".json"
        };

        if (!ShowFileDialog(dialog))
        {
            return;
        }

        var colors = ColorProperties().ToDictionary(
            p => p.Name,
            p => (string?)p.GetValue(_settings) ?? string.Empty);

        try
        {
            System.IO.File.WriteAllText(
                dialog.FileName,
                System.Text.Json.JsonSerializer.Serialize(
                    colors, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            System.Windows.MessageBox.Show(this, ex.Message, Strings.ColorImportFailedTitle,
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ImportColors_Click(object sender, RoutedEventArgs e)
    {
        ClosePicker(keep: true);

        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            FileName = Strings.ColorFileDefaultName,
            Filter = Strings.ColorFileFilter,
            CheckFileExists = true
        };

        if (!ShowFileDialog(dialog))
        {
            return;
        }

        Dictionary<string, string>? colors = null;
        try
        {
            colors = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(
                System.IO.File.ReadAllText(dialog.FileName));
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException
            or System.IO.IOException
            or UnauthorizedAccessException
            or NotSupportedException)
        {
            // Nothing usable in it, which the count below reports the same way
            // a valid-but-empty file would - the user's question is "did my
            // colours arrive", not which kind of unreadable this was.
        }

        int applied = 0;
        if (colors is not null)
        {
            foreach (var property in ColorProperties())
            {
                // Each value is parsed before it is stored. A file that names a
                // real colour badly must not be able to put a string into
                // settings that every ColorConverter call afterwards throws on.
                if (colors.TryGetValue(property.Name, out string? hex) &&
                    ParseHex(hex, Colors.Black) is { } color)
                {
                    property.SetValue(_settings, $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}");
                    applied++;
                }
            }
        }

        if (applied == 0)
        {
            System.Windows.MessageBox.Show(this, Strings.ColorImportFailedBody,
                Strings.ColorImportFailedTitle, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        RefreshSwatches();
        _onChanged();
    }

    private bool ShowFileDialog(Microsoft.Win32.CommonDialog dialog)
    {
        _isFileDialogOpen = true;
        try
        {
            return dialog.ShowDialog(this) == true;
        }
        finally
        {
            _isFileDialogOpen = false;
        }
    }

    // ----- 랜덤 배색 ---------------------------------------------------------
    //
    // One die per theme (2026-08-09). A roll doesn't pick 16
    // random colors - it picks 1~3 HUES and derives every slot from role
    // rules, which is what keeps a roll wearable: surfaces are a near-neutral
    // tint of the base hue on a brightness ladder, text is forced through a
    // readability floor against the surface it actually sits on (4.5:1
    // WCAG-style for names, 3:1 for the secondary "더 보기" row), and only
    // the accent hue - selection, active guide line, highlight names - gets
    // real saturation. Rolling again is the intended gesture; the rules only
    // guarantee "wearable", the user's eye does the rest.

    private static readonly Random PaletteRandom = new();

    // Both themes' colors as they stood before the FIRST roll of this window
    // session - 랜덤 전으로 returns to exactly that state however many rolls
    // happened in between, and closing the window is what commits. Captured
    // via the same reflection set export/import use, so a color added later
    // is covered without anyone remembering this exists.
    private Dictionary<string, string>? _preRandomSnapshot;

    // Taken beside the snapshot above, and needed because that one goes
    // through ColorProperties() - which reads 선택된 폴더 영역 as whatever it
    // currently RESOLVES to, never as "nothing chosen". Restoring from it
    // alone would pin the row to a colour on the way back, and the row would
    // quietly stop following the background from then on.
    private (string? Dark, string? Light)? _preRandomZone;

    // Both dice roll the theme being LOOKED AT. That was already the rule when
    // there was a pair per theme - the inactive theme's pair was disabled - so
    // collapsing them to one pair changed which buttons exist, not what a roll
    // does.
    private void Random_Click(object sender, RoutedEventArgs e)
        => ApplyRandomPalette(light: _settings.IsLightMode);

    private void Daring_Click(object sender, RoutedEventArgs e)
        => ApplyRandomPalette(light: _settings.IsLightMode, daring: true);

    private void Mono_Click(object sender, RoutedEventArgs e)
        => ApplyRandomPalette(light: _settings.IsLightMode, mono: true);

    // Grey at the SAME LUMINANCE the colour had. That is the whole reason the
    // mono button rolls first and drains the colour after, instead of drawing
    // greys of its own: every readability floor in GenerateRandomPalette is a
    // contrast ratio, contrast is computed from luminance alone, and a swap
    // that holds luminance still holds every one of those floors with it. A
    // hand-drawn grey set would have to restate them and could drift from
    // them later.
    //
    // The inverse of the sRGB transfer curve, because averaging the bytes
    // instead ("(R+G+B)/3") is a different number - it would move the contrast
    // it is supposed to preserve, most visibly on saturated blues.
    // A stored hex, drained. Guarded because these strings can have been hand
    // edited - AppSettings.Normalize repairs what it can on the way in, but a
    // colour that arrived after that is not its business, and a mono press is
    // the wrong place to learn about it.
    private static string GreyOf(string hex)
    {
        try
        {
            return Hex(ToGrey((Color)ColorConverter.ConvertFromString(hex)));
        }
        catch (FormatException)
        {
            return hex;
        }
        catch (InvalidOperationException)
        {
            return hex;
        }
    }

    private static Color ToGrey(Color c)
    {
        double linear = Luminance(c);
        double srgb = linear <= 0.0031308
            ? linear * 12.92
            : 1.055 * Math.Pow(linear, 1 / 2.4) - 0.055;
        byte level = (byte)Math.Clamp(Math.Round(srgb * 255), 0, 255);
        return Color.FromArgb(c.A, level, level, level);
    }

    // ----- 연타를 막는 짧은 잠금 ---------------------------------------------
    //
    // A roll rewrites every colour in the app and the whole tree is repainted
    // for it, so a held or hammered button queues that work faster than it can
    // be done - and the palettes in between are never seen anyway.
    //
    // The buttons go DEAD for a moment rather than the presses being dropped
    // quietly: a button that ignores you looks broken, one that greys out has
    // told you why. Re-enabled through RefreshSwatches, so which of them comes
    // back is decided by the theme rule in one place rather than here.
    private const int RollLockMs = 450;
    private System.Windows.Threading.DispatcherTimer? _rollLockTimer;

    private void LockRollButtons()
    {
        RandomButton.IsEnabled = false;
        DaringButton.IsEnabled = false;
        MonoButton.IsEnabled = false;

        _rollLockTimer ??= CreateRollLockTimer();
        _rollLockTimer.Stop();
        _rollLockTimer.Start();
    }

    private System.Windows.Threading.DispatcherTimer CreateRollLockTimer()
    {
        var timer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(RollLockMs),
        };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            RefreshSwatches();
        };
        return timer;
    }

    private void UndoRandom_Click(object sender, RoutedEventArgs e)
    {
        if (_preRandomSnapshot is null)
        {
            return;
        }

        ClosePicker(keep: true);
        foreach (var property in ColorProperties())
        {
            if (_preRandomSnapshot.TryGetValue(property.Name, out string? hex))
            {
                property.SetValue(_settings, hex);
            }
        }

        if (_preRandomZone is { } zone)
        {
            _settings.StoredSelectionZoneColor = zone.Dark;
            _settings.StoredLightSelectionZoneColor = zone.Light;
        }

        // The snapshot survives the undo: roll → undo → roll again → undo
        // still lands on the pre-first-roll palette.
        RefreshSwatches();
        _onChanged();
    }

    private void ApplyRandomPalette(bool light, bool daring = false, bool mono = false)
    {
        // The inactive theme's die is disabled (see RefreshSwatches), so this
        // guard is belt-and-braces: rolling a theme that isn't showing would
        // write the Current* slots of the WRONG theme.
        if (_settings.IsLightMode != light)
        {
            return;
        }

        ClosePicker(keep: true);
        _preRandomSnapshot ??= ColorProperties().ToDictionary(
            p => p.Name, p => (string?)p.GetValue(_settings) ?? string.Empty);
        _preRandomZone ??= (_settings.StoredSelectionZoneColor, _settings.StoredLightSelectionZoneColor);
        UndoRandomButton.IsEnabled = true;

        LockRollButtons();

        var palette = GenerateRandomPalette(light, PaletteRandom, daring);
        // Every colour drained at once, through the one call below - so a
        // colour added to the palette later cannot be the one that stays
        // coloured on a mono press.
        Func<Color, string> Write = mono ? c => Hex(ToGrey(c)) : Hex;
        CurrentBackgroundColorHex = Write(palette.Background);
        CurrentHeaderBackgroundColorHex = Write(palette.Header);
        CurrentHistoryBackgroundColorHex = Write(palette.History);
        CurrentViewerBackgroundColorHex = Write(palette.Viewer);
        CurrentHoverBackgroundColorHex = Write(palette.Hover);
        CurrentSelectionColorHex = Write(palette.Selection);
        CurrentGuideLineColorHex = Write(palette.Guide);
        CurrentGuideLineActiveColorHex = Write(palette.GuideActive);
        CurrentPanelDividerColorHex = Write(palette.Guide);
        CurrentFolderNameColorHex = Write(palette.Text);
        CurrentFileNameColorHex = Write(palette.Text);
        CurrentFolderNameHoverColorHex = Write(palette.TextHover);
        CurrentFileNameHoverColorHex = Write(palette.TextHover);
        CurrentFolderNameHighlightColorHex = Write(palette.Highlight);
        CurrentFileNameHighlightColorHex = Write(palette.Highlight);
        CurrentShowMoreColorHex = Write(palette.ShowMore);
        // The panel's three take the SAME three inks the names below them do,
        // rather than a roll of their own. A roll answers "show me a palette",
        // and inventing a second text colour for the panel would be the roll
        // making a design decision the user makes with these rows - the three
        // exist so the two lists CAN differ, not so they always do.
        CurrentPanelNameColorHex = Write(palette.Text);
        CurrentPanelNameHoverColorHex = Write(palette.TextHover);
        CurrentPanelNameHighlightColorHex = Write(palette.Highlight);
        // 펼침기호 takes the ACTIVE guide line, not the resting one: both are
        // marks the tree draws for structure rather than content, and the arrow
        // is the one of the pair the eye is meant to find - the same step up the
        // active guide already is.
        CurrentExpanderColorHex = Write(palette.GuideActive);
        // The lit chips ride the roll's SELECTION colour, which is what this
        // palette already means by "the lit one". Their text takes the same
        // highlight the tree's own highlighted names do, so a roll that lands on
        // a dark selection gets light text and the other way round - the roll
        // already worked that pair out and the strip should not disagree with
        // the tree three inches above it.
        CurrentFilterChipCheckedColorHex = Write(palette.Selection);
        // CHOSEN AGAINST THE CHIP, not inherited from the tree (2026-08-16,
        // reported on a mono roll in the light theme: a lit chip came out dark
        // grey with dark letters on it).
        //
        // It used to take palette.Highlight on the reasoning that the strip
        // should not disagree with the tree three inches above it. The flaw is
        // in what each of those inks was worked out against: the tree's
        // highlight is readable over the tree's BACKGROUND, while this one has
        // to be readable over the SELECTION colour, and a roll is free to land
        // those two far apart. In a light theme especially - the background is
        // light, so the highlight is dark, and a selection that rolls dark puts
        // dark on dark.
        //
        // Picking by contrast keeps the original intent where it holds: when
        // the highlight does read over the chip, it wins and the strip matches
        // the tree exactly as before. It only gives way when it cannot be read,
        // which is the case that was reported.
        CurrentFilterChipCheckedFontColorHex = Write(
            InkOver(palette.Selection, palette.Highlight, palette.Text, palette.Background));
        // The exclude chip is NOT rolled. It is the one control in the strip
        // that removes, and it says so with a warm hue - a roll landing on a
        // green or a blue would take the only thing that distinguishes it and
        // leave two chips that look alike and do opposite things. It keeps
        // whatever the user (or the theme's default) has through every roll.
        //
        // MONO IS THE EXCEPTION, and adding it (2026-08-16, reported) is what
        // showed the reason above is about a HUE rather than about the chip.
        // Nothing is being taken from it here: with the whole strip grey there
        // is no hue left for a warm one to stand out from, and the two rows
        // that ignored the button were simply the only colour left on a
        // palette the user had asked to be greyscale.
        //
        // Drained from what they ARE rather than from a roll, since these two
        // are the ones the roll never produced. And it costs no contrast: the
        // grey holds the luminance the colour had (see ToGrey), so every pair
        // these take part in reads exactly as well as it did in red.
        if (mono)
        {
            CurrentFilterChipExcludeColorHex = GreyOf(CurrentFilterChipExcludeColorHex);
            CurrentFilterChipExcludeCheckedColorHex = GreyOf(CurrentFilterChipExcludeCheckedColorHex);
        }
        // The handle is the roll's one loud voice - see RollHandle for why it
        // is no longer just the rolled background.
        CurrentAutoHideHandleColorHex = Write(palette.Handle);

        // 선택된 폴더 영역 is NOT rolled - it is handed back to the background
        // that was just rolled, which is the level it sat at before this row
        // existed (the author's call, 2026-08-26). A roll that gave it a
        // colour of its own would be picking the one value on this page whose
        // job is to stay a step behind whatever the ground turned out to be,
        // and mono would have to drain it too.
        if (_settings.IsLightMode)
        {
            _settings.StoredLightSelectionZoneColor = null;
        }
        else
        {
            _settings.StoredSelectionZoneColor = null;
        }

        RefreshSwatches();
        _onChanged();
    }

    private readonly record struct RandomPalette(
        Color Background, Color Header, Color History, Color Viewer,
        Color Hover, Color Selection, Color Guide, Color GuideActive,
        Color Text, Color TextHover, Color Highlight, Color ShowMore,
        Color Handle);

    // The hues a "primary" reads as - red, yellow, green, cyan, blue, magenta.
    // The bolder roll starts from one of these with a little play either side,
    // where the ordinary roll starts anywhere on the wheel. That is most of
    // what makes it look deliberate rather than merely saturated: a colour ten
    // degrees off pure blue still reads as BLUE, where one at 205° reads as
    // "some sort of teal".
    private static readonly double[] PrimaryHues = { 0, 60, 120, 180, 240, 300 };

    // `daring` is the second button (see ApplyRandomPalette). SAME RULES, and
    // that is the point: every readability floor below is still walked, so the
    // bolder roll cannot produce a palette the calm one would have refused. It
    // moves three things - where the hue starts, how many hues there are, and
    // how far the saturation ceilings go.
    //
    // A separate button rather than a change to the die that exists: the
    // ordinary roll is worth keeping as it is, because the quiet differences it
    // makes are exactly what shows up differently on someone else's monitor.
    private static RandomPalette GenerateRandomPalette(bool light, Random rng, bool daring = false)
    {
        // 1~3 hues: base always; a rough complement (150~210° away) when two;
        // an analogous neighbour (25~45°) as the third. Everything else in a
        // roll is derived, never independently random.
        //
        // The bolder roll always takes at least TWO, so there is a real second
        // colour in the palette rather than one hue at several brightnesses.
        double baseHue = rng.NextDouble() * 360;
        int hueCount = 1 + rng.Next(3);
        double accentHue = hueCount >= 2
            ? Wrap(baseHue + (rng.Next(2) == 0 ? -1 : 1) * (150 + rng.NextDouble() * 60))
            : baseHue;
        double neighborHue = hueCount == 3
            ? Wrap(baseHue + (rng.Next(2) == 0 ? -1 : 1) * (25 + rng.NextDouble() * 20))
            : baseHue;

        // THE BOLDER ROLL DRAWS FOUR SEPARATE PRIMARIES instead (2026-08-11).
        // Deriving them by angle was the reason it still came out looking like
        // one colour: a complement and an analogous neighbour of the same base
        // are a HARMONY, which is the opposite of what this button is for. Four
        // draws from red/yellow/green/cyan/blue/magenta with a little play
        // either side give surfaces that are genuinely different colours.
        //
        // Four rather than one per surface: the tree, the panel behind it and
        // the viewer being three unrelated colours is the effect asked for -
        // every element having its own would be noise rather than a palette.
        double[] party = daring
            ? PrimaryHues.OrderBy(_ => rng.Next()).Take(4)
                .Select(h => Wrap(h + (rng.NextDouble() * 24 - 12))).ToArray()
            : Array.Empty<double>();
        if (daring)
        {
            baseHue = party[0];
            accentHue = party[1];
            neighborHue = party[2];
        }

        // Roughly one roll in four is a BOLD one: same rules, saturation
        // ceilings lifted. The calm band on its own read as timid - every roll
        // differing from the last only slightly - and starved the blues in
        // particular, since a blue at low saturation greys out sooner than a
        // warm hue does, so uniformly-drawn blue rolls kept arriving invisible
        // (2026-08-09). The readability floors below are what make a bolder
        // ceiling safe to sell.
        bool bold = daring || rng.NextDouble() < 0.28;
        // The daring bands are far above the others, and that alone was not
        // enough: a surface at 0.08 BRIGHTNESS is near-black whatever its
        // saturation, so the tree's ground could never actually look red. The
        // brightness moves too - see bgVal in each branch.
        double surfaceSat = light
            ? (daring ? 0.22 + rng.NextDouble() * 0.34
                : bold ? 0.05 + rng.NextDouble() * 0.11 : 0.02 + rng.NextDouble() * 0.07)
            : (daring ? 0.42 + rng.NextDouble() * 0.42
                : bold ? 0.06 + rng.NextDouble() * 0.16 : 0.03 + rng.NextDouble() * 0.10);
        double accentSat = daring
            ? 0.56 + rng.NextDouble() * 0.32
            : bold ? 0.34 + rng.NextDouble() * 0.26 : 0.16 + rng.NextDouble() * 0.22;

        // The ceilings the derived colours are held under. They exist so a bold
        // accent cannot be multiplied into a shout further down; the bolder
        // roll wants the shout, so its ceiling is raised rather than removed.
        double satCap = daring ? 0.94 : 0.72;
        double hoverCap = daring ? 0.44 : bold ? 0.30 : 0.22;

        if (!light)
        {
            // 0.07~0.15 -> 0.05~0.11 (2026-08-10). Dark rolls were
            // landing at the pale end of dark and reading as charcoal rather
            // than as a dark theme. Everything else on this branch is derived
            // from bgVal by fixed offsets, so the surfaces keep their
            // relationships to each other and only the ground moves down.
            // The daring band sits well above the ordinary one. A dark theme
            // still, but far enough up that a saturated hue reads as that hue
            // rather than as black with a rumour of colour in it.
            // 0.05~0.11 -> 0.06~0.17, drawn with a bias toward the dark end
            // (2026-08-13). The move down on 08-10 was asked for and was right,
            // but it went far enough that the band bottomed out: 0.06 of range
            // at the black end is a set of rolls nobody can tell apart, and the
            // quiet die exists precisely for the small differences between one
            // roll and the next ("다크모드 일반 랜덤들은 좀 다 너무 꺼멓네요").
            //
            // So the floor comes up a little and the CEILING is what really
            // moves - and the curve is what stops that from being a brightness
            // bump in disguise. A plain linear widening to the same top would
            // have dragged the average up with it and undone the correction;
            // squaring the draw leaves most rolls where the user put them and
            // spends the new headroom on the occasional lighter one. Mean 0.08
            // -> 0.10, against the 0.11 this band had before 08-10.
            double bgVal = daring
                ? 0.13 + rng.NextDouble() * 0.15
                : 0.06 + Math.Pow(rng.NextDouble(), 1.6) * 0.11;
            var background = FromHsv(baseHue, surfaceSat, bgVal, 255);
            var hover = FromHsv(neighborHue, Math.Min(hoverCap, surfaceSat + 0.06), bgVal + 0.08, 255);
            // A step up in both saturation and brightness, so the selection box
            // reads a little stronger than the surface. Capped so a bold roll's
            // already-high accent saturation can't be multiplied into a shout.
            var selection = FromHsv(accentHue, Math.Min(satCap, accentSat * 1.18), bgVal + 0.17, 255);
            // Text is checked against HOVER, the lightest surface a name
            // actually sits on in the dark theme - passing there passes
            // everywhere.
            //
            // For a DARING roll the surfaces below pull apart, so "the lightest
            // surface" is no longer necessarily hover - the text is walked
            // against whichever of the tree's own three it reads worst on.
            var header = daring
                ? FromHsv(party[2], Math.Min(satCap, surfaceSat + 0.10), bgVal + 0.02 + rng.NextDouble() * 0.16, 255)
                : FromHsv(baseHue, surfaceSat, bgVal + 0.025, 255);
            var history = daring
                ? FromHsv(party[3], Math.Min(satCap, surfaceSat + 0.06), bgVal - 0.04 + rng.NextDouble() * 0.14, 255)
                : FromHsv(baseHue, surfaceSat, bgVal + 0.015, 255);
            // Against the BACKGROUND too now: once the ground itself carries a
            // real colour at a real brightness, it is a surface names have to
            // survive on, which it was not while it sat at near-black.
            var text = EnsureContrast(baseHue, 0.05 + rng.NextDouble() * 0.05, 0.76,
                Worst(Worst(hover, history), background), 4.5, towardLight: true);
            return new RandomPalette(
                Background: background,
                Header: header,
                History: history,
                // THE VIEWER IS ALLOWED TO BE A DIFFERENT ROOM in a daring
                // roll. It shows pictures, not names, so nothing has to stay
                // readable on it - which makes it the one surface that can take
                // a whole other colour without costing anything.
                Viewer: daring
                    ? FromHsv(party[1], Math.Min(satCap, surfaceSat + 0.14), bgVal - 0.02 + rng.NextDouble() * 0.20, 255)
                    : background,
                Hover: hover,
                Selection: selection,
                Guide: FromHsv(baseHue, surfaceSat, bgVal + 0.10, 255),
                GuideActive: FromHsv(accentHue, accentSat * 0.55, bgVal + 0.30, 255),
                Text: text,
                TextHover: Emphasize(text, towardLight: true),
                // Raised with the selection it sits on, so the pair moves
                // together rather than the box getting stronger under a name
                // that stayed where it was.
                Highlight: EnsureContrast(accentHue, accentSat * 0.5, 0.96, selection, 5.0, towardLight: true),
                ShowMore: EnsureContrast(baseHue, 0.06, 0.60, background, 3.0, towardLight: true),
                Handle: RollHandle(accentHue, accentSat, background, rng, towardLight: true));
        }
        else
        {
            // NEARLY WHITE, about a third of the time (2026-08-11). The dark
            // side got this treatment on 08-10 - its rolls were landing at the
            // pale end of dark and reading as charcoal - and the light side was
            // left with the matching problem at its own end: every light roll
            // carried a visible tint, so none of them was simply a white
            // sidebar with colour in it. The draw only empties the TINT; the
            // hue is still there for everything derived below.
            bool nearWhite = !daring && rng.NextDouble() < 0.34;
            if (nearWhite)
            {
                surfaceSat *= 0.25;
            }

            // Down off pure white for a daring roll, the mirror of the dark
            // branch coming up off black: at 0.97 a saturated hue is a tint,
            // and a tint is what this button exists not to be.
            double bgVal = nearWhite
                ? 0.985 + rng.NextDouble() * 0.015
                : daring ? 0.80 + rng.NextDouble() * 0.14
                : 0.95 + rng.NextDouble() * 0.04;
            var background = FromHsv(baseHue, surfaceSat, bgVal, 255);
            var hover = FromHsv(neighborHue,
                Math.Min(daring ? 0.36 : bold ? 0.24 : 0.16, surfaceSat + 0.05), bgVal - 0.07, 255);
            // Same step up the dark branch takes - more saturation, and one
            // notch further from the background it sits on.
            var selection = FromHsv(accentHue,
                daring ? 0.44 + rng.NextDouble() * 0.30
                    : bold ? 0.31 + rng.NextDouble() * 0.21 : 0.18 + rng.NextDouble() * 0.15,
                0.855, 255);
            // Same as the dark branch: a daring roll pulls the surfaces apart,
            // so the text is walked against the worst of the tree's own.
            var header = daring
                ? FromHsv(party[2], Math.Min(satCap, surfaceSat + 0.12), bgVal - 0.02 - rng.NextDouble() * 0.14, 255)
                : FromHsv(baseHue, surfaceSat, bgVal - 0.035, 255);
            var history = daring
                ? FromHsv(party[3], Math.Min(satCap, surfaceSat + 0.08), bgVal + 0.03 - rng.NextDouble() * 0.14, 255)
                : FromHsv(baseHue, surfaceSat, bgVal - 0.02, 255);
            var text = EnsureContrast(baseHue, 0.10 + rng.NextDouble() * 0.10, 0.27,
                Worst(Worst(hover, history), background), 4.5, towardLight: false);
            return new RandomPalette(
                Background: background,
                Header: header,
                History: history,
                Viewer: daring
                    ? FromHsv(party[1], Math.Min(satCap, surfaceSat + 0.16), bgVal + 0.02 - rng.NextDouble() * 0.22, 255)
                    : background,
                Hover: hover,
                Selection: selection,
                Guide: FromHsv(baseHue, surfaceSat + 0.02, bgVal - 0.13, 255),
                GuideActive: FromHsv(accentHue, accentSat * 0.5, bgVal - 0.38, 255),
                Text: text,
                TextHover: Emphasize(text, towardLight: false),
                Highlight: EnsureContrast(accentHue, Math.Min(daring ? 0.88 : 0.62, accentSat + 0.18), 0.19, selection, 5.0, towardLight: false),
                ShowMore: EnsureContrast(baseHue, 0.08, 0.45, background, 3.0, towardLight: false),
                Handle: RollHandle(accentHue, accentSat, background, rng, towardLight: false));
        }
    }

    // THE ONE THING IN A ROLL THAT IS MEANT TO BE LOUD, while everything else
    // stays quiet (2026-08-10). It used to be handed the rolled BACKGROUND, which reproduced the
    // handle's follow-the-background default - correct as a default, and
    // exactly wrong for a die roll, since the one control the palette could
    // show off with came out invisible.
    //
    // The accent hue at a saturation floor rather than the roll's own: a calm
    // roll's accent is deliberately washed out, and a handle drawn at that
    // saturation is the thing this is fixing. Brightness goes the OPPOSITE way
    // from the theme so it separates from the ground either way - bright on a
    // dark sidebar, deep on a light one - and the contrast walk is the same
    // guarantee the text colours get, so a hue whose natural brightness fights
    // the background is pushed until it clears it.
    private static Color RollHandle(
        double accentHue, double accentSat, Color background, Random rng, bool towardLight)
    {
        double sat = Math.Clamp(accentSat + 0.24, 0.45, 0.85);
        double val = towardLight
            ? 0.72 + rng.NextDouble() * 0.16
            : 0.52 + rng.NextDouble() * 0.16;
        return EnsureContrast(accentHue, sat, val, background, 3.0, towardLight);
    }

    // Hover names get one visible step MORE presence than resting ones
    // (2026-08-09): a 15% blend
    // toward the theme's bright end (white in dark, black in light). A blend
    // can only ADD contrast over the already-enforced resting text, so no
    // re-check is needed, and it degrades gracefully when the resting text is
    // already near the end of the range.
    private static Color Emphasize(Color c, bool towardLight)
    {
        byte target = towardLight ? (byte)255 : (byte)0;
        return Color.FromArgb(c.A, Mix(c.R), Mix(c.G), Mix(c.B));

        byte Mix(byte channel) => (byte)Math.Round(channel + (target - channel) * 0.15);
    }

    private static double Wrap(double hue) => (hue % 360 + 360) % 360;

    // Walks brightness toward the readable end (bleeding saturation out once
    // brightness runs out) until the WCAG-style contrast ratio clears the
    // target. Terminates: each step moves monotonically and both walks have
    // hard bounds.
    // The surface a name will read WORST on, of the two handed in. Which one
    // that is stopped being obvious once a daring roll let the tree's surfaces
    // take separate hues and brightnesses: hover used to be the extreme by
    // construction, and now the history strip can be. Mid-grey, so the answer
    // is "whichever is nearer the middle", not "whichever is lighter".
    private static Color Worst(Color a, Color b)
        => Math.Abs(Luminance(a) - 0.18) < Math.Abs(Luminance(b) - 0.18) ? a : b;

    private static Color EnsureContrast(double hue, double sat, double val,
        Color against, double target, bool towardLight)
    {
        var color = FromHsv(hue, sat, val, 255);
        for (int i = 0; i < 60 && ContrastRatio(color, against) < target; i++)
        {
            if (towardLight ? val < 1 : val > 0)
            {
                val = towardLight ? Math.Min(1, val + 0.025) : Math.Max(0, val - 0.025);
            }
            else if (sat > 0)
            {
                sat = Math.Max(0, sat - 0.05);
            }
            else
            {
                break;
            }
            color = FromHsv(hue, sat, val, 255);
        }
        return color;
    }

    private static double Luminance(Color c)
    {
        return 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);

        static double Channel(byte value)
        {
            double s = value / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }
    }

    // Which of the palette's own inks to lay on `over`, preferred first.
    //
    // 4.5 is the ratio the rest of this file builds to, so the first candidate
    // that reaches it wins and the order is the preference: the tree's
    // highlight, then its plain text, then - only if a roll has put the chip
    // somewhere neither can be read - the BACKGROUND colour, which is the one
    // ink in the palette guaranteed to be far from the selection, since the
    // roll builds the selection to contrast with it.
    //
    // Drawn from the palette rather than reaching for white or black: a roll
    // is a set of colours that go together, and a pure white dropped into it
    // is the one thing in the strip that did not come from the roll.
    private static Color InkOver(Color over, params Color[] preferred)
    {
        foreach (var ink in preferred)
        {
            if (ContrastRatio(ink, over) >= 4.5)
            {
                return ink;
            }
        }

        // Nothing reached it - take the best of them rather than the last.
        var best = preferred[0];
        foreach (var ink in preferred)
        {
            if (ContrastRatio(ink, over) > ContrastRatio(best, over))
            {
                best = ink;
            }
        }
        return best;
    }

    private static double ContrastRatio(Color a, Color b)
    {
        double la = Luminance(a);
        double lb = Luminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    private static string Hex(Color c) => $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}";

    private void ResetDefaults_Click(object sender, RoutedEventArgs e)
    {
        string modeLabel = _settings.IsLightMode ? Strings.ColorThemeLightLabel : Strings.ColorThemeDarkLabel;
        var result = System.Windows.MessageBox.Show(this, string.Format(Strings.ColorResetConfirmBody, modeLabel),
            Strings.ColorResetConfirmTitle, MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (result != System.Windows.MessageBoxResult.Yes)
        {
            return;
        }

        var defaults = new AppSettings { IsLightMode = _settings.IsLightMode };
        CurrentBackgroundColorHex = defaults.IsLightMode ? defaults.LightBackgroundColorHex : defaults.BackgroundColorHex;
        CurrentFolderNameColorHex = defaults.IsLightMode ? defaults.LightFolderNameColorHex : defaults.FolderNameColorHex;
        CurrentFolderNameHighlightColorHex = defaults.IsLightMode ? defaults.LightFolderNameHighlightColorHex : defaults.FolderNameHighlightColorHex;
        CurrentFileNameColorHex = defaults.IsLightMode ? defaults.LightFileNameColorHex : defaults.FileNameColorHex;
        CurrentFileNameHighlightColorHex = defaults.IsLightMode ? defaults.LightFileNameHighlightColorHex : defaults.FileNameHighlightColorHex;
        CurrentSelectionColorHex = defaults.IsLightMode ? defaults.LightSelectionColorHex : defaults.SelectionColorHex;
        CurrentHistoryBackgroundColorHex = defaults.IsLightMode ? defaults.LightHistoryBackgroundColorHex : defaults.HistoryBackgroundColorHex;
        CurrentHoverBackgroundColorHex = defaults.IsLightMode ? defaults.LightHoverBackgroundColorHex : defaults.HoverBackgroundColorHex;
        CurrentFolderNameHoverColorHex = defaults.IsLightMode ? defaults.LightFolderNameHoverColorHex : defaults.FolderNameHoverColorHex;
        CurrentFileNameHoverColorHex = defaults.IsLightMode ? defaults.LightFileNameHoverColorHex : defaults.FileNameHoverColorHex;
        CurrentShowMoreColorHex = defaults.IsLightMode ? defaults.LightShowMoreColorHex : defaults.ShowMoreColorHex;
        CurrentPanelNameColorHex = defaults.IsLightMode ? defaults.LightPanelNameColorHex : defaults.PanelNameColorHex;
        CurrentPanelNameHighlightColorHex = defaults.IsLightMode ? defaults.LightPanelNameHighlightColorHex : defaults.PanelNameHighlightColorHex;
        CurrentPanelNameHoverColorHex = defaults.IsLightMode ? defaults.LightPanelNameHoverColorHex : defaults.PanelNameHoverColorHex;
        CurrentGuideLineColorHex = defaults.IsLightMode ? defaults.LightGuideLineColorHex : defaults.GuideLineColorHex;
        CurrentGuideLineActiveColorHex = defaults.IsLightMode ? defaults.LightGuideLineActiveColorHex : defaults.GuideLineActiveColorHex;
        CurrentExpanderColorHex = defaults.IsLightMode ? defaults.LightExpanderColorHex : defaults.ExpanderColorHex;
        CurrentFilterChipCheckedColorHex = defaults.IsLightMode ? defaults.LightFilterChipCheckedBackgroundColorHex : defaults.FilterChipCheckedBackgroundColorHex;
        CurrentFilterChipCheckedFontColorHex = defaults.IsLightMode ? defaults.LightFilterChipCheckedForegroundColorHex : defaults.FilterChipCheckedForegroundColorHex;
        CurrentFilterChipExcludeColorHex = defaults.IsLightMode ? defaults.LightFilterChipExcludeColorHex : defaults.FilterChipExcludeColorHex;
        CurrentFilterChipExcludeCheckedColorHex = defaults.IsLightMode ? defaults.LightFilterChipExcludeCheckedBackgroundColorHex : defaults.FilterChipExcludeCheckedBackgroundColorHex;
        CurrentHeaderBackgroundColorHex = defaults.IsLightMode ? defaults.LightHeaderBackgroundColorHex : defaults.HeaderBackgroundColorHex;
        CurrentPanelDividerColorHex = defaults.IsLightMode ? defaults.LightPanelDividerColorHex : defaults.PanelDividerColorHex;
        CurrentViewerBackgroundColorHex = defaults.IsLightMode ? defaults.LightViewerBackgroundColorHex : defaults.ViewerBackgroundColorHex;

        // Written out rather than put back to "follow the background": the
        // background on the line above has just been reset as well, so the two
        // still match, and every row on this page ends up holding a colour of
        // its own - which is what the button says it does.
        CurrentAutoHideHandleColorHex = defaults.IsLightMode ? defaults.LightAutoHideHandleColorHex : defaults.AutoHideHandleColorHex;

        // The OPPOSITE of the line above, on purpose. The handle row is written
        // out so every row on this page ends up holding a colour of its own;
        // this one is CLEARED, so it goes back to following the background -
        // which is the state it is meant to be in, and the only state that
        // stays right when the background is repicked later.
        if (_settings.IsLightMode)
        {
            _settings.StoredLightSelectionZoneColor = null;
        }
        else
        {
            _settings.StoredSelectionZoneColor = null;
        }

        RefreshSwatches();
        _onChanged();
    }

    private void BackgroundSwatch_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        => PickColor(BackgroundSwatch, () => CurrentBackgroundColorHex, hex => CurrentBackgroundColorHex = hex);

    private void FolderNameFontSwatch_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        => PickColor(FolderNameFontSwatch, () => CurrentFolderNameColorHex, hex => CurrentFolderNameColorHex = hex);

    private void FolderNameHighlightFontSwatch_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        => PickColor(FolderNameHighlightFontSwatch, () => CurrentFolderNameHighlightColorHex, hex => CurrentFolderNameHighlightColorHex = hex);

    private void FileNameFontSwatch_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        => PickColor(FileNameFontSwatch, () => CurrentFileNameColorHex, hex => CurrentFileNameColorHex = hex);

    private void FileNameHighlightFontSwatch_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        => PickColor(FileNameHighlightFontSwatch, () => CurrentFileNameHighlightColorHex, hex => CurrentFileNameHighlightColorHex = hex);

    private void SelectionSwatch_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        => PickColor(SelectionSwatch, () => CurrentSelectionColorHex, hex => CurrentSelectionColorHex = hex);

    private void SelectionZoneSwatch_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        => PickColor(SelectionZoneSwatch, () => CurrentSelectionZoneColorHex, hex => CurrentSelectionZoneColorHex = hex);

    private void HistorySwatch_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        => PickColor(HistorySwatch, () => CurrentHistoryBackgroundColorHex, hex => CurrentHistoryBackgroundColorHex = hex);

    private void HoverBackgroundSwatch_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        => PickColor(HoverBackgroundSwatch, () => CurrentHoverBackgroundColorHex, hex => CurrentHoverBackgroundColorHex = hex);

    private void FolderNameHoverFontSwatch_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        => PickColor(FolderNameHoverFontSwatch, () => CurrentFolderNameHoverColorHex, hex => CurrentFolderNameHoverColorHex = hex);

    private void FileNameHoverFontSwatch_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        => PickColor(FileNameHoverFontSwatch, () => CurrentFileNameHoverColorHex, hex => CurrentFileNameHoverColorHex = hex);

    private void ShowMoreFontSwatch_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        => PickColor(ShowMoreFontSwatch, () => CurrentShowMoreColorHex, hex => CurrentShowMoreColorHex = hex);

    private void GuideLineSwatch_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        => PickColor(GuideLineSwatch, () => CurrentGuideLineColorHex, hex => CurrentGuideLineColorHex = hex);

    private void ExpanderSwatch_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        => PickColor(ExpanderSwatch, () => CurrentExpanderColorHex, hex => CurrentExpanderColorHex = hex);

    private void FilterChipCheckedSwatch_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        => PickColor(FilterChipCheckedSwatch, () => CurrentFilterChipCheckedColorHex, hex => CurrentFilterChipCheckedColorHex = hex);

    private void FilterChipCheckedFontSwatch_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        => PickColor(FilterChipCheckedFontSwatch, () => CurrentFilterChipCheckedFontColorHex, hex => CurrentFilterChipCheckedFontColorHex = hex);

    private void FilterChipExcludeSwatch_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        => PickColor(FilterChipExcludeSwatch, () => CurrentFilterChipExcludeColorHex, hex => CurrentFilterChipExcludeColorHex = hex);

    private void FilterChipExcludeCheckedSwatch_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        => PickColor(FilterChipExcludeCheckedSwatch, () => CurrentFilterChipExcludeCheckedColorHex, hex => CurrentFilterChipExcludeCheckedColorHex = hex);

    private void PanelNameFontSwatch_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        => PickColor(PanelNameFontSwatch, () => CurrentPanelNameColorHex, hex => CurrentPanelNameColorHex = hex);

    private void PanelNameHighlightFontSwatch_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        => PickColor(PanelNameHighlightFontSwatch, () => CurrentPanelNameHighlightColorHex, hex => CurrentPanelNameHighlightColorHex = hex);

    private void PanelNameHoverFontSwatch_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        => PickColor(PanelNameHoverFontSwatch, () => CurrentPanelNameHoverColorHex, hex => CurrentPanelNameHoverColorHex = hex);

    private void GuideLineActiveSwatch_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        => PickColor(GuideLineActiveSwatch, () => CurrentGuideLineActiveColorHex, hex => CurrentGuideLineActiveColorHex = hex);

    private void HeaderSwatch_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        => PickColor(HeaderSwatch, () => CurrentHeaderBackgroundColorHex, hex => CurrentHeaderBackgroundColorHex = hex);

    private void PanelDividerSwatch_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        => PickColor(PanelDividerSwatch, () => CurrentPanelDividerColorHex, hex => CurrentPanelDividerColorHex = hex);

    private void ViewerBackgroundSwatch_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        => PickColor(ViewerBackgroundSwatch, () => CurrentViewerBackgroundColorHex, hex => CurrentViewerBackgroundColorHex = hex);

    private void AutoHideHandleSwatch_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        => PickColor(AutoHideHandleSwatch, () => CurrentAutoHideHandleColorHex, hex => CurrentAutoHideHandleColorHex = hex);

    // ----- 색상 코드 직접 입력 -----------------------------------------------
    //
    // Windows' own picker has three RGB number boxes and nowhere to paste
    // "#2E7D32" from a browser or a design tool, which is how colours actually
    // travel (2026-07-31). So the swatch answers a right-click with
    // a box holding its current value.
    //
    // Right-click rather than a field on every row: seventeen rows with a text
    // box each would eat the width the Korean labels need, and multiply tab
    // order, validation and the Insert guard by seventeen - the opposite of the
    // direction this window has been going. A shared box at the bottom was the
    // other candidate and needs a "selected row" concept this window does not
    // have. Both were weighed and dropped with the user before any of this
    // existed; the tooltip on every swatch is what keeps the gesture findable.
    // One place that says which swatch edits which colour, so the left-click
    // picker and this cannot end up disagreeing - or a colour added later be
    // wired to one and not the other.
    // ----- 체인: 묶어서 한 번에 -----------------------------------------------
    //
    // EVERY LIT ROW IS ONE GROUP. Predefined pairs were the first cut - folder
    // against file at each of the three roles, which is what a random roll
    // already does - and they were wrong, because a link on every row promises
    // that the rows YOU light are the ones that move (2026-08-15: two rows lit
    // in different pairs, and setting one left the other standing). A mark
    // that appears on a row has to mean something about that row.
    //
    // So there is no group table; the chained set is the group. The cost is
    // that there is exactly ONE chain - names and backgrounds cannot be two
    // separate links at once - which is the honest first version of this and
    // the thing to revisit if it ever gets in the way.
    private Border[]? _colorSwatches;

    private Border[] ColorSwatches => _colorSwatches ??= new[]
    {
        FolderNameFontSwatch, FolderNameHighlightFontSwatch, FolderNameHoverFontSwatch,
        FileNameFontSwatch, FileNameHighlightFontSwatch, FileNameHoverFontSwatch,
        PanelNameFontSwatch, PanelNameHighlightFontSwatch, PanelNameHoverFontSwatch,
        ShowMoreFontSwatch, HeaderSwatch, HistorySwatch, BackgroundSwatch,
        ViewerBackgroundSwatch, SelectionSwatch, SelectionZoneSwatch, HoverBackgroundSwatch,
        GuideLineSwatch, GuideLineActiveSwatch, PanelDividerSwatch,
        AutoHideHandleSwatch,
        // 펼침기호 and the four filter chips were absent until 2026-08-26, which
        // made them ASYMMETRIC: lit, they could drag the group along, and the
        // group could never drag them - the loop below only visits this array.
        // Every row carries the toggle, so every row has to be here, or the
        // mark says something about that row that is not true.
        ExpanderSwatch, FilterChipCheckedSwatch, FilterChipCheckedFontSwatch,
        FilterChipExcludeSwatch, FilterChipExcludeCheckedSwatch,
    };

    private bool IsChained(Border swatch)
        => swatch.Name is { Length: > 0 } name && _settings.ChainedColorRows.Contains(name);

    // The one write. Everything that sets a colour from this window goes
    // through it - the picker (live, per frame of a drag) and the hex box -
    // so a linked row cannot be updated by one path and missed by the other.
    //
    // A row that is not itself linked moves alone even if its partner is
    // lit: the link is a property of the PAIR, and reading it any other way
    // would let an unlit row be dragged along by a lit one.
    private void SetColorChained(Border swatch, string hex)
    {
        WriteColorRow(swatch, hex);

        if (!IsChained(swatch))
        {
            return;
        }

        foreach (var other in ColorSwatches)
        {
            if (!ReferenceEquals(other, swatch) && IsChained(other))
            {
                WriteColorRow(other, hex);
            }
        }
    }

    private void WriteColorRow(Border swatch, string hex)
    {
        if (ColorBindingFor(swatch) is not { } binding)
        {
            return;
        }

        binding.Set(hex);
        if (ColorConverter.ConvertFromString(hex) is Color color)
        {
            swatch.Background = new SolidColorBrush(color);
        }
    }

    private void ChainToggle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Primitives.ToggleButton toggle || toggle.Tag is not string name)
        {
            return;
        }

        if (toggle.IsChecked == true)
        {
            if (!_settings.ChainedColorRows.Contains(name))
            {
                _settings.ChainedColorRows.Add(name);
            }
        }
        else
        {
            _settings.ChainedColorRows.Remove(name);
        }

        // Linking does NOT pull the rows together on the spot. The press says
        // "from now on these move as one", and making it also overwrite one of
        // the two colours would mean a click that quietly threw a colour away
        // - with no way to know which of the pair was about to win.
    }

    private void RefreshChainToggles()
    {
        foreach (var swatch in ColorSwatches)
        {
            if (FindName(swatch.Name + "Chain") is System.Windows.Controls.Primitives.ToggleButton toggle)
            {
                toggle.IsChecked = IsChained(swatch);
            }
        }
    }

    private (Func<string> Get, Action<string> Set)? ColorBindingFor(Border swatch)
    {
        if (ReferenceEquals(swatch, BackgroundSwatch))
            return (() => CurrentBackgroundColorHex, hex => CurrentBackgroundColorHex = hex);
        if (ReferenceEquals(swatch, FolderNameFontSwatch))
            return (() => CurrentFolderNameColorHex, hex => CurrentFolderNameColorHex = hex);
        if (ReferenceEquals(swatch, FolderNameHighlightFontSwatch))
            return (() => CurrentFolderNameHighlightColorHex, hex => CurrentFolderNameHighlightColorHex = hex);
        if (ReferenceEquals(swatch, FolderNameHoverFontSwatch))
            return (() => CurrentFolderNameHoverColorHex, hex => CurrentFolderNameHoverColorHex = hex);
        if (ReferenceEquals(swatch, FileNameFontSwatch))
            return (() => CurrentFileNameColorHex, hex => CurrentFileNameColorHex = hex);
        if (ReferenceEquals(swatch, FileNameHighlightFontSwatch))
            return (() => CurrentFileNameHighlightColorHex, hex => CurrentFileNameHighlightColorHex = hex);
        if (ReferenceEquals(swatch, FileNameHoverFontSwatch))
            return (() => CurrentFileNameHoverColorHex, hex => CurrentFileNameHoverColorHex = hex);
        if (ReferenceEquals(swatch, ShowMoreFontSwatch))
            return (() => CurrentShowMoreColorHex, hex => CurrentShowMoreColorHex = hex);
        if (ReferenceEquals(swatch, PanelNameFontSwatch))
            return (() => CurrentPanelNameColorHex, hex => CurrentPanelNameColorHex = hex);
        if (ReferenceEquals(swatch, PanelNameHighlightFontSwatch))
            return (() => CurrentPanelNameHighlightColorHex, hex => CurrentPanelNameHighlightColorHex = hex);
        if (ReferenceEquals(swatch, PanelNameHoverFontSwatch))
            return (() => CurrentPanelNameHoverColorHex, hex => CurrentPanelNameHoverColorHex = hex);
        if (ReferenceEquals(swatch, SelectionSwatch))
            return (() => CurrentSelectionColorHex, hex => CurrentSelectionColorHex = hex);
        if (ReferenceEquals(swatch, SelectionZoneSwatch))
            return (() => CurrentSelectionZoneColorHex, hex => CurrentSelectionZoneColorHex = hex);
        if (ReferenceEquals(swatch, HistorySwatch))
            return (() => CurrentHistoryBackgroundColorHex, hex => CurrentHistoryBackgroundColorHex = hex);
        if (ReferenceEquals(swatch, HoverBackgroundSwatch))
            return (() => CurrentHoverBackgroundColorHex, hex => CurrentHoverBackgroundColorHex = hex);
        if (ReferenceEquals(swatch, GuideLineSwatch))
            return (() => CurrentGuideLineColorHex, hex => CurrentGuideLineColorHex = hex);
        if (ReferenceEquals(swatch, GuideLineActiveSwatch))
            return (() => CurrentGuideLineActiveColorHex, hex => CurrentGuideLineActiveColorHex = hex);
        if (ReferenceEquals(swatch, ExpanderSwatch))
            return (() => CurrentExpanderColorHex, hex => CurrentExpanderColorHex = hex);
        if (ReferenceEquals(swatch, FilterChipCheckedSwatch))
            return (() => CurrentFilterChipCheckedColorHex, hex => CurrentFilterChipCheckedColorHex = hex);
        if (ReferenceEquals(swatch, FilterChipCheckedFontSwatch))
            return (() => CurrentFilterChipCheckedFontColorHex, hex => CurrentFilterChipCheckedFontColorHex = hex);
        if (ReferenceEquals(swatch, FilterChipExcludeSwatch))
            return (() => CurrentFilterChipExcludeColorHex, hex => CurrentFilterChipExcludeColorHex = hex);
        if (ReferenceEquals(swatch, FilterChipExcludeCheckedSwatch))
            return (() => CurrentFilterChipExcludeCheckedColorHex, hex => CurrentFilterChipExcludeCheckedColorHex = hex);
        if (ReferenceEquals(swatch, HeaderSwatch))
            return (() => CurrentHeaderBackgroundColorHex, hex => CurrentHeaderBackgroundColorHex = hex);
        if (ReferenceEquals(swatch, PanelDividerSwatch))
            return (() => CurrentPanelDividerColorHex, hex => CurrentPanelDividerColorHex = hex);
        if (ReferenceEquals(swatch, ViewerBackgroundSwatch))
            return (() => CurrentViewerBackgroundColorHex, hex => CurrentViewerBackgroundColorHex = hex);
        if (ReferenceEquals(swatch, AutoHideHandleSwatch))
            return (() => CurrentAutoHideHandleColorHex, hex => CurrentAutoHideHandleColorHex = hex);

        return null;
    }

    // Every box names its swatch in its Tag, so these handlers serve all
    // sixteen. The Tag holds the NAME and this looks it up, rather than holding
    // an {Binding ElementName=...} to the swatch itself: that binding is not
    // resolved yet when the box raises Loaded, so the first fill found no
    // swatch and every box came up EMPTY until it had been clicked into and
    // left again (2026-08-02). A name has no such moment.
    private Border? SwatchOf(object sender)
        => (sender as TextBox)?.Tag is string name ? FindName(name) as Border : null;

    // Collected as they load rather than named one by one, so RefreshSwatches
    // can put the current values back into all of them without a sixteen-line
    // list to keep in step.
    private readonly List<TextBox> _hexBoxes = new();

    private void HexBox_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBox box)
        {
            return;
        }

        // Every text box in this app disables overtype - a bare Insert
        // otherwise flips it on and breaks Korean composition, with no visible
        // sign it happened (see OvertypeGuard).
        OvertypeGuard.Disable(box);

        if (!_hexBoxes.Contains(box))
        {
            _hexBoxes.Add(box);
        }

        RefreshHexBox(box);
    }

    private void HexBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            ApplyHexBox(sender);
            return;
        }

        // Puts back what the swatch actually holds. The box stays where it is -
        // there is nothing to close - so "cancel" can only mean "undo what I
        // typed".
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            RefreshHexBox(sender);
        }
    }

    // Committing on focus loss as well as on Enter: the box is a permanent part
    // of the row, so tabbing or clicking away from it is a perfectly ordinary
    // way to finish, and having to press Enter would silently drop the edit.
    private void HexBox_LostKeyboardFocus(object sender, RoutedEventArgs e) => ApplyHexBox(sender);

    private void ApplyHexBox(object sender)
    {
        if (sender is not TextBox box || SwatchOf(sender) is not { } swatch ||
            ColorBindingFor(swatch) is not { } binding)
        {
            return;
        }

        // Anything that doesn't parse is simply dropped and the box goes back to
        // the colour that is actually set - no error, no red outline, no dialog.
        // Showing the real value again says "that wasn't taken" more clearly
        // than a message would.
        if (ParseHex(box.Text, (Color)ColorConverter.ConvertFromString(binding.Get())) is not { } color)
        {
            RefreshHexBox(sender);
            return;
        }

        // The chain paints this row's swatch as well as any linked partner's,
        // so there is no separate Background write here any more.
        SetColorChained(swatch, $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}");
        foreach (var other in _hexBoxes)
        {
            RefreshHexBox(other);
        }

        UpdateResetButtonEnabled();
        _onChanged();
    }

    private void RefreshHexBox(object sender)
    {
        if (sender is TextBox box && SwatchOf(sender) is { } swatch &&
            ColorBindingFor(swatch) is { } binding)
        {
            box.Text = FormatHex(binding.Get());
        }
    }

    // Without the alpha byte. Every colour here is opaque, and an "FF" in front
    // of the part people actually paste is just something to delete - the
    // stored value keeps whatever alpha it had (see ParseHex).
    private static string FormatHex(string storedHex)
    {
        var color = (Color)ColorConverter.ConvertFromString(storedHex);
        return $"#{color.R:X2}{color.G:X2}{color.B:X2}";
    }

    // Takes what a colour code looks like anywhere it might be copied from:
    // with or without the "#", three digits or six, either case, and the eight
    // -digit form this app stores. The alpha of the value being replaced is
    // kept unless the input states its own, so pasting "#2E7D32" over an opaque
    // colour cannot silently turn it transparent.
    private static Color? ParseHex(string? text, Color fallback)
    {
        string value = (text ?? string.Empty).Trim().TrimStart('#');
        if (!value.All(Uri.IsHexDigit))
        {
            return null;
        }

        // #abc is the CSS shorthand: each digit doubled.
        if (value.Length == 3)
        {
            value = string.Concat(value.Select(c => new string(c, 2)));
        }

        return value.Length switch
        {
            6 => Color.FromArgb(
                fallback.A,
                Convert.ToByte(value[..2], 16),
                Convert.ToByte(value.Substring(2, 2), 16),
                Convert.ToByte(value.Substring(4, 2), 16)),
            8 => Color.FromArgb(
                Convert.ToByte(value[..2], 16),
                Convert.ToByte(value.Substring(2, 2), 16),
                Convert.ToByte(value.Substring(4, 2), 16),
                Convert.ToByte(value.Substring(6, 2), 16)),
            _ => null,
        };
    }

    // ----- 색상 피커 ---------------------------------------------------------
    //
    // Windows' own ColorDialog was here until 2026-08-04, and the reason it
    // went is the OK button: a colour is judged against the actual sidebar,
    // not a 40px sample, and the native dialog only hands the value over once
    // it closes. So every attempt cost open-pick-confirm-look-reopen. This one
    // writes through on every movement of the handle, and the tree behind the
    // window repaints as it goes.
    //
    // What that costs per mouse-move is one ApplyColorSettings: about twenty
    // SolidColorBrush allocations dropped into the resource dictionary. No
    // layout, no folder re-read, no disk. It stays a full apply rather than
    // touching only the edited brush BECAUSE several brushes are derived from
    // the picked ones (the quieted name variants, the inactive selection) -
    // updating one alone would leave those disagreeing with it mid-drag.
    private Border? _pickerSwatch;
    private Action<string>? _pickerSet;
    private string? _pickerOriginalHex;

    // Hue is kept here rather than recomputed from the current colour, because
    // it cannot be: at zero saturation every hue is the same grey, so a handle
    // dragged into the white or black corner would come back red on the next
    // move. Saturation and brightness have no such trouble.
    private double _pickerHue;
    private double _pickerSat;
    private double _pickerVal;

    // The alpha the row already had. No control for it: every colour this app
    // ships is opaque, and the hex box takes 8 digits for anyone who needs
    // otherwise - so the picker preserves what it was given instead of
    // silently flattening it.
    private byte _pickerAlpha = 0xFF;

    // Static, so the colours picked while setting one row are still offered
    // while setting the next - and after this window is closed and reopened.
    // This replaces the native dialog's custom-colour palette, which had the
    // same lifetime for the same reason.
    private static readonly List<string> _recentColors = new();

    private const int RecentColorLimit = 8;

    private void PickColor(Border swatch, Func<string> getHex, Action<string> setHex)
    {
        // Whatever was open is left as it stands - the values are already
        // applied, so there is nothing to confirm.
        ClosePicker(keep: true);

        if (ColorConverter.ConvertFromString(getHex()) is not Color current)
        {
            return;
        }

        _pickerSwatch = swatch;
        // Through the chain, not the setter the caller handed in - that one
        // knows about one row only. See SetColorChained.
        _pickerSet = hex => SetColorChained(swatch, hex);
        _pickerOriginalHex = getHex();
        _pickerAlpha = current.A;
        (_pickerHue, _pickerSat, _pickerVal) = ToHsv(current);

        BuildRecentColors();

        // Shown first, then laid out, and only then measured and drawn into: a
        // Collapsed element has no size at all, so both the panel's placement
        // and the handles' positions would be computed against zeros.
        PickerLayer.Visibility = Visibility.Visible;
        PickerLayer.UpdateLayout();
        PositionPickerPanel(swatch);
        UpdatePickerVisuals();
    }

    // Anchored under the swatch it belongs to, and pulled back inside the
    // window when there is no room below - the rows near the bottom of a
    // seventeen-row list are exactly the ones that would otherwise open a
    // panel half off the window.
    private void PositionPickerPanel(Border swatch)
    {
        double panelWidth = PickerPanel.ActualWidth;
        double panelHeight = PickerPanel.ActualHeight;

        // Both measured against the WINDOW and subtracted, rather than the
        // swatch against the layer directly: the layer is the swatch's sibling
        // in the outer grid, not its ancestor, and TransformToAncestor throws
        // on anything else (it did - 2026-08-04, on the first open).
        var swatchOrigin = swatch.TransformToAncestor(this).Transform(new Point(0, 0));
        var layerOrigin = PickerLayer.TransformToAncestor(this).Transform(new Point(0, 0));
        var origin = new Point(swatchOrigin.X - layerOrigin.X, swatchOrigin.Y - layerOrigin.Y);

        double left = origin.X + swatch.ActualWidth - panelWidth;
        double top = origin.Y + swatch.ActualHeight + 4;

        if (top + panelHeight > PickerLayer.ActualHeight)
        {
            top = origin.Y - panelHeight - 4;
        }

        Canvas.SetLeft(PickerPanel, Math.Max(6, Math.Min(left, PickerLayer.ActualWidth - panelWidth - 6)));
        Canvas.SetTop(PickerPanel, Math.Max(6, top));
    }

    // keep: leave the colour where the handle left it (the ordinary ending -
    // an outside click, or moving on to another row). Esc instead asks for the
    // colour this row had when the panel opened, which is the only undo a
    // window that applies as you go can offer.
    private void ClosePicker(bool keep)
    {
        if (PickerLayer.Visibility != Visibility.Visible)
        {
            return;
        }

        if (!keep && _pickerOriginalHex is { } original && _pickerSet is { } setHex)
        {
            setHex(original);
            ApplyPickedColor(original);
        }
        else if (_pickerSwatch is not null)
        {
            RememberRecentColor(CurrentPickerHex());
        }

        _pickerApplyTimer?.Stop();
        _pickerDirty = false;

        PickerLayer.Visibility = Visibility.Collapsed;
        _pickerSwatch = null;
        _pickerSet = null;
        _pickerOriginalHex = null;

        // Cleared last: the hover rows hold the hovered look only while one of
        // them is being picked, and that is no longer true as of this line.
        RefreshLabelPreviews();
    }

    private void ColorSettingsWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
        {
            return;
        }

        // The picker gets first refusal: one Esc should put back the colour
        // being picked, not close the whole window with it applied. A second
        // Esc then closes, as it always did - that used to be the 닫기 button's
        // IsCancel, and the button is gone.
        if (PickerLayer.Visibility == Visibility.Visible)
        {
            ClosePicker(keep: false);
        }
        else
        {
            Close();
        }

        e.Handled = true;
    }

    private void PickerLayer_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Only the layer itself: a press that reached the panel is a press on
        // the picker, and bubbles up here afterwards.
        if (ReferenceEquals(e.OriginalSource, PickerLayer))
        {
            ClosePicker(keep: true);
        }
    }

    private void FieldHost_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        FieldHost.CaptureMouse();
        TrackField(e);
    }

    private void FieldHost_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (FieldHost.IsMouseCaptured)
        {
            TrackField(e);
        }
    }

    private void FieldHost_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        FieldHost.ReleaseMouseCapture();
        FinishPickerDrag();
    }

    private void HueHost_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        HueHost.CaptureMouse();
        TrackHue(e);
    }

    private void HueHost_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (HueHost.IsMouseCaptured)
        {
            TrackHue(e);
        }
    }

    private void HueHost_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        HueHost.ReleaseMouseCapture();
        FinishPickerDrag();
    }

    private void TrackField(System.Windows.Input.MouseEventArgs e)
    {
        var point = e.GetPosition(FieldHost);
        _pickerSat = Clamp01(point.X / Math.Max(1, FieldHost.ActualWidth));
        _pickerVal = 1 - Clamp01(point.Y / Math.Max(1, FieldHost.ActualHeight));

        // The handle follows every single move; only the colour going out to
        // the app is throttled. Moving these two together made the handle
        // itself run at the throttle's rate, which reads exactly like a 30Hz
        // screen (2026-08-04) - and it was never the expensive half.
        UpdatePickerVisuals();
        QueuePickerUpdate();
    }

    private void TrackHue(System.Windows.Input.MouseEventArgs e)
    {
        var point = e.GetPosition(HueHost);
        _pickerHue = Clamp01(point.X / Math.Max(1, HueHost.ActualWidth)) * 360.0;
        UpdatePickerVisuals();
        QueuePickerUpdate();
    }

    private System.Windows.Threading.DispatcherTimer? _pickerApplyTimer;
    private bool _pickerDirty;

    // A mouse sends 125 to 1000 positions a second. Acting on each one repeats
    // the same work dozens of times for a single visible result, and that work
    // reaches the whole app: the sidebar behind this window redraws every
    // element that uses the colour being changed, which is why it is felt most
    // with a lot of rows on screen (2026-08-04).
    //
    // Thirty a second is the rate here. Sixty - one per frame - was tried
    // first and was still enough to spin the fans up; half of them is under
    // what the eye picks out on a colour sliding through neighbouring shades,
    // and it halves everything downstream.
    //
    // Whatever the throttle drops, the release puts back: FinishPickerDrag
    // applies the exact colour the handle was left on, so the value that
    // lands is never the one from a frame or two earlier.
    private const int PickerApplyIntervalMs = 33;

    private void QueuePickerUpdate()
    {
        _pickerDirty = true;

        if (_pickerApplyTimer is null)
        {
            _pickerApplyTimer = new System.Windows.Threading.DispatcherTimer(
                System.Windows.Threading.DispatcherPriority.Render)
            {
                Interval = TimeSpan.FromMilliseconds(PickerApplyIntervalMs),
            };
            _pickerApplyTimer.Tick += (_, _) => FlushPickerUpdate();
        }

        _pickerApplyTimer.Start();
    }

    private void FlushPickerUpdate()
    {
        if (!_pickerDirty)
        {
            return;
        }

        _pickerDirty = false;

        // The drag may have ended, or moved to another row, since the tick was
        // scheduled.
        if (PickerLayer.Visibility != Visibility.Visible)
        {
            return;
        }

        // The handle has already moved (see TrackField). This is only the
        // expensive half: the colour reaching the app behind the window.
        ApplyPickedColor(CurrentPickerHex());
    }

    // Called when the button comes up: stops the throttle and lands the exact
    // colour under the handle, so nothing rests on which tick happened to be
    // last.
    private void FinishPickerDrag()
    {
        _pickerApplyTimer?.Stop();
        _pickerDirty = true;
        FlushPickerUpdate();
    }

    private void UpdatePickerVisuals()
    {
        FieldHueFill.Fill = new SolidColorBrush(FromHsv(_pickerHue, 1, 1, 0xFF));

        double fieldX = _pickerSat * FieldHost.ActualWidth;
        double fieldY = (1 - _pickerVal) * FieldHost.ActualHeight;
        foreach (var thumb in new[] { FieldThumbOuter, FieldThumbInner })
        {
            Canvas.SetLeft(thumb, fieldX - thumb.Width / 2);
            Canvas.SetTop(thumb, fieldY - thumb.Height / 2);
        }

        Canvas.SetLeft(HueThumb, _pickerHue / 360.0 * HueHost.ActualWidth - HueThumb.Width / 2);

        var current = FromHsv(_pickerHue, _pickerSat, _pickerVal, _pickerAlpha);
        PickerHexText.Text = $"#{current.R:X2}{current.G:X2}{current.B:X2}";
    }

    private string CurrentPickerHex()
    {
        var color = FromHsv(_pickerHue, _pickerSat, _pickerVal, _pickerAlpha);
        return $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";
    }

    // Writes the colour everywhere it shows at once: the setting, this row's
    // swatch, every hex box (they would otherwise disagree with the swatch
    // beside them - 2026-08-02), the reset button's enabled state, and the app
    // behind this window.
    private void ApplyPickedColor(string hex)
    {
        if (_pickerSet is not { } setHex || _pickerSwatch is not { } swatch)
        {
            return;
        }

        setHex(hex);
        if (ColorConverter.ConvertFromString(hex) is Color color)
        {
            swatch.Background = new SolidColorBrush(color);
        }

        foreach (var box in _hexBoxes)
        {
            RefreshHexBox(box);
        }

        RefreshLabelPreviews();
        UpdateResetButtonEnabled();
        _onChanged();
    }

    private void RememberRecentColor(string hex)
    {
        _recentColors.RemoveAll(existing => string.Equals(existing, hex, StringComparison.OrdinalIgnoreCase));
        _recentColors.Insert(0, hex);
        while (_recentColors.Count > RecentColorLimit)
        {
            _recentColors.RemoveAt(_recentColors.Count - 1);
        }
    }

    private void BuildRecentColors()
    {
        PickerRecent.Children.Clear();
        foreach (string hex in _recentColors)
        {
            if (ColorConverter.ConvertFromString(hex) is not Color color)
            {
                continue;
            }

            var chip = new Border
            {
                Width = 14,
                Height = 14,
                Margin = new Thickness(3, 0, 0, 0),
                CornerRadius = new CornerRadius(2),
                BorderThickness = new Thickness(1),
                BorderBrush = (Brush)FindResource("ControlBorder"),
                Background = new SolidColorBrush(color),
                Cursor = System.Windows.Input.Cursors.Hand,
                ToolTip = hex
            };
            chip.MouseLeftButtonUp += (_, _) =>
            {
                _pickerAlpha = color.A;
                (_pickerHue, _pickerSat, _pickerVal) = ToHsv(color);
                UpdatePickerVisuals();
                ApplyPickedColor(CurrentPickerHex());
            };
            PickerRecent.Children.Add(chip);
        }
    }

    private static double Clamp01(double value) => Math.Max(0, Math.Min(1, value));

    private static (double Hue, double Sat, double Val) ToHsv(Color color)
    {
        double r = color.R / 255.0, g = color.G / 255.0, b = color.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b));
        double min = Math.Min(r, Math.Min(g, b));
        double delta = max - min;

        double hue = 0;
        if (delta > 0)
        {
            if (max == r)
            {
                hue = 60 * (((g - b) / delta) % 6);
            }
            else if (max == g)
            {
                hue = 60 * (((b - r) / delta) + 2);
            }
            else
            {
                hue = 60 * (((r - g) / delta) + 4);
            }
        }

        if (hue < 0)
        {
            hue += 360;
        }

        return (hue, max <= 0 ? 0 : delta / max, max);
    }

    // CLAMPED AT THE DOOR (2026-08-11). Nothing that calls this today hands it
    // a value outside 0~1 - the ranges were checked one by one - but there was
    // no guard, and a cast from a double past 255 to a byte does NOT saturate
    // in C#: it produces an unspecified value, so an over-bright colour would
    // come out as a dark one rather than as white. That is a wrong colour with
    // nothing to show it went wrong, and the round that lifts saturation
    // ceilings for the bolder roll is exactly the change that could reach it.
    //
    // The hue is wrapped rather than clamped because it is an angle; a negative
    // one would otherwise pick a channel branch AND a negative component.
    private static Color FromHsv(double hue, double sat, double val, byte alpha)
    {
        hue = ((hue % 360) + 360) % 360;
        sat = Math.Clamp(sat, 0, 1);
        val = Math.Clamp(val, 0, 1);

        double c = val * sat;
        double x = c * (1 - Math.Abs(((hue / 60.0) % 2) - 1));
        double m = val - c;

        (double r, double g, double b) = (hue % 360) switch
        {
            < 60 => (c, x, 0.0),
            < 120 => (x, c, 0.0),
            < 180 => (0.0, c, x),
            < 240 => (0.0, x, c),
            < 300 => (x, 0.0, c),
            _ => (c, 0.0, x),
        };

        return Color.FromArgb(alpha, Channel(r + m), Channel(g + m), Channel(b + m));

        // Belt as well as braces: the arithmetic above cannot leave 0~1 once
        // the inputs are clamped, and it costs nothing to make sure the cast
        // can never be the thing that decides a colour.
        static byte Channel(double value)
            => (byte)Math.Clamp(Math.Round(value * 255), 0, 255);
    }
}
