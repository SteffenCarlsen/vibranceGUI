using System;
using System.Drawing;
using System.IO;
using System.Text.Json;
using System.Windows.Forms;

namespace vibrance.GUI.common
{
    public enum ThemePreference
    {
        System,
        Light,
        Dark
    }

    internal static class AppTheme
    {
        private sealed class Preferences
        {
            public Preferences() { }
            public ThemePreference Theme { get; set; }
            public bool EnablePauseHotkey { get; set; }
        }

        private static Preferences _preferences = new Preferences();
        private static readonly string PreferencesPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "vibranceGUI", "appearance.json");

        public static ThemePreference Preference => _preferences.Theme;
        public static bool EnablePauseHotkey => _preferences.EnablePauseHotkey;
        public static Color LinkColor => !SystemInformation.HighContrast && SystemColors.Control.GetBrightness() < 0.5f
            ? Color.FromArgb(113, 184, 255) : SystemColors.HotTrack;
        public static Color SuccessColor => SystemInformation.HighContrast
            ? SystemColors.ControlText
            : SystemColors.Control.GetBrightness() < 0.5f
                ? Color.FromArgb(93, 219, 182) : Color.FromArgb(0, 112, 83);

        // Call before creating any window. Native controls and dialogs then share the theme.
        public static void Initialize()
        {
            try
            {
                if (File.Exists(PreferencesPath))
                {
                    _preferences = JsonSerializer.Deserialize<Preferences>(File.ReadAllText(PreferencesPath))
                        ?? new Preferences();
                    if (!Enum.IsDefined(_preferences.Theme))
                        _preferences.Theme = ThemePreference.System;
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is JsonException)
            {
                _preferences = new Preferences();
            }
            Initialize(_preferences.Theme);
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

        public static void Save(ThemePreference preference, bool enablePauseHotkey)
        {
            var preferences = new Preferences { Theme = preference, EnablePauseHotkey = enablePauseHotkey };
            Directory.CreateDirectory(Path.GetDirectoryName(PreferencesPath));
            string temporaryPath = PreferencesPath + ".tmp";
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(preferences));
            File.Move(temporaryPath, PreferencesPath, true);
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
