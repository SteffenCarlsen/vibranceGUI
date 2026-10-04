using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Forms;
using Microsoft.Win32;

namespace vibrance.GUI.common
{
    public enum ThemePreference
    {
        System,
        Light,
        Dark
    }

    internal static partial class AppTheme
    {
        internal sealed class Preferences
        {
            public Preferences() { }
            public ThemePreference Theme { get; set; }
            public bool EnablePauseHotkey { get; set; }
            public Keys PauseHotkeyKeyData { get; set; } = PauseHotkey.Default;
        }

        private static Preferences _preferences = new Preferences();
        private static bool _applying;
        private static ThemePreference? _pendingPreference;
        private static WeakReference<VibranceGUI> _systemOwner;
        private static readonly string PreferencesPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "vibranceGUI", "appearance.json");

        public static ThemePreference Preference => _preferences.Theme;
        public static bool EnablePauseHotkey => _preferences.EnablePauseHotkey;
        public static Keys PauseHotkeyKeyData => _preferences.PauseHotkeyKeyData;
        public static Color LinkColor => !SystemInformation.HighContrast && SystemColors.Control.GetBrightness() < 0.5f
            ? Color.FromArgb(113, 184, 255) : SystemColors.HotTrack;
        public static Color SuccessColor => SystemInformation.HighContrast
            ? SystemColors.ControlText
            : SystemColors.Control.GetBrightness() < 0.5f
                ? Color.FromArgb(93, 219, 182) : Color.FromArgb(0, 112, 83);

        // Call before creating any window. Native controls and dialogs then share the theme.
        public static void Initialize()
        {
            _preferences = ReadPreferences(PreferencesPath);
            Initialize(_preferences.Theme);
        }

        internal static Preferences ReadPreferences(string path)
        {
            var preferences = new Preferences();
            try
            {
                if (File.Exists(path))
                {
                    preferences = JsonSerializer.Deserialize<Preferences>(File.ReadAllText(path))
                        ?? new Preferences();
                    if (!Enum.IsDefined(preferences.Theme)) preferences.Theme = ThemePreference.System;
                    if (!PauseHotkey.IsValid(preferences.PauseHotkeyKeyData)) preferences.PauseHotkeyKeyData = PauseHotkey.Default;
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is JsonException)
            {
                preferences = new Preferences();
            }
            return preferences;
        }

        public static void Initialize(ThemePreference preference)
        {
            _preferences.Theme = preference;
            Application.SetColorMode(preference switch
            {
                ThemePreference.Dark => SystemColorMode.Dark,
                ThemePreference.Light => SystemColorMode.Classic,
                _ => SystemColorMode.System
            });
        }

        public static void Apply(ThemePreference preference)
        {
            // SetColorMode pumps messages while broadcasting the palette change. A second
            // selection must wait until the current pass has refreshed all existing controls.
            _pendingPreference = preference;
            if (_applying) return;
            _applying = true;
            try
            {
                while (_pendingPreference is ThemePreference next)
                {
                    _pendingPreference = null;
                    Initialize(next);
                    var forms = Application.OpenForms.Cast<Form>().ToList();
                    if (_systemOwner != null && _systemOwner.TryGetTarget(out var owner)
                        && owner.CanApplyTheme && !forms.Contains(owner))
                        forms.Add(owner); // Fresh minimized startup has a handle before it enters OpenForms.
                    foreach (Form form in forms)
                    {
                        if (form.IsDisposed || form.Disposing || form is VibranceGUI closingMain && !closingMain.CanApplyTheme) continue;
                        RefreshControl(form);
                        if (form is VibranceGUI main) main.RefreshThemeColors();
                    }
                }
            }
            finally { _applying = false; }
        }

        public static void WatchSystemPreferences(VibranceGUI owner)
        {
            if (_systemOwner == null) SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
            _systemOwner = new WeakReference<VibranceGUI>(owner);
        }

        public static void UnwatchSystemPreferences(VibranceGUI owner)
        {
            if (_systemOwner == null || !_systemOwner.TryGetTarget(out var current) || current == owner)
            {
                var remaining = Application.OpenForms.OfType<VibranceGUI>()
                    .FirstOrDefault(window => window != owner && window.CanApplyTheme);
                if (remaining != null)
                {
                    _systemOwner = new WeakReference<VibranceGUI>(remaining);
                    return;
                }
                SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
                _systemOwner = null;
            }
        }

        private static void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs args)
        {
            if (args.Category is UserPreferenceCategory.Accessibility or UserPreferenceCategory.Color
                or UserPreferenceCategory.General or UserPreferenceCategory.VisualStyle)
                RefreshSystemPreferences();
        }

        internal static void RefreshSystemPreferences()
        {
            var reference = _systemOwner;
            if (reference == null || !reference.TryGetTarget(out var owner) || !owner.CanApplyTheme || !owner.IsHandleCreated) return;
            try
            {
                owner.BeginInvoke((Action)(() =>
                {
                    if (owner.CanApplyTheme) Apply(_pendingPreference ?? Preference);
                }));
            }
            catch (InvalidOperationException) { } // The owner can close between the checks and the post.
        }

        internal static void RefreshMenu(ContextMenuStrip menu)
        {
            if (!menu.IsDisposed) RefreshControl(menu);
        }

        private static void RefreshControl(Control control)
        {
            if (control.IsDisposed || control.Disposing) return;
            if (control is Form)
            {
                control.BackColor = SystemColors.Control;
                control.ForeColor = SystemColors.ControlText;
            }
            if (control is LinkLabel link)
                link.LinkColor = link.ActiveLinkColor = link.VisitedLinkColor = LinkColor;
            if (control is TrackBar slider)
                // A known-color brush can retain the old native system palette after a live switch.
                slider.BackColor = Color.FromArgb(SystemColors.Control.ToArgb());
            if (control is Button button)
            {
                button.FlatAppearance.BorderColor = SystemColors.ControlDark;
                if (button.FlatStyle is FlatStyle.Flat or FlatStyle.Popup)
                {
                    // WinForms caches the owner-draw adapter chosen under the old color mode.
                    // A measured alternate style replaces it without recreating the HWND.
                    FlatStyle style = button.FlatStyle;
                    button.FlatStyle = style == FlatStyle.Flat ? FlatStyle.Popup : FlatStyle.Flat;
                    _ = button.GetPreferredSize(Size.Empty);
                    button.FlatStyle = style;
                    _ = button.GetPreferredSize(Size.Empty);
                }
            }
            if (control is ToolStrip menu)
            {
                menu.RenderMode = ToolStripRenderMode.System;
                menu.BackColor = SystemColors.Control;
                menu.ForeColor = SystemColors.ControlText;
            }
            if (control.IsHandleCreated) RefreshNativeTheme(control);
            foreach (Control child in control.Controls) RefreshControl(child);
            control.Invalidate(true);
        }

        private static void RefreshNativeTheme(Control control)
        {
            bool dark = Application.IsDarkModeEnabled;
            IntPtr window = control.Handle; // Borrowed handles stay owned by their controls.
            SetTheme(window, dark ? control is ComboBox ? "DarkMode_CFD" : "DarkMode_Explorer" : null);
            if (control is ListView list)
            {
                list.BackColor = SystemColors.Window;
                list.ForeColor = SystemColors.WindowText;
                SendMessageW(window, 0x1001, IntPtr.Zero, (IntPtr)ColorTranslator.ToWin32(list.BackColor));
                SendMessageW(window, 0x1024, IntPtr.Zero, (IntPtr)ColorTranslator.ToWin32(list.ForeColor));
                SendMessageW(window, 0x1026, IntPtr.Zero, (IntPtr)(-1));
                SetTheme(SendMessageW(window, 0x101F, IntPtr.Zero, IntPtr.Zero), dark ? "DarkMode_ItemsView" : null);
            }
            if (control is ComboBox)
            {
                var info = new ComboBoxInfo { Size = (uint)Marshal.SizeOf<ComboBoxInfo>() };
                if (GetComboBoxInfo(window, ref info) == 0) throw new Win32Exception(Marshal.GetLastPInvokeError());
                SetTheme(info.List, dark ? "DarkMode_Explorer" : null);
            }
            if (control is Form)
            {
                int enabled = dark ? 1 : 0;
                int result = DwmSetWindowAttribute(window, 20, ref enabled, sizeof(int));
                if (result < 0) Debug.WriteLine("The window caption does not support the selected color mode: " + result);
            }
        }

        private static void SetTheme(IntPtr window, string theme)
        {
            if (window != IntPtr.Zero) Marshal.ThrowExceptionForHR(SetWindowTheme(window, theme, null));
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRectangle { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        private struct ComboBoxInfo
        {
            public uint Size;
            public NativeRectangle ItemBounds, ButtonBounds;
            public uint ButtonState;
            public IntPtr Combo, Item, List;
        }

        [LibraryImport("uxtheme.dll", StringMarshalling = StringMarshalling.Utf16)]
        private static partial int SetWindowTheme(IntPtr window, string theme, string classes);
        [LibraryImport("user32.dll", SetLastError = true)]
        private static partial int GetComboBoxInfo(IntPtr window, ref ComboBoxInfo info);
        [LibraryImport("user32.dll")]
        private static partial IntPtr SendMessageW(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
        [LibraryImport("dwmapi.dll")]
        private static partial int DwmSetWindowAttribute(IntPtr window, uint attribute, ref int value, uint size);

        public static void Save(ThemePreference preference, bool enablePauseHotkey, Keys keyData) =>
            Save(preference, enablePauseHotkey, keyData, PreferencesPath);

        internal static void Save(ThemePreference preference, bool enablePauseHotkey, Keys keyData, string path)
        {
            if (!PauseHotkey.IsValid(keyData)) throw new ArgumentException("Invalid pause shortcut.", nameof(keyData));
            var preferences = new Preferences { Theme = preference, EnablePauseHotkey = enablePauseHotkey, PauseHotkeyKeyData = keyData };
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            string temporaryPath = path + ".tmp";
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(preferences));
            File.Move(temporaryPath, path, true);
            _preferences = preferences;
        }

        public static void Configure(Form form)
        {
            form.Font = new Font("Segoe UI", 10F, FontStyle.Regular, GraphicsUnit.Point);
            form.BackColor = SystemColors.Control;
            form.ForeColor = SystemColors.ControlText;
        }

        public static Button Button(string text, EventHandler click)
        {
            var button = new Button
            {
                Text = text,
                AutoSize = true,
                MinimumSize = new Size(96, 36),
                Padding = new Padding(12, 4, 12, 4),
                FlatStyle = FlatStyle.Flat,
                Margin = new Padding(0, 0, 8, 0),
                UseVisualStyleBackColor = true
            };
            button.FlatAppearance.BorderColor = SystemColors.ControlDark;
            button.Click += click;
            return button;
        }
    }
}
