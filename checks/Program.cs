using System.Drawing.Imaging;
using System.Xml.Serialization;
using vibrance.GUI.common;
using vibrance.GUI.NVIDIA;
using System.Text.Json;

internal static class Checks
{
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            if (args.Length == 2 && args[0] == "--gpu-smoke")
            {
                using var backend = new NvidiaDisplayBackend();
                Assert(backend.DisplayNames.Count > 0, backend.InitializationError);
                var results = backend.DisplayNames.Select(backend.TestWriteAndRestore).ToList();
                File.WriteAllText(args[1], JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
                foreach (var result in results)
                    Console.WriteLine($"{result.Display}: native {result.Original} -> {result.Observed} (target {result.Target}) -> restored {result.Restored}; pass={result.Passed}");
                Assert(results.All(result => result.Passed), "NVIDIA hardware smoke did not read back the change and exact restoration. Inspect " + args[1]);
                return 0;
            }
            if (args.Length == 2 && args[0] == "--theme-switch")
            {
                CheckLiveThemeSwitch(args[1]);
                return 0;
            }
            if (args.Length == 2 && args[0] == "--hotkey-ui")
            {
                CheckHotkeyUi(args[1]);
                return 0;
            }
            if (args.Length == 1 && args[0] == "--hotkey-native")
            {
                CheckNativeHotkeys();
                return 0;
            }
            if (args.Length == 3 && args[0] == "--render")
            {
                Application.EnableVisualStyles();
                Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
                AppTheme.Initialize(Enum.Parse<ThemePreference>(args[1], true));
                using var form = new PreviewWindow();
                Render(form, args[2]);
                form.Size = form.MinimumSize;
                Render(form, Path.ChangeExtension(args[2], null) + "-minimum.png");
                var list = (ListView)form.Controls.Find("listApplications", true).Single();
                var modes = new List<ResolutionModeWrapper>
                {
                    new() { DmPelsWidth = 1920, DmPelsHeight = 1080, DmBitsPerPel = 32, DmDisplayFrequency = 144 },
                    new() { DmPelsWidth = 1280, DmPelsHeight = 720, DmBitsPerPel = 32, DmDisplayFrequency = 144 }
                };
                using var settingsForm = new PreviewSettingsWindow(list.Items[0], modes);
                Render(settingsForm, Path.ChangeExtension(args[2], null) + "-profile.png");
                using var processForm = new PreviewProcessesWindow(form);
                Render(processForm, Path.ChangeExtension(args[2], null) + "-processes.png");
                Console.WriteLine("Rendered " + args[1] + " UI without starting monitoring or reading user settings.");
                return 0;
            }
            string directory = Path.Combine(Path.GetTempPath(), "vibranceGUI-checks-" + Guid.NewGuid().ToString("N"), "æøå-日本語");
            Directory.CreateDirectory(directory);
            // Temp files are deliberately retained on failure for inspection.
            CheckSettings(directory);
            CheckEquality();
            RuntimeChecks.Run();
            HotkeyChecks.Run(directory);
            Console.WriteLine("PASS: settings roundtrip, independent recovery, Unicode paths, backups, ranges, profile validation and path equality.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void CheckHotkeyUi(string outputDirectory)
    {
        Application.EnableVisualStyles();
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
        Directory.CreateDirectory(outputDirectory);
        foreach (var theme in new[] { ThemePreference.Light, ThemePreference.Dark })
        {
            AppTheme.Initialize(theme);
            var backend = new PreviewProxy();
            using var main = new PreviewWindow(backend);
            PrepareOffscreen(main);
            Application.DoEvents();
            int mutations = backend.MutatingCalls;
            IntPtr mainHandle = main.Handle;
            var shortcut = (Button)main.Controls.Find("buttonPauseHotkey", true).Single();
            string originalLabel = shortcut.Text;
            int attempts = 0;
            bool collision = true;
            using var dialog = new PreviewHotkeyDialog(PauseHotkey.Default, keyData =>
            {
                attempts++;
                return collision ? "This shortcut is already in use.\n\nThe current shortcut remains active. Choose another key." : main.ApplyPauseHotkey(keyData);
            });
            PrepareOffscreen(dialog);
            Application.DoEvents();
            var field = (TextBox)dialog.Controls.Find("textBoxHotkey", true).Single();
            var save = (Button)dialog.Controls.Find("buttonSave", true).Single();
            var feedback = (Label)dialog.Controls.Find("labelHotkeyStatus", true).Single();
            var capture = field.GetType().GetMethod("ProcessCmdKey", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            object[] input = { Message.Create(field.Handle, 0x100, IntPtr.Zero, IntPtr.Zero), Keys.Control | Keys.Shift | Keys.P };
            Assert((bool)capture.Invoke(field, input)!, "The shortcut field did not consume a shortcut keydown.");
            Assert(dialog.KeyData == (Keys.Control | Keys.Shift | Keys.P) && field.Text == "Ctrl+Shift+P",
                "The actual shortcut input path did not record/display the selected chord.");
            dialog.CaptureShortcut(Keys.ControlKey | Keys.Control);
            dialog.CaptureShortcut(Keys.F12);
            Assert(dialog.KeyData == (Keys.Control | Keys.Shift | Keys.P), "Invalid input replaced the last valid shortcut.");
            save.PerformClick();
            Application.DoEvents();
            Assert(attempts == 1 && dialog.Visible && dialog.DialogResult != DialogResult.OK && shortcut.Text == originalLabel,
                "A collision closed the editor or changed the current shortcut.");
            CaptureWindow(dialog, Path.Combine(outputDirectory, theme + "-collision.png"));
            Assert(feedback.Bottom <= save.Parent!.Top && save.RectangleToScreen(save.ClientRectangle).Bottom <= dialog.RectangleToScreen(dialog.ClientRectangle).Bottom,
                "Shortcut feedback or Save button is clipped.");
            collision = false;
            save.PerformClick();
            Application.DoEvents();
            Assert(attempts == 2 && dialog.DialogResult == DialogResult.OK && shortcut.Text == "Ctrl+Shift+P",
                "Saving the shortcut did not immediately update the main window.");
            using var cancel = new PreviewHotkeyDialog(Keys.Control | Keys.Shift | Keys.P, _ => throw new InvalidOperationException("Cancel committed a candidate."));
            PrepareOffscreen(cancel);
            Application.DoEvents();
            cancel.CaptureShortcut(Keys.F8);
            CaptureWindow(cancel, Path.Combine(outputDirectory, theme + "-capture.png"));
            ((Button)cancel.Controls.Find("buttonCancel", true).Single()).PerformClick();
            Assert(cancel.DialogResult != DialogResult.OK && shortcut.Text == "Ctrl+Shift+P", "Cancel applied an unsaved candidate.");
            Assert(main.Handle == mainHandle && backend.MutatingCalls == mutations,
                "Shortcut editing recreated the main window or changed GPU/monitoring state.");
            CaptureWindow(main, Path.Combine(outputDirectory, theme + "-main.png"));
            main.Size = main.MinimumSize;
            CaptureWindow(main, Path.Combine(outputDirectory, theme + "-main-minimum.png"));
        }
        Console.WriteLine("PASS: actual shortcut keydown capture, validation, collision/retry and cancel; immediate label update; Light/Dark renders; unchanged main HWND and zero GPU/monitor lifecycle calls.");
    }

    private static void CheckNativeHotkeys()
    {
        // Hidden task-owned HWNDs only. No key injection, settings, GPU, or user-app calls.
        using var firstWindow = new Form();
        using var secondWindow = new Form();
        var first = new PauseHotkeyBinding();
        var second = new PauseHotkeyBinding();
        Keys chord = Keys.None;
        try
        {
            foreach (Keys key in new[] { Keys.F24, Keys.F23, Keys.F22, Keys.F21 })
                if (first.TrySet(firstWindow.Handle, Keys.Control | Keys.Alt | Keys.Shift | key, out _))
                { chord = first.KeyData; break; }
            Assert(chord != Keys.None, "No unused task-only shortcut was available for native registration smoke.");
            Assert(!second.TrySet(secondWindow.Handle, chord, out _), "Windows allowed conflicting global shortcut registrations.");
            Assert(first.IsRegistered && first.KeyData == chord, "A native collision released the working shortcut.");
            var oldMessage = Message.Create(firstWindow.Handle, 0x0312, (IntPtr)1,
                (IntPtr)(((int)(chord & Keys.KeyCode) << 16) | 7));
            Assert(first.Matches(oldMessage), "Native modifier/key message matching failed.");
            Keys replacement = Keys.None;
            foreach (Keys key in new[] { Keys.F20, Keys.F19, Keys.F18, Keys.F17 })
                if (first.TrySet(firstWindow.Handle, Keys.Control | Keys.Alt | Keys.Shift | key, out _))
                { replacement = first.KeyData; break; }
            Assert(replacement != Keys.None && !first.Matches(oldMessage), "Native rebind failed or accepted the stale original message.");
            Assert(second.TrySet(secondWindow.Handle, chord, out string failure), "Native rebind did not release the original shortcut: " + failure);
            Assert(first.TryClear(out failure) && second.TryClear(out failure), "Native shortcut cleanup failed: " + failure);
            Console.WriteLine("PASS: native RegisterHotKey collision, spare-ID rebind, original-key release, stale-message validation and cleanup on hidden task-owned windows; no physical keys injected.");
        }
        finally
        {
            first.TryClear(out _);
            second.TryClear(out _);
        }
    }

    private static void Render(Form form, string output)
    {
        // WM_PRINT skips children of invisible forms. Show only our fake-backed window
        // outside the desktop, with activation disabled, to create its native controls.
        form.ShowInTaskbar = false;
        form.StartPosition = FormStartPosition.Manual;
        form.Location = new Point(-32000, -32000);
        form.Show();
        Application.DoEvents();
        form.PerformLayout();
        using var bitmap = new Bitmap(form.Width, form.Height);
        form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        bitmap.Save(output, ImageFormat.Png);
        var layout = new List<object>();
        void Capture(Control control, string parent)
        {
            string id = parent + "/" + control.GetType().Name + ":" + control.Text;
            layout.Add(new { Id = id, Bounds = control.Bounds.ToString(), Preferred = control.PreferredSize.ToString(),
                control.AutoSize, Dock = control.Dock.ToString() });
            foreach (Control child in control.Controls) Capture(child, id);
        }
        Capture(form, "");
        File.WriteAllText(Path.ChangeExtension(output, ".layout.json"), JsonSerializer.Serialize(layout, new JsonSerializerOptions { WriteIndented = true }));
        form.Hide();
    }

    private static void CheckLiveThemeSwitch(string outputDirectory)
    {
        Application.EnableVisualStyles();
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
        AppTheme.Initialize(ThemePreference.Light);
        var backend = new PreviewProxy();
        using var main = new PreviewWindow(backend);
        var programs = (ListView)main.Controls.Find("listApplications", true).Single();
        var modes = new List<ResolutionModeWrapper>
        {
            new() { DmPelsWidth = 1920, DmPelsHeight = 1080, DmBitsPerPel = 32, DmDisplayFrequency = 144 },
            new() { DmPelsWidth = 1280, DmPelsHeight = 720, DmBitsPerPel = 32, DmDisplayFrequency = 120 }
        };
        using var profile = new PreviewSettingsWindow(programs.Items[0], modes, backend);
        using var processes = new PreviewProcessesWindow(main);
        var windows = new Form[] { main, profile, processes };
        foreach (var window in windows) PrepareOffscreen(window);
        Application.DoEvents();
        programs.Items[1].Selected = true;
        var resolution = Descendants(profile).OfType<ComboBox>().Single();
        resolution.SelectedIndex = 1;
        var gameLevel = Descendants(profile).OfType<TrackBar>().Single();
        gameLevel.Value = 49;
        var desktopLevel = Descendants(main).OfType<TrackBar>().Single();
        desktopLevel.Value = 19;
        var appearance = (ComboBox)main.Controls.Find("comboBoxTheme", true).Single();
        var originalControls = windows.SelectMany(window => Descendants(window)).ToArray();
        var handles = windows.Select(window => window.Handle).ToArray();
        int mutationsBefore = backend.MutatingCalls;
        int processId = Environment.ProcessId;
        Directory.CreateDirectory(outputDirectory);
        foreach (var theme in new[] { ThemePreference.Dark, ThemePreference.Light, ThemePreference.Dark, ThemePreference.System, ThemePreference.Light })
        {
            appearance.SelectedItem = theme;
            Application.DoEvents();
            Assert(AppTheme.Preference == theme && (ThemePreference)appearance.SelectedItem! == theme,
                "The appearance dropdown did not apply the selected theme immediately.");
            Assert(Environment.ProcessId == processId && windows.All(window => !window.IsDisposed), "Theme switching restarted/disposed a window or process.");
            Assert(windows.SelectMany(window => Descendants(window)).SequenceEqual(originalControls), "Theme switching replaced UI controls.");
            Assert(windows.Select(window => window.Handle).SequenceEqual(handles), "Theme switching recreated a top-level window.");
            Assert(programs.SelectedItems.Count == 1 && programs.SelectedItems[0].Text == "VALORANT", "Theme switching lost the selected game.");
            Assert(resolution.SelectedIndex == 1 && gameLevel.Value == 49 && desktopLevel.Value == 19, "Theme switching lost unsaved profile/desktop edits.");
            Assert(backend.MutatingCalls == mutationsBefore, "Theme switching changed vibrance or stopped/reinitialized monitoring.");
            bool dark = theme == ThemePreference.Dark || theme == ThemePreference.System && SystemColors.Control.GetBrightness() < 0.5f;
            foreach (var window in windows)
            {
                Assert((window.BackColor.GetBrightness() < 0.5f) == dark, "An existing window did not update its background.");
                foreach (var list in Descendants(window).OfType<ListView>())
                    Assert((list.BackColor.GetBrightness() < 0.5f) == dark && (list.ForeColor.GetBrightness() > 0.5f) == dark,
                        "An existing list retained the previous theme's colors.");
                CaptureWindow(window, Path.Combine(outputDirectory, theme + "-" + window.GetType().Name + ".png"));
                AssertSliderBackground(window, dark);
            }
            if (theme == ThemePreference.Dark)
            {
                // Windows controls can render inactive selections differently by OS.
                // A live switch must match a window created in the same dark mode.
                using var fresh = new PreviewWindow();
                PrepareOffscreen(fresh);
                Application.DoEvents();
                var freshPrograms = (ListView)fresh.Controls.Find("listApplications", true).Single();
                freshPrograms.Items[1].Selected = true;
                Assert(SelectionBackground(programs).ToArgb() == SelectionBackground(freshPrograms).ToArgb(),
                    "Live dark mode rendered a different inactive selection than startup dark mode.");
                CaptureWindow(fresh, Path.Combine(outputDirectory, "Dark-FreshPreviewWindow.png"));
                AssertSliderBackground(fresh, dark: true);
                using var freshProfile = new PreviewSettingsWindow(freshPrograms.Items[0], modes);
                PrepareOffscreen(freshProfile);
                Application.DoEvents();
                AssertSliderBackground(freshProfile, dark: true);
            }
        }
        // SetColorMode pumps native messages; a queued second choice can arrive before
        // the first refresh completes. The final choice must win in that same process.
        main.BeginInvoke((Action)(() => appearance.SelectedItem = ThemePreference.Light));
        appearance.SelectedItem = ThemePreference.Dark;
        Application.DoEvents();
        Assert(AppTheme.Preference == ThemePreference.Light && (ThemePreference)appearance.SelectedItem! == ThemePreference.Light,
            $"A reentrant theme selection did not keep the user's final choice (selected={appearance.SelectedItem}, applied={AppTheme.Preference}).");
        Assert(windows.Select(window => window.Handle).SequenceEqual(handles) && backend.MutatingCalls == mutationsBefore,
            "A reentrant theme selection recreated a top-level window or changed monitoring.");
        appearance.SelectedItem = ThemePreference.System;
        desktopLevel.BackColor = Color.Magenta;
        int uiThread = Environment.CurrentManagedThreadId;
        int refreshThread = 0;
        desktopLevel.BackColorChanged += (_, _) => refreshThread = Environment.CurrentManagedThreadId;
        PostSystemRefresh();
        Application.DoEvents();
        Assert(refreshThread == uiThread && desktopLevel.BackColor.ToArgb() == SystemColors.Control.ToArgb(),
            "System preference notification did not refresh existing controls on their UI thread.");
        Assert(AppTheme.Preference == ThemePreference.System && (ThemePreference)appearance.SelectedItem! == ThemePreference.System,
            "System preference notification changed the saved theme choice.");

        // Autostart can create only a hidden HWND, without adding the form to OpenForms.
        // It still needs updated controls and tray colors when Windows changes appearance.
        var hiddenBackend = new PreviewProxy();
        using (var hidden = new PreviewWindow(hiddenBackend))
        {
            hidden.SetAllowVisible(false);
            hidden.ShowInTaskbar = false;
            hidden.Show();
            Assert(!hidden.Visible && hidden.IsHandleCreated, "Minimized preview unexpectedly became visible.");
            var hiddenSlider = Descendants(hidden).OfType<TrackBar>().Single();
            hiddenSlider.BackColor = Color.Magenta;
            PostSystemRefresh();
            Application.DoEvents();
            Assert(hiddenSlider.BackColor.ToArgb() == SystemColors.Control.ToArgb() && !hidden.Visible,
                "System preference notification skipped or showed the minimized main window.");
            PostSystemRefresh();
            hidden.Dispose(); // A queued callback must safely skip an owner disposed before dispatch.
            Application.DoEvents();
        }
        PostSystemRefresh();
        Application.DoEvents();
        Assert(windows.Select(window => window.Handle).SequenceEqual(handles) && backend.MutatingCalls == mutationsBefore
            && hiddenBackend.MutatingCalls == 0 && resolution.SelectedIndex == 1 && gameLevel.Value == 49 && desktopLevel.Value == 19,
            "Notification or disposal changed window identity, unsaved edits, or monitoring.");
        Console.WriteLine("PASS: live/reentrant dropdown Light/Dark/System switching; existing windows/controls, selection and unsaved edits preserved; rendered slider backgrounds; UI-thread system notifications, minimized startup and disposal; zero GPU/monitor lifecycle calls.");
    }

    private static void PostSystemRefresh()
    {
        var task = Task.Run(AppTheme.RefreshSystemPreferences);
        Assert(task.Wait(TimeSpan.FromSeconds(10)), "Posting a system theme refresh blocked a worker thread.");
        task.GetAwaiter().GetResult();
    }

    private static void AssertSliderBackground(Control parent, bool dark)
    {
        foreach (var slider in Descendants(parent).OfType<TrackBar>())
        {
            // A stale native brush can disagree with BackColor. Sample client pixels
            // outside the thumb/rail to verify the actual rendered background.
            using var bitmap = new Bitmap(slider.Width, slider.Height);
            slider.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
            var background = bitmap.GetPixel(3, 3);
            Assert((background.GetBrightness() < 0.5f) == dark,
                $"An existing {parent.GetType().Name} slider rendered the previous theme's background ({background}).");
        }
    }

    private static IEnumerable<Control> Descendants(Control parent)
    {
        foreach (Control control in parent.Controls)
        {
            yield return control;
            foreach (var child in Descendants(control)) yield return child;
        }
    }

    private static Color SelectionBackground(ListView list)
    {
        using var bitmap = new Bitmap(list.Width, list.Height);
        list.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
        var row = list.SelectedItems[0].Bounds;
        return bitmap.GetPixel(list.Columns[0].Width - 8, row.Top + row.Height / 2);
    }

    private static void PrepareOffscreen(Form form)
    {
        form.ShowInTaskbar = false;
        form.StartPosition = FormStartPosition.Manual;
        form.Location = new Point(-32000, -32000);
        form.Show();
    }

    private static void CaptureWindow(Form form, string output)
    {
        form.PerformLayout();
        using var bitmap = new Bitmap(form.Width, form.Height);
        form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
        bitmap.Save(output, ImageFormat.Png);
    }

    private static void CheckSettings(string directory)
    {
        var settings = new SettingsController(directory);
        settings.ReadVibranceSettings(GraphicsAdapter.Nvidia, out int level, out bool primary, out bool never, out var profiles);
        Assert(level == 0 && !primary && !never && profiles.Count == 0, "Fresh settings defaults changed.");
        var game = new ApplicationSetting("Game", @"C:\Games\Game.exe", 50, null, false);
        Assert(settings.SetVibranceSettings("12", "True", "True", new List<ApplicationSetting> { game }), "Unicode settings save failed: " + settings.LastError);
        settings.ReadVibranceSettings(GraphicsAdapter.Nvidia, out level, out primary, out never, out profiles);
        Assert(level == 12 && primary && never && profiles.Single().IngameLevel == 50, "Low desktop level/profile roundtrip failed.");
        File.Delete(Path.Combine(directory, "applicationData.xml"));
        settings.ReadVibranceSettings(GraphicsAdapter.Nvidia, out level, out primary, out never, out profiles);
        Assert(level == 12 && primary && never && profiles.Count == 0, "Missing profiles discarded valid desktop settings.");
        Assert(settings.SetVibranceSettings("20", "False", "False", new List<ApplicationSetting> { game }), "Profile save failed.");
        Assert(settings.SetVibranceSettings("22", "True", "False", new List<ApplicationSetting> { game }), "Atomic replacement failed.");
        using (var reader = File.OpenRead(Path.Combine(directory, "applicationData.xml.bak")))
            Assert(((List<ApplicationSetting>)new XmlSerializer(typeof(List<ApplicationSetting>)).Deserialize(reader)!).Count == 1, "Previous XML backup is unreadable.");
        Assert(settings.SetVibranceSetting("inactiveValue", "9999"), "INI write failed.");
        settings.ReadVibranceSettings(GraphicsAdapter.Nvidia, out level, out primary, out never, out profiles);
        Assert(level == 0 && profiles.Count == 1, "Invalid desktop level discarded profiles or wasn't bounded.");
        File.WriteAllText(Path.Combine(directory, "vibranceGUI.ini"), "[Settings]\ninactiveValue=wrong\naffectPrimaryMonitorOnly=wrong\nneverSwitchResolution=True\n");
        settings.ReadVibranceSettings(GraphicsAdapter.Nvidia, out level, out primary, out never, out profiles);
        Assert(level == 0 && !primary && never && profiles.Count == 1, "Independent malformed fields weren't recovered.");
        File.WriteAllText(Path.Combine(directory, "applicationData.xml"), "<broken");
        settings.ReadVibranceSettings(GraphicsAdapter.Nvidia, out level, out primary, out never, out profiles);
        Assert(level == 0 && profiles.Count == 0 && settings.LastError != null, "Malformed profiles didn't surface a recoverable error.");
        Assert(settings.BackupUnreadableProfiles(), "Unreadable profiles could not be preserved before an explicit edit.");
        Assert(Directory.GetFiles(directory, "applicationData.xml.*.corrupt").Any(path => File.ReadAllText(path) == "<broken"),
            "Explicit edit backup changed the unreadable original.");
        var duplicates = new List<ApplicationSetting>
        {
            new("One", @"C:\Games\Game.exe", 9999, null, true),
            new("Duplicate", @"c:\games\GAME.EXE", 50, null, false),
            new("Invalid", "", 20, null, false)
        };
        Assert(settings.SetVibranceSettings("0", "False", "False", duplicates), "Validation setup save failed.");
        settings.ReadVibranceSettings(GraphicsAdapter.Nvidia, out level, out primary, out never, out profiles);
        Assert(level == 0 && profiles.Count == 1 && profiles[0].IngameLevel == 63 && !profiles[0].IsResolutionChangeNeeded,
            "Invalid/duplicate profiles or null resolution weren't normalized.");
        settings.ReadVibranceSettings(GraphicsAdapter.Amd, out level, out primary, out never, out profiles);
        Assert(level == 0 && profiles.Single().IngameLevel == 300, "AMD ranges weren't applied on read.");
    }

    private static void CheckEquality()
    {
        var lower = new ApplicationSetting { FileName = @"c:\games\game.exe" };
        var upper = new ApplicationSetting { FileName = @"C:\GAMES\GAME.EXE" };
        Assert(lower.Equals(upper) && lower.GetHashCode() == upper.GetHashCode(), "Windows executable identity is case sensitive.");
        Assert(new ApplicationSetting().GetHashCode() == 0, "Incomplete profile hash throws.");
        var a = new ResolutionModeWrapper { DmPelsWidth = 1920, DmPelsHeight = 1080, DmDisplayFrequency = 144 };
        var b = new ResolutionModeWrapper { DmPelsWidth = 1920, DmPelsHeight = 1080, DmDisplayFrequency = 144 };
        Assert(a.Equals(b) && a.GetHashCode() == b.GetHashCode(), "Resolution equality/hash mismatch.");
    }

    private sealed class PreviewProxy : IVibranceProxy
    {
        public int MutatingCalls { get; private set; }
        public GraphicsAdapter GraphicsAdapter => GraphicsAdapter.Nvidia;
        public string InitializationError => "";
        public VibranceInfo GetVibranceInfo() => new() { isInitialized = true, szGpuName = "Preview GPU" };
        public void SetApplicationSettings(List<ApplicationSetting> profiles) => MutatingCalls++;
        public void SetShouldRun(bool value) => MutatingCalls++;
        public void SetVibranceWindowsLevel(int value) => MutatingCalls++;
        public void SetVibranceIngameLevel(int value) => MutatingCalls++;
        public void SetAffectPrimaryMonitorOnly(bool value) => MutatingCalls++;
        public void SetNeverSwitchResolution(bool value) => MutatingCalls++;
        public void HandleDvcExit() => MutatingCalls++;
        public bool UnloadLibraryEx() { MutatingCalls++; return true; }
    }

    private static string FormatLevel(int value) => Math.Round(50 + value * 50.0 / 63) + "%";

    private sealed class PreviewWindow : VibranceGUI
    {
        public PreviewWindow() : base((_, _) => new PreviewProxy(), 0, 0, 63, 0, FormatLevel, initializeRuntime: false) { }
        public PreviewWindow(PreviewProxy backend) : base((_, _) => backend, 0, 0, 63, 0, FormatLevel, initializeRuntime: false) { }
        protected override bool ShowWithoutActivation => true;
    }
    private sealed class PreviewSettingsWindow : VibranceSettings
    {
        public PreviewSettingsWindow(ListViewItem item, List<ResolutionModeWrapper> modes)
            : base(new PreviewProxy(), 0, 63, 45, item,
                new ApplicationSetting("Counter-Strike 2", "preview-cs2.exe", 45, modes[0], false), modes, FormatLevel) { }
        public PreviewSettingsWindow(ListViewItem item, List<ResolutionModeWrapper> modes, PreviewProxy backend)
            : base(backend, 0, 63, 45, item,
                new ApplicationSetting("Counter-Strike 2", "preview-cs2.exe", 45, modes[0], false), modes, FormatLevel) { }
        protected override bool ShowWithoutActivation => true;
    }
    private sealed class PreviewProcessesWindow : ProcessExplorer
    {
        public PreviewProcessesWindow(VibranceGUI parent) : base(parent, initializeProcesses: false) { }
        protected override bool ShowWithoutActivation => true;
    }
    private sealed class PreviewHotkeyDialog : HotkeyDialog
    {
        public PreviewHotkeyDialog(Keys current, Func<Keys, string> apply) : base(current, apply) { }
        protected override bool ShowWithoutActivation => true;
    }
}
