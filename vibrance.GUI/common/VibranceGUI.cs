using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace vibrance.GUI.common
{
    public partial class VibranceGUI : Form
    {
        private readonly int _minTrackBarValue, _maxTrackBarValue, _defaultIngameValue;
        private readonly Func<int, string> _resolveLabelLevel;
        private readonly IVibranceProxy _v;
        private readonly bool _initializeRuntime;
        private const string AppName = "vibranceGUI";
        private bool _allowVisible = true, _loadingSettings = true, _settingsLoaded, _closing, _paused, _runtimeInitialized;
        private readonly PauseHotkeyBinding _pauseHotkey = new();
        private Keys _pauseHotkeyKeyData;
        private HotkeyDialog _hotkeyDialog;
        private List<ApplicationSetting> _applicationSettings = new List<ApplicationSetting>();
        private readonly List<ResolutionModeWrapper> _supportedResolutionList = new List<ResolutionModeWrapper>();
        private readonly Dictionary<string, Tuple<ResolutionModeWrapper, List<ResolutionModeWrapper>>> _windowsResolutionSettings = new Dictionary<string, Tuple<ResolutionModeWrapper, List<ResolutionModeWrapper>>>();

        public VibranceGUI(Func<List<ApplicationSetting>, Dictionary<string, Tuple<ResolutionModeWrapper, List<ResolutionModeWrapper>>>, IVibranceProxy> getProxy,
            int defaultWindowsLevel, int minTrackBarValue, int maxTrackBarValue, int defaultIngameValue,
            Func<int, string> resolveLabelLevel, bool initializeRuntime = true)
        {
            _minTrackBarValue = minTrackBarValue;
            _maxTrackBarValue = maxTrackBarValue;
            _defaultIngameValue = defaultIngameValue;
            _resolveLabelLevel = resolveLabelLevel;
            _initializeRuntime = initializeRuntime;
            _pauseHotkeyKeyData = AppTheme.PauseHotkeyKeyData;
            InitializeComponent();
            trackBarWindowsLevel.Minimum = minTrackBarValue;
            trackBarWindowsLevel.Maximum = maxTrackBarValue;
            trackBarWindowsLevel.Value = Math.Clamp(defaultWindowsLevel, minTrackBarValue, maxTrackBarValue);
            labelWindowsLevel.Text = _resolveLabelLevel(trackBarWindowsLevel.Value);
            comboBoxTheme.SelectedItem = AppTheme.Preference;
            checkBoxPauseHotkey.Checked = AppTheme.EnablePauseHotkey;
            buttonPauseHotkey.Text = PauseHotkey.Format(_pauseHotkeyKeyData);
            foreach (Screen screen in Screen.AllScreens)
            {
                if (ResolutionHelper.GetCurrentResolutionSettings(out Devmode mode, screen.DeviceName))
                {
                    List<ResolutionModeWrapper> modes = ResolutionHelper.EnumerateSupportedResolutionModes(screen.DeviceName);
                    if (screen.Primary) _supportedResolutionList.AddRange(modes);
                    _windowsResolutionSettings.Add(screen.DeviceName, Tuple.Create(new ResolutionModeWrapper(mode), modes));
                }
            }
            _v = getProxy(_applicationSettings, _windowsResolutionSettings);
            SetGuiEnabledFlag(false);
            InitializeApplicationList();
            if (!initializeRuntime)
            {
                AddApplicationListItem(new ApplicationSetting("Counter-Strike 2", "preview-cs2.exe", defaultIngameValue, null, false));
                AddApplicationListItem(new ApplicationSetting("VALORANT", "preview-valorant.exe", defaultIngameValue, null, false));
                statusLabel.Text = "Preview";
                statusLabel.ForeColor = AppTheme.SuccessColor;
                SetGuiEnabledFlag(true);
                _loadingSettings = false;
            }
            AppTheme.WatchSystemPreferences(this);
        }

        protected override void SetVisibleCore(bool value)
        {
            if (!_allowVisible)
            {
                value = false;
                if (!IsHandleCreated) CreateHandle();
            }
            base.SetVisibleCore(value);
        }
        public void SetAllowVisible(bool value) => _allowVisible = value;

        private void Form1_Load(object sender, EventArgs e)
        {
            if (!_initializeRuntime || _runtimeInitialized || _closing) return;
            _runtimeInitialized = true;
            try
            {
                var registry = new RegistryController();
                checkBoxAutostart.Checked = registry.IsProgramRegistered(AppName);
                string startupPath = "\"" + Application.ExecutablePath + "\" -minimized";
                if (checkBoxAutostart.Checked && !registry.IsStartupPathUnchanged(AppName, startupPath)
                    && !registry.RegisterProgram(AppName, startupPath))
                    MessageBox.Show(this, "Updating Autostart Path failed!", AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                var settings = new SettingsController();
                settings.ReadVibranceSettings(_v.GraphicsAdapter, out int desktopLevel, out bool primaryOnly,
                    out bool neverSwitchResolution, out _applicationSettings);
                trackBarWindowsLevel.Value = Math.Clamp(desktopLevel, _minTrackBarValue, _maxTrackBarValue);
                checkBoxPrimaryMonitorOnly.Checked = primaryOnly;
                checkBoxNeverChangeResolutions.Checked = neverSwitchResolution;
                foreach (ApplicationSetting profile in _applicationSettings) AddApplicationListItem(profile);
                _settingsLoaded = string.IsNullOrEmpty(settings.LastError);
                _loadingSettings = false;
                notifyIcon.Visible = true;
                if (!_v.GetVibranceInfo().isInitialized)
                {
                    statusLabel.Text = "GPU unavailable";
                    MessageBox.Show(this, _v.InitializationError ?? "The graphics driver could not be initialized.",
                        AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                _v.SetApplicationSettings(_applicationSettings);
                _v.SetVibranceWindowsLevel(trackBarWindowsLevel.Value);
                _v.SetAffectPrimaryMonitorOnly(primaryOnly);
                _v.SetNeverSwitchResolution(neverSwitchResolution);
                _v.SetShouldRun(true);
                SetGuiEnabledFlag(true);
                UpdateRunningStatus();
                UpdatePauseHotkey();
                if (!string.IsNullOrEmpty(settings.LastError))
                    MessageBox.Show(this, settings.LastError + "\n\nThe original file will be preserved before saving new profiles.",
                        "Settings could not be loaded", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            catch (Exception ex)
            {
                Log(ex);
                _loadingSettings = false;
                statusLabel.Text = "Initialization failed";
                MessageBox.Show(this, ex.Message, AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void Form1_Resize(object sender, EventArgs e)
        {
            if (WindowState == FormWindowState.Minimized) Hide();
        }
        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (_closing) return;
            _closing = true;
            AppTheme.UnwatchSystemPreferences(this);
            settingsSaveTimer.Stop();
            if (!_initializeRuntime) return;
            if (_settingsLoaded) ForceSaveVibranceSettings();
            UnregisterPauseHotkey();
            notifyIcon.Visible = false;
            foreach (Form ownedForm in OwnedForms) ownedForm.Close();
            statusLabel.Text = "Closing...";
            try { _v.SetShouldRun(false); }
            catch (Exception ex) { Log(ex); }
            try { _v.HandleDvcExit(); }
            catch (Exception ex) { Log(ex); }
            bool cleanedUp = false;
            try { cleanedUp = _v.UnloadLibraryEx(); }
            catch (Exception ex) { Log(ex); }
            if (!cleanedUp)
            {
                const string warning = "Display settings could not be fully restored or graphics driver cleanup failed. Check your display settings in Windows or your graphics driver.";
                Log(warning);
                MessageBox.Show(this, warning, AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
        private void exitToolStripMenuItem_Click(object sender, EventArgs e) => Close();
        private void showToolStripMenuItem_Click(object sender, EventArgs e) => ShowWindow();
        private void notifyIcon_MouseClick(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left) ShowWindow();
        }
        private void ShowWindow()
        {
            _allowVisible = true;
            Show();
            WindowState = FormWindowState.Normal;
            Activate();
        }

        private void trackBarWindowsLevel_Scroll(object sender, EventArgs e)
        {
            labelWindowsLevel.Text = _resolveLabelLevel(trackBarWindowsLevel.Value);
            if (_loadingSettings || !_initializeRuntime) return;
            _v.SetVibranceWindowsLevel(trackBarWindowsLevel.Value);
            QueueSave();
        }
        private void checkBoxPrimaryMonitorOnly_CheckedChanged(object sender, EventArgs e)
        {
            if (_loadingSettings || !_initializeRuntime) return;
            _v.SetAffectPrimaryMonitorOnly(checkBoxPrimaryMonitorOnly.Checked);
            QueueSave();
        }
        private void checkBoxNeverChangeResolutions_CheckedChanged(object sender, EventArgs e)
        {
            if (_loadingSettings || !_initializeRuntime) return;
            _v.SetNeverSwitchResolution(checkBoxNeverChangeResolutions.Checked);
            QueueSave();
        }
        private void QueueSave()
        {
            if (!_settingsLoaded || _closing) return;
            settingsSaveTimer.Stop();
            settingsSaveTimer.Start();
        }
        private void settingsSaveTimer_Tick(object sender, EventArgs e)
        {
            settingsSaveTimer.Stop();
            ForceSaveVibranceSettings();
        }
        private void ForceSaveVibranceSettings()
        {
            if (!_settingsLoaded || !_initializeRuntime) return;
            var settings = new SettingsController();
            if (!settings.SetVibranceSettings(trackBarWindowsLevel.Value.ToString(),
                checkBoxPrimaryMonitorOnly.Checked.ToString(), checkBoxNeverChangeResolutions.Checked.ToString(), _applicationSettings))
            {
                statusLabel.Text = "Settings could not be saved";
                Log(settings.LastError ?? "Saving settings failed.");
                if (_closing) MessageBox.Show(this, settings.LastError ?? "Settings could not be saved.",
                    AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
        private bool PrepareProfileSave()
        {
            if (_settingsLoaded) return true;
            var settings = new SettingsController();
            if (settings.BackupUnreadableProfiles()) { _settingsLoaded = true; return true; }
            MessageBox.Show(this, settings.LastError ?? "The original profiles could not be preserved.",
                "Settings could not be saved", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        private void checkBoxAutostart_CheckedChanged(object sender, EventArgs e)
        {
            if (_loadingSettings || !_initializeRuntime) return;
            var registry = new RegistryController();
            string path = "\"" + Application.ExecutablePath + "\" -minimized";
            bool success = checkBoxAutostart.Checked ? registry.RegisterProgram(AppName, path) : registry.UnregisterProgram(AppName);
            if (!success)
            {
                _loadingSettings = true;
                checkBoxAutostart.Checked = registry.IsProgramRegistered(AppName);
                _loadingSettings = false;
            }
            notifyIcon.BalloonTipText = success
                ? checkBoxAutostart.Checked ? "Registered to Autostart!" : "Unregistered from Autostart!"
                : "Updating Autostart failed!";
            notifyIcon.ShowBalloonTip(1000);
        }
        private void comboBoxTheme_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (_loadingSettings || _closing || comboBoxTheme.SelectedItem is not ThemePreference preference) return;
            try
            {
                AppTheme.Apply(preference);
                if (_closing || IsDisposed) return;
                if (_initializeRuntime) SaveAppearancePreferences();
                labelThemeStatus.Text = "Appearance changes apply immediately.";
            }
            catch (Exception ex) when (ex is ExternalException || ex is Win32Exception)
            {
                if (!_initializeRuntime) throw;
                MessageBox.Show(this, ex.Message, "Appearance could not be applied", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
        internal void RefreshThemeColors()
        {
            statusLabel.ForeColor = statusLabel.Text is "Running!" or "Preview" ? AppTheme.SuccessColor : SystemColors.ControlText;
            AppTheme.RefreshMenu(contextMenuStrip);
        }
        internal bool CanApplyTheme => !_closing && !IsDisposed && !Disposing;
        private void checkBoxPauseHotkey_CheckedChanged(object sender, EventArgs e)
        {
            if (_loadingSettings || !_initializeRuntime) return;
            UpdatePauseHotkey();
            SaveAppearancePreferences();
        }
        private void SaveAppearancePreferences()
        {
            try { AppTheme.Save((ThemePreference)comboBoxTheme.SelectedItem, checkBoxPauseHotkey.Checked, _pauseHotkeyKeyData); }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                MessageBox.Show(this, ex.Message, "Appearance could not be saved", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
        private void UpdatePauseHotkey()
        {
            if (!checkBoxPauseHotkey.Checked || !_v.GetVibranceInfo().isInitialized)
            {
                if (!_pauseHotkey.TryClear(out string error))
                {
                    _loadingSettings = true;
                    checkBoxPauseHotkey.Checked = _pauseHotkey.IsRegistered;
                    _loadingSettings = false;
                    MessageBox.Show(this, error, "Pause shortcut unavailable", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                return;
            }
            if (_pauseHotkey.TrySet(Handle, _pauseHotkeyKeyData, out string failure)) return;
            _loadingSettings = true;
            checkBoxPauseHotkey.Checked = _pauseHotkey.IsRegistered;
            _loadingSettings = false;
            MessageBox.Show(this, failure,
                "Pause shortcut unavailable", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        private void buttonPauseHotkey_Click(object sender, EventArgs e)
        {
            if (_closing) return;
            using var dialog = new HotkeyDialog(_pauseHotkeyKeyData, ApplyPauseHotkey);
            _hotkeyDialog = dialog;
            try { dialog.ShowDialog(this); }
            finally { _hotkeyDialog = null; }
        }

        internal string ApplyPauseHotkey(Keys keyData)
        {
            if (!PauseHotkey.IsValid(keyData)) return "Choose Ctrl, Alt or Shift with a key, or F1–F24. F12 is reserved by Windows.";
            if (_closing) return "The application is closing.";
            if (_initializeRuntime && checkBoxPauseHotkey.Checked && _v.GetVibranceInfo().isInitialized
                && !_pauseHotkey.TrySet(Handle, keyData, out string error)) return error;
            _pauseHotkeyKeyData = keyData;
            buttonPauseHotkey.Text = PauseHotkey.Format(keyData);
            if (!_initializeRuntime) return null;
            try { AppTheme.Save((ThemePreference)comboBoxTheme.SelectedItem, checkBoxPauseHotkey.Checked, keyData); }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                return "The shortcut was applied for this session, but could not be saved.\n\n" + ex.Message;
            }
            return null;
        }

        private void UnregisterPauseHotkey()
        {
            if (!_pauseHotkey.TryClear(out string error)) Log(error);
        }
        protected override void OnHandleDestroyed(EventArgs e)
        {
            UnregisterPauseHotkey();
            _pauseHotkey.DetachWindow();
            base.OnHandleDestroyed(e);
        }
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            if (_initializeRuntime && _v != null && !_closing)
            {
                // Minimized startup never raises Load, but still needs its observer and tray icon.
                if (!_runtimeInitialized) BeginInvoke((Action)(() => Form1_Load(this, EventArgs.Empty)));
                else if (!_loadingSettings) UpdatePauseHotkey();
            }
        }
        protected override void WndProc(ref Message message)
        {
            if (message.Msg == 0x0312 && !_closing && checkBoxPauseHotkey?.Checked == true && _pauseHotkey.Matches(message))
            {
                if (_hotkeyDialog is { IsDisposed: false }) _hotkeyDialog.CaptureShortcut(_pauseHotkey.KeyData);
                else buttonPause_Click(this, EventArgs.Empty);
            }
            base.WndProc(ref message);
        }

        private void buttonPause_Click(object sender, EventArgs e)
        {
            if (!_initializeRuntime || !_v.GetVibranceInfo().isInitialized || _closing) return;
            _paused = !_paused;
            _v.SetShouldRun(!_paused);
            UpdateRunningStatus();
        }
        private void UpdateRunningStatus()
        {
            statusLabel.Text = _paused ? "Paused" : "Running!";
            statusLabel.ForeColor = _paused ? SystemColors.ControlText : AppTheme.SuccessColor;
            buttonPause.Text = pauseToolStripMenuItem.Text = _paused ? "Resume" : "Pause";
            notifyIcon.Text = _paused ? "vibranceGUI — Paused" : "vibranceGUI — Running";
        }
        private void SetGuiEnabledFlag(bool enabled)
        {
            trackBarWindowsLevel.Enabled = checkBoxPrimaryMonitorOnly.Enabled = checkBoxNeverChangeResolutions.Enabled = enabled;
            buttonAddProgram.Enabled = buttonProcessExplorer.Enabled = buttonPause.Enabled = pauseToolStripMenuItem.Enabled = enabled;
            listApplications.Enabled = checkBoxPauseHotkey.Enabled = enabled;
            UpdateProgramActions();
        }
        private void UpdateProgramActions()
        {
            buttonEditProgram.Enabled = listApplications.Enabled && listApplications.SelectedItems.Count == 1;
            buttonRemoveProgram.Enabled = listApplications.Enabled && listApplications.SelectedItems.Count > 0;
        }
        private void listApplications_SelectedIndexChanged(object sender, EventArgs e) => UpdateProgramActions();
        private void listApplications_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter) { listApplications_DoubleClick(sender, e); e.Handled = true; }
            if (e.KeyCode == Keys.Delete) { buttonRemoveProgram_Click(sender, e); e.Handled = true; }
        }

        private static void OpenLink(string url)
        {
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
            catch (Win32Exception ex) { MessageBox.Show(ex.Message, AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }
        private void projectToolStripMenuItem_Click(object sender, EventArgs e) => OpenLink(AboutDialog.RepositoryUrl);
        private void aboutToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (_closing) return;
            using var dialog = new AboutDialog(OpenLink);
            dialog.ShowDialog(this);
        }
        private void buttonAddProgram_Click(object sender, EventArgs e)
        {
            using var dialog = new OpenFileDialog { Filter = "Programs (*.exe)|*.exe|All files (*.*)|*.*", CheckFileExists = true, Multiselect = false };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            using Icon icon = ExtractProgramIcon(dialog.FileName);
            AddProgramIntern(new ProcessExplorerEntry(dialog.FileName, icon, Path.GetFileNameWithoutExtension(dialog.FileName)));
        }
        public void AddProgramExtern(ProcessExplorerEntry entry)
        {
            if (_closing || IsDisposed) return;
            if (InvokeRequired) Invoke((Action)(() => AddProgramIntern(entry)));
            else AddProgramIntern(entry);
        }
        private void AddProgramIntern(ProcessExplorerEntry entry)
        {
            if (!File.Exists(entry.Path)) return;
            ListViewItem existing = listApplications.Items.Cast<ListViewItem>().FirstOrDefault(item =>
                string.Equals((string)item.Tag, entry.Path, StringComparison.OrdinalIgnoreCase));
            listApplications.SelectedIndices.Clear();
            ListViewItem item = existing ?? AddApplicationListItem(new ApplicationSetting(entry.ProcessName, entry.Path,
                _defaultIngameValue, null, false), entry.Icon);
            item.Selected = true;
            item.EnsureVisible();
            listApplications_DoubleClick(this, EventArgs.Empty);
        }
        private void InitializeApplicationList()
        {
            if (listApplications.LargeImageList != null) return;
            var images = new ImageList(components) { ImageSize = new Size(24, 24), ColorDepth = ColorDepth.Depth32Bit };
            listApplications.LargeImageList = listApplications.SmallImageList = images;
        }
        private ListViewItem AddApplicationListItem(ApplicationSetting profile, Icon suppliedIcon = null)
        {
            using Icon icon = suppliedIcon == null ? ExtractProgramIcon(profile.FileName) : (Icon)suppliedIcon.Clone();
            var images = listApplications.LargeImageList;
            images.Images.Add(icon);
            var item = new ListViewItem(new[] { profile.Name ?? Path.GetFileNameWithoutExtension(profile.FileName),
                _resolveLabelLevel(profile.IngameLevel), profile.IsResolutionChangeNeeded ? profile.ResolutionSettings?.ToString() ?? "Windows" : "Windows" }, images.Images.Count - 1)
            { Tag = profile.FileName, ToolTipText = profile.FileName };
            listApplications.Items.Add(item);
            return item;
        }
        internal static Icon ExtractProgramIcon(string path)
        {
            try { if (File.Exists(path)) return Icon.ExtractAssociatedIcon(path) ?? (Icon)SystemIcons.Application.Clone(); }
            catch (Exception ex) when (ex is ArgumentException || ex is IOException || ex is Win32Exception) { }
            return (Icon)SystemIcons.Application.Clone();
        }
        private void listApplications_DoubleClick(object sender, EventArgs e)
        {
            if (listApplications.SelectedItems.Count != 1 || !_initializeRuntime || _closing) return;
            ListViewItem item = listApplications.SelectedItems[0];
            ApplicationSetting current = _applicationSettings.FirstOrDefault(profile => string.Equals(profile.FileName, (string)item.Tag, StringComparison.OrdinalIgnoreCase));
            using var dialog = new VibranceSettings(_v, _minTrackBarValue, _maxTrackBarValue, _defaultIngameValue,
                item, current, _supportedResolutionList, _resolveLabelLevel);
            if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                if (!PrepareProfileSave()) { if (current == null) RemoveApplicationListItem(item); return; }
                ApplicationSetting updated = dialog.GetApplicationSetting();
                _applicationSettings.Remove(current);
                _applicationSettings.Add(updated);
                item.SubItems[1].Text = _resolveLabelLevel(updated.IngameLevel);
                item.SubItems[2].Text = updated.IsResolutionChangeNeeded ? updated.ResolutionSettings.ToString() : "Windows";
                _v.SetApplicationSettings(_applicationSettings);
                ForceSaveVibranceSettings();
            }
            else if (current == null) RemoveApplicationListItem(item);
        }
        private void buttonRemoveProgram_Click(object sender, EventArgs e)
        {
            if (!_initializeRuntime || listApplications.SelectedItems.Count == 0 || !PrepareProfileSave()) return;
            foreach (ListViewItem item in listApplications.SelectedItems.Cast<ListViewItem>().ToArray())
            {
                _applicationSettings.RemoveAll(profile => string.Equals(profile.FileName, (string)item.Tag, StringComparison.OrdinalIgnoreCase));
                RemoveApplicationListItem(item);
            }
            _v.SetApplicationSettings(_applicationSettings);
            ForceSaveVibranceSettings();
        }
        private void RemoveApplicationListItem(ListViewItem item)
        {
            int removedIndex = item.ImageIndex;
            listApplications.Items.Remove(item);
            listApplications.LargeImageList.Images.RemoveAt(removedIndex);
            foreach (ListViewItem remaining in listApplications.Items)
                if (remaining.ImageIndex > removedIndex) remaining.ImageIndex--;
        }
        private void buttonProcessExplorer_Click(object sender, EventArgs e)
        {
            var existing = OwnedForms.OfType<ProcessExplorer>().FirstOrDefault();
            if (existing != null) { existing.Activate(); return; }
            new ProcessExplorer(this).Show(this);
        }
        public static void Log(Exception ex) => Log(ex.ToString());
        public static void Log(string message)
        {
            try
            {
                string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "vibranceGUI.log");
                File.AppendAllText(path, DateTime.Now.ToString("O") + " " + message + Environment.NewLine);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
