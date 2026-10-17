# KinetikDock 1.2.0

Adds live folder portals, tabbed groups, named workspaces, quick-access and search hotkeys, multi-item reference moves/copies, keyboard selection, accessibility objects, desktop routing rules with a review screen, layout undo/redo and JSON snapshots.

Groups now resize and scroll, offer per-group appearance and locking, and use nearest-edge snapping with hysteresis. Missing-monitor recovery preserves saved positions. Portal navigation supports filtering, sorting and back/forward history.

Rendering is limited to visible rows. Icon/path completions refresh affected groups in batches; caches are bounded, saves are debounced, shell launches run off the UI thread, and inactive tabs/workspaces release drawing surfaces and folder watchers. A lightweight appearance option disables shadows.

Existing layouts migrate to the Main workspace and inherit the existing global appearance settings. Desktop organisation is opt-in and adds references without moving source files. See README for shortcuts and limits, and PERFORMANCE_1.2.md for measured results.

## Earlier release: 1.1.0

Adds three terminal themes, the same green, amber and yellow phosphor colours as Kinetik's dashboard and Kinetik Screen: **Terminal**, **Amber terminal** and **Yellow terminal**. They draw in a monospace font (Cascadia Mono, or Consolas) unless you've picked a font, and their divider lines glow in the theme's colour. Pick one from the theme list, or from Kinetik Hub's Theme for all.
