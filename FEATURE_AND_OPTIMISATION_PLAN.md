# Kinetik Dock: Fences-style feature and optimisation plan

Original proposal prepared 17 October 2026. Release coverage is recorded below; the remaining sections preserve the full candidate backlog.

## Implementation update: version 1.2.0

The proposal below remains the full candidate backlog. Version 1.2 implements the main desktop organisation workflow and its performance foundation:

| Area | Delivered in 1.2 |
|---|---|
| Performance | Affected-group refresh, 16 ms update coalescing during bursts, visible-row drawing, screen-bounded scrolling surfaces, a 24 MB / 2,048-entry icon cache, a bounded existence cache, queue-saturation retry, cached fonts, debounced/changed-value settings writes, background shell launches and inactive-surface cleanup |
| Groups | Edge/corner resize, adaptive columns, scrolling/scrollbar, compact/list views, per-group icons/labels/accent/lock, clickable collapse chevron and nearest-edge snapping with hysteresis and Alt bypass |
| Organisation | Tab combination/switching/overflow/detach, named workspaces and per-group workspace moves |
| Access | Quick-access and search hotkeys, show/hide groups, keyboard selection/navigation, tooltips, basic accessible shortcut objects and visible keyboard focus |
| Editing and recovery | Stable item IDs, multi-select drag/copy/move, selected-reference removal, session layout undo/redo, additive JSON import/export snapshots and missing-monitor recovery that preserves intended positions |
| Folder portals | Background enumeration, active-folder watchers, progressive availability/error state, folder navigation, up/home/back/forward, filename filters, name/type/date/size sorting, hidden-file option, Explorer access and offline/reconnect handling |
| Automatic organisation | Review screen, editable ordered filename/folder rules, checked-reference import, optional new-desktop-item watching, duplicate prevention and preservation of originals |
| Configuration | Searchable behaviour settings, hotkey enable/disable, lightweight appearance, resource diagnostics, README/release notes and reproducible benchmark/integration harnesses |

Optional work still in the backlog includes: thumbnails; richer custom icons and independent text/panel opacity; hover/edge auto-hide and gestures; tab drag/reorder and automatic workspace switching; saved profiles for different monitor topologies; selection rectangles and alignment guides; physical file operations in portals; richer shell properties/argument editors; project launches; metadata/tag/history search; fully configurable hotkey combinations; portable settings migration; helper-process isolation for hung shell handlers; native desktop-icon management and Windows virtual-desktop binding. Some require Windows compatibility investigation rather than a straightforward UI change.

Mixed-DPI support and basic accessibility paths were added, but physical monitor reconnect and individual screen-reader compatibility still need manual testing. See PERFORMANCE_1.2.md for the measured stress-test results and validation limits. Existing settings are migrated on load; no live user layout was changed during development checks.

## Goal and scope

Make Kinetik Dock feel immediate, organise a busy desktop like Fences, and keep background CPU, memory and disk activity low. Preserve the portable Windows app and Kinetik appearance.

This is a broad candidate backlog, not a commitment to implement every idea. Optional features should consume resources only when enabled. Performance findings below are from source inspection, not runtime profiling; targets are provisional until a baseline is recorded.

## Existing foundation

The current app uses C#, Windows Forms, .NET Framework 4.8 and individually drawn layered windows. It already supports shortcut groups, apps/files/folders/URLs, drag-and-drop, reorder, moves between groups, dragging items out, roll-up, basic edge/group snapping, three icon sizes, labels, themes, scale, opacity, layers, global locking, sorting by name and backup/restore.

It already loads shell icons and checks target existence on background workers, uses bounded work queues, caches icons and existence results, and reuses drawing surfaces, shadows and fonts. These foundations should be refined rather than rebuilt without evidence.

Important distinction: a folder shortcut opens Explorer. A folder portal would display the folder's current contents inside the group. Current groups hold references; they do not own or relocate the original desktop files.

## 1. UI and interaction improvements

### Group layout

- Resize from edges and corners with a minimum size and optional aspect constraints.
- Fixed-height groups with scrolling instead of growing beyond the screen.
- Automatic columns that adapt to width; retain manual columns as an option.
- Compact, comfortable and touch-friendly spacing presets.
- Grid, compact list and optional details views.
- Per-group icon size, label visibility and spacing overrides.
- Adjustable title height, padding, border, corner radius and shadow.
- Alignment guides, consistent gaps and a configurable snap grid.
- Snap hysteresis so movement does not jitter between competing edges; modifier key to bypass snapping.
- Align/distribute selected groups and tidy a layout in one action.
- Screen-edge docking, optional auto-hide and a small reveal handle.
- Lock individual groups separately from the existing global lock.
- Explicit edit-layout mode with visible handles; clean appearance during ordinary use.
- Saved normal/expanded sizes so roll-up and resize behave predictably.

### Titles, controls and appearance

- Clickable collapse chevron, optional add/menu buttons and discoverable title tooltips.
- Rename a group inline; choose single-click or double-click title behaviour.
- Hover-to-expand with configurable delay and delayed collapse to prevent accidental closing.
- Collapse/expand all groups and optionally remember each group's state per workspace.
- Per-group colour/accent, optional small header icon and item count.
- Separate panel transparency from text/icon opacity for readability.
- Wallpaper-friendly subdued styling and an opaque high-contrast option.
- Optional icon tint/desaturation while retaining full-colour icons by default.
- Custom local icons for shortcuts and groups.
- Optional short transitions that respect reduced-motion settings; immediate interactions in lightweight mode.
- Consistent loading, empty, missing-drive, inaccessible-folder and error states.

### Item interaction

- Multi-select with Ctrl/Shift and an optional selection rectangle.
- Drag several items together with a clear count and destination preview.
- Clear insertion marker and drag ghost; auto-scroll while dragging near an edge.
- Hover a tab or collapsed title while dragging to reveal its contents.
- Explicit modifier behaviour for copying/moving references between groups.
- Configurable single-click/double-click launch.
- Tooltips with full names and paths; optional file metadata loaded on demand.
- Inline shortcut rename, copy path, open containing folder and Windows properties.
- Bulk move, remove, sort and repair references.
- Undo/redo for group edits, layout changes and shortcut removal.
- Duplicate detection with intentional duplicates allowed across groups.
- Distinguish shortcut labels from actual file names when editing.

### Settings and accessibility

- A searchable settings window instead of relying on deeply nested menus.
- Global defaults with clear per-group overrides and reset-to-default actions.
- A visible shortcut reference and hotkey conflict reporting.
- Keyboard navigation, Enter to launch, F2 to rename, Escape to dismiss and context-menu key support.
- Accessible names, focus indicators, high-contrast support and screen-reader semantics. Custom-drawn no-activate windows need deliberate accessibility work, not just extra key handlers.
- Correct scaling and pointer targets on mixed-DPI monitors.
- Live appearance preview with cancel/revert; simple and advanced settings sections.

## 2. Fences-style organisation features

### Folder portals

- Display a selected folder's live contents in a group.
- Browse into subfolders with breadcrumbs, back/forward/up and a home-folder action.
- Open the current folder in Explorer; choose whether a folder click navigates or opens Explorer.
- Sort by name, type, date or size; folders first and ascending/descending options.
- Include/exclude filters, optional hidden files and a clear filtered-item count.
- Support local folders, removable storage, network shares and locally available cloud folders.
- Show offline/loading status without blocking the desktop; manual reconnect/refresh.
- Offer reference-only drops first. Actual copy/move operations require explicit action labels and shell collision/progress handling.
- Optional thumbnails for visible items only; icons by default to limit memory and shell work.
- Watch only active folders, handle watcher overflow with a bounded rescan and avoid traversing entire folder trees by default.

### Tabs and workspaces

- Combine groups into a shared tabbed panel.
- Reorder tabs, drag out to detach, merge panels and choose the initial tab.
- Optional hover-to-switch tabs and keyboard tab navigation.
- Named workspaces such as Work, Personal and Gaming, each with its own groups and layout.
- Quick workspace switching and optional desktop-page navigation.
- Keep chosen groups visible across workspaces.
- Manual workspace rules first; optional app/time/display-triggered switching later.
- Windows virtual-desktop association as an exploratory feature, dependent on a reliable Windows integration path.

### Instant access and clean desktop

- A configurable global hotkey to temporarily raise groups above application windows.
- Hold-to-show or toggle mode; Escape/outside click restores the previous state and focus.
- Search all Dock items from the quick-access view, with instant local filtering.
- Show/hide all Dock groups and optionally restore their previous collapsed states.
- Optional desktop double-click gesture to hide/reveal groups.
- Optional reveal-at-screen-edge or mouse gesture with accidental-trigger protection.
- Full-screen/presentation behaviour: hide or suppress automatic reveals when requested.

### Automatic organisation

- An opt-in inbox group for newly discovered desktop shortcuts/files.
- Routing rules by extension, name pattern, source folder or item type.
- Preview a rule's matches before enabling it; show rule order and conflicts.
- Route references by default; physical file relocation is a separate explicit mode.
- Suggested groups for apps, documents, media and folders; accept or edit before applying.
- Keep manually placed items pinned so automatic rules do not reshuffle them.
- Exclusions, pause automation, undo and a small local activity history.
- Import current desktop shortcuts into Dock without deleting the originals.
- Optional management/hiding of native desktop icons as a later shell-integration feature with a reliable restore path.

## 3. Additional Kinetik-specific features

- Layout profiles for each monitor arrangement, including laptop-only and docked setups.
- Export/import individual groups and entire workspaces in a versioned human-readable format.
- Layout snapshots with preview and one-click rollback.
- Repair missing targets: browse to a replacement, relink a moved parent folder or remove stale references in bulk.
- Search by name, group, type and optional tags; maintain the index in memory from configured items.
- Pin favourites and optionally sort by local launch history; history collection is off by default.
- Recent items scoped to a group, with a clear-history control.
- Launch arguments, working directory and optional run-as-administrator action for suitable targets.
- Launch a selected set of apps as a project, with user-configured delay and missing-target handling.
- Templates for common group layouts and duplicate-group actions.
- Optional group hotkeys and numeric quick-launch shortcuts.
- Kinetik Hub integration for shared themes and opening Dock settings, consistent with existing suite behaviour.
- Local diagnostics view/export showing resource use, redraw activity, cache sizes and errors without a continuously running telemetry service.
- Optional command-line actions to reveal a group, select a workspace or import a layout.
- Optional portable settings mode alongside the existing registry mode, with migration and backup protection.

## 4. Performance work grounded in the current source

| Current observation | Proposed improvement | Expected effect / qualification |
|---|---|---|
| `DockWork.Initialize(RenderAll)` connects every published result to every group | Publish changed paths/item IDs, track affected groups and merge updates into one UI dispatch | Reduce startup and icon-loading redraw bursts; verify with render counts |
| Hover changes call a complete `Render()` | Separate layout, static artwork and interaction state; repaint only affected content where practical | Less CPU per hover; layered-window upload may still cover the full surface |
| `Render()` recalculates geometry and `PaintContent()` draws every item | Cache layout and add a fixed viewport with visible-row rendering | Bounded rendering and bitmap dimensions for large groups |
| Every render calls `ShowWindow` and layer-related positioning | Update visibility and z-order only when state changes, retaining shell recovery logic | Fewer redundant native calls; validate Show Desktop and focus behaviour |
| `ShellIcon` retains successful entries until a full clear | Add byte-budgeted LRU eviction, obsolete-size cleanup and deliberate invalidation | Prevent cache growth across many items/DPI sizes; budget must avoid reload churn |
| Target existence results remain in a path dictionary | Prune entries when no group references them; invalidate on relevant events | Bound long-session state and improve freshness |
| Background work is bounded but has no visibility priority or item cancellation | Prioritise visible items, cancel stale queued work and retry useful requests rejected by a full queue | Improve perceived loading and avoid wasted work |
| All shell icons share one STA worker | Keep safe shell threading; detect stalls and consider a restartable helper process if profiling shows shell hangs | Isolate uninterruptible shell handlers; extra process costs memory, so defer unless justified |
| `Paths()` can check filesystem state during drag-over | Parse the incoming payload once and validate asynchronously; revalidate at drop | Avoid repeated synchronous path checks during dragging |
| `Target.Open`/`Reveal` also perform synchronous filesystem work | Remove unnecessary preflight I/O or schedule it without blocking input | Improve response for unavailable/network paths; measure shell launch latency separately |
| Each save serializes all groups and writes all settings fields | Debounce bursts, track dirty data and persist an immutable snapshot safely | Reduce UI stalls and registry writes; flush pending changes on controlled exit |
| Startup retries settings reads using `Thread.Sleep` | Keep recovery protection but schedule retries without freezing a visible UI | Better response during exceptional settings failures; normal startup may be unaffected |
| Letter tiles create a font during painting; static content is rebuilt | Reuse suitable fonts/letter artwork and cache text measurement with explicit invalidation | Reduce allocations and GC pressure; keep caches bounded |
| Theme tiles each have a 15 ms timer | Inspect active animation lifetime; share a scheduler and stop activity when hidden or settled | Lower settings-window overhead if profiling confirms redundant ticks |

Additional performance candidates:

- Keep ordinary idle operation event-driven with no continuous redraw loop.
- Coalesce rendering to at most one update per affected group per frame during bursts; do not introduce a permanent frame timer.
- Skip visual updates for hidden groups and inactive tabs; refresh dirty state on reveal.
- Enumerate folder portals in batches on background workers with progressive results and cancellation.
- Share watchers for the same folder; debounce event bursts; release unused watchers.
- Use shell change notifications where appropriate rather than repeatedly probing every target.
- Apply longer retry backoff to offline network paths; avoid exhausting all workers with slow remote requests.
- Avoid cloud placeholder hydration for thumbnails, metadata or existence probes where Windows APIs permit.
- Cap thumbnail memory separately from icon memory; release large inactive rendering surfaces after a sensible delay.
- Reuse device contexts/bitmaps where size is unchanged; audit temporary images, menus, COM objects and event subscriptions for deterministic cleanup.
- Avoid scaling an already correctly sized icon with expensive interpolation when a direct draw produces equivalent output.
- Cache monitor/snap geometry during a drag instead of repeatedly querying/reconstructing it.
- Use arithmetic grid hit testing once scrolling/multi-selection make linear scans significant.
- Provide a lightweight appearance preset: opaque background, minimal shadows, icons only and no animation/thumbnails.
- Respect reduced motion and optionally battery saver for effects and background refresh.
- Handle session lock, suspend/resume, DPI changes and Explorer restarts without polling continuously.
- Record slow operations in a bounded diagnostic buffer, disabled or minimal during ordinary use.
- Consider renderer/runtime changes only after profiling: a framework migration or GPU renderer can increase complexity and memory without fixing the actual bottleneck.
- Avoid forcing garbage collections, repeatedly trimming the working set or raising process priority as a substitute for fixing allocation and work volume.

## 5. Reliability and desktop integration

- Restore logical placement after monitor removal/reconnection without overwriting the user's saved arrangement.
- Persist monitor identity plus DPI-independent placement, and recover groups that are only marginally on-screen.
- Test Show Desktop, Explorer restart, taskbar relocation, Remote Desktop and sleep/resume.
- Preserve focus when showing ordinary desktop groups; explicitly manage keyboard focus only in search/edit/quick-access modes.
- Investigate proper desktop hosting as a separate compatibility task. Current bottom-of-z-order windows are not equivalent to owning native Explorer desktop icons; undocumented shell hosting needs fallbacks and Windows-version testing.
- Version the expanded data model and migrate existing settings; preserve unreadable settings and a last-known-good backup.
- Define failure handling for disk/registry writes and make unsaved changes visible without blocking ordinary clicks.
- Add stable item IDs before tabs, undo and automatic rules so reordering cannot invalidate identities.
- Keep shortcut removal distinct from actual file deletion. If portal deletion is later offered, use explicit shell/Recycling Bin semantics.
- Ensure missing drives, denied access, corrupt shortcuts and icon-handler failures cannot freeze or remove saved items.

## 6. Measurement and acceptance criteria

Before changing performance behaviour, measure the same release build and machine with fixed datasets. Report private bytes, working set, CPU time, process thread count, GDI/USER handles, redraws, disk writes, cache bytes, queue depth and input-to-visible-update latency. Working set alone is not a complete memory measure.

Suggested scenarios:

1. Cold/warm launch with 10 groups and 200 local items; record time until the first interactive group and until visible icons settle.
2. Ten minutes idle, then repeated hover, drag, roll-up and quick-access interactions.
3. Large collection of 1,000 references, with most items outside the viewport or in inactive tabs.
4. Optional folder portals with a large folder, a burst of changes, an unavailable share and cloud placeholders.
5. Mixed 100/150/200% DPI displays, disconnect/reconnect, Explorer restart and repeated workspace switches.
6. An extended session repeatedly adding/removing groups and changing themes/icon sizes; watch for persistent cache/handle growth.

Provisional goals, not current results or guarantees:

- Ordinary local hover/collapse/tab visual response at the 95th percentile within 50 ms on the reference machine.
- Drag rendering generally stays within a 16.7 ms frame budget on a 60 Hz display; measure tail latency and input lag as well as averages.
- Idle CPU near the measurement floor, aiming below 0.1% averaged over a representative idle interval with the CPU accounting convention stated.
- No periodic settings writes while unchanged and no full-layout redraw for a single unrelated icon result.
- No filesystem/shell enumeration on painting or drag-over input paths.
- Memory plateaus after cache warm-up; choose explicit cache/surface budgets after baseline profiling, accounting for DPI and group count.
- Meaningful before/after CPU and memory comparisons use identical content and enabled features. Do not promise a fixed percentage reduction before measurement.
- Existing tests plus targeted interaction and recovery checks pass; add focused tests for new rules, migrations, undo and layout logic.

## 7. Recommended implementation order

| Stage | Scope | Relative effort | Completion gate |
|---|---|---|---|
| 1. Measure and remove avoidable work | Baseline, affected-group refresh, update batching, drag payload caching, cache budgets, save batching | Medium | Measured improvement without changing established interactions |
| 2. Core desktop feel | Resizable scrolling groups, cached layout, better snapping, clickable chevron, per-group lock/style, monitor recovery | Medium-large | Large groups remain responsive; mixed-DPI drag/resize verified |
| 3. Everyday access | Global quick access, local search, keyboard support, multi-select and undo | Large | Predictable focus, hotkeys and reversible edits |
| 4. Live organisation | Folder portals with bounded I/O, watcher lifecycle and offline states | Large | Large/offline folders cannot block the UI or grow resources indefinitely |
| 5. More organisation | Tabs, workspaces, snapshots and desktop-import/routing rules | Large | Existing layouts migrate and automation previews/undo work |
| 6. Optional extras | Thumbnails, tint, advanced gestures, project launches and deeper Explorer/virtual-desktop integration | Variable; some exploratory | Each feature earns its resource cost and passes compatibility checks |

Best first release: stages 1 and 2, followed by quick access/search. Folder portals and tabs provide the biggest later Fences-style expansion. They should build on scrolling, bounded caches and selective rendering first.

## Reference material

Source inspection: `KinetikDock.cs`, `SuiteSupport.cs`, `README.md`, build script and current tests. This was a planning review; no runtime benchmarks or application tests were performed for this document.

Official Fences references used to establish the comparison, not to imply feature parity:

- [Fences product overview](https://www.stardock.com/products/fences/)
- [Fences 6 announcement: tabs and Folder Portal navigation](https://www.stardock.com/news/535945/stardock-releases-fences-6)
- [Fences guided tour: creation, Folder Portals and Peek](https://www.stardock.com/blog/508751/fences-4--a-guided-tour)

All entries above are proposals for Kinetik Dock; some go beyond the comparison product. Desktop hooks, virtual-desktop integration and shell ownership need feasibility work before they can be promised.
