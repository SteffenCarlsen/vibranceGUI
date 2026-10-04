using System;
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
            ClientSize = new Size(480, 300);
            MinimumSize = new Size(420, 340);
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            Text = "Pause hotkey";

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, Padding = new Padding(24), ColumnCount = 1, RowCount = 5
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.Controls.Add(new Label
            {
                Text = "Choose a pause shortcut", AutoSize = true, Dock = DockStyle.Top,
                Font = new Font("Segoe UI", 16F, FontStyle.Bold), Margin = new Padding(0, 0, 0, 12)
            }, 0, 0);
            layout.Controls.Add(new Label
            {
                Text = "Press Ctrl, Alt or Shift with a key, or F1–F24 (except F12).",
                AutoSize = true, Dock = DockStyle.Top, Margin = new Padding(0, 0, 0, 16)
            }, 0, 1);
            _captureBox = new CaptureTextBox(this)
            {
                Name = "textBoxHotkey", Text = PauseHotkey.Format(KeyData), ReadOnly = true,
                ShortcutsEnabled = false, Dock = DockStyle.Top, Margin = new Padding(0, 0, 0, 12),
                Font = new Font("Segoe UI", 12F), AccessibleName = "Pause shortcut"
            };
            layout.Controls.Add(_captureBox, 0, 2);
            _feedback = new Label
            {
                Name = "labelHotkeyStatus", Text = "Select the field above and press your shortcut.",
                AutoSize = true, Dock = DockStyle.Top, Margin = new Padding(0, 0, 0, 16)
            };
            layout.Controls.Add(_feedback, 0, 3);
            var actions = new FlowLayoutPanel
            {
                AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft,
                Margin = Padding.Empty
            };
            var save = AppTheme.Button("Save", SaveShortcut);
            save.Name = "buttonSave";
            var cancel = AppTheme.Button("Cancel", (sender, args) => Close());
            cancel.Name = "buttonCancel";
            cancel.DialogResult = DialogResult.Cancel;
            actions.Controls.Add(save);
            actions.Controls.Add(cancel);
            layout.Controls.Add(actions, 0, 4);
            AcceptButton = save;
            CancelButton = cancel;
            Controls.Add(layout);
            RefreshCaptureColors();
            Shown += (sender, args) =>
            {
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
                    : "Use Ctrl, Alt or Shift with a key, or F1–F24 (except F12).";
                return;
            }
            KeyData = keyData;
            _captureBox.Text = PauseHotkey.Format(keyData);
            _captureBox.SelectAll();
            _feedback.Text = "Press Save to apply " + PauseHotkey.Format(keyData) + ".";
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
                    _dialog._feedback.Text = "Windows-key shortcuts are not supported. Use Ctrl, Alt or Shift instead.";
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
