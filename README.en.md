<div align="right">

[简体中文](README.md) · [English](README.en.md)

</div>

# Nook

Tidy your desktop into drawers that drop down from the top edge of your screen.

A hairline rail lives along the top edge — each tick is one drawer. Push the mouse up, pause for a beat, and the drawer slides out from under the rail. Move away and it retracts.

## What it is

Windows desktops collect icons faster than you can delete shortcuts. Nook groups them into "drawers" so the desktop stays clean, and you pull one down from the top edge when you need it.

**It doesn't hide desktop icons — it actually moves the files into the drawer** (a `.nook-items` folder on the same volume), so it's near-instant and never blocks on cross-drive moves. The original path is kept in the config, and everything can be sent back to the desktop in one click.

## Usage

- Push the mouse to the very top edge → the rail appears
- Rest on a tick → a small card pops out first (drawer name, item count, first few icons); hold for ~0.5s and the full drawer drops down
- Click a tick → opens instantly, no waiting
- Drag a tick sideways → reorder drawers; an insertion marker shows where it will land
- Move away → the drawer retracts, the rail stays (switchable to "close on click" in the tray)
- Drop files onto the panel → they're filed into the current drawer
- Right-click the panel / click `···` next to the title → manage menu (rename, icon size, restore to desktop, backup…)

The tray icon offers: drawer list, backup & restore, run at startup, close-on-mouse-leave, hover-to-open, always-visible rail, always-on-top.

Unchecking **close-on-mouse-leave** pins the drawer in place until you click anywhere outside the panel.

## Highlights

- **Yields when the top edge is taken**: when the foreground window is maximized, or fullscreen (exclusive games, borderless fullscreen, full-screen video), the rail retreats off-screen. Fullscreen hides it completely; when maximized, resting the mouse on the very top edge for ~0.15s brings it back temporarily. It returns automatically on the desktop or normal windows
- **Hover preview**: not sure which drawer holds what? Pause on a tick to see its name and icons without opening anything
- **Real glass**: the panel backdrop is the actual desktop, captured and genuinely blurred — not a translucent white. It keeps refreshing while open, so the glass follows the desktop
  (trade-off: the panel is invisible to screenshot/recording tools — that's the price of continuous refresh, otherwise it would capture itself)
- **Icon snapshots**: a PNG is captured when an item is added, so icons never turn blank after the file moves
- **Steam games**: cover art comes from the Steam library and CDN — much nicer than system icons
- **Icons stay in memory**: the second open of a drawer is near-instant
- **Backup**: the whole drawer structure + icons zips up in one file; a safety snapshot of the current state is taken before any restore

## Build

Requires the .NET 9 SDK (Windows).

```bash
dotnet build -c Release
dotnet run -c Release
```

Single-file publish:

```bash
# Depends on the installed runtime, ~1 MB
dotnet publish -c Release -r win-x64 --self-contained false \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish/dep

# Bundles the runtime, ~146 MB, runs on any Win10/11 x64 machine
dotnet publish -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish/portable
```

## Where the data lives

- Config: `%APPDATA%\Nook\drawers.json`
- Icon snapshots: `%APPDATA%\Nook\icons\`
- Backups: `%APPDATA%\Nook\backups\`
- Filed-away files: `.nook-items\` on the volume each source file lived on

Uninstalling doesn't touch these. Drawer items stay where they are and can be moved back manually.

## Debug switches

A few self-test switches are built into the binary, so development doesn't require staring at the screen:

```bash
Nook.exe --capture out.png "drawer" [small|medium|large]   # render a panel off-screen to PNG
Nook.exe --uicheck                                          # drive open/anchor/retract, results go to the log
Nook.exe --perf "drawer"                                    # open twice in one process, compare cold vs warm
Nook.exe --reorder "drawer" ±1                              # reorder and print the new sequence
Nook.exe --selftest                                         # verify move-in / move-back
Nook.exe --backup path.zip / --restore path.zip
Nook.exe --autostart on|off
```

Logs go to `%TEMP%\nook.log`.

## Fonts

UI typefaces are [Sora](https://github.com/sora-xor/sora-font) and [JetBrains Mono](https://github.com/JetBrains/JetBrainsMono), both under the SIL Open Font License 1.1 — see `Assets/Fonts/licenses/`.

## License

[MIT](LICENSE)
