# Modernization verification

Snapshot: **2026-10-04**, Europe/Copenhagen. Local preview on `feature/modern-vibrance`, based on upstream/fork master `919a9f2`. The repository was clean before this work. `origin` now points to `SteffenCarlsen/vibranceGUI`; `upstream` retains `juv/vibranceGUI`.

## Build and portable distribution

`dotnet build vibrance.GUI.sln -c Release` passed with **zero warnings and zero errors**, using stable SDK **10.0.401**. `global.json` excludes preview SDKs. The migrated SDK project targets `net10.0-windows`; publishing uses `win-x64`, self-contained/single-file deployment, embedded debug symbols, bundled native runtime dependencies, and no trimming. Legacy Fody, unused service-locator references, old ADL bindings, and the bundled x86 NVIDIA wrapper have been removed.

`scripts/publish.ps1` produces one distributable `artifacts/win-x64/vibrance.GUI.exe`, approximately **111.4 MiB**. The executable reports **.NET 10.0.12**. No release has been published and no GitHub Actions run has been claimed as passing.

The published executable ran `--diagnostics` successfully with `DOTNET_ROOT` and `DOTNET_ROOT_X64` pointed at an empty task-local directory. Those environment variables alone are not definitive evidence: .NET 7+ ignores the old `DOTNET_MULTILEVEL_LOOKUP` switch. A second launch with **.NET 10 host tracing** established the actual deployment path: the host detected a single-file bundle, read its embedded runtime configuration, reported execution as a **self-contained app**, and used its **internal hostpolicy 10.0.12**. Independent inspection found **254 bundle entries**, `includedFrameworks` for .NETCore/WindowsDesktop 10.0.12 without a requested shared framework, both runtime packs, and System.Private.CoreLib. Native PE exports matched Microsoft's [static single-file host](https://github.com/dotnet/runtime/blob/v10.0.0/src/native/corehost/apphost/static/CMakeLists.txt#L116), which links CoreCLR directly. This supports the runtime-bundled distribution claim without uninstalling or altering the user's installed runtimes. Host trace and diagnostic JSON are local artifacts; they are not a clean-VM test of every Windows prerequisite.

Primary deployment references: [single-file publishing](https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/overview), [host tracing environment variables](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-environment-variables).

## Automated regression checks

`dotnet run --project checks/vibrance.GUI.Checks.csproj -c Release` passed. Checks execute production settings code and the production shared controller with fake driver/mode functions; they do not reproduce the implementation in a separate model.

1. Native structure sizes and NVIDIA legacy/native scale endpoints; no placeholder writes during construction/settings configuration; apply a known saved desktop level only after activation.
2. Primary/all-display scope, known executable-path collisions, process-name fallback when Windows denies the image query, duplicate foreground events, cross-monitor restore, pause/resume, paused edits, scope changes, and removing the last profile.
3. Failed color restores retained for retry, including scope changes; exit releases backend resources.
4. Vibrance-only profiles call no resolution APIs; game-to-game transition restores an owned resolution; the global opt-out restores an already changed mode. A rejected mode test with a concurrent external/game modeset does not acquire restore ownership. Partial apply and failed resolution restoration retain their baseline for retry.
5. Unicode settings directory, independent malformed-field recovery, valid levels below AMD neutral, bounds, duplicate/invalid profiles, atomic profile replacement and readable `.bak`, preservation of unreadable XML before an explicit edit, and consistent case-insensitive equality/hashing.

Native foreground event delivery, actual exclusive-fullscreen games, physical shortcut delivery in games, autostart after login, and live resolution modesets are separate acceptance cases. No display resolution was changed for these tests.

## Local GPU evidence

Read-only Windows inventory found:

| Adapter | Installed driver | Relevant result |
| --- | --- | --- |
| NVIDIA GeForce RTX 3080 | `32.0.16.1714` (617.14) | Four attached outputs enumerate and expose digital vibrance. |
| AMD Radeon(TM) Graphics | `32.0.11024.2` | Driver remains installed. No AMD-owned attached saturation-capable output was found in this setup. |

The published executable's diagnostic run selected **NVIDIA** while both vendor drivers were installed. It reported four NVIDIA outputs and an informative AMD capability result. This verifies the local dual-driver initialization path; installed AMD chipset/iGPU drivers are not a reason to refuse launch.

The opt-in NVIDIA smoke check used the production native backend. For each of `DISPLAY1` through `DISPLAY4`, it captured native digital vibrance **50**, applied **51**, read back **51**, restored **50** in `finally`, and read back **50**. All native status results were **0** and all four checks passed. The restore used the exact captured native value, not a rounded UI percentage. The smoke test did not load user profiles, start foreground monitoring, or change resolutions.

This is driver write/readback evidence on this machine. It does not establish perceived color change, actual game switching, absence of frametime spikes, HDR correctness, Reference Mode behavior, another GPU model, or AMD saturation writes. NVIDIA DVC itself remains a private driver interface; unsupported versions are reported as a capability failure.

## UI evidence

Dark and Light main/profile/process-picker previews were rendered and visually inspected. The harness uses a fake GPU backend, static process rows, and an explicit theme; it does not read or save the user's preferences, register hotkeys, touch autostart, or start monitoring. It creates its own windows outside the desktop without activation/taskbar entries solely to initialize the native controls for bitmap rendering.

The default main window shows complete settings, buttons, both sample profiles, status, and attribution. At the allowed minimum size, buttons and profiles remain visible with vertical scrolling to the remaining content. The profile resolution selector and save/cancel controls fit. Theme and saved resolution selections are populated directly rather than reset by delayed data binding. Both themes were inspected across all three windows.

The live-theme follow-up passed `dotnet run --project checks/vibrance.GUI.Checks.csproj -c Release --no-build -- --theme-switch artifacts/theme-switch` with a zero-warning/zero-error Release build. It exercises the actual appearance dropdown through Dark, Light, Dark, System, and Light in **one process**, with the main window, profile editor, and process picker already open. Their managed controls and top-level HWNDs remain identical, the selected game and unsaved desktop/profile/resolution values remain intact, and the fake backend records **zero GPU or monitoring lifecycle calls**. Rendered slider pixels verify that native background brushes change in both directions; newly created Dark windows and profile editors also pass. The inactive selected row matches a fresh native Dark window on this OS.

The harness also passes a queued second choice during `SetColorMode` message processing, background-thread system appearance notification dispatch onto the UI thread, a main window created hidden as for minimized autostart, and disposal before a queued refresh executes. It reproduced and caught the notification race that could overwrite a queued manual choice; the latest pending choice now takes precedence. The user's appearance file SHA-256 remained unchanged throughout these checks. The workflow now runs these checks alongside the existing regression suite.

Appearance preferences apply immediately to existing windows and the tray menu. System preference notifications refresh the current choice without saving settings or restarting monitoring. The local OS is **Windows 10 IoT Enterprise LTSC, build 19044**. At this earlier revision, explicit Dark client controls visibly rendered on that machine, but System still delegated to .NET's native detection, which requires Windows 11. **That System limitation is superseded by the follow-up below.** Actual OS light/dark transitions and entering/leaving high contrast were not performed: the notification dispatch and disposal checks use the production refresh helper without modifying Windows preferences. The bitmap renderer does not establish the live native title bar, Open File dialog, every Windows version, or every DPI configuration.

Local preview files: `artifacts/ui-dark.png`, `artifacts/ui-light.png`, corresponding `-minimum`, `-profile`, and `-processes` variants. Layout measurements accompany the renders as `.layout.json`.

Live-switch render evidence is in `artifacts/theme-switch/`. The updated self-contained executable is published separately to `artifacts/live-theme/vibrance.GUI.exe`, preserving the earlier running `artifacts/win-x64` preview. Replacing the old running version requires starting the updated executable once; subsequent appearance changes require no restart.

## Configurable pause shortcut

The shortcut follow-up passed a Release build with **zero warnings and errors**, the complete existing regression suite plus `HotkeyChecks`, `--hotkey-ui artifacts/hotkey-ui`, and the existing live-theme suite. The model checks exercise production shortcut validation, legacy JSON default migration, custom preference roundtrip, failed saves, native registration ownership using fake APIs, collision preservation, spare-ID rebinding, rollback failures, stale message rejection, already-absent registration cleanup, and destroyed-window rebinding. Saved shortcuts default to Ctrl+Alt+V when the new field is missing or invalid.

The editor checks execute the capture textbox's actual keydown path and actual Save/Cancel buttons in task-owned windows outside the desktop without activation. Invalid input retains the last valid chord; collision feedback keeps the dialog open and the current shortcut label unchanged; retry applies the new label immediately; Cancel leaves the current shortcut unchanged. Light/Dark main windows, minimum-size scrolling, capture, and multiline collision feedback were rendered and visually inspected. The main HWND remains unchanged and the fake backend receives zero GPU/monitoring lifecycle calls. User appearance preferences retained their original SHA-256.

`--hotkey-native` also passed against **real RegisterHotKey/UnregisterHotKey** on two hidden task-owned windows. It reserved an unused Ctrl+Alt+Shift function-key chord, verified a competing registration failed, rebound through a spare ID, verified the original chord became available, rejected a constructed stale message, and released both registrations in `finally`. No physical keyboard input was injected. This verifies registration/rebinding/collision handling, not physical keyboard delivery while a protected or exclusive-fullscreen game has focus.

The previous shortcut remains reserved while recording; its matching WM_HOTKEY is routed into capture rather than pause. If preference saving fails after successful registration, the UI explicitly reports that the new shortcut remains active for this session. All subsequent saves and HWND rebinding use that actual session choice. The updated self-contained build is published to `artifacts/hotkey-config/vibrance.GUI.exe`; the running earlier preview is preserved.

## System appearance on Windows 10 and 11

System now reads the current user's Windows apps preference (`AppsUseLightTheme`) without modifying it, then requests explicit Dark or Classic while retaining **System** as the selected preference. This avoids .NET's Windows 11 gate on native System detection. Manual Light/Dark choices remain independent of that setting; high contrast takes precedence and retains the Windows system palette. Missing, unreadable, or unexpected settings fall back to Light.

A new cold-start assertion compares the selected color mode with an independent read of the Windows apps preference **before creating any forms**. It failed before this fix on the local Windows 10 machine, whose apps preference is Dark (`AppsUseLightTheme = 0`). After the fix, a Release build passed with **zero warnings and zero errors**, and `--render System artifacts/system-theme/startup.png` passed. This verifies System startup in the task-owned fake-backend preview, rather than relying on a preceding manual Dark selection.

Resolver regression checks cover synthetic Dark/Light/default values, manual overrides, and high-contrast precedence. The existing live-switch harness also derives its System expectation independently from the Windows preference. Current Windows 10 startup renders are in `artifacts/system-theme/startup*.png`; current-preference live-switch renders are in `artifacts/system-theme/switch/`.

These checks do not change Windows appearance, write user appearance/profile preferences, or call the GPU/monitoring backend. They do not establish actual OS Light/Dark or high-contrast transitions, startup of the user's live app, native title-bar/system-dialog appearance, or Windows 11 runtime behavior. Those remain separate from the cold-start and existing-control render evidence.

## Dropdown readability follow-up

The expanded appearance and resolution dropdowns exposed a gap in the earlier closed-control renders: native popup text and background could retain different themes. Both use a standard WinForms owner-drawn DropDownList now. Every row draws its background and text from the current palette; dark highlighted rows use white text, disabled rows use Window/GrayText without a selection highlight, and high contrast retains system colors. Native item strings, keyboard selection, accessibility, font/DPI item sizing, and focus cues remain in place. Explicit RGB brush refresh handles existing controls after a theme change.

`--dropdowns artifacts/dropdowns` passed with a zero-warning/zero-error Release build and the full existing regression suite. It creates fresh Light/Dark/System windows within one process and repeatedly selects Light, Dark, and System on the existing main window. The **39 native popup captures** cover both dropdowns, highlighted and ordinary rows, and disabled resolution rows. Enabled text/background pairs meet a measured 4.45 minimum contrast; native glyph pixels and row backgrounds are checked, while disabled rows retain the system disabled palette. HWNDs, selected values and unsaved resolution remain unchanged; the fake backend receives zero GPU or monitoring lifecycle calls.

The probe obtains the actual ComboLBox HWND, guards that it belongs to the test process and is hidden, positions only that window outside the desktop without activation or showing it, and sends WM_PRINT into an owned bitmap. Actual native DrawItem callbacks prove the popup painting path was exercised. It restores bounds and verifies visibility/control state in finally. PNGs and callback evidence are saved under `artifacts/dropdowns/` and were visually inspected. It does not move the user's pointer, inject keys, open their dropdowns, or establish visible hover/scroll/compositor behavior.

## Repository links and About

Main-window Twitter/follow and donation prompts were replaced with a compact GitHub link to `SteffenCarlsen/vibranceGUI` and an About button. The tray also exposes the current project and About. About identifies the fork as this application's upstream repository, credits juv / juvlarN and the original `juv/vibranceGUI` project, retains the exact original PayPal support link, and credits the original AMD implementation by juRiiir3. Assembly repository metadata and README point to the fork; source copyright notices and Git history/remotes are retained.

The Release build passed with zero warnings/errors, the existing regression and live-theme suites passed, and main/About Light/Dark renders at default/minimum sizes were inspected under `artifacts/branding/`. About displays the application's assembly version, including its source revision, rather than the test host's version. Render previews inject a callback that would fail if any browser were opened automatically; no support/repository destination was launched during rendering. Link targets and click handlers were checked in source.

## Visual redesign and compact dialogs — 2026-10-04

The native WinForms layout now uses lighter section headings and separators, a thin spectrum accent at the wordmark, restrained blue Add/Save actions, and quieter supporting text. Sections retain GroupBox accessibility and use the native outline in high contrast. Secondary and disabled buttons retain native theme painting; custom primary colors fall back to the Windows high-contrast palette. The existing controls, icons, single-row footer, autostart tooltip, and single shortcut instruction are preserved.

The shortcut editor keeps a usable explicit width and fits its height to content. Empty feedback no longer reserves space; errors wrap at the actual content width and grow the same window, then collapse after valid capture. About also fits its content. Height is bounded by the current screen's working area, with scrolling for exceptional content and an upward position correction for overflowing onscreen windows. Off-desktop previews remain off the desktop.

The Release build passed with zero warnings/errors. Existing production regression, live/reentrant theme switching, 39 native dropdown cases, System startup, and expanded shortcut UI checks passed. Light/Dark main, profile, process-picker, About, and compact shortcut renders were inspected under `artifacts/modern-ui/`. Shortcut checks cover actual minimum width, complete multiline collision/save-failure text, visible actions, growth/shrinkage, and preserved controls, HWND, captured key, and session binding. At the local 96 DPI, the Dark shortcut editor's initial client area is 440 × 177 pixels, compared with the previous 480 × 300 pixels.

The shortcut suite additionally uses WinForms autoscaling and larger text for a labeled synthetic 150% layout stress check. Its PNG/JSON evidence records the real `DeviceDpi` separately; this does not verify moving the application between monitors with different scaling or actual `WM_DPICHANGED` delivery. Visual checks use fake backends and task-owned off-desktop windows, without user settings, physical key injection, or GPU writes. Native high-contrast appearance and real monitor DPI transitions still require their own live checks.

An alignment follow-up removes the main sections' extra horizontal inset, shares the header/list/footer margins, right-aligns Pause and the percentage, and centers appearance/footer controls using layout anchors rather than fixed pixel nudges. Section headings use normal text-renderer padding and respect the section's content inset. Light/Dark normal/minimum and profile renders were inspected under `artifacts/aligned-ui/`; the build, existing live-theme suite, and native dropdown checks passed. Minimum-size wrapping and scrolling remain intact.

The wordmark and subtitle also suppress native font-overhang padding so their visible left edges match the spectrum accent. The two captions retain Label sizing/accessibility, theme roles, normal background painting, and graphics clipping/translation. About uses a plain label in the main window and tray. The Release build passed with zero warnings/errors, Light/Dark normal/minimum renders were inspected under `artifacts/header-alignment/`, and the existing live/reentrant theme-switch checks passed. These are task-owned previews at the local DPI, not physical mixed-DPI monitor checks.

## Focused local commit history — 2026-10-04

The unpublished modernization history was reorganized into focused commits from original baseline `919a9f2`. The previous 17-commit development history is preserved locally as `backup/2026-10-04-before-commit-isolation`. Commit bodies explain the user-visible behavior, original-repository issue/PR relationships and applicable validation. Fully qualified links use related/reimplementation wording; they do not close original issues or claim hardware certification.

Each of the 15 code/build revisions was built in Release in a task-owned worktree. Dependency-sensitive activation keeps both proxy replacements, foreground hook/timer lifetime, guarded logging and UI-thread configuration-before-start together. At the final code revision, every production, test, build and workflow blob matches the previously running source revision `9c5e3f4`; the final documentation commit adds the upstream changelog references and commit index.

Production settings/controller/shortcut checks, live theme switching, System startup, 39 native dropdown cases, shortcut layout checks and Light/Dark normal/minimum renders passed at the relevant revisions. The final code build has zero warnings/errors. This history work did not repeat GPU writes or physical monitor modesets, restart the user's application, push a branch, or run remote CI.

## Immediate title-bar refresh — 2026-10-04

The reported live Light/Dark switch updated client controls while leaving the native title bar in its previous appearance. The theme path already set the DWM dark-caption attribute, but its final `Control.Invalidate` only invalidated client painting. After a successful DWM update, the app now calls `RedrawWindow` with `RDW_INVALIDATE | RDW_FRAME | RDW_UPDATENOW | RDW_NOCHILDREN`. This requests synchronous non-client painting without recreating, activating, moving or resizing the window. The borrowed HWND remains owned by the form. [Microsoft's frame repaint documentation](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-redrawwindow).

The existing live-theme suite now attaches task-owned native window observers to the main, profile and process-picker previews. It checks completed `WM_NCPAINT` processing before `DoEvents`, delays or rendering across six repeated Light/Dark/System passes, including the already-selected Light mode. The check failed before the fix with `PreviewWindow did not repaint its non-client frame synchronously for Light`; after the fix, all windows processed one or two synchronous frame-paint messages per pass. HWNDs, client/window bounds, foreground and focus remained unchanged. Evidence is in `artifacts/caption-refresh/after/caption-refresh.json`.

The Release build passed with zero warnings/errors, and production regression, complete live/reentrant theme switching and 39 native dropdown checks passed. Preview tests used fake backends, no user settings or physical input, and zero GPU/monitor lifecycle calls. This establishes native frame-paint delivery, not physical compositor pixel timing or certification of other Windows/high-contrast configurations.

## Caption-width spectrum accent — 2026-10-04

The header caption column now uses native automatic sizing and the spectrum band fills that column. Its width follows the wider title/subtitle rather than a fixed 168 pixels; the remaining column keeps Pause aligned to the right. Font/DPI layout uses the existing native controls without manual text measurements or early HWND creation. At the local DPI, the band is 218 pixels wide. The zero-warning/error Release build and safe Light/Dark normal/minimum renders passed under `artifacts/ui-polish-followup/`; the header text is complete and its visible left edge remains aligned. The user's app was not restarted.

## Consistent About body colors — 2026-10-04

The About version and AMD attribution now inherit the form's ordinary text color, matching the original-creator text through Light/Dark changes. Links keep their existing link palette and the title stays bold; muted subtitles elsewhere retain their styling. The zero-warning/error Release build and safe Light/Dark About renders passed under `artifacts/ui-polish-followup/`, showing white body text in Dark and black body text in Light. The user's app was not restarted.

## Automatic regular releases — 2026-10-04

The initial release automation used application version **3.0.0**. Successful pushes to `master` in `SteffenCarlsen/vibranceGUI` publish regular releases after the existing Windows build/regression/UI checks and a new read-only startup check of the portable executable. The release job downloads that verified artifact instead of rebuilding it. Pull requests and manual dispatch retain read-only build permissions; only the gated release job grants `contents: write`.

The release script checks checkout identity, the executable's version/source revision, and any existing remote tag before publication. It uploads the executable and matching SHA-256 file into a draft, preserves completed published assets on reruns, and resumes incomplete drafts. Tags contain the full source commit as build metadata. Notes include the detailed commit bodies since the nearest completed ancestor release, retaining relevant original issue/PR links. A shared release-job concurrency group uses `queue: max` to serialize publication while retaining up to 100 waiting jobs. Only the current `master` tip is marked Latest; older builds can finish without replacing it, and a completed current-master release can reconcile Latest on rerun without uploading or editing its assets or notes.

The Release build passed with zero warnings/errors, along with production regression and complete live/reentrant theme-switch checks. The task-specific self-contained executable completed `--diagnostics` with `ReadOnly: true`, `ProcessArchitecture: X64`, and product version `3.0.0+72f319c6c31f2e769706389058ad17f04013edb1`. PowerShell parsing passed. Actionlint 1.7.12 passed with only its unknown `concurrency.queue` field diagnostic excluded: that version does not recognize `queue`, whose `max` syntax and pending-job limit were checked against [GitHub's current documentation](https://docs.github.com/en/actions/how-tos/write-workflows/choose-when-workflows-run/control-workflow-concurrency). Eleven offline CLI fixtures exercised new publication, completed-release asset preservation and Latest reconciliation, draft resumption, older-build Latest handling, nearest-ancestor changelog selection, wrong-tag and wrong-binary rejection, and failures during asset upload, release-history lookup, or current-master lookup. They used the actual script, compiled executable, checksum and local Git history while mocking GitHub and remote tag reads.

No GitHub release, remote tag, push, or workflow run was created during local validation. The fixtures establish local control flow and asset/source checks, not live GitHub publication or hosted-runner behavior. The workflow takes effect after it reaches `master`. The user's running application and settings were not changed or restarted.

The subsequent version follow-up set the application version to **3.0.0.0**, matching the assembly and file versions. Its Release build passed with zero warnings/errors; the compiled DLL reports `FileVersion: 3.0.0.0` and `ProductVersion: 3.0.0.0+f63391f8574eba4d6411d8a48b7aefff02b56f34`. Source inspection confirmed the existing workflow and release script accept the four-component version. The app was not restarted.

## Autostart adapter preservation — 2026-10-04

The adapter review on [fork PR #1](https://github.com/SteffenCarlsen/vibranceGUI/pull/1#discussion_r4179161300) identified that Run registration and path refresh discarded an explicit vendor override. Normal startup now keeps the explicit choice separate from the automatically selected backend and passes it to both registration paths. A supported existing Run command retains its override during path repair when the current launch has none; automatic entries remain automatic. Windows' command-line parser handles quoted and Unicode paths, with its allocation owned by a `SafeHandle` and released using `LocalFree`.

The zero-warning/error Release build, production regression suite and existing live/reentrant theme-switch checks passed. `StartupChecks` executes the production parser/builder for automatic startup, both explicit vendors, unchanged/moved paths, quoting, Unicode, current-override precedence, and malformed/duplicate/unknown arguments. The tests use supplied command strings and do not write the user's Run key or settings. Sign-in execution remains a separate live acceptance case. The running application was not restarted; previously overwritten profiles are not recovered by this fix.

## Remaining compatibility limits

The [exhaustive upstream review](UPSTREAM_REVIEW.md) maps all **6 open PRs and 31 open issues** to implemented safeguards, deferred features, and required hardware checks. Source fixes are not blanket resolution of those reporters' machines.

1. Actual foreground switching and exit in the user's games, especially protected/exclusive-fullscreen cases, remains untested live.
2. Other modern NVIDIA GPUs/drivers, AMD-only displays and Vulkan, laptop external/internal routing, and mixed-vendor output control require their own acceptance. Only one vendor is controlled per session.
3. HDR/SDR profiles and gamma/brightness/contrast expansion remain deferred. No calibration or HDR support is claimed.
4. Newly attached outputs/GPUs require restarting to refresh the supported output inventory. Existing display caches are invalidated on display changes; requested mode support is rechecked, but the profile dropdown is a startup snapshot.
5. eGPU unplug/replug, dual-mode monitors, real resolution failures, physical shortcut delivery in games, autostart login, native window/dialog theme behavior, and driver frametime performance are not certified by these checks.
