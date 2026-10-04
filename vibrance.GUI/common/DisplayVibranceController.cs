using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using Microsoft.Win32;

namespace vibrance.GUI.common
{
    internal delegate bool ResolutionChangeHandler(ResolutionModeWrapper requested, string deviceName, out bool modeSetAttempted);

    // Shared by both drivers: remember what was changed, independently of where focus lands.
    public abstract class DisplayVibranceController : IVibranceProxy
    {
        private readonly object _sync = new object();
        private readonly IDisplayVibranceBackend _backend;
        private readonly string _primaryDisplayName;
        private readonly bool _subscribeToForegroundEvents;
        private readonly Func<string, Devmode?> _readResolution;
        private readonly ResolutionChangeHandler _changeResolution;
        private readonly Dictionary<string, Tuple<ResolutionModeWrapper, List<ResolutionModeWrapper>>> _resolutions;
        private readonly HashSet<string> _changedDisplays = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, ResolutionModeWrapper> _changedResolutions = new Dictionary<string, ResolutionModeWrapper>(StringComparer.OrdinalIgnoreCase);
        private List<ApplicationSetting> _applications;
        private WinEventHook _hook;
        private string _lastTransition;
        private bool _disposed;
        private bool _desktopLevelConfigured;
        private bool _desktopLevelPending;
        private bool _hasStarted;
        private VibranceInfo _info;

        internal DisplayVibranceController(IDisplayVibranceBackend backend, GraphicsAdapter adapter, int desktopLevel,
            List<ApplicationSetting> applications,
            Dictionary<string, Tuple<ResolutionModeWrapper, List<ResolutionModeWrapper>>> resolutions,
            bool subscribeToForegroundEvents = true, string primaryDisplayName = null,
            Func<string, Devmode?> readResolution = null,
            ResolutionChangeHandler changeResolution = null)
        {
            _backend = backend;
            _primaryDisplayName = primaryDisplayName;
            _subscribeToForegroundEvents = subscribeToForegroundEvents;
            _readResolution = readResolution ?? (name => ResolutionHelper.GetCurrentResolutionSettings(out Devmode mode, name) ? mode : null);
            _changeResolution = changeResolution ?? ResolutionHelper.ChangeResolutionEx;
            GraphicsAdapter = adapter;
            _applications = applications ?? new List<ApplicationSetting>();
            _resolutions = resolutions ?? new Dictionary<string, Tuple<ResolutionModeWrapper, List<ResolutionModeWrapper>>>();
            _info = new VibranceInfo
            {
                isInitialized = backend.DisplayNames.Count > 0,
                szGpuName = backend.GpuName,
                activeOutput = backend.DisplayNames.Count,
                displayHandles = new List<int>(),
                userVibranceSettingDefault = desktopLevel
            };
        }

        public GraphicsAdapter GraphicsAdapter { get; }
        public string InitializationError => _backend.InitializationError;
        public VibranceInfo GetVibranceInfo() { lock (_sync) return _info; }

        public void SetApplicationSettings(List<ApplicationSetting> applications)
        {
            lock (_sync) { _applications = applications?.ToList() ?? new List<ApplicationSetting>(); _lastTransition = null; }
            RefreshForeground();
        }

        public void SetShouldRun(bool shouldRun)
        {
            lock (_sync)
            {
                if (_disposed) return;
                _info.shouldRun = shouldRun;
                if (shouldRun && !_hasStarted)
                {
                    _desktopLevelPending = _desktopLevelConfigured;
                    _hasStarted = true;
                }
                _lastTransition = null;
                if (shouldRun && _info.isInitialized && _subscribeToForegroundEvents && _hook == null)
                {
                    _hook = WinEventHook.GetInstance();
                    _hook.WinEventHookHandler += OnForegroundChanged;
                    SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
                }
                if (!shouldRun) { RestoreChangedDisplays(); RestoreResolutions(); }
            }
            if (shouldRun) RefreshForeground();
        }

        public void SetVibranceWindowsLevel(int level)
        {
            lock (_sync)
            {
                _info.userVibranceSettingDefault = level;
                _desktopLevelConfigured = true;
                // Settings loaded before monitoring starts are read-only. A later explicit
                // slider change, including while paused, is applied on the next desktop transition.
                _desktopLevelPending = _hasStarted;
                _lastTransition = null;
            }
            RefreshForeground();
        }

        public void SetVibranceIngameLevel(int level) { lock (_sync) _info.userVibranceSettingActive = level; }

        public void SetAffectPrimaryMonitorOnly(bool value)
        {
            lock (_sync) { _info.affectPrimaryMonitorOnly = value; _lastTransition = null; }
            RefreshForeground();
        }

        public void SetNeverSwitchResolution(bool value)
        {
            lock (_sync)
            {
                _info.neverChangeResolution = value;
                _lastTransition = null;
                if (value) RestoreResolutions();
            }
            RefreshForeground();
        }

        private void RefreshForeground()
        {
            if (_hook != null && _info.shouldRun) OnForegroundChanged(this, WinEventHook.ReadForeground());
        }

        private void OnDisplaySettingsChanged(object sender, EventArgs args)
        {
            lock (_sync)
            {
                if (_disposed) return;
                _lastTransition = null;
                _backend.InvalidateCache();
            }
            RefreshForeground();
        }

        private void OnForegroundChanged(object sender, WinEventHookEventArgs args)
        {
            try
            {
                if (args.Handle != IntPtr.Zero && args.Handle != WinEventHook.GetForegroundWindow()) return;
                string display = args.Handle == IntPtr.Zero ? null : Screen.FromHandle(args.Handle).DeviceName;
                ApplyForeground(args.ProcessName, display, args.ExecutablePath);
            }
            catch (Exception ex) { VibranceGUI.Log(ex); }
        }

        // Regression checks call this without attaching a Windows hook or touching a driver.
        internal void ApplyForeground(string processName, string deviceName, string executablePath = null)
        {
            lock (_sync)
            {
                if (_disposed || !_info.isInitialized || !_desktopLevelConfigured) return;
                if (!_info.shouldRun) { RestoreChangedDisplays(); RestoreResolutions(); return; }
                ApplicationSetting profile = _applications.FirstOrDefault(x => MatchesProcess(x, processName, executablePath));
                var resolution = profile?.ResolutionSettings;
                string primary = _primaryDisplayName ?? Screen.PrimaryScreen?.DeviceName;
                string transition = profile == null ? "desktop" : $"{processName}|{deviceName}|{primary}|{profile.IngameLevel}|{_info.affectPrimaryMonitorOnly}|{_info.neverChangeResolution}|{profile.IsResolutionChangeNeeded}|{resolution?.DmPelsWidth}|{resolution?.DmPelsHeight}|{resolution?.DmDisplayFrequency}|{resolution?.DmBitsPerPel}|{resolution?.DmDisplayFixedOutput}";
                if (transition == _lastTransition) return;

                string[] targets = profile == null ? Array.Empty<string>() :
                    (_info.affectPrimaryMonitorOnly
                        ? _backend.DisplayNames.Where(x => string.Equals(x, primary, StringComparison.OrdinalIgnoreCase))
                        : _backend.DisplayNames).ToArray();

                RestoreChangedDisplays(targets);
                RestoreResolutions(profile != null && !_info.neverChangeResolution && profile.IsResolutionChangeNeeded &&
                    profile.ResolutionSettings != null && deviceName != null
                    ? new HashSet<string>(new[] { deviceName }, StringComparer.OrdinalIgnoreCase) : null);
                if (_desktopLevelPending)
                {
                    bool applied = true;
                    foreach (string display in _backend.DisplayNames.Where(x => !targets.Contains(x, StringComparer.OrdinalIgnoreCase) &&
                        (!_info.affectPrimaryMonitorOnly || string.Equals(x, primary, StringComparison.OrdinalIgnoreCase))))
                    {
                        if (!_backend.SetLevel(display, _info.userVibranceSettingDefault)) { _changedDisplays.Add(display); applied = false; }
                    }
                    _desktopLevelPending = !applied;
                }
                if (profile == null)
                {
                    if (_changedDisplays.Count == 0 && _changedResolutions.Count == 0 && !_desktopLevelPending) _lastTransition = transition;
                    return;
                }

                Devmode? mode = deviceName == null || _info.neverChangeResolution || !profile.IsResolutionChangeNeeded ||
                    profile.ResolutionSettings == null ? null : _readResolution(deviceName);
                if (!_info.neverChangeResolution && profile.IsResolutionChangeNeeded && profile.ResolutionSettings != null &&
                    deviceName != null && _resolutions.ContainsKey(deviceName) && mode is Devmode current &&
                    !ResolutionHelper.MatchesRequestedMode(profile.ResolutionSettings, current))
                {
                    // Retain the baseline even when a driver reports failure after a partial modeset.
                    bool newlyOwned = !_changedResolutions.ContainsKey(deviceName);
                    var previous = new ResolutionModeWrapper(current);
                    if (newlyOwned) _changedResolutions[deviceName] = previous;
                    _changeResolution(profile.ResolutionSettings, deviceName, out bool modeSetAttempted);
                    if (newlyOwned && !modeSetAttempted)
                        _changedResolutions.Remove(deviceName); // A rejected test does not own a game's concurrent modeset.
                }

                bool success = true;
                foreach (string display in targets)
                {
                    _changedDisplays.Add(display);
                    if (!_backend.SetLevel(display, profile.IngameLevel)) success = false;
                }
                bool pendingColorRestore = _changedDisplays.Any(x => !targets.Contains(x, StringComparer.OrdinalIgnoreCase));
                bool pendingResolutionRestore = _changedResolutions.Keys.Any(x =>
                    _info.neverChangeResolution || !profile.IsResolutionChangeNeeded ||
                    profile.ResolutionSettings == null || !string.Equals(x, deviceName, StringComparison.OrdinalIgnoreCase));
                if (success && !_desktopLevelPending && !pendingColorRestore && !pendingResolutionRestore) _lastTransition = transition;
            }
        }

        private void RestoreChangedDisplays(IEnumerable<string> except = null)
        {
            var retained = new HashSet<string>(except ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            foreach (string display in _changedDisplays.Where(x => !retained.Contains(x)).ToArray())
                if (_backend.SetLevel(display, _info.userVibranceSettingDefault)) _changedDisplays.Remove(display);
        }

        internal static bool MatchesProcess(ApplicationSetting profile, string processName, string executablePath)
        {
            if (!string.Equals(profile.Name, processName, StringComparison.OrdinalIgnoreCase)) return false;
            return string.IsNullOrWhiteSpace(profile.FileName) ||
                string.IsNullOrWhiteSpace(executablePath) || // Preserve name matching when Windows denies an image query.
                string.Equals(profile.FileName, executablePath, StringComparison.OrdinalIgnoreCase);
        }

        private void RestoreResolutions(HashSet<string> retained = null)
        {
            foreach (var entry in _changedResolutions.ToArray())
                if ((retained == null || !retained.Contains(entry.Key)) && _changeResolution(entry.Value, entry.Key, out _))
                    _changedResolutions.Remove(entry.Key);
        }

        public void HandleDvcExit()
        {
            lock (_sync)
            {
                if (_disposed) return;
                _info.shouldRun = false;
                _lastTransition = null;
                RestoreChangedDisplays();
                RestoreResolutions();
            }
        }

        public bool UnloadLibraryEx()
        {
            lock (_sync)
            {
                if (_disposed) return true;
                HandleDvcExit();
                if (_hook != null)
                {
                    _hook.WinEventHookHandler -= OnForegroundChanged;
                    SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
                    _hook.RemoveWinEventHook();
                }
                _backend.Dispose();
                _disposed = true;
                return _changedDisplays.Count == 0 && _changedResolutions.Count == 0;
            }
        }
    }
}
