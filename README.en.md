<div align="right">

[简体中文](README.md) · [English](README.en.md)

</div>

# Nook

Tidy your desktop into drawers that drop down from the top edge of your screen.

A hairline rail lives along the top edge — each tick is one drawer. Push the mouse up, pause for a beat, and the drawer drops down. Move away and it retracts.

## Why I wrote it

My desktop collected 90+ icons and I couldn't take it anymore. Deleting felt wrong, and every "desktop organizer" out there just wants to build folders for me. So Nook groups icons into "drawers", leaves nothing but the wallpaper, and pulls one down from the top when you need it.

It doesn't hide icons — it actually **moves the files into the drawer** (a plain move within the same disk volume, near-instant). The original path is kept in the config, and everything goes back to the desktop in one click.

## How to use it

- Push the mouse to the very top edge, and the rail appears
- Resting on a tick pops out a small card with the drawer's name and icons; hold half a second and the full drawer drops down — or just click the tick to open instantly
- Drag a tick sideways to reorder drawers
- Drop files onto the panel to file them into the current drawer
- Right-click the panel or hit `···` to manage it: rename, icon size, restore to desktop, backup

The tray icon has the drawer list, accent color, backup & restore, autostart and a few switches worth knowing:

- **Accent color** — nine presets plus a color picker; ticks, accent bars and borders all follow, so it can match your wallpaper
- **Live glass backdrop** — the panel background is the actual desktop, genuinely blurred, and it keeps updating while open. Trade-off: the panel is invisible to screenshots and recordings, otherwise it would capture itself
- **Hover preview** — know what's inside a drawer without opening it
- **Steam covers** — game icons come straight from Steam, way better than system icons
- **Icon snapshots** — icons never turn blank after a file moves
- **Fullscreen courtesy** — the rail hides while you're gaming; when a window is maximized, resting on the top edge brings it back temporarily
- **Close behavior** — retract on mouse-leave, or on outside click, your call
- **Icons stay in memory** — the second open of a drawer is near-instant
- **Backup** — one-click zip, and it snapshots the current state before any restore

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

UI typefaces are [Sora](https://github.com/sora-xor/sora-font) and [JetBrains Mono](https://github.com/JetBrains/JetBrainsMono), both SIL OFL 1.1 — licenses in `Assets/Fonts/licenses/`.

## License

[MIT](LICENSE), do whatever you like.
