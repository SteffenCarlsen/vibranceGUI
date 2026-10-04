# vibranceGUI — SteffenCarlsen fork

**This repository is an independently maintained fork of [juv / juvlarN's original vibranceGUI](https://github.com/juv/vibranceGUI). It is not the original project or an original-project release.**

vibranceGUI switches NVIDIA digital vibrance or AMD saturation when a configured game is in the foreground, then restores your configured desktop level when you leave it. This fork modernizes the GPU integrations, runtime, interface, and profile handling while retaining the original application's purpose.

| Purpose | Link |
| --- | --- |
| Fork repository and project homepage | [SteffenCarlsen/vibranceGUI](https://github.com/SteffenCarlsen/vibranceGUI) |
| Contact, support, and bug reports for this fork | [Fork issues](https://github.com/SteffenCarlsen/vibranceGUI/issues) |
| Contributions to this fork | [Fork pull requests](https://github.com/SteffenCarlsen/vibranceGUI/pulls) |
| Original project and inherited history | [juv/vibranceGUI](https://github.com/juv/vibranceGUI) |

The current development version is **3.0.0**. The modernization is on `feature/modern-vibrance`. The [changelog](#changelog-since-the-original-project) records this fork's changes from original commit `919a9f2`; it does not relabel the original application's releases.

## What this fork changes

1. **GPU compatibility:** direct NVIDIA NVAPI and AMD ADL2 capability probing, including startup with NVIDIA and AMD graphics drivers installed together. Keep your chipset and integrated-graphics drivers installed.
2. **Portable .NET 10:** a self-contained Windows x64 executable with the .NET runtime included.
3. **Modern interface:** clearer sections, aligned margins, a restrained color accent, compact dialogs, and readable System / Light / Dark themes.
4. **Configurable pause hotkey:** optional pause/resume, custom shortcuts, immediate rebinding, and preservation of the previous binding when a new shortcut conflicts.
5. **More reliable automation:** foreground matching, scoped restoration, optional resolution changes, fewer redundant driver calls, and safer settings recovery.

## Requirements and builds

Supported target: **Windows 10/11 x64**, with an attached display whose GPU exposes NVIDIA digital vibrance or AMD saturation control. The published `vibrance.GUI.exe` bundles **.NET 10**; users do not need to install .NET separately.

Build the current version from this checkout using the [build instructions](#build-and-verify). Published builds of this fork belong on [this fork's Releases page](https://github.com/SteffenCarlsen/vibranceGUI/releases); workflow artifacts belong to [this fork's Actions](https://github.com/SteffenCarlsen/vibranceGUI/actions). The changelog's current 3.0.0 entry is unreleased until published from `master`.

The original project's website and historical contact links are listed under [attribution](#original-project-and-attribution). They are references to the original application, not the homepage, downloads, or support channels for this fork.

## Getting started

1. Close any other running copy of vibranceGUI and run `vibrance.GUI.exe`.
2. Set **Windows Vibrance Level** to the desktop level you want.
3. Use **Add** to select a running program or **Add manually** to select an executable.
4. Double-click a profile, or select it and choose **Edit**, to set its game vibrance and optional resolution.
5. Optionally enable autostart or the pause hotkey, and choose System, Light, or Dark appearance.

Minimizing hides the window in the tray. Pause restores the desktop settings; Resume reconciles the current foreground program. Exiting stops monitoring and restores the displays the app changed. Pause/Resume, Show, GitHub, About, and Exit are also available from the tray menu.

**GitHub** opens this fork's repository. **About** shows the application version and source revision, this fork's repository, original project/developer credits, and the original developer's support link.

## Profiles and monitor options

- A profile applies while its program is in the foreground. When Windows allows the executable path to be read, it must match the configured executable. If a protected process denies that query, matching falls back to the process name.
- **Affect Primary Monitor only** limits color changes to the primary monitor. Restoration tracks the displays previously changed, including when focus moves to another monitor or the scope changes.
- Resolution changes are optional per profile and target the foreground window's monitor independently of the color-only primary-monitor option. **Never change resolutions** disables them globally. A vibrance-only profile does not change resolution or refresh rate.
- Requested resolution modes are checked before applying. The app restores captured modes only for displays whose resolution it changed, and retains failed restoration operations for retry.
- The running-program picker loads asynchronously. Protected or inaccessible programs can be added manually; missing executables remain in saved profiles with a fallback icon.

## Appearance

**System** is the default when no appearance preference is saved. It follows the Windows **app appearance** setting at startup and while running, including Windows 10. Explicit Light and Dark choices retain your manual preference. Windows high contrast takes precedence and uses the system palette.

Changes apply to existing windows and the tray without restarting monitoring or discarding selected profiles and unsaved edits. Appearance and resolution dropdowns paint readable selected, ordinary, and disabled rows across theme changes. Native title bars and Windows file dialogs can look different on older Windows versions; detailed render and compatibility evidence is in [VERIFICATION.md](docs/VERIFICATION.md).

## Pause hotkey

The hotkey is opt-in. Enable **Enable pause hotkey**, click the shortcut button, press your preferred combination, and choose **Save**. The default is **Ctrl+Alt+V**.

1. Use Ctrl, Alt, or Shift with one key, or an unmodified F1–F24 key. F12 is reserved and cannot be used; Windows-key combinations are unsupported.
2. Tab/Shift+Tab navigate the editor; Esc or Cancel discards unapplied edits. It does not revert a shortcut already registered when a later preference save failed.
3. A successful change applies immediately. A conflicting shortcut leaves the previous binding active and the editor open.
4. Recording the current shortcut does not toggle pause. Repeated keydown messages do not repeatedly toggle automation.
5. If registration succeeds but preference saving fails, the editor reports that the new shortcut is active for this session and allows retrying the save.

The editor displays the shortcut instructions once. Feedback is reserved for errors; the compact window expands for wrapped error text and shrinks when it clears.

## Autostart

Autostart adds a value for the current executable to your Windows user's registry:

```text
HKCU\Software\Microsoft\Windows\CurrentVersion\Run
```

The command quotes the executable path and adds `-minimized`, so the app starts in the tray when you sign in. No administrator rights are required. Unchecking autostart removes the value. If you move the executable, starting it manually from the new location refreshes an existing autostart entry. The checkbox tooltip summarizes this behavior.

## GPU support and limitations

| Configuration | Behavior / verification boundary |
| --- | --- |
| NVIDIA display with AMD integrated graphics / both drivers installed | Probe attached, controllable outputs instead of rejecting both installed drivers. No driver removal is required. |
| NVIDIA GPU | Load the process-appropriate NVAPI supplied by the installed driver directly; the old bundled x86 `vibranceDLL.dll` is no longer used. Digital vibrance uses a private, driver-dependent interface, so unsupported driver versions can still fail capability checks. |
| AMD GPU | Use process-appropriate ADL2 and check display saturation capability. An installed driver alone does not establish support. |
| Laptop / hybrid GPU | The display must be connected to a GPU exposing the required color control. The discrete rendering GPU does not necessarily own the internal panel; an external monitor may have a different owner. |
| NVIDIA and AMD both own monitors | Automatic color-backend selection prefers supported NVIDIA outputs. `--adapter amd` selects AMD instead. Color control uses one vendor per session; other-vendor and unsupported outputs receive no color changes. Optional resolution profiles are independent. |
| Intel-only output | Intel saturation control is not implemented. |
| HDR / Reference Mode | A successful driver call does not establish a visible change. NVIDIA **Override to reference mode** can bypass color adjustments. HDR behavior and separate SDR/HDR profiles remain unverified. |
| New GPU / driver generation | Capability probing replaces model-name assumptions, but does not certify every GPU/driver combination. Unsupported calls are reported. |

Foreground hooks, periodic reconciliation, and change detection avoid redundant color writes on repeated foreground events and mouse clicks. Driver-dependent frametime issues still require measurement on the affected hardware.

The supported output inventory is captured at startup. **Restart after attaching a new output/GPU or changing GPU/display routing.** Existing caches are invalidated on display changes and requested mode support is rechecked, but the profile resolution dropdown is a startup snapshot.

Local verification covered an RTX 3080 with four attached outputs while the AMD integrated-graphics driver remained installed, including NVIDIA write/readback/restoration. AMD-only writes, other modern GPU generations, real laptop routing, protected/fullscreen games, physical hotkey delivery in games, real resolution failures, HDR, and monitor DPI transitions need their own live checks. See [verification evidence](docs/VERIFICATION.md).

## Command-line options and diagnostics

| Option | Purpose |
| --- | --- |
| `-minimized` | Start in the tray. |
| `--adapter nvidia` | Select NVIDIA explicitly for a normal run. |
| `--adapter amd` | Select AMD explicitly for a normal run. |
| `--diagnostics <file.json>` | Probe GPU APIs and attached displays, then write a read-only report. |

```powershell
.\vibrance.GUI.exe --diagnostics .\gpu-diagnostics.json
```

Diagnostics do **not** start monitoring, load profiles, change vibrance, or change resolution. The report can include monitor/device names; inspect it before sharing.

## Settings and recovery

| File | Purpose |
| --- | --- |
| `%APPDATA%\vibranceGUI\vibranceGUI.ini` | Desktop vibrance and monitor/resolution options. |
| `%APPDATA%\vibranceGUI\applicationData.xml` | Program profiles. |
| `%LOCALAPPDATA%\vibranceGUI\appearance.json` | Appearance, optional hotkey state, and saved shortcut. |

The fork retains the original settings paths for migration. The original app and this fork share those settings and a single-instance lock; close the other copy before starting.

Invalid desktop-option fields in the INI recover independently. Profile XML must deserialize as a file: malformed XML or an unreadable numeric field can prevent the profiles from loading and is reported. Parseable profiles have their levels bounded and invalid/duplicate entries handled. Unicode paths are supported. Profile saves serialize to a temporary file before replacing XML; the previous XML is retained as `applicationData.xml.bak`, and that previous file may itself be unreadable.

An unreadable profile file is reported and is not overwritten on ordinary close. Explicit edits first preserve it as a uniquely named `.corrupt` file. To recover a known-readable `.bak`: exit the app, preserve the unreadable file, and copy the backup to `applicationData.xml`. Save and restoration failures are reported rather than silently treated as success.

## Build and verify

Developers need Windows and the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0). `global.json` selects a stable .NET 10 SDK even if a newer preview is installed.

```powershell
dotnet build .\vibrance.GUI.sln -c Release
dotnet run --project .\checks\vibrance.GUI.Checks.csproj -c Release
dotnet run --project .\checks\vibrance.GUI.Checks.csproj -c Release --no-build -- --theme-switch .\artifacts\theme-switch
dotnet run --project .\checks\vibrance.GUI.Checks.csproj -c Release --no-build -- --hotkey-ui .\artifacts\hotkey-ui
dotnet run --project .\checks\vibrance.GUI.Checks.csproj -c Release --no-build -- --dropdowns .\artifacts\dropdowns
.\scripts\publish.ps1
```

The default publish output is `artifacts\win-x64\vibrance.GUI.exe`. Publishing bundles managed assemblies and native .NET runtime libraries; trimming is disabled for WinForms/XML serialization compatibility.

The [Windows GitHub Actions workflow](.github/workflows/build.yml) builds, runs regression/UI checks, publishes the portable executable, and verifies its read-only diagnostic startup on pull requests, `master` pushes, and manual dispatch. A successful push to this fork's `master` then publishes a **regular release** from that exact verified executable. Pull requests and manual runs only produce workflow artifacts. A push containing several commits releases the pushed branch tip once.

Each release includes `vibrance.GUI.exe`, `SHA256SUMS.txt`, and changelog notes containing the detailed commit descriptions and original issue/PR references since the nearest published ancestor release. Tags use the application version plus the full source commit, for example `v3.0.0+g<40-character-commit>`. The commit is build metadata rather than a prerelease suffix. About and the executable also include their source revision.

Uploads are completed in a draft before publication. Rerunning a failed job resumes its draft; rerunning a completed release keeps its published assets and can restore its Latest label if it still matches `master`. Release jobs publish one at a time, with up to 100 waiting jobs retained in [GitHub's concurrency queue](https://docs.github.com/en/actions/how-tos/write-workflows/choose-when-workflows-run/control-workflow-concurrency). Only a build matching the current `master` tip is marked Latest, so an older build finishing later cannot replace it. Release publication requires the workflow to be on `master`; local checks do not establish that a remote run has passed or a release exists.

Safe preview renders use fake backends, skip user settings, and never start monitoring:

```powershell
dotnet run --project .\checks\vibrance.GUI.Checks.csproj -c Release -- --render Dark .\artifacts\ui-dark.png
dotnet run --project .\checks\vibrance.GUI.Checks.csproj -c Release -- --render Light .\artifacts\ui-light.png
dotnet run --project .\checks\vibrance.GUI.Checks.csproj -c Release -- --render System .\artifacts\system-theme\startup.png
```

The opt-in native shortcut check reserves and releases unused combinations on task-owned hidden windows; it does not inject keyboard input or touch user settings:

```powershell
dotnet run --project .\checks\vibrance.GUI.Checks.csproj -c Release --no-build -- --hotkey-native
```

Expanded shortcut checks cover compact/default/minimum-width layouts, complete multiline errors, state preservation, and a labeled synthetic 150% scaling case. Synthetic scaling is separate from real monitor DPI transition evidence.

## Support and contributing

Use [this fork's issue tracker](https://github.com/SteffenCarlsen/vibranceGUI/issues) to contact the fork maintainer or report a problem, and [this fork's pull requests](https://github.com/SteffenCarlsen/vibranceGUI/pulls) for code contributions.

For a GPU/profile issue, include:

1. Your build version/source revision and Windows version.
2. GPU models, driver versions, which GPU owns the affected monitor, and HDR/Reference Mode state.
3. Steps to reproduce, expected behavior, and relevant profile/monitor/resolution options.
4. A reviewed diagnostic report when useful.

The [original PR and issue review](docs/UPSTREAM_REVIEW.md) records the **2026-10-04 snapshot** of six open PRs and 31 issues. GPU selection/restoration/resolution fixes and the pause-hotkey idea were reimplemented in this fork. The legacy .NET 4.8 migration was superseded by .NET 10; gamma, brightness, contrast, and automatic SDR/HDR profiles remain deferred. The review distinguishes source fixes from still-unverified reports on individual machines.

## Changelog since the original project

This changelog covers this fork's changes after [original commit `919a9f2`](https://github.com/juv/vibranceGUI/commit/919a9f2). Dates use Europe/Copenhagen. Development entries describe implemented changes, not published releases. Earlier original-project history remains in the inherited Git history and original repository.

### 2026-10-04 — 3.0.0 (unreleased)

#### Runtime and distribution

1. Migrated the original .NET Framework 4.0/x86 project to SDK-style .NET 10 WinForms, with Windows x64 as the supported target.
2. Added self-contained, single-file publishing with native runtime dependencies included and no separate .NET installation for users.
3. Removed legacy Fody/service-locator dependencies, obsolete ADL bindings, and the bundled NVIDIA wrapper.
4. Added portable publishing, stable SDK selection, Windows build/UI/regression workflow steps, and read-only GPU diagnostics.
5. Added automatic regular releases after successful `master` pushes, with the verified portable executable, SHA-256 checksum, source-specific tags and detailed commit changelogs; removed the application's preview version suffix.

The .NET migration supersedes the framework-only portion of original [PR #153](https://github.com/juv/vibranceGUI/pull/153). Runtime-bundled x64 publishing and the Windows checks are additional fork work.

#### GPU and restoration

1. Replaced driver-file detection with attached-output capability probing, allowing NVIDIA displays alongside installed AMD integrated-graphics drivers.
2. Added architecture-appropriate direct NVAPI/ADL2 integration, explicit vendor selection, and bounded/deduplicated display enumeration.
3. Loaded saved desktop settings before activation, preventing zero/default placeholder color writes during startup.
4. Tracked changed outputs and restored the configured desktop level on foreground transitions, pause, scope changes, last-profile removal, and exit.
5. Preserved failed color/restoration operations for retry and scoped profile color application to supported outputs of the selected vendor.

Original-repository context: [PR #157](https://github.com/juv/vibranceGUI/pull/157) for mixed-driver selection, [PR #158](https://github.com/juv/vibranceGUI/pull/158) for enumeration/empty-profile safeguards, and [PR #160](https://github.com/juv/vibranceGUI/pull/160) for device ownership and restoration. Related reports include [#145](https://github.com/juv/vibranceGUI/issues/145), [#142](https://github.com/juv/vibranceGUI/issues/142), [#67](https://github.com/juv/vibranceGUI/issues/67), [#144](https://github.com/juv/vibranceGUI/issues/144), [#95](https://github.com/juv/vibranceGUI/issues/95), [#60](https://github.com/juv/vibranceGUI/issues/60), [#36](https://github.com/juv/vibranceGUI/issues/36), and [#111](https://github.com/juv/vibranceGUI/issues/111). These are independently reimplemented safeguards; they do not certify every reporter's hardware or game.

#### Profiles, resolution, and settings

1. Added executable-path matching with protected-process name fallback, foreground reconciliation, and suppression of redundant color writes.
2. Preserved optional resolution profiles while testing requested modes, applying per display, reading back results, and restoring only captured modes the app changed.
3. Added independent recovery for malformed desktop INI options, Unicode settings support, bounded levels, preservation of valid AMD desktop levels below 100%, and invalid/duplicate profile handling.
4. Added atomic XML replacement, backups of the previous XML, preservation of unreadable originals before edits, and reported save/cleanup failures.
5. Improved the profile list and asynchronous running-program picker: explicit Edit/keyboard actions, guarded empty selections, cancellation on close, fallback icons, and manual addition for inaccessible programs.

Resolution safety reimplements behavior proposed in original [PR #159](https://github.com/juv/vibranceGUI/pull/159), related to [#114](https://github.com/juv/vibranceGUI/issues/114), [#132](https://github.com/juv/vibranceGUI/issues/132), [#133](https://github.com/juv/vibranceGUI/issues/133), [#134](https://github.com/juv/vibranceGUI/issues/134), [#98](https://github.com/juv/vibranceGUI/issues/98), and [#110](https://github.com/juv/vibranceGUI/issues/110). Foreground reconciliation and fewer writes address source-level risks discussed in [#137](https://github.com/juv/vibranceGUI/issues/137), [#113](https://github.com/juv/vibranceGUI/issues/113), and [#156](https://github.com/juv/vibranceGUI/issues/156); game-specific detection and driver frametime behavior remain acceptance cases. The empty-selection guard follows [PR #158](https://github.com/juv/vibranceGUI/pull/158). Atomic saves, corrupt-file preservation and the asynchronous picker are additional fork improvements.

#### Interface and appearance

1. Modernized the main window, profile editor, process picker, About, and shortcut editor with quieter sections, restrained primary actions, compact buttons, a spectrum accent, and layouts that wrap or scroll at smaller sizes.
2. Added System / Light / Dark appearance, immediate updates to existing windows/tray with an explicit immediate title-bar repaint, preservation of selections/unsaved edits, and system-preference notifications.
3. Resolved Windows app appearance before creating forms so System starts correctly in dark mode on Windows 10 as well as Windows 11; retained high-contrast precedence.
4. Fixed unreadable dropdown popup rows and stale colors across repeated theme changes, including highlighted/disabled states and slider/button painting.
5. Aligned section/header/list/footer margins and the visible left edges of the wordmark, subtitle, and spectrum accent, whose width follows the wider caption; centered mixed settings controls, right-aligned Pause/percentage, and kept the footer on one row. Removed the redundant appearance hint and About ellipsis.

#### Pause shortcut and autostart

1. Added opt-in pause/resume, default Ctrl+Alt+V, a shortcut editor, validated custom bindings, and backward-compatible preference defaults.
2. Applied shortcut changes live with conflict preservation, repeat suppression, stale-message rejection, and retained ownership until native registrations are released.
3. Preserved the current shortcut while recording and kept an applied-but-unsaved shortcut active for the session with retryable feedback.
4. Displayed shortcut instructions once, reserved feedback for errors, added the app icon, and sized the editor to content with wrapped errors, safe growth/shrinkage, and work-area handling.
5. Retained per-user registry autostart and startup path refresh, and added a tooltip explaining tray launch at sign-in, removal, and the absence of an administrator requirement.

The pause/toggle idea is selectively reimplemented from original [PR #153](https://github.com/juv/vibranceGUI/pull/153) and [issue #143](https://github.com/juv/vibranceGUI/issues/143). Configurable capture, safe rebinding and the compact editor are fork improvements. Multipresets and gamma/brightness/contrast from those discussions remain deferred. Autostart behavior is retained from the original; its explanatory tooltip is new.

#### Fork identity and attribution

1. Pointed the working repository's `origin` to SteffenCarlsen/vibranceGUI while retaining the original repository as the `upstream` Git remote.
2. Pointed application/project links and assembly repository metadata to this fork.
3. Removed original follow/donation prompts from the main window/tray. About retains juv / juvlarN and juRiiir3 credits, original GitHub links, and the original developer's support link.
4. Added the application icon and full version/source revision to About, with consistent ordinary body-text colors, plus documentation of fork support channels, original-project references, compatibility evidence, and this dated changelog.

#### Focused commit index

The fork changes are grouped by behavior rather than by individual UI corrections. Each commit has a detailed body describing the change, applicable checks and related original issues/PRs. Inspect the full descriptions with `git log --reverse --format=full 919a9f2..feature/modern-vibrance`. This index lists the initial implementation commits; documentation and follow-up fixes follow them.

| Date | Commit | Change |
| --- | --- | --- |
| 2026-10-04 | `cc305e2` | Migrate to .NET 10 and bundle the Windows desktop runtime |
| 2026-10-04 | `0f7ef77` | Preserve game profiles and recover saved desktop preferences safely |
| 2026-10-04 | `644343f` | Add direct NVIDIA digital-vibrance bindings and display capability probing |
| 2026-10-04 | `e469801` | Use an owned AMD ADL2 context for attached saturation-capable outputs |
| 2026-10-04 | `9486b29` | Test and verify temporary resolution changes on the requested display |
| 2026-10-04 | `416e2f6` | Support mixed GPU drivers and restore owned display changes reliably |
| 2026-10-04 | `7120f69` | Expose read-only GPU diagnostics and explicit vendor selection |
| 2026-10-04 | `714261b` | Refresh native profile workflows and configure automation before startup |
| 2026-10-04 | `39c874c` | Keep the running-program picker responsive and safe when it closes |
| 2026-10-04 | `024021a` | Verify production safeguards and publish a portable Windows executable |
| 2026-10-04 | `1a9c4d0` | Apply System, Light and Dark appearance without restarting |
| 2026-10-04 | `ffd3b67` | Add configurable pause shortcuts with conflict-safe live rebinding |
| 2026-10-04 | `0244154` | Follow Windows dark-mode startup and keep dropdown rows readable |
| 2026-10-04 | `d27887f` | Identify this fork and retain original attribution in About |
| 2026-10-04 | `773fc1c` | Align native layouts and size dialogs to their content |

### 2024-12-20 — original baseline, commit `919a9f2`

The fork starts from the original repository's code at this commit, whose assembly version is **2.3.1.1**. The legacy project targets .NET Framework 4.0 and x86, uses the original GPU integrations and interface, and references the original developer contacts and website. This is the baseline commit date, not a claim that version 2.3.1.1 was first released on that date.

## Original project and attribution

Original creator/NVIDIA implementation: [juv / juvlarN](https://github.com/juv). Original AMD implementation: **juRiiir3**. Original source: [juv/vibranceGUI](https://github.com/juv/vibranceGUI). Existing source attribution and inherited history are retained.

The original README referenced [vibrancegui.com](http://vibrancegui.com/), [juvlarN's Twitter](https://twitter.com/juvlarN), and [juRiiir3's Twitter](https://twitter.com/juRiiir3). These are **historical original-project website/contact references**, not contact or download links for this fork. Their current support availability is not asserted here.

The application's About dialog preserves the original developer's GitHub/project and PayPal support links. Supporting the original developer is separate from obtaining support for this fork; fork questions belong in [SteffenCarlsen/vibranceGUI issues](https://github.com/SteffenCarlsen/vibranceGUI/issues).
