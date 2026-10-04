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
            if (args.Length == 2 && args[0] == "--dropdowns")
            {
                CheckDropdowns(args[1]);
                return 0;
            }
            if (args.Length == 3 && args[0] == "--render")
            {
                Application.EnableVisualStyles();
                Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
                AppTheme.Initialize(Enum.Parse<ThemePreference>(args[1], true));
                if (AppTheme.Preference == ThemePreference.System)
                    Assert(Application.IsDarkModeEnabled == (!SystemInformation.HighContrast && SystemPrefersDark()),
                        "System startup did not follow the Windows apps appearance setting before creating any forms.");
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
                using var about = new PreviewAboutDialog();
                Render(about, Path.ChangeExtension(args[2], null) + "-about.png");
                about.Size = about.MinimumSize;
                Render(about, Path.ChangeExtension(args[2], null) + "-about-minimum.png");
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
            CheckThemeResolution();
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
            Assert(string.IsNullOrEmpty(feedback.Text), "A fresh shortcut editor displayed stale feedback.");
            CheckHotkeyWidths(dialog, Path.Combine(outputDirectory, theme + "-compact-startup"), compact: true);
            var capture = field.GetType().GetMethod("ProcessCmdKey", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            object[] input = { Message.Create(field.Handle, 0x100, IntPtr.Zero, IntPtr.Zero), Keys.Control | Keys.Shift | Keys.P };
            Assert((bool)capture.Invoke(field, input)!, "The shortcut field did not consume a shortcut keydown.");
            Assert(dialog.KeyData == (Keys.Control | Keys.Shift | Keys.P) && field.Text == "Ctrl+Shift+P",
                "The actual shortcut input path did not record/display the selected chord.");
            Assert(string.IsNullOrEmpty(feedback.Text), "A valid captured shortcut retained feedback.");
            CheckHotkeyWidths(dialog, Path.Combine(outputDirectory, theme + "-compact-valid"), compact: true);
            dialog.CaptureShortcut(Keys.ControlKey | Keys.Control);
            dialog.CaptureShortcut(Keys.F12);
            Assert(dialog.KeyData == (Keys.Control | Keys.Shift | Keys.P), "Invalid input replaced the last valid shortcut.");
            save.PerformClick();
            Application.DoEvents();
            Assert(attempts == 1 && dialog.Visible && dialog.DialogResult != DialogResult.OK && shortcut.Text == originalLabel,
                "A collision closed the editor or changed the current shortcut.");
            CaptureWindow(dialog, Path.Combine(outputDirectory, theme + "-collision.png"));
            CheckHotkeyWidths(dialog, Path.Combine(outputDirectory, theme + "-collision-layout"), compact: false);
            Assert(dialog.KeyData == (Keys.Control | Keys.Shift | Keys.P) && shortcut.Text == originalLabel && attempts == 1,
                "Resizing collision feedback changed the candidate/current shortcut or retried its registration.");
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
            CheckHotkeyWidths(cancel, Path.Combine(outputDirectory, theme + "-compact-cancel"), compact: true);
            ((Button)cancel.Controls.Find("buttonCancel", true).Single()).PerformClick();
            Assert(cancel.DialogResult != DialogResult.OK && shortcut.Text == "Ctrl+Shift+P", "Cancel applied an unsaved candidate.");

            // Simulate the actual partial-success contract: the session chord changes,
            // persistence fails, and the editor remains open to show the complete error.
            const string saveFailure = "The shortcut was applied for this session, but could not be saved.\n\n"
                + "Access to the path 'C:\\Users\\Example\\AppData\\Local\\vibranceGUI\\appearance.json' is denied.\n\n"
                + "The current shortcut remains available for this session. Check access to the appearance settings folder and retry saving before closing the editor.";
            int failedSaveAttempts = 0;
            using var failedSave = new PreviewHotkeyDialog(Keys.Control | Keys.Shift | Keys.P, keyData =>
            {
                failedSaveAttempts++;
                Assert(main.ApplyPauseHotkey(keyData) == null, "Fake session shortcut application failed.");
                return saveFailure;
            });
            PrepareOffscreen(failedSave);
            Application.DoEvents();
            failedSave.CaptureShortcut(Keys.Control | Keys.Alt | Keys.F9);
            CheckHotkeyWidths(failedSave, Path.Combine(outputDirectory, theme + "-save-failure-before"), compact: true);
            ((Button)failedSave.Controls.Find("buttonSave", true).Single()).PerformClick();
            Application.DoEvents();
            var failureFeedback = (Label)failedSave.Controls.Find("labelHotkeyStatus", true).Single();
            Assert(failedSaveAttempts == 1 && failedSave.Visible && failedSave.DialogResult != DialogResult.OK &&
                failureFeedback.Text == saveFailure && shortcut.Text == PauseHotkey.Format(Keys.Control | Keys.Alt | Keys.F9),
                "A persistence failure closed the editor, hid part of its feedback, or lost the applied session shortcut.");
            CheckHotkeyWidths(failedSave, Path.Combine(outputDirectory, theme + "-save-failure"), compact: false);
            Assert(failedSaveAttempts == 1 && failedSave.KeyData == (Keys.Control | Keys.Alt | Keys.F9),
                "Error layout resizing retried persistence or lost the current candidate.");
            string sessionLabel = shortcut.Text;
            ((Button)failedSave.Controls.Find("buttonCancel", true).Single()).PerformClick();
            Assert(failedSave.DialogResult != DialogResult.OK && shortcut.Text == sessionLabel,
                "Closing a failed-save editor reverted the successfully applied session chord.");

            // Exercise WinForms' autoscale bounds and larger glyphs at synthetic 150%.
            // PerformAutoScale does not itself change the native monitor DPI/font context.
            // DeviceDpi stays real; this is a layout stress case, not WM_DPICHANGED proof.
            using var scaled = new PreviewHotkeyDialog(PauseHotkey.Default, _ => saveFailure);
            int unscaledMinimumWidth = scaled.MinimumSize.Width;
            var scaledField = (TextBox)scaled.Controls.Find("textBoxHotkey", true).Single();
            float unscaledFontSize = scaledField.Font.SizeInPoints;
            var fontSources = Descendants(scaled).Prepend(scaled).Select(control => (Control: control, Font: control.Font)).ToArray();
            var largerFonts = new List<Font>();
            scaled.Disposed += (_, _) => { foreach (var font in largerFonts) font.Dispose(); };
            SizeF actualScale = scaled.CurrentAutoScaleDimensions;
            scaled.AutoScaleDimensions = new SizeF(actualScale.Width / 1.5F, actualScale.Height / 1.5F);
            scaled.PerformAutoScale();
            foreach (var source in fontSources)
            {
                var font = new Font(source.Font.FontFamily, source.Font.SizeInPoints * 1.5F, source.Font.Style, GraphicsUnit.Point);
                largerFonts.Add(font);
                source.Control.Font = font;
            }
            PrepareOffscreen(scaled);
            Application.DoEvents();
            Assert(scaled.MinimumSize.Width >= unscaledMinimumWidth * 1.45 && scaledField.Font.SizeInPoints >= unscaledFontSize * 1.45,
                "The synthetic 150% preview did not scale both dialog dimensions and shortcut text.");
            int compactScaledHeight = scaled.ClientSize.Height;
            CheckHotkeyWidths(scaled, Path.Combine(outputDirectory, theme + "-synthetic-150-compact"), compact: true, syntheticScalePercent: 150);
            ((Button)scaled.Controls.Find("buttonSave", true).Single()).PerformClick();
            Application.DoEvents();
            Assert(scaled.Visible && scaled.DialogResult != DialogResult.OK && scaled.ClientSize.Height > compactScaledHeight,
                "Synthetic 150% multiline feedback did not expand the same compact editor.");
            CheckHotkeyWidths(scaled, Path.Combine(outputDirectory, theme + "-synthetic-150-error"), compact: false, syntheticScalePercent: 150);
            scaled.CaptureShortcut(Keys.Control | Keys.Shift | Keys.F10);
            Application.DoEvents();
            Assert(scaled.ClientSize.Height <= compactScaledHeight + 2,
                "Clearing feedback did not shrink the scaled editor back to its compact content height.");
            CheckHotkeyWidths(scaled, Path.Combine(outputDirectory, theme + "-synthetic-150-cleared"), compact: true, syntheticScalePercent: 150);
            Assert(main.Handle == mainHandle && backend.MutatingCalls == mutations,
                "Shortcut editing recreated the main window or changed GPU/monitoring state.");
            CaptureWindow(main, Path.Combine(outputDirectory, theme + "-main.png"));
            main.Size = main.MinimumSize;
            CaptureWindow(main, Path.Combine(outputDirectory, theme + "-main-minimum.png"));
        }
        Console.WriteLine("PASS: actual shortcut capture, validation, collision/retry/cancel and session-only save failure; compact/default/minimum-width and synthetic 150% layouts; Light/Dark renders with DeviceDpi evidence; unchanged HWND and zero GPU/monitor lifecycle calls.");
    }

    private static void CheckHotkeyWidths(PreviewHotkeyDialog dialog, string outputPrefix, bool compact, int syntheticScalePercent = 100)
    {
        int originalWidth = dialog.Width;
        IntPtr originalHandle = dialog.Handle;
        Keys originalKey = dialog.KeyData;
        var originalControls = Descendants(dialog).ToArray();
        int minimumWidth = dialog.MinimumSize.Width;
        Assert(minimumWidth > 0 && minimumWidth <= originalWidth, "The shortcut editor has no usable minimum width.");
        try
        {
            foreach (var size in new[] { (Width: originalWidth, Name: "default"), (Width: minimumWidth, Name: "minimum-width") })
            {
                dialog.Width = size.Width;
                dialog.PerformLayout();
                Application.DoEvents();
                Assert(dialog.Width == size.Width,
                    $"The shortcut editor rejected its requested {size.Name} width ({size.Width}); actual width {dialog.Width}.");
                Assert(dialog.Handle == originalHandle && dialog.KeyData == originalKey &&
                    Descendants(dialog).SequenceEqual(originalControls), "Shortcut layout resizing recreated controls or changed the captured chord.");
                var field = (TextBox)dialog.Controls.Find("textBoxHotkey", true).Single();
                var feedback = (Label)dialog.Controls.Find("labelHotkeyStatus", true).Single();
                var save = (Button)dialog.Controls.Find("buttonSave", true).Single();
                var cancel = (Button)dialog.Controls.Find("buttonCancel", true).Single();
                Control actions = save.Parent!;
                Rectangle client = dialog.RectangleToScreen(dialog.ClientRectangle);
                Rectangle fieldBounds = field.RectangleToScreen(new Rectangle(Point.Empty, field.Size));
                Rectangle actionsBounds = actions.RectangleToScreen(actions.ClientRectangle);
                Rectangle saveBounds = save.RectangleToScreen(new Rectangle(Point.Empty, save.Size));
                Rectangle cancelBounds = cancel.RectangleToScreen(new Rectangle(Point.Empty, cancel.Size));
                Assert(client.Contains(fieldBounds) && client.Contains(actionsBounds) && client.Contains(saveBounds) && client.Contains(cancelBounds),
                    "A shortcut input/action control extends outside the compact dialog's client bounds.");
                Assert(!saveBounds.IntersectsWith(cancelBounds) && fieldBounds.Bottom <= actionsBounds.Top,
                    "Shortcut input or Save/Cancel buttons overlap.");
                foreach (var label in Descendants(dialog).OfType<Label>().Where(label => label.Visible && !string.IsNullOrEmpty(label.Text)))
                {
                    Rectangle labelBounds = label.RectangleToScreen(new Rectangle(Point.Empty, label.Size));
                    int preferredHeight = label.GetPreferredSize(new Size(label.Width, 0)).Height;
                    Assert(client.Contains(labelBounds) && label.Height + 1 >= preferredHeight,
                        $"Shortcut text is clipped at width {dialog.ClientSize.Width}: label {label.Name}, actual height {label.Height}, required {preferredHeight}.");
                }
                if (compact)
                {
                    Assert(string.IsNullOrEmpty(feedback.Text), "Compact capture layout unexpectedly contains feedback.");
                    int allowedGap = field.Margin.Bottom + actions.Margin.Top + 4;
                    Assert(actionsBounds.Top - fieldBounds.Bottom <= allowedGap,
                        "An empty feedback row still reserves a large blank region in the shortcut editor.");
                }
                else
                {
                    Rectangle feedbackBounds = feedback.RectangleToScreen(new Rectangle(Point.Empty, feedback.Size));
                    Assert(feedback.Visible && !string.IsNullOrEmpty(feedback.Text) &&
                        fieldBounds.Bottom <= feedbackBounds.Top && feedbackBounds.Bottom <= actionsBounds.Top,
                        "Multiline shortcut feedback overlaps the input/actions or is hidden.");
                }
                string output = outputPrefix + "-" + size.Name + ".png";
                CaptureWindow(dialog, output);
                File.WriteAllText(Path.ChangeExtension(output, ".json"), JsonSerializer.Serialize(new
                {
                    Case = Path.GetFileName(outputPrefix),
                    WidthCase = size.Name,
                    DeviceDpi = dialog.DeviceDpi,
                    SyntheticScalePercent = syntheticScalePercent,
                    AutoScaleMode = dialog.AutoScaleMode.ToString(),
                    ClientWidth = dialog.ClientSize.Width,
                    ClientHeight = dialog.ClientSize.Height,
                    MinimumWidth = minimumWidth,
                    FeedbackVisible = feedback.Visible,
                    FeedbackWidth = feedback.Width,
                    FeedbackHeight = feedback.Height,
                    FeedbackPreferredHeight = feedback.GetPreferredSize(new Size(feedback.Width, 0)).Height,
                    InputBounds = fieldBounds.ToString(),
                    ActionBounds = actionsBounds.ToString()
                }, new JsonSerializerOptions { WriteIndented = true }));
            }
        }
        finally { if (!dialog.IsDisposed) dialog.Width = originalWidth; }
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

    private static void CheckDropdowns(string outputDirectory)
    {
        Application.EnableVisualStyles();
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
        Directory.CreateDirectory(outputDirectory);
        foreach (var initial in new[] { ThemePreference.Light, ThemePreference.Dark, ThemePreference.System })
        {
            AppTheme.Initialize(initial);
            var backend = new PreviewProxy();
            using var main = new PreviewWindow(backend);
            var programs = (ListView)main.Controls.Find("listApplications", true).Single();
            var modes = new List<ResolutionModeWrapper>
            {
                new() { DmPelsWidth = 1920, DmPelsHeight = 1080, DmBitsPerPel = 32, DmDisplayFrequency = 144 },
                new() { DmPelsWidth = 1280, DmPelsHeight = 720, DmBitsPerPel = 32, DmDisplayFrequency = 120 },
                new() { DmPelsWidth = 2560, DmPelsHeight = 1440, DmBitsPerPel = 32, DmDisplayFrequency = 144 }
            };
            using var profile = new PreviewSettingsWindow(programs.Items[0], modes, backend);
            PrepareOffscreen(main);
            PrepareOffscreen(profile);
            Application.DoEvents();
            var appearance = (ComboBox)main.Controls.Find("comboBoxTheme", true).Single();
            var resolution = Descendants(profile).OfType<ComboBox>().Single();
            Descendants(profile).OfType<CheckBox>().Single().Checked = true;
            resolution.SelectedIndex = 1;
            var handles = new[] { main.Handle, profile.Handle, appearance.Handle, resolution.Handle };
            int mutations = backend.MutatingCalls;
            int pass = 0;
            foreach (var theme in new[] { initial, ThemePreference.Light, ThemePreference.Dark, ThemePreference.Light, ThemePreference.Dark, ThemePreference.System })
            {
                appearance.SelectedItem = theme;
                Application.DoEvents();
                string prefix = Path.Combine(outputDirectory, initial + "-" + pass++ + "-" + theme);
                DropdownChecks.Verify(appearance, prefix + "-appearance.png");
                DropdownChecks.Verify(resolution, prefix + "-resolution.png");
                Assert(handles.SequenceEqual(new[] { main.Handle, profile.Handle, appearance.Handle, resolution.Handle }),
                    "Dropdown painting or theme changes recreated an existing HWND.");
                Assert(AppTheme.Preference == theme && (ThemePreference)appearance.SelectedItem! == theme && resolution.SelectedIndex == 1,
                    "Dropdown painting or theme changes lost the selected theme or unsaved resolution.");
                Assert(backend.MutatingCalls == mutations, "Dropdown painting changed GPU or monitoring state.");
            }
            resolution.Enabled = false;
            DropdownChecks.Verify(resolution, Path.Combine(outputDirectory, initial + "-disabled-resolution.png"));
        }
        Console.WriteLine("PASS: native dropdown row/text contrast in fresh Light/Dark/System windows and repeated theme changes; selected/unselected/disabled rows; stable HWNDs/selections and zero GPU/monitor lifecycle calls.");
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
            bool dark = !SystemInformation.HighContrast && (theme == ThemePreference.Dark || theme == ThemePreference.System && SystemPrefersDark());
            Assert(Application.IsDarkModeEnabled == dark, "The selected theme did not match the requested Windows apps appearance.");
            foreach (var window in windows)
            {
                Assert(SystemInformation.HighContrast ? window.BackColor.ToArgb() == SystemColors.Control.ToArgb()
                    : (window.BackColor.GetBrightness() < 0.5f) == dark, "An existing window did not update its background.");
                foreach (var list in Descendants(window).OfType<ListView>())
                    Assert(SystemInformation.HighContrast
                        ? list.BackColor.ToArgb() == SystemColors.Window.ToArgb() && list.ForeColor.ToArgb() == SystemColors.WindowText.ToArgb()
                        : (list.BackColor.GetBrightness() < 0.5f) == dark && (list.ForeColor.GetBrightness() > 0.5f) == dark,
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

    private static bool SystemPrefersDark()
    {
        // Independent expectation from Windows intent, not the output palette under test.
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
    }

    private static void CheckThemeResolution()
    {
        Assert(AppTheme.ResolveColorMode(ThemePreference.System, false, 0) == SystemColorMode.Dark,
            "System did not resolve a dark Windows apps setting.");
        foreach (int? value in new int?[] { 1, null, -1, 999 })
            Assert(AppTheme.ResolveColorMode(ThemePreference.System, false, value) == SystemColorMode.Classic,
                "System did not resolve a light/default apps setting.");
        Assert(AppTheme.ResolveColorMode(ThemePreference.Light, false, 0) == SystemColorMode.Classic
            && AppTheme.ResolveColorMode(ThemePreference.Dark, false, 1) == SystemColorMode.Dark,
            "Manual theme choice was overridden by Windows appearance.");
        foreach (var theme in Enum.GetValues<ThemePreference>())
            foreach (int? value in new int?[] { 0, 1, null })
                Assert(AppTheme.ResolveColorMode(theme, true, value) == SystemColorMode.Classic,
                    "High contrast did not retain the Windows system palette.");
        Console.WriteLine("PASS: Windows apps theme resolution, manual overrides, unavailable settings, and high contrast precedence.");
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
            Assert(SystemInformation.HighContrast ? background.ToArgb() == SystemColors.Control.ToArgb()
                : (background.GetBrightness() < 0.5f) == dark,
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
    private sealed class PreviewAboutDialog : AboutDialog
    {
        public PreviewAboutDialog() : base(_ => throw new InvalidOperationException("Rendering About must not open a browser.")) { }
        protected override bool ShowWithoutActivation => true;
    }
}
