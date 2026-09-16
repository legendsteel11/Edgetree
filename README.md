# Edgetree v2.6.1

[한국어 안내](README-ko.md)

https://edgetree.vercel.app/

A lightweight, always-on-hand file explorer for Windows that docks to the left
or right edge of the screen, VS Code Explorer style. It's not meant to replace
Windows Explorer — it's a quick way to glance at a folder structure and jump
straight to a file without opening a full Explorer window.

## Download

Grab the latest build from the [Releases page](https://github.com/legendsteel11/Edgetree/releases/latest). Three options are attached to each release:

- **`Edgetree-<version>-win-x64-setup.exe`** (~49 MB) — the installer. Click
  through it and Edgetree lands in your Start menu, with a clean uninstall.
- **`Edgetree-<version>-win-x64-standalone.exe`** (~155 MB) — one file, nothing
  to install. Keep it wherever you like and run it.
- **`Edgetree-<version>-win-x64.exe`** (~1 MB) — the same app, using the
  [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0)
  already on the machine.

Settings live in `%AppData%\Edgetree` and all three share them, so moving from
one to another keeps your bookmarks and colors — and uninstalling
leaves them where they are.

## Screenshots

![Edgetree docked to the left edge of the screen](screenshot/EdgetreeDemo.gif)

## Features

### Docking and auto-hide

- **Docks to the screen edge** — left by default, right if you prefer. It
  spans the full working height and stays out of Alt+Tab while docked.
- **Auto-hide with the pin button.** Click the pin and the window shrinks to
  a thin sliver at the edge; move your mouse there and it slides back open.
  The pin lies on its side while auto-hiding — click it again to pin the
  window open. The sliver can be a short handle at the middle of the edge
  instead of running its whole height, and it takes a color of its own.
- **Floating mode.** Drag the header away from the edge to get a normal,
  movable window. The pin snaps it back to the edge, and both modes remember
  their size and position.
- **Resize by dragging**, or double-click the resize line to fit the width
  to the longest visible name.

### The tree

- **Starts at "This PC"** with every drive listed; folders load as you
  expand them, and the app reopens exactly where you left off.
- **Single click expands a folder** (the whole row, VS Code style);
  double-click opens a file.
- **Large folders stay fast.** Each folder shows its first 50 items with a
  "Show N more" row for the rest, so the tree never renders thousands of
  rows at once. The cap is adjustable (1–50).
- **File type filter**: a row of chips along the bottom — code, images,
  documents, media, archives, executables — picks what the tree shows and
  drops the rest. Type extensions of your own under Custom, or ones to always
  hide under Exclude. Search still finds what the filter is hiding.
- **Path bar**: type a path to go straight there. `Ctrl+←` / `Ctrl+→`, or the
  mouse thumb buttons, step back and forward through the folders you have been
  in, and the bar lists them.
- **Network locations**: add a share with no drive letter (`\\server\share`)
  from the right-click menu on empty tree space, and it stays in the tree.
- **Multi-select** with `Ctrl+click` and `Shift+click`, then copy, delete,
  or drag the whole selection out in one go.
- **Auto-Collapse Folders** (optional): expanding a folder closes the others, so
  only the path you're on stays open.
- **Inline rename** with `F2`, right in the row — no popup. Clicking an
  already-selected file again after a pause works too, like Explorer.
- **Sorting** by name, date, type or size, ascending or descending — globally, or per
  folder from its right-click menu.
- **Bookmarks** (`Ctrl+Alt+K`): mark rows you keep coming back to and cycle
  through them with `Ctrl+Alt+L` / `Ctrl+Alt+J`.
- **Hide what you don't use.** Right-click a folder — or a whole drive — and
  take it out of the tree. Hidden ones collect in a list right below, to be
  brought back one at a time or all at once. Search still reaches inside them.
- **Live updates**: changes made outside the app (new, renamed, or deleted
  files) show up in the tree on their own.

### Files and the right-click menu

- **A full right-click menu**: new folder, open, open with, copy/paste,
  compress, rename, delete to the Recycle Bin, copy path, open in terminal,
  reveal in Explorer, properties — with the usual shortcuts (`F2`, `F5`,
  `F7`, `Delete`, `Ctrl+C`/`Ctrl+V`, `Enter`).
- **Compress to zip** from the same menu, and unpack a `.zip` with Extract.
- **Image preview.** Right-click an image and a thumbnail appears at the top
  of the menu, with its format, pixel size, file size, and modified date.
  Click the thumbnail to open the file.
- **Drag files out** into Explorer or any other app — a standard Windows
  file drag.

### The multimedia panel

- **Pictures, music and video in the app.** The picture icon in the title bar
  opens a panel beside the tree — on either side, your choice. Turn on
  **Open on double-click** and files from the tree open here instead of in
  your usual program; there is also an option to open the panel as soon as
  you select a file. `Backspace` folds the panel away, and the playback
  volume is remembered across restarts.
- **Pictures**: wheel to zoom, `Ctrl`/`Shift`+wheel for finer steps, drag to
  move around with a navigator in the corner. Double-click switches Fit ↔ 1:1,
  and Fill crops the picture to the panel. PSD, RAW and JXL read alongside the
  usual formats. The thumbnail bar takes a size of its own, and a picture
  drags straight out into another app. The mouse thumb buttons page through
  the folder.
- **Slideshow**: right-click a picture, or `F8`.
- **Wallpaper**: set a picture as the desktop background, separately on each
  monitor.
- **Music keeps playing** while you work — close the panel and the sound
  stays on. Album art, and a clock over the panel with `F9`.
- **Video** with HDR correction and subtitles. Subtitles take a size, a
  position and a sync offset (`<` and `>` shift the sync), and the size
  scales with the film, so it holds its proportion at any resolution or window
  size. `F` fits the window to the film and the black bands go (window mode).
- **The film you were watching** stays at the foot of the tree when you move to
  another folder, and picking it up resumes from where you stopped.
- **Full screen**: wheel-click or `Enter`, `Esc` to leave. It grows the
  window to the screen, or fills the window you already have — pick which
  from the right-click menu. It works while docked, and the thumbnail bar
  steps aside for a film.

### Bookmarks and search

- **Bookmark panel**: keep the folders and files you go back to, and reach any
  of them with one click — the tree expands and scrolls straight there, even
  across drives. Every row is numbered, and that number is its place in the
  `Ctrl+Alt+L`/`J` cycle; drag a row to change it.
- **Folder search** (`Ctrl+F`): pick a folder and find files by name, with
  substring or `*`/`?` wildcard matching. Results are grouped by folder;
  click one to jump to it in the tree, or drag it into another app.
- **The index is saved**, so a folder you've searched before is ready
  instantly on the next launch — on a NAS this turns minutes of indexing
  into about a second. Searching works while indexing is still running.

### Looks and settings

- **Two icon styles**: the same icons Windows Explorer shows (default), or
  the bundled [Material Icon Theme](THIRD-PARTY-NOTICES.md) set.
- **25 customizable colors**, with separate dark and light palettes and a
  one-click reset. Each one takes a `#RRGGBB` code, so a color copied from a
  browser or a design tool can be pasted straight in.
- **Font size** (`Ctrl` `+`/`-`, 9–20pt), indent width, row spacing, and
  scrollbar width are all adjustable — icons and menus scale along.
- **Sharp at any display scale** (125%, 150%, 200%…): rendered at the
  actual scale instead of being stretched.
- **Korean and English UI.**
- **Presets**: keep the whole shape of the app — window mode, position, size,
  docking, colors, file types and the current folder — as up to five presets.
  `Ctrl+1`–`Ctrl+5` switches between them and `Ctrl+Shift+S` overwrites the
  one you are in; they also sit on the title bar's right-click menu and the
  tree's empty-space menu.
- **Settings export/import** as a JSON file, bookmarks included.
- The rest lives in the options ("...") menu: start with Windows, always on
  top, minimize to tray, update notification, and more. An update dot on the
  options button tells you when a new release is out.

## Requirements

- Windows 10/11
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (to build/run
  from source)

## Building & running

```bash
git clone <this-repo-url>
cd Edgetree
dotnet run --project src/Edgetree
```

Or build the whole solution:

```bash
dotnet build Edgetree.sln
```

## Publishing a standalone executable

Framework-dependent (small, needs the .NET 8 Desktop Runtime on the target machine):

```bash
dotnet publish src/Edgetree -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true
```

Self-contained (large, no .NET install needed on the target machine):

```bash
dotnet publish src/Edgetree -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

`IncludeNativeLibrariesForSelfExtract` matters here — without it, WPF's native
interop DLLs (`D3DCompiler_47_cor3.dll`, `wpfgfx_cor3.dll`, etc.) get dropped
loose next to the exe instead of bundled into it, so the exe stops working the
moment it's moved on its own.

Either way, the resulting `.exe` is in
`src/Edgetree/bin/Release/net8.0-windows/win-x64/publish/`.

The installer is built from a third publish — the same self-contained build
without `PublishSingleFile`, so the files stay loose. That is the point of it:
a single-file self-contained exe unpacks itself into memory and reads as
350-400 MB in Task Manager on a machine with no .NET, while loose files are
mapped from disk normally.

```bash
dotnet publish src/Edgetree -c Release -r win-x64 --self-contained true -o publish/folder
"$LOCALAPPDATA/Programs/Inno Setup 6/ISCC.exe" installer/Edgetree.iss
```

[Inno Setup 6](https://jrsoftware.org/isinfo.php) compiles it, and the result
lands in `releases/v<version>/` beside the other two. The script reads its
version out of the exe it packages, so bumping the csproj is enough.

## Changelog

### v2.6.1 (2026-09-14)

- Properties is now on a thumbnail's right-click menu.
- Right-click a file in the tree and Go to folder takes you to the folder it sits in. Alt+↑ does the same.
- Right-click menus now use the height of the screen, so a menu that fits shows everything without scrolling.
- The playing indicator and the play/pause button are easier to see.
- The multimedia panel starts closed when launched by Start with Windows, and folds on auto-hide when there is nothing in it to show.
- Fixed the edges of PNG thumbnails with transparent corners looking broken.

### v2.6.0 (2026-09-08)

- A picture at 1:1 now lands on whole screen pixels. It could sit half a pixel off before, which softened every edge.
- Fixed the app closing when a path was copied while another program held the clipboard.

### v2.5.9 (2026-09-05)

- The bookmark panel now mutes an entry the tree is not showing. Right-click an excluded bookmark folder to stop excluding it from there.
- Fixed an empty area appearing below the tree after moving to another folder.
- A jump to a bookmark or a folder now places the row in one movement instead of two.
- Fixed Auto-collapse folders intermittently not running.

### v2.5.8 (2026-08-31)

- Group by folder and the sort order in the search results now work independently. Name · Date modified and Ascending · Descending stay available while results are grouped, and with the grouping off each file row carries its folder path on the right.
- Click a folder header in the search results to go to that folder in the tree. Enter does the same.
- Fixed a bookmarked file showing an expand arrow in the tree.

### v2.5.7 (2026-08-30)

- A new setting, Show path bar at top, in Options → General. Switch it on and the path bar sits directly under the header instead of at the bottom. It keeps that place in the search view too.
- Fixed clicking a bookmarked file reporting that the item could not be found. Folders and files both navigate correctly now.
- Fixed a folder reached through a bookmark collapsing on its first tree click with Expand on a single click off. The first click keeps the selection, and the second one collapses.

### v2.5.6 (2026-08-29)

- A new setting, Expand on a single click, in Options → General. Switch it off and the first click on a folder selects it, while the second one expands or collapses.
- Create shortcut has been added to the tree's right-click menu. With several items selected it makes one for each.
- Shift+Backspace collapses every expanded folder, the same as Shift+clicking the collapse-all icon.
- Show This PC icon, in Options → General, puts Windows' own This PC icon in the title bar.
- Fixed the check on a selected thumbnail being hard to see in light mode. Its ink is now taken from the selection color.
- Fixed the rename box appearing dark in light mode.

### v2.5.5 (2026-08-27)

- A faint band now marks the full extent of the folder you are in, so you can see at a glance where you are. Its background color can be set separately in the color settings.
- The playback row at the foot of the tree now has play and pause beside the stop button, and shows a moving mark while music is playing.
- The list of excluded folders now shows which drive each folder is on. Network and cloud locations are marked on the icon.
- A folder that contains nothing but excluded folders is no longer shown as empty. Deleting a folder now tells you how many excluded folders are inside it.
- Tooltips in the thumbnail bar and list now appear faster.
- In a folder's right-click menu, Open in default app now reads Expand or Collapse. Fixed a problem where that row and Enter did nothing on a folder that was already expanded.
- The English wording has been reviewed throughout the app, and every label now reads in sentence case.
- The color window's buttons were rearranged: Close on the far left, the four palette buttons on the right. Fixed the uneven spacing around one of its dividers.
- Fixed a problem caused by a slight unintended drag when moving a folder: a drive row picked up that way offered to copy the whole drive into the folder it landed on, under a confirmation with no name in it.

### v2.5.4 (2026-08-26)

- An expanded folder says when it is empty, and when the file type filter is what emptied it, how many files it is hiding.
- The numbers in the bookmark panel take the bookmark name’s colour. They used to borrow the Show More row’s, so darkening that colour took the numbers with it.
- Fixed rows in the tree’s right-click menu not greying out when they do not apply - Hide on a file, Extract on something that is not a zip, and others.
- Fixed Edgetree closing along with another application when that application failed while a file was being dragged into it.
- Hide This Folder in the tree’s right-click menu is now Hide, which is also what it says for a drive or several items.
- Alt+Enter opens Properties from the tree, from the open menu, or from a search result.
- Fixed the mark that says a menu continues past the screen not appearing until the wheel was touched.
- Fixed the thumbnail list alternating between two heights because of the scrollbar’s width.

### v2.5.3 (2026-08-24)

- Fixed the multimedia panel’s file details overlapping the thumbnail bar, and the panel height shifting, after using full screen.

### v2.5.2 (2026-08-24)

- A folder's sort order now applies to its subfolders. A subfolder with its own sort keeps it.
- Shift-clicking the Collapse All icon in the title bar folds without storing the expanded state.
- Hovering the arrows at the top or bottom of a long menu scrolls it.
- Connecting or removing a USB or cloud drive updates the tree automatically.
- The selected cell in the thumbnail list is easier to pick out.
- An X beside the title ends detached audio playback.
- Show Hidden and System Items moved to Options → General.
- Clicking a thumbnail for an item hidden behind Show More in the tree now selects it.

### v2.5.1 (2026-08-22)

- Fixed a crash when files arrived in the folder on show.
- Refreshing renews the thumbnails of files whose content changed.

### v2.5.0 (2026-08-22)

- The thumbnail bar can lay its pictures out as a scrolling list, now the default.
- Ctrl+wheel sizes the thumbnails; dragging the edge shows more rows.
- Select several pictures and copy, cut, delete or drag them out together.
- Files with no picture of their own show their file-type icon.
- Thumbnails no longer come up upside down now and then.
- A thumbnail click no longer fails to change the picture.

### v2.4.2 (2026-08-19)

- The film you were watching stays at the foot of the tree, and picks up where you left it.
- Subtitles scale with the film. `<` and `>` shift the sync; the menu sets their position.
- Fill the desktop without undocking first.
- `F` fits the window to the film, so the black bands go.
- The mouse thumb buttons move through folders, and through pictures.
- The playback volume is remembered.

### v2.4.1 (2026-08-18)

- Saving presets one after another no longer leaves the tray popping.
- Two help rows no longer show Korean text on the English screen.
- One spelling throughout: colors, minimize, grayscale.
- The language dialog now asks to change the language instead of saying it already changed.
- Counts of one read correctly.
- Accordion Mode is now Auto-Collapse Folders, the name its Korean row already carried.

### v2.4.0 (2026-08-17)

- Favorites are now merged into Bookmarks, and your saved items carry over.
- Drag rows in the Bookmarks panel to reorder them.
- Presets now store the window mode, and the app reopens in the mode it was closed in.
- Added an option to keep the current window size when entering full screen.
- Album art now supports Fit, 1:1, and Fill, and the selected size persists across restarts.
- Clicking an already-open folder now only selects it; a second click collapses it.
- Fixed folders collapsing on their own and the tree resetting to C:.

### v2.3.1 (2026-08-17)

- Folder auto-collapse now applies to bookmark, favourite and search jumps too
- Presets on the tree's empty-space right-click menu
- Fixed: the window could not be widened to the screen edge in full screen

### v2.3.0 (2026-08-16)

- PSD, RAW, JXL and more picture formats in the panel
- A clock over the panel (F9)
- Switch presets with Ctrl+1-5, overwrite with Ctrl+Shift+S
- The multimedia panel opens when you select a file (option)
- An option to show drive icons
- Colour settings for the expand arrow and the footer chips
- The track you picked reads apart from the one that is playing
- Show more / Show less selects the parent folder, and the indent guides follow at once
- A reworked expand arrow, with the indent guide through its centre
- Adding a bookmark or favourite opens the list it went into
- Fixed: in some large or network folders, editing the search box while it indexed left the app unresponsive
- Fixed: the thumbnail bar read ahead into large files like PSD and RAW
- Fixed: in full screen, the playback controls hid themselves while being pointed at

### v2.2.0 (2026-08-15)

- Swap the multimedia panel and the tree (option)
- Fixed: some jumps to a bookmark or favourite left the tree at the bottom
- The tree folding itself shut while the window was resized
- Fill — crop a picture to the panel
- Fine zoom on a picture with Ctrl or Shift and the wheel
- Right-click a folder to play the music and video in it
- The folders you have been in, listed on the path bar
- The app can be made smaller
- Colour chains and a greyscale roll, and shading at the ends of a list

### v2.1.0 (2026-08-14)

- Image slideshow (right-click a picture, or F8)
- Set a different wallpaper on each monitor (whichever one the app is on)
- A row half-clipped at the bottom of the panel now takes one click
- Fixed: un-picking one of several selected rows could sometimes clear the whole selection
- Fixed: a favorite or bookmark did not always land at the top after the jump

### v2.0.5 (2026-08-13)

- The app's file operations brought in line with Windows Explorer's
- Add a network location (right-click the empty area)
- Option to show every file in a folder at once
- Bug fixes and stability improvements
### v2.0.4 (2026-08-13)

- Tree positioning refinements
- The music player's controls tidied up

### v2.0.3 (2026-08-13)

- Bug fix — resolved an issue with a particular keyboard shortcut

### v2.0.2 (2026-08-12)

- **The image viewer is now the multimedia panel**: it shows pictures, plays
  film and sound, and carries a track on with nothing on screen at all
- **Double-click opens a file here**: turn it on in Options → Multimedia panel
  and images, music and video open in the app's own panel instead of going to
  Windows. Enter follows the same setting; everything else still opens in the
  default program
- **Which track is sounding, at a glance**: the mark on the album art is a
  triangle before it starts and two bars while it is playing, so tracks that
  share a cover tell themselves apart. While you are looking at a different
  file, the playback area takes a backing of its own to say the sound belongs
  somewhere else
- **Pressing play no longer moves the panel**: album art used to change size as
  a track started
- **Clear all favorites**: on the right-click menu of a favorite. The bookmark
  panel carries the same
- **A tidier colour window**: the theme row went from six buttons to four, and
  the theme in use is marked in the app's own blue
- **Options are saved as they are set**: a setting changed in the options menu
  is written the moment it is clicked

### v2.0.1 (2026-08-12)

- **Stronger logic for where the tree lands and what it puts on screen**
- **Drive rows carry the icon for what kind of drive they are** — fixed,
  removable, network and optical each read as themselves
- **Full-screen playback controls read clearly in the light theme** — the seek
  bar and the buttons were recoloured for the dark plate they sit on
- **A long menu no longer moves under the pointer** — running down past the
  bottom of a scrolling menu and back up could carry the rows with it

### v2.0.0 (2026-08-12)

- **Type a path directly**, and step back and forward through where you have
  been (`Ctrl+←`, `Ctrl+→`)
- **Image viewer** — a thumbnail bar and a navigator
- **Video playback** — HDR correction and subtitles
- **Music playback** — set it as the app's player and carry on elsewhere
  (viewing images, managing files, searching)
- **Presets** — keep the app's shape and settings and bring them back exactly
  (up to five)
- **More varied random colour modes**
- **Memory and performance work**, bug fixes, a faster app
- **`F1` help**

### v1.7.1 (2026-08-07)

- Fixed the app closing unexpectedly in rare cases while it slides
  open, seen with a shortened band and auto-hide together

### v1.7.0 (2026-08-07)

- **It need not fill the screen**: drag the docked app's top or
  bottom edge to set how much of the screen edge it takes. The top edge moves it
  as well, so where it sits and how tall it is come out of one gesture, and a
  double-click on either edge goes back to the full edge. It is kept as a share
  of the work area rather than in pixels, so a different monitor does not undo
  it, and the auto-hide handle and bar measure from that band — a window
  occupying the top third leaves its handle in that third
- **A first run starts with the handle**: a machine with no settings yet opens
  with the short handle at the middle of the screen edge instead of a bar down
  the whole side. An existing install keeps whatever it was already using

### v1.6.0 (2026-08-06)

- **An installer**: a third way to get it, alongside the two single exes. It
  lands in your Start menu, uninstalls cleanly, and leaves your settings in
  place either way
- **Auto-hide handle**: hiding can leave a short handle at the middle of the
  screen edge instead of a sliver running its whole height (options menu →
  "Handle Instead of Full Edge"). The rest of the edge stays clear, so the
  screen corners and any drag passing along it are no longer interrupted
- **A colour for the handle and bar**: set at the bottom of Color Settings.
  This is the one part of the app that sits on the desktop, against your
  wallpaper, so it gets its own colour - stored per theme, and carried along by
  the colour export
- **Your own extensions in the file filter**: Show File Types → "Custom…" takes
  a comma-separated list and adds it as a kind of its own. It combines with the
  others, and search still finds everything regardless
- **Resize a floating window from the top**, and double-click the header to
  restore a maximized one
- **The update mark reaches the tray icon**: a red dot on the icon and a line
  at the top of its menu. The dot on the options button is out of sight while
  the app is hidden or in the tray, which is exactly when this matters
- Fixed a new folder being created with a Korean name on an English install,
  and the row you picked up losing its selection mid-drag

### v1.5.0 (2026-08-04)

- **Colour picker**: clicking a swatch in Color Settings opens the app's own
  picker, and the app takes each colour as the handle moves
- **Export and import the colours on their own**: the palette travels as one
  file, so another PC can be set up to match
- **Click a bookmark's ribbon to release it**, at the right edge of the row
- **Each colour's label wears its own colour**: the ones that only appear in a
  particular situation - Show More, highlight, hover - can be judged where they
  are being chosen rather than after the fact
- Changing the file filter or the row count keeps your place in the tree, and
  the indent guides no longer swallow a click

### v1.4.2 (2026-08-03)

- **Show file types**: pick what the tree lists from the bottom bar - code,
  images, documents, media, archives, programs, other - any number at once, and
  "All" to clear it. The options menu and the right-click menus offer the same
  list. Folders are never filtered, and search still finds everything
- **Font weight**: normal, bold, bold folders only, or bold files only
- **Tooltips follow the theme**, and hovering a search result shows its full path
- **Long menus say which way there is more**, with an arrow at either end
- **Right-clicking empty space** now reaches bookmark jumps, the hidden-folder
  list and the file-type filter
- **Indent click fixed**: clicking the empty space to the left of a row no longer
  collapses the folder, so a stray click on a drive root no longer folds the
  whole drive and throws the view upward

### v1.4.1 (2026-08-02)

- **Bookmarks in the side panel**: options menu → "Side Panel" picks what the
  panel shows - bookmarks, favorites, or nothing. Bookmark rows are numbered
  from the top (which is also how many `Ctrl+Alt+L` presses away they are), and
  the one the tree is standing on is marked in blue. "Hidden" folds the panel
  away without deleting anything
- **Hide several folders at once**: pick them with Ctrl/Shift, then right-click →
  "Hide Selected Folders", and clear away everything you never open in one go
- **Long menus scroll**: a menu with more entries than the screen can hold now
  scrolls to the end instead of running off it - the hidden-folder list and the
  bookmark list included
- **Folder copy fixed**: pasting a folder into itself never finished copying.
  It is refused on the spot now
- **Bookmark jumps fixed**: fixed an issue that could occur when jumping to a
  bookmark
- Deleting a file now clears its bookmark too
- Making several new folders in a row now leaves the name box open on the last
  one only

### v1.4.0 (2026-08-02)

- **Hide a folder**: right-click → "Hide This Folder" takes it out of the tree,
  and a drive can go the same way. Whatever you hide collects in the "Hidden
  Folders" list right below it, where you can bring back one or all of them.
  Search still finds what is inside a hidden folder, and going in there from a
  result or a bookmark shows it in the tree for as long as you stay
- **Type a colour code**: every colour in the settings window now carries a
  `#RRGGBB` field, so a code copied from a browser or a design tool can simply
  be pasted in
- **Jumps land at the top**: a favorite, bookmark or search result now arrives at
  the top of the tree - room is made for it even at the very end, and a file
  lands there itself. The move happens in one step, with no intermediate
  position on the way
- **Full path on hover**: hovering a tree row shows its full path, including the
  rows whose names are too long to fit
- **Right-click menu regrouped**: items are gathered by what they do. Refresh
  moved down to the file commands, and Search in This Folder to the group that
  hands the location elsewhere
- Also: the bookmark list is available from the right-click menu too, and a
  search that finds nothing says how old its index is. Fixed: clicking the first
  row of the search results, dragging a scrollbar out of an auto-hidden window,
  and the Insert key interfering with Korean input

### v1.3.5 (2026-07-30)

- **Bookmarks from the right-click menu**: Bookmark is now a submenu carrying set
  and clear plus **previous and next bookmark**, each with its shortcut beside it.
  The shortcuts also show while no bookmark has been set yet
- **Right-click menu states**: rows that don't apply to what you clicked are
  greyed again (New Folder and Refresh on a file, Rename on a multi-selection)

### v1.3.4 (2026-07-30)

- **Cut**: cut with the right-click menu or Ctrl+X and paste into another folder
  to move rather than copy. It uses the same clipboard convention Explorer does,
  so cutting in one and pasting in the other works either way round. Cut rows
  keep a faded icon and an italic name, so it stays clear what is on its way out
- **The search list says when it is out of date**: a blue dot appears on the
  reindex button once a file has been added or removed inside the folder you are
  searching. Reindexing clears it
- **Icon licence notices**: About now names where the file and folder icons
  (Material Icon Theme) and the interface glyphs (Google's Material Symbols) come
  from, with their licences. The app carries the full Apache License 2.0 text and
  opens it from that window
- **Folder clicks in quick succession**: clicking a folder open and shut quickly
  could leave one of those clicks without effect. Fixed
- Reindex moved to the right of the results line, and the search history list now
  uses the same scrollbar as the rest of the app

### v1.3.3 (2026-07-28)

- **Bookmark list**: the options menu now shows every bookmark you have set.
  Click one to go there — the list stays open, so several can be checked in a
  row — drop one with the "−" at the end of its row, or clear them all from the
  bottom. The shortcuts are spelled out underneath
- **Search sorting opens a menu**: folder grouping, name or date modified, and
  the direction, all named. No more clicking through five states to reach the
  last one
- **Color Settings and About follow the font**: what Ctrl +/- does to the tree
  now reaches those windows too, swatches included
- **Works with an auto-hidden taskbar**: moving the cursor to the bottom edge
  brings the taskbar up as it should
- Clicking the space between the title bar icons no longer undocks the window
- The last favorite is no longer clipped after a restart
- The +/− steppers in the options menu stay centred at any font size

### v1.3.2 (2026-07-26)

- **Drag onto the hidden edge to open it**: drag a file from another window,
  rest it on the thin bar at the screen edge for a moment, and the app
  opens so you can drop it on the folder you want. Brushing past leaves it
  closed
- **Network drives (NAS and the like) hold up**: the window keeps working
  when a mapped drive stops answering. The drive keeps its place in the tree
  instead of vanishing, greys out with its folders folded away, and comes
  back on its own once the drive does
- **Sort by type or size**, alongside name and date modified — per folder or
  as the app-wide default. Clicking a row's sort icon opens that folder's
  sort menu directly
- **Reorder favorites** by dragging them, and hover one to see its full path
  when several folders share a name
- **Bookmarks tidy themselves**: a bookmark on a file or folder that has been
  deleted is dropped, while bookmarks on a drive that isn't answering are
  left alone
- The About window now links to the maker's other tool, TabStick

### v1.3.1 (2026-07-25)

- **Always on top**: now in effect from the moment the app starts, and an
  auto-hidden window can no longer end up behind another window with its
  sliver unresponsive
- **Favorites**: clicking a favorite you are already on brings that folder
  back to the top, however far the tree has been scrolled since
- **Items per folder**: the stepper responds immediately as you click through
  it

### v1.3.0 (2026-07-25)

- **Bookmarks**: mark a file or folder (`Ctrl+Alt+K`) and cycle through your
  marks (`Ctrl+Alt+L` / `Ctrl+Alt+J`) — marks survive restarts
- **Compress and extract**: zip the selection from the right-click menu
  (several items become one archive), and unpack a `.zip` into a folder of
  the same name
- **New folder shortcut**: `F7`
- **Faster scrolling**: hold `Ctrl` while scrolling the tree
- **Network drive badge**: folders on a network drive carry a small mark on
  their icon
- **Expanded folders stay open**: pasting, deleting, renaming, or dropping
  files in keeps the folders you had open and the place you were looking at
- **Dropping onto a file row** puts the files in that file's folder, the
  same as paste
- Right-click menu spacing tuned for low-resolution screens; adding a
  favorite no longer resizes the favorites panel

### v1.2.2 (2026-07-23)

- **Network drives behave better**: a drive that is asleep or briefly
  unreachable keeps its place in the tree and expands normally once it
  responds
- **Update download link**: when a newer release is available, the About
  window now shows a direct download link under the version
- Update-notification dot placement polish

### v1.2.1 (2026-07-23)

- **Image preview in the right-click menu**: a thumbnail with format, pixel
  size, file size, and modified date — click it to open the file
- **The pin is now a pinned/auto-hide toggle**: click to auto-hide (the pin
  lies down), click again to pin open (it stands back up) — replaces the old
  app-icon click
- **Menus follow the font zoom**: right-click/options menu text and spacing
  now scale with the tree's Ctrl `+`/`-` zoom, with tightened row rhythm
- **Windows Explorer icons are now the default** (the Material set remains an
  option)
- **Update notification**: a small dot on the options ("...") button when a
  newer release is available
- **Better behavior while you work elsewhere**: the selection highlight dims
  while the app is in the background, and inline rename no longer carries
  across app switches
- **Fresher folder contents**: re-expanding a collapsed folder now reflects
  changes made in the meantime, and external add/delete updates redraw more
  steadily
- Multi-select (Shift+click ranges) and right-click interaction polish;
  color settings now save immediately

### v1.2.0 (2026-07-21)

- **Multi-select**: Ctrl/Shift+click several files or folders to copy,
  delete, or drag them out together
- **Icon style option**: the same icons Windows Explorer shows

Earlier versions are covered on the
[GitHub releases page](https://github.com/legendsteel11/Edgetree/releases).

## Requests & bug reports

Email pjh85336@gmail.com.

## Other tools by the same maker

[TabStick](https://tabstick.com/) — index notes that stick beside
the window they belong to.

[SweepCap](https://sweepcap.vercel.app/) — hold Ctrl+Win and drag; the shot is
on your clipboard and in your Pictures folder the moment you let go.

[DeskNoise](https://desk-noise.vercel.app/) — a background sound generator,
five sounds mixed across three layers.

[MagicLoupe](https://magicloupe.vercel.app/) — a screen magnifier, color picker,
and live size and spacing measurement app.

## License

MIT — see [LICENSE.md](LICENSE.md). File and folder icons come from the Material
Icon Theme project (MIT) and the interface glyphs from Google's Material Symbols
(Apache License 2.0) — see [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md), and
the Apache License text the app itself carries at
[src/Edgetree/Resources/APACHE-2.0.txt](src/Edgetree/Resources/APACHE-2.0.txt).

## About Development

This tool was designed and iterated on by the author, with implementation
done in collaboration with Claude Code (Anthropic). Feature decisions,
UX design, and real-world testing were driven by daily personal use.
