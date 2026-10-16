# Kinetik Dock: groups of shortcuts on the Windows desktop

[![License: GPL v3](https://img.shields.io/badge/license-GPL--3.0-blue)](LICENSE)
![Windows 10 and 11](https://img.shields.io/badge/Windows-10%20%7C%2011-0078D4)

**Kinetik Dock** is a free, open-source app for Windows 11 and Windows 10 that keeps **groups of shortcuts on your desktop**: apps, files, folders and web links, each group in its own card. Think desktop fences, in the look of [Kinetik](https://github.com/WastedDesigner/Kinetik), the taskbar system monitor.

It's a small portable app with no installer and no account.

![Kinetik Dock in the Midnight theme: groups for apps, folders and web links](screenshots/groups.png)

![The same groups in the Light theme](screenshots/groups-light.png)

> **About this project:** I made this for my own desktop and decided to share it in case anyone else finds it useful. If you run into a bug or have an idea for a feature, please open an issue.

## Features

- **Any number of groups**, each with its own name, number of columns and place on screen
- **Drop to add:** drag apps, shortcuts, files or folders from File Explorer, or a link from your browser's address bar, onto a group
- **Click to open**; right-click a shortcut to show it in File Explorer, rename it, change what it opens, or move it to another group
- **Drag to rearrange** within a group or move a shortcut to another group. You can also drag one out onto anything that takes files.
- **Roll up:** double-click a group's title to fold it down to just its title bar
- **Real icons** from Windows at any size (small, normal or large), with names under them or not. Web links get a coloured letter tile.
- **Missing targets** (a removed app, an unplugged drive) are faded out rather than dropped
- **Groups line up:** when you drag a group near another one or near a screen edge, it snaps into line with it
- **Themes** in Kinetik's style, a choice of fonts, size and background opacity; on the desktop, normal windows or always on top; lock them all in place
- **Settings backup** next to the exe, offered again on a fresh Windows

## Download and use

1. Download `KinetikDock.zip` from Releases and extract it anywhere.
2. Run `KinetikDock.exe`. It starts with one empty group: drop some shortcuts on it.
3. Right-click a group for Add, Columns, Roll up, New group and the style settings. Right-click a shortcut for its own menu.

Settings, including the groups, are kept in `HKCU\Software\KinetikDock`. The app makes no network connections: it only opens what you click.

## Requirements

Windows 10 or 11 (64-bit) with .NET Framework 4.8, which ships with both.

## Building from source

No SDK needed: `build.bat` compiles `KinetikDock.cs` with the .NET Framework compiler that ships with Windows. `build.bat package` also makes `KinetikDock.zip` for a release.

`KinetikDock.exe --demo` runs a separate copy with made-up groups that reads and saves no settings. Add `--theme Light` (or any theme name) to pick its theme, and `--shot file.png` to draw the groups into a PNG and quit, which is how the screenshots were made.

## License

[GPL-3.0-or-later](LICENSE). Copyright © 2026 Akila Sella Hennedige.

## Local maintenance build 1.0.1

Includes the approved 16 October 2026 diagnostics fixes. Cross-compilation and regression results are recorded in the Apps diagnostics report; Windows GUI and hardware validation remains pending. Run `python tests/run-tests.py` for non-GUI regressions, and `build.bat package` to compile and create a hash-verified release package. Shared support is compiled into the EXE; no additional runtime DLL is required.
