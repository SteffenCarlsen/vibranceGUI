# Upstream pull request and issue review

Snapshot: **2026-10-04**, Europe/Copenhagen. Reviewed repository baseline: `919a9f2` (`juv/vibranceGUI` master), before this fork's modernization.

The review found **6 open pull requests and 31 open issues upstream**, and **0 open pull requests and 0 open issues in SteffenCarlsen's fork**. GitHub REST requests used `--paginate`, `per_page=100`, and excluded pull requests from the issues response. Each open PR's files, head SHA, conversation, and inline review comments were fetched; issue descriptions and comment threads were fetched separately. Counts describe this snapshot, not a permanent state. Sources: [upstream PRs](https://github.com/juv/vibranceGUI/pulls), [upstream issues](https://github.com/juv/vibranceGUI/issues), [fork](https://github.com/SteffenCarlsen/vibranceGUI).

## Decisions

1. Reimplement the specific GPU-selection, restore, enumeration, and resolution defects in PRs #157–#160 in the modern code. Their fixture/build/hardware statements are the authors' evidence, not verification of this fork.
2. Select the optional pause/resume hotkey from #153. The .NET Framework 4.8 and Fody migration does not fit the move to modern self-contained .NET.
3. Defer the brightness/contrast/gamma feature from #140. Its GDI implementation introduces concrete calibration, resource-lifetime, and event-frequency defects. It is not needed to modernize vibrance.
4. Preserve useful existing resolution functionality while correcting its ownership and failure handling. A vibrance-only profile must never alter a game's own resolution or refresh rate.
5. Keep driver/hardware limitations explicit. Source fixes and simulated tests cannot establish that every new GPU, laptop routing arrangement, HDR mode, or AMD Vulkan driver works.

## Open pull requests

### [#157: Start on machines with two GPU drivers](https://github.com/juv/vibranceGUI/pull/157)

Reviewed head: `99882c6ab29e52a262fbb57d21cba510b3b4bf0f`. **Rework and implement the behavior.**

The baseline `GraphicsAdapterHelper.GetAdapter()` treats the simultaneous presence of AMD and NVIDIA DLL files as an error. `Program.Main()` then offers DDU and exits. Installed driver files are not proof that both GPUs drive a display. This directly explains the refusal to launch on the reported 9950X3D + NVIDIA desktop (#145), without implying that CPU chipset drivers themselves are faulty.

The PR uses `EnumDisplayDevices`, attached-to-desktop flags, a chooser, and a persisted choice. Those are useful ideas; a modern backend should query the display's actual vendor and capability, support an explicit override where ambiguity remains, and avoid telling users to uninstall working iGPU drivers. Display ownership matters more than desktop/laptop labels: an NVIDIA-connected external monitor can expose vibrance even when an internal laptop panel cannot.

The PR only invokes its new enumeration when both vendor DLLs exist. That leaves unsupported/stale single-driver cases on the old heuristic. Its stored preference is also checked against a driver file, not current display support. Do not preserve those restrictions merely to reproduce the patch. On x64, select `nvapi64.dll` and the matching AMD library rather than assuming the original x86 filenames.

Acceptance: NVIDIA-only, AMD-only, AMD iGPU + NVIDIA display, both vendors with attached displays, disconnected eGPU, unknown virtual/RDP display, and unavailable chosen vendor. Real laptop routing and AMD-only hardware remain necessary for full support claims.

### [#158: Bound enumeration and stop stranding vibrance](https://github.com/juv/vibranceGUI/pull/158)

Reviewed head: `f53c867f584009a3096aeaa955194033bd4d8778`. **Implement the fixes with the new backend.**

Three reachable failures are present in the baseline: NVIDIA enumeration has no ceiling if the wrapper never returns its sentinel; both vendor foreground handlers skip the entire restore branch after removal of the last profile; and double-clicking empty ListView space indexes `SelectedItems[0]`. The bounded enumeration and selection guard are small, useful changes. Restore must remain reachable with zero profiles and when automation is paused.

The PR's large proxy diff mostly removes an outer profile-count guard, so it does not by itself solve monitor selection or driver-support issues. Its fake enumeration tests do not establish safe eGPU hot-unplug behavior. Use distinct handles, termination on actual API failure, bounded loops, and capability detection rather than merely a large fixed limit over the old wrapper.

Acceptance: a stuck/repeated enumeration terminates; last-profile removal restores previously changed displays; empty double-click does nothing; startup without a supported display remains responsive. Hardware eGPU disconnect/reconnect is a separate check.

### [#159: Resolution changes and repeated dialogs](https://github.com/juv/vibranceGUI/pull/159)

Reviewed head: `d70701852de16ab5f02237bc9fa9d5debfdbedc5`. **Rework and implement the safety and ownership fixes.**

The baseline stages a named-device change with `CDS_UPDATEREGISTRY | CDS_NORESET`, then globally commits every device's pending registry settings and discards the commit result. It shows modal failure dialogs directly from the foreground-event callback. Restore is tied to the screen that later receives focus and can replay a stale desktop mode even when this application did not apply a resolution profile. These are concrete defects; the original cause of every `DISP_CHANGE_BADFLAGS` report is not established.

The PR's test-before-apply, named-device calls, achieved-mode comparison, logging, and bounded failure notification are valuable. Track restoration per display actually changed, preserve the original mode until restoration succeeds, and keep resolution handling independent of whether vibrance already equals the desired level. A checkbox that prohibits new changes must not strand an already applied mode.

Do not blindly copy permanent failure suppression: #159 has no production reset for a suppressed target unless a different target/direction succeeds. A monitor mode or topology change should refresh supported modes and allow recovery. Dual-mode monitors can withdraw previously available resolutions, so cached capability lists must also refresh. Preserve an applied or uncertain change until readback/restoration settles it; a successful staging/test call is not proof of actual application.

Acceptance: no mode set without an explicit profile; no repeated modal dialogs on a failing driver; failed apply does not claim success; supported width/height/bpp/refresh readback settles the operation; switching foreground screens restores the original game display; external desktop changes are not overwritten; dual-mode/hot-plug refresh does not capture the app's own temporary mode as desktop baseline.

### [#160: Restore what was changed](https://github.com/juv/vibranceGUI/pull/160)

Reviewed head: `4a2fe97c55bf624324971c681f27d06443e89a3d`. **Implement the restore model and verify the changed semantics.**

The baseline NVIDIA constructor writes a default struct value before loading the saved desktop level and targets the first enumerated handle rather than an identified monitor. The game apply branch overwrites that handle; later restore/exit may affect the wrong screen, and switching focus to another monitor can skip restore entirely. AMD restores all displays regardless of the primary-only setting and writes the desktop level to all displays immediately before every game apply. The latter write is redundant and changes unrelated monitors.

The PR records written displays by device name and restores those records; this is materially better than restoring whichever screen now has focus. Keep failed restorations recorded, drain them on normal exit, and handle successive games on different monitors. Guard initialization until saved levels are loaded; otherwise AMD can write saturation 0 at startup (#111).

The PR deliberately changes the old behavior that kept a visible game's vibrance when focus moved to another monitor. It also leaves NVIDIA's apply path ignoring the primary-only checkbox. The fork should state and test the selected scope consistently for both vendors; the checkbox name alone is not evidence of current behavior. Do not import its restore fixture wholesale as proof of this fork: tests must exercise the actual modern callback/controller path.

Acceptance: initialization performs no zero-value placeholder write; game-to-desktop, game-to-game, last-profile deletion, pause, scope change, and exit restore the correct displays; untouched secondary displays retain their own settings; failed writes/restores remain diagnosable and retryable.

### [#153: .NET 4.8 and profile toggle hotkey](https://github.com/juv/vibranceGUI/pull/153)

Reviewed head: `0cfd442f81f888b2015cc69799af45051f54b496`. **Selectively reimplement optional pause/resume; supersede the framework migration.**

An optional toggle provides the comparison/reset workflow requested in #143 and a useful temporary pause while a game retains focus. Native `RegisterHotKey` is sufficient. Keep normal automatic switching as the default and return changed displays to desktop levels when paused.

The patch depends on the unreleased #140 color branch and should not be transplanted as one feature. Verified patch problems include accepting multiple non-modifier keys while silently keeping the last one, ignoring hotkey registration failure, AMD toggle-off writing all displays regardless of scope, and resetting toggle state when settings are reapplied. Use a bounded shortcut definition or validated single-key input, report shortcut conflicts, suppress repeat toggles, unregister on shutdown, and restore before pausing. .NET Framework 4.8 does not supply the requested runtime-bundled modern .NET artifact.

Acceptance: opt-in registration, collision feedback, one toggle per press, immediate scoped restore on pause, current foreground reapplied on resume, and clean shutdown. Gamma presets are a separate deferred feature.

### [#140: Add color settings](https://github.com/juv/vibranceGUI/pull/140)

Reviewed head: `18e54cd537e7e00cfe265590b45b0a6c78fd073f`. **Defer the color feature; extract only independently justified bug fixes.**

The feature uses Windows `SetDeviceGammaRamp` rather than a verified equivalent to GPU control-panel brightness/contrast/gamma. Its restore synthesizes a ramp from sliders and does not replay the display's original ramp; neutral settings produce an identity ramp, which can erase a loaded calibration. `CreateDC` is paired with `ReleaseDC` rather than `DeleteDC`. Its `GammaRamp.Equals` returns false for the very type it should compare. The NVIDIA apply call passes gamma in the brightness argument position. These are patch defects independently of any particular reporter's root cause.

It also widens `SetWinEventHook(FOREGROUND, FOREGROUND)` to `SetWinEventHook(FOREGROUND, MINIMIZEEND)` without filtering `eventType`. Those arguments form a range, so mouse-capture events run the full process/GPU path. This is a plausible app-side contributor to #156 in the 2.5.0 beta. The reviewed baseline subscribes only to foreground events and does not have that broad-range mechanism. Adding restore/minimize events should use exact hooks or strict filtering plus current-foreground reconciliation.

Microsoft documents that gamma ramp application can silently report success without applying, can be overwritten by games/Windows/other software, and has undefined behavior with HDR and color calibration. The upstream maintainer describes the same problems in [#153's discussion](https://github.com/juv/vibranceGUI/pull/153#issuecomment-3831729237). The PR's claim to fix many historical issues is not blanket evidence; some reports precede the gamma feature entirely.

Useful isolated ideas are correct DPI handling, restoring only app-owned resolution changes, and explicit backend overrides. Avoid the patch's manual Windows driver-DLL rename/delete advice. Future gamma work requires a supported design, captured-baseline restoration, resource-lifetime checks, and real HDR/game/calibration acceptance before offering it as reliable.

Primary references: [Microsoft gamma ramp limitations](https://learn.microsoft.com/en-us/windows/win32/api/wingdi/nf-wingdi-setdevicegammaramp), [WinEvent event-range/thread semantics](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwineventhook).

## Exhaustive open-issue mapping

The following rows define the corresponding safeguard or acceptance check. **They are not declarations that the original reporters' machines have been fixed.** Automated coverage, local smoke evidence, and outstanding hardware validation are recorded separately in the fork's verification documentation. Feature requests marked deferred remain outside this modernization's implemented feature set.

### Implemented safeguards and limits

The modern source has been reviewed independently of the PR authors' patches. The fork implements direct process-appropriate NVAPI/ADL2 capability probing, bounded adapter/display enumeration, a shared display-ownership controller, exact foreground events with one-second reconciliation, unchanged-state caching, and the optional pause shortcut. Saved settings are loaded before monitoring begins; known desktop preferences are then applied by normal startup. Diagnostic startup constructs probes without starting the observer or applying levels. Primary-only scope targets the current Windows primary output; otherwise the selected vendor's supported outputs are targeted.

| Issue group | Actual disposition | Evidence boundary |
|---|---|---|
| #145, #142, #67, #150 | Driver-presence rejection replaced by supported-output probing; manual vendor override retained. | Source safeguard. Real laptop/mixed-vendor acceptance remains separate. |
| #138, #156 | Enumeration bounded; mouse capture events excluded; successful repeated state skips GPU work. | Source safeguard. No claim that an affected driver's frametime spikes or eGPU recovery is solved. |
| #144, #95, #60, #36, #111 | Configured-level initialization, explicit primary/all-output scope, ownership-based restores, empty-list restoration, pending restore retries, and exit failure warning implemented. | Source safeguard and fake-controller regression coverage; original hardware reports remain unconfirmed. |
| #137, #113 | Missed-event reconciliation and limited-information executable queries implemented. Process-name fallback is retained when Windows denies a path query; a known different path is rejected. | Source safeguard. Protected-game/fullscreen behavior still needs actual game acceptance. |
| #134, #133, #132, #114, #110, #98 | Named-device temporary modes, test-before-apply, readback, supported-mode refresh during requests, and owned restoration implemented. An explicit attempted-modeset flag prevents a rejected test from claiming a concurrent game-created mode. | Source safeguard. No hardware modeset acceptance is implied. |
| #81, #131, #128, #55, #107, #116, #149 | Relevant initialization/scope/range/redundant-write safeguards and diagnostics implemented. Specific driver/color/refresh/crash causes remain unresolved. | Partial prevention and diagnosis. These issues remain hardware-dependent acceptance cases. |
| #143, #120, #147, #161, #8 | Optional vibrance pause shortcut implemented. Backend/diagnostic/minimized launch flags supported. General running-instance CLI, multi-presets, separate HDR profiles, and GDI gamma/brightness/contrast deferred. | Feature disposition; do not advertise deferred features as implemented. |

Display-change events invalidate requested-value caches and NVIDIA can reacquire an invalidated handle for an existing output. **The supported-output inventory is captured at startup; a newly connected output/GPU may require restarting the application.** This is not complete dynamic eGPU/topology support. Mode availability is re-enumerated before a requested resolution change; the profile editor's displayed mode list is its startup snapshot. The polling timer does not establish how an affected driver performs, and direct fake-controller calls do not by themselves test Win32 event delivery.

### GPU detection, capability, and performance

| Issue | Evidence and fork action | Required check / remaining limit |
|---|---|---|
| [#150 External-monitor laptop support](https://github.com/juv/vibranceGUI/issues/150) | Detect attached display ownership and DVC capability instead of rejecting all laptops or both drivers. A commenter reports an older forced-vendor build working on Advanced Optimus. | External NVIDIA-driven monitor and internal iGPU-driven panel must be tested separately; capability cannot be inferred from the RTX 4070 name. |
| [#145 9950X3D dual GPU detected](https://github.com/juv/vibranceGUI/issues/145) | Remove the DLL-presence hard stop; AMD iGPU driver installation is legitimate. | AMD iGPU + NVIDIA-connected monitor must select an available NVIDIA backend and start without removing drivers. |
| [#142 NVIDIA + AMD chipset/iGPU](https://github.com/juv/vibranceGUI/issues/142) | Same selection defect as #145; retain both required drivers and give an explicit backend choice where needed. | Disabled/unattached AMD display entry must not block NVIDIA; mixed attached vendors need deterministic scope. |
| [#67 Two different GPU vendors](https://github.com/juv/vibranceGUI/issues/67) | Same detection defect, but genuinely mixed display ownership requires correct routing rather than merely bypassing the error. | Verify both connected vendors and vendor override; a single selected backend cannot claim to control the other vendor's displays. |
| [#138 High CPU after eGPU disconnect](https://github.com/juv/vibranceGUI/issues/138) | Bound/deduplicate display enumeration, handle API failure, invalidate request caches on display changes, and keep unsupported startup responsive. | Real Thunderbolt unplug/replug remains unverified. Newly attached outputs are not re-enumerated during the session and can require restart; enumeration bounds do not prove hot-plug recovery. |
| [#149 Driver 591.44 vibrance failure](https://github.com/juv/vibranceGUI/issues/149) | Capability/return-code diagnostic; explain Reference Mode. NVIDIA documents that override ignores color adjustments. Another reporter already had the option off, so it is not the sole confirmed cause. | Check manual driver vibrance, displayed API support, and actual write on affected driver; retain unresolved driver-specific failure. |
| [#156 Click-related frametime spikes](https://github.com/juv/vibranceGUI/issues/156) | Preserve exact foreground event subscription/filtering; avoid process/window enumeration and redundant GPU calls on duplicate state. #140's broad range includes mouse capture; causal attribution to the driver is unproven. | Click events must not run the profile path; duplicate foreground state must not reapply. PresentMon/FrameView on affected NVIDIA drivers is needed to claim the reported spikes resolved. |
| [#107 Only one monitor / saturation ceiling](https://github.com/juv/vibranceGUI/issues/107) | Route by device name and query supported ranges; display-selection and values above 100 are distinct symptoms. | Multi-monitor target test plus real adapter range/readback. The reporter supplied too little GPU detail to establish the saturation ceiling cause. |

### Restore, monitor scope, and saved state

| Issue | Evidence and fork action | Required check / remaining limit |
|---|---|---|
| [#144 Closing game leaves vibrance](https://github.com/juv/vibranceGUI/issues/144) | Restore the tracked game display regardless of the main window's monitor or new foreground monitor; restore on normal app exit. | Game on primary, app on secondary, game close; no hardware conclusion from fake backend checks alone. |
| [#95 Exit / frequent alt-tab retains vibrance](https://github.com/juv/vibranceGUI/issues/95) | Remove the wrong-focus restore gate and reconcile missed foreground changes; empty profile list must still restore. | Repeat cross-monitor alt-tab/game close; ICC DisplayCAL interaction remains a separate real-hardware variable. |
| [#60 Secondary screen affected](https://github.com/juv/vibranceGUI/issues/60) | Eliminate arbitrary first-handle startup write and unconditional AMD all-display restore; retain written-display ownership. | Primary-only mode must leave an untouched second display unchanged, including desktop-slider changes and exit. |
| [#36 Wrong monitor + BadMode](https://github.com/juv/vibranceGUI/issues/36) | Stable device-name targeting plus named-device resolution ownership. | Repeat alt-tab across unequal-resolution monitors; mode rejection must be logged without repeated dialogs. |
| [#111 AMD saturation 0 after restart](https://github.com/juv/vibranceGUI/issues/111) | Do not apply placeholder defaults before saved settings are loaded; validate AMD levels and restore readiness. Source timing explains a plausible startup race. | Synthetic foreground during initialization must perform no default-zero write; AMD login/logoff hardware reproduction remains required. |
| [#81 Startup settings later revert](https://github.com/juv/vibranceGUI/issues/81) | Load settings before applying, persist the final slider change, and reconcile session/display changes without force-writing continuously. | Save/restart and delayed login tests; Windows secure desktop/another driver's color initialization can overwrite settings and is not proven fixed. |
| [#131 Only exit applies level / degraded colors](https://github.com/juv/vibranceGUI/issues/131) | Correct desktop-level initialization and apply/restore ordering; avoid importing GDI color handling. Maintainer suspects an NVIDIA API lock, but no established root cause is provided. | Repeated startup/slider/exit checks and driver control-panel comparison. Keep this reported behavior unconfirmed; do not diagnose it as #140 gamma loss in an earlier release. |
| [#128 Gray contrast/brightness after exit](https://github.com/juv/vibranceGUI/issues/128) | Avoid unrelated topology resets and GDI ramp replacement. One user reports beta improvement; others still report symptoms. | Real RGB/YCbCr multi-monitor/color-managed acceptance is outstanding. The report predates #140, so that patch's calibration defect is a prevention finding, not proof of this original cause. |
| [#55 Color/contrast shift after CS:GO](https://github.com/juv/vibranceGUI/issues/55) | Scoped restore and no gamma/color-space modification; preserve the existing color pipeline. | Driver/color-calibration hardware comparison is needed; matching a symptom does not establish a cause. |

### Foreground detection and resolution changes

| Issue | Evidence and fork action | Required check / remaining limit |
|---|---|---|
| [#137 Escape from Tarkov foreground missed](https://github.com/juv/vibranceGUI/issues/137) | Reconcile actual foreground periodically in addition to events; use bounded limited-information process lookup and exact executable matching when the path is available, with the original name fallback when it is denied. | Missed-event recovery and rapid switching checks, then real exclusive-fullscreen Tarkov acceptance. Broad install-directory matching is avoided because it can apply a game profile to launchers or unrelated executables. |
| [#113 Battlefield 2042 inconsistent](https://github.com/juv/vibranceGUI/issues/113) | Same missed-event reconciliation and protected-process-safe lookup. The reporter observed absent callbacks; blaming anti-cheat is not established. | Real BF2042 foreground/exit check; limited-query access can still be denied, which must not crash the app. |
| [#134 CS2 jumps monitor on alt-tab](https://github.com/juv/vibranceGUI/issues/134) | Only restore resolutions changed by this app; replace global topology commit with named-device apply. Comments show disabling resolution handling helps some users. | Vibrance-only profile must issue zero mode changes. CS2/Valve window positioning remains a hardware/game acceptance case. |
| [#133 Stretched resolution cannot restore](https://github.com/juv/vibranceGUI/issues/133) | Record each original mode before a successful/uncertain apply; restore on the original device, including exit/pause. | Failed restore retains ownership and clear diagnostic; real fullscreen/stretched monitor check remains. |
| [#132 Repeated BadFlags in Valorant](https://github.com/juv/vibranceGUI/issues/132) | Test mode first, read back result, deduplicate errors, and refresh supported modes on change. Maintainer cites dual-mode monitor capability withdrawal. | Failure must not spam dialogs or repeatedly lower refresh. Dual-mode OLED switching is a separate physical-device check. |
| [#114 BadFlags / desktop stuck](https://github.com/juv/vibranceGUI/issues/114) | Same resolution safeguards plus preservation of original baseline; no unsupported-mode success claim. | Faked failure/readback/revert paths; no automated hardware resolution torture test that risks stranding the display. |
| [#110 BadFlags without reproduction](https://github.com/juv/vibranceGUI/issues/110) | Device/mode/result diagnostics and bounded failure handling. There is insufficient information to establish the driver's specific rejection cause. | Ensure graceful rejection and retry after relevant mode/topology change; exact original symptom remains unconfirmed. |
| [#98 Refresh forced to 60 Hz](https://github.com/juv/vibranceGUI/issues/98) | Never revert a game-created mode when no app resolution was applied; include refresh in capture/apply/readback and update external desktop baselines safely. | 165 Hz desktop, non-native game, external Windows refresh adjustment: no unowned reset. Real monitor refresh verification remains required. |
| [#116 AMD Vulkan black screen](https://github.com/juv/vibranceGUI/issues/116) | Remove redundant saturation writes; avoid writes for repeated unchanged state; keep resolution opt-out functional. No source evidence establishes the exact Radeon crash. | RX 5700 XT + RDR2/Vulkan alt-tab is outstanding hardware validation. Do not claim fixed or silently remove AMD switching to hide the report. |

### Requested features

| Issue | Decision | Required check / remaining limit |
|---|---|---|
| [#143 Reset/toggle shortcut and presets](https://github.com/juv/vibranceGUI/issues/143) | Implement optional pause/resume that restores desktop vibrance; defer multi-preset gamma/brightness expansion. | Shortcut conflicts, repeat suppression, both vendors' scoped restore, and immediate resume of current foreground. |
| [#120 Command-line control](https://github.com/juv/vibranceGUI/issues/120) | Backend override, minimized launch, and diagnostics are useful. General on/off/level commands to an already running singleton require deliberate IPC/state semantics; do not equate launch flags with a working remote-control feature. | Document only actually supported arguments. Arbitrary running-instance command control remains deferred unless implemented and tested separately. |
| [#147 Separate SDR/HDR vibrance](https://github.com/juv/vibranceGUI/issues/147) | Defer automatic mode-specific profiles until HDR detection, driver DVC behavior, and HDR transitions have been verified. | HDR/SDR transitions and real display support; a second slider without correct mode/routing handling would manufacture support. |
| [#161 Contrast/gamma/brightness controls](https://github.com/juv/vibranceGUI/issues/161) | Defer #140's GDI implementation for the defects and platform limitations above. | Supported color API/design and calibration-safe hardware acceptance required. |
| [#8 More color controls](https://github.com/juv/vibranceGUI/issues/8) | Same deferred color-feature decision; optional vibrance pause fulfills only the toggle portion, not brightness/gamma. | Historical discussion and beta availability are not proof of a reliable current implementation. |

## Evidence boundaries and release acceptance

1. **Source defects:** wrong GPU selection, initialization placeholders, unbounded enumeration, unowned or wrong-screen restore, event-range expansion in #140, empty-selection crash, and gamma-resource/calibration defects are directly visible in reviewed code.
2. **Regression checks:** should exercise the modern production decision paths with bounded fake native devices. They establish policy/ownership/failure behavior, not that a driver honored a write or that colors looked correct.
3. **Local smoke checks:** should establish current display enumeration, library load/capability detection, UI layout/theme, profile selection, and an actual portable artifact starting without a separately installed runtime. A launch alone does not prove game switching.
4. **Hardware acceptance outstanding:** modern NVIDIA drivers including the #156 versions, AMD saturation/Vulkan, external-monitor laptop routing, eGPU unplug/replug, multiple mixed-vendor displays, HDR, dual-mode monitors, and calibration coexistence. Scope the advertised compatibility to observed capability and report results individually.
5. **No external activity:** this review does not post comments, close issues, merge upstream PRs, or claim upstream fixes deployed. Feature conclusions are implemented selectively in this fork and require its own validation.

NVIDIA's [Reference Mode documentation](https://www.nvidia.com/content/Control-Panel-Help/vLatest/en-us/mergedProjects/nvdsp/CS_Adjust_Color_Settings_Advanced.htm) confirms that reference override ignores color adjustments. It is a useful diagnostic for #149, not a universal explanation for current-driver failures. Microsoft [gamma ramp guidance](https://learn.microsoft.com/en-us/windows/win32/api/wingdi/nf-wingdi-setdevicegammaramp) supports deferring the unreliable GDI color expansion.
