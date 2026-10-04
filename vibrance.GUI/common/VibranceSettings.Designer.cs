using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace vibrance.GUI.common
{
    partial class VibranceSettings
    {
        private IContainer components;
        private TrackBar trackBarIngameLevel;
        private Label labelIngameLevel, labelTitle;
        private Button buttonSave, buttonCancel;
        private PictureBox pictureBox;
        private ComboBox cBoxResolution;
        private CheckBox checkBoxResolution;

        protected override void Dispose(bool disposing)
        {
            if (disposing) components?.Dispose();
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            components = new Container();
            var resources = new ComponentResourceManager(typeof(VibranceSettings));
            SuspendLayout();
            AppTheme.Configure(this);
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(490, 430);
            MinimumSize = new Size(440, 450);
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            Icon = (Icon)resources.GetObject("$this.Icon");
            Text = "vibranceGUI";
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(24), ColumnCount = 1, RowCount = 4 };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var header = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Top, ColumnCount = 2, Margin = new Padding(0, 0, 0, 18) };
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 64));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            pictureBox = new PictureBox { Size = new Size(48, 48), SizeMode = PictureBoxSizeMode.Zoom, Margin = Padding.Empty, TabStop = false };
            labelTitle = new Label { Text = "Settings for ", AutoSize = true, Dock = DockStyle.Fill, Font = new Font("Segoe UI", 14F, FontStyle.Bold), Margin = Padding.Empty };
            header.Controls.Add(pictureBox, 0, 0);
            header.Controls.Add(labelTitle, 1, 0);
            layout.Controls.Add(header, 0, 0);

            var levelGroup = new GroupBox { Text = "Ingame Vibrance Level", Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(12), Margin = new Padding(0, 0, 0, 16) };
            var levelLayout = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2 };
            levelLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            levelLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 76));
            trackBarIngameLevel = new TrackBar { Dock = DockStyle.Fill, TickStyle = TickStyle.None, Margin = Padding.Empty, AccessibleName = "Ingame Vibrance Level" };
            trackBarIngameLevel.ValueChanged += trackBarIngameLevel_Scroll;
            labelIngameLevel = new Label { Text = "50%", AutoSize = true, Font = new Font("Segoe UI", 14F, FontStyle.Bold), Anchor = AnchorStyles.Left };
            levelLayout.Controls.Add(trackBarIngameLevel, 0, 0);
            levelLayout.Controls.Add(labelIngameLevel, 1, 0);
            levelGroup.Controls.Add(levelLayout);
            layout.Controls.Add(levelGroup, 0, 1);

            var resolutionGroup = new GroupBox { Text = "Ingame Resolution", Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(12), Margin = new Padding(0, 0, 0, 16) };
            var resolutionLayout = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, RowCount = 3 };
            resolutionLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (int row = 0; row < 3; row++) resolutionLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            resolutionLayout.Controls.Add(new Label { Text = "For (Borderless) Windowed Mode players only!", AutoSize = true, Margin = new Padding(0, 0, 0, 10) });
            checkBoxResolution = new CheckBox { Text = "Change Resolution when Ingame", AutoSize = true, Margin = new Padding(0, 0, 0, 10) };
            checkBoxResolution.CheckedChanged += checkBoxResolution_CheckedChanged;
            resolutionLayout.Controls.Add(checkBoxResolution);
            cBoxResolution = new ComboBox { Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDownList, Enabled = false, Margin = Padding.Empty, AccessibleName = "Ingame Resolution" };
            resolutionLayout.Controls.Add(cBoxResolution);
            resolutionGroup.Controls.Add(resolutionLayout);
            layout.Controls.Add(resolutionGroup, 0, 2);
            var actions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Margin = Padding.Empty };
            buttonSave = AppTheme.Button("Save", buttonSave_Click);
            buttonCancel = AppTheme.Button("Cancel", (sender, args) => Close());
            buttonCancel.DialogResult = DialogResult.Cancel;
            actions.Controls.Add(buttonSave);
            actions.Controls.Add(buttonCancel);
            layout.Controls.Add(actions, 0, 3);
            AcceptButton = buttonSave;
            CancelButton = buttonCancel;
            Controls.Add(layout);
            ResumeLayout(true);
        }
    }
}
