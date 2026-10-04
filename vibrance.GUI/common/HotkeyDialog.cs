using System;
using System.ComponentModel;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace vibrance.GUI.common
{
    internal partial class HotkeyDialog : Form
    {
        private readonly Func<Keys, string> _tryApply;
        private readonly TextBox _captureBox;
        private readonly Label _feedback;

        internal Keys KeyData { get; private set; }

        internal HotkeyDialog(Keys current, Func<Keys, string> tryApply = null)
        {
            _tryApply = tryApply;
            KeyData = PauseHotkey.IsValid(current) ? current : PauseHotkey.Default;
            AppTheme.Configure(this);
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(440, 180);
            MinimumSize = new Size(400, 1);
            AutoScroll = true;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            Icon = (Icon)new ComponentResourceManager(typeof(VibranceGUI)).GetObject("$this.Icon");
            Text = "Pause hotkey";

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Top, Padding = new Padding(16), ColumnCount = 1, RowCount = 5,
                AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var heading = new Label
            {
                Text = "Choose a pause shortcut", AutoSize = true, Dock = DockStyle.Top,
                Font = new Font("Segoe UI", 14F, FontStyle.Bold), Margin = new Padding(0, 0, 0, 8),
                MaximumSize = new Size(408, 0)
            };
            layout.Controls.Add(heading, 0, 0);
            var instructions = AppTheme.MutedLabel("Press Ctrl, Alt or Shift with a key, or F1–F24 (except F12).");
            instructions.Dock = DockStyle.Top;
            instructions.Margin = new Padding(0, 0, 0, 10);
            instructions.MaximumSize = new Size(408, 0);
            layout.Controls.Add(instructions, 0, 1);
            _captureBox = new CaptureTextBox(this)
            {
                Name = "textBoxHotkey", Text = PauseHotkey.Format(KeyData), ReadOnly = true,
                ShortcutsEnabled = false, Dock = DockStyle.Top, Margin = new Padding(0, 0, 0, 12),
                Font = new Font("Segoe UI", 11F), AccessibleName = "Pause shortcut"
            };
            layout.Controls.Add(_captureBox, 0, 2);
            _feedback = new Label
            {
                Name = "labelHotkeyStatus",
                AutoSize = true, Dock = DockStyle.Top, Margin = Padding.Empty,
                MaximumSize = new Size(408, 0), Visible = false
            };
            _feedback.TextChanged += (sender, args) =>
            {
                bool hasError = !string.IsNullOrEmpty(_feedback.Text);
                _feedback.Visible = hasError;
                _feedback.Margin = new Padding(0, 0, 0, hasError ? 12 : 0);
            };
            layout.Controls.Add(_feedback, 0, 3);
            var actions = new FlowLayoutPanel
            {
                AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft,
                Margin = Padding.Empty
            };
            var save = AppTheme.Button("Save", SaveShortcut, primary: true);
            save.Name = "buttonSave";
            var cancel = AppTheme.Button("Cancel", (sender, args) => Close());
            cancel.Name = "buttonCancel";
            cancel.DialogResult = DialogResult.Cancel;
            actions.Controls.Add(save);
            actions.Controls.Add(cancel);
            layout.Controls.Add(actions, 0, 4);
            AcceptButton = save;
            CancelButton = cancel;
            bool fitting = false;
            void FitHeight()
            {
                if (fitting || IsDisposed || Disposing) return;
                fitting = true;
                try
                {
                    int chrome = Height - ClientSize.Height;
                    Rectangle workingArea = Screen.FromRectangle(Bounds).WorkingArea;
                    int available = Math.Max(1, workingArea.Height - chrome);
                    int height = Math.Min(available, layout.GetPreferredSize(new Size(layout.Width, 0)).Height);
                    MinimumSize = new Size(MinimumSize.Width, height + chrome);
                    if (ClientSize.Height != height) ClientSize = new Size(ClientSize.Width, height);
                    if (workingArea.IntersectsWith(Bounds) && Bottom > workingArea.Bottom)
                        Top = Math.Max(workingArea.Top, workingArea.Bottom - Height);
                }
                finally { fitting = false; }
            }
            layout.SizeChanged += (sender, args) =>
            {
                int width = Math.Max(1, layout.ClientSize.Width - layout.Padding.Horizontal);
                var maximum = new Size(width, 0);
                heading.MaximumSize = instructions.MaximumSize = _feedback.MaximumSize = maximum;
                FitHeight();
            };
            layout.Layout += (sender, args) => FitHeight();
            Controls.Add(layout);
            RefreshCaptureColors();
            Shown += (sender, args) =>
            {
                FitHeight();
                if (ActiveForm == this) _captureBox.Focus();
                _captureBox.SelectAll();
            };
        }

        internal void CaptureShortcut(Keys keyData)
        {
            if (IsDisposed || Disposing) return;
            if (!PauseHotkey.IsValid(keyData))
            {
                _feedback.Text = (keyData & Keys.KeyCode) == Keys.F12
                    ? "F12 is reserved by Windows. Choose another key."
                    : "That key cannot be used as a shortcut.";
                return;
            }
            KeyData = keyData;
            _captureBox.Text = PauseHotkey.Format(keyData);
            _captureBox.SelectAll();
            _feedback.Text = string.Empty;
        }

        private void SaveShortcut(object sender, EventArgs args)
        {
            string error = _tryApply?.Invoke(KeyData);
            if (!string.IsNullOrEmpty(error))
            {
                // The caller may have applied the key for this session before a save failed.
                // Its message describes that state; retaining the candidate allows a retry.
                _feedback.Text = error;
                if (ActiveForm == this) _captureBox.Focus();
                return;
            }
            DialogResult = DialogResult.OK;
            Close();
        }

        protected override void OnSystemColorsChanged(EventArgs args)
        {
            base.OnSystemColorsChanged(args);
            RefreshCaptureColors();
        }

        private void RefreshCaptureColors()
        {
            if (_captureBox == null) return;
            _captureBox.BackColor = Color.FromArgb(SystemColors.Window.ToArgb());
            _captureBox.ForeColor = Color.FromArgb(SystemColors.WindowText.ToArgb());
        }

        [LibraryImport("user32.dll")]
        private static partial short GetKeyState(int virtualKey);

        private sealed class CaptureTextBox : TextBox
        {
            private readonly HotkeyDialog _dialog;

            internal CaptureTextBox(HotkeyDialog dialog) => _dialog = dialog;

            protected override bool ProcessCmdKey(ref Message message, Keys keyData)
            {
                // Keys.Modifiers omits the Windows keys. Read the state associated with
                // this input message so a logo chord cannot become a different shortcut.
                if (GetKeyState((int)Keys.LWin) < 0 || GetKeyState((int)Keys.RWin) < 0)
                {
                    _dialog._feedback.Text = "Windows-key shortcuts are not supported.";
                    return true;
                }
                // Preserve normal dialog navigation and the Cancel shortcut.
                if (keyData is Keys.Tab or (Keys.Shift | Keys.Tab) or Keys.Escape)
                    return base.ProcessCmdKey(ref message, keyData);
                _dialog.CaptureShortcut(keyData);
                return true;
            }
        }
    }
}
