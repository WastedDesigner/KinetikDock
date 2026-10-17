# Kinetik Dock 1.2 performance and verification

Measured locally on 17 October 2026. These are reproducible development checks, not a promise about every PC or a full Windows compatibility certification.

## Rendering stress test

The same harness was compiled with the original repository version 1.1.0 and with 1.2.0. Both used Framework 4.8/x64, the default theme, four columns and 1,000 generated URL shortcuts in one group. After initial shell work settled, it rendered the group once to warm its caches and measured ten subsequent complete group redraws. No GC or working-set trimming was forced.

Three consecutive runs per build:

| Metric | 1.1.0 | 1.2.0 |
|---|---:|---:|
| Ten redraws, milliseconds | 1191 / 1203 / 1305 | 29 / 31 / 29 |
| Median time for ten redraws | 1203 ms | 29 ms |
| Drawing surface | 356 × 20,576 px | 356 × 548 px |
| Private bytes, median | 131,784,704 | 41,414,656 |
| Working-set bytes, median | 178,716,672 | 62,238,720 |

For this particular stress test, median redraw time fell about **97.6%** and private memory about **68.6%**. The main reason is a bounded scrolling viewport: only the visible rows are drawn, and the application no longer keeps an extremely tall group bitmap and matching shadow/text layers. Reused fonts also reduce allocation work.

This compares the original auto-growing behaviour with the new scrolling behaviour. It is not an isolated renderer microbenchmark, an input-to-photon latency test, an application-launch benchmark, or a claim that small ordinary layouts use 68.6% less RAM. Timing and memory vary with system load, DPI, theme, icon sizes and content.

Reproduce with `powershell -File tests/run-benchmark.ps1`. To compare an older source tree, pass `-SourceDirectory <path>`. The harness uses demo mode, does not modify saved settings and briefly displays generated test windows.

## Default-demo idle check

The old default three-group demo used 0 measured CPU milliseconds during a ten-second settled interval, about 35.88 MB private memory, 53.61 MB working set, 495 handles and 14 threads. Both builds are event-driven; idle CPU was already low, so rendering savings should not be presented as a dramatic idle-CPU improvement.

The final 1.2 default-demo check measured 0 CPU milliseconds over the same ten-second settled interval, 33.00 MB private memory, 51.20 MB working set, 475 handles and 19 threads. These are individual snapshots; thread count can vary with Framework/shell activity, and it did not decrease in this sample. Near-zero measured CPU in both versions means no claim of improved idle CPU can be made from this check.

## Verification coverage

- Existing shared regression suite: 42 checks.
- New Dock integration suite: 57 checks, plus one fixture-loading check when generating the showcase screenshot. Covers migration defaults and stable IDs; nearest snapping/hysteresis; large-group viewport, scrolling and hit testing; selective/batched rendering; accessible item names/bounds; multi-reference reordering/copy/move; routing rules and duplicate prevention; offscreen placement recovery; collapse; tabs/workspaces; surface release; quick access; undo/redo; asynchronous portal enumeration, watchers and reconnect; filters and navigation history; serialization; cache bounds; save debouncing; idle rendering.
- Release build and SHA-256 package verification passed. Demo startup and graceful shutdown passed. Default dark/light screenshots and a portal/tab/list-view showcase were generated and visually inspected.

The integration suite uses generated URLs and temporary local folders. It does not exercise live user files, real external app launches, every third-party shell extension, actual network outages, cloud placeholder hydration, all screen readers, or a physical mixed-DPI monitor reconnect. Those need manual checks on the relevant setup. No claim of universal Fences feature parity is made.
