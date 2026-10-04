using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace vibrance.GUI.common
{
    partial class VibranceGUI
    {
        private IContainer components;
        private NotifyIcon notifyIcon;
        private ContextMenuStrip contextMenuStrip;
        private ToolStripMenuItem pauseToolStripMenuItem;
        private CheckBox checkBoxAutostart, checkBoxPrimaryMonitorOnly, checkBoxNeverChangeResolutions, checkBoxPauseHotkey;
        private TrackBar trackBarWindowsLevel;
        private Label labelWindowsLevel, statusLabel;
        private ComboBox comboBoxTheme;
        private Button buttonPause, buttonPauseHotkey, buttonAddProgram, buttonProcessExplorer, buttonEditProgram, buttonRemoveProgram;
        private ListView listApplications;
        private ToolTip toolTip;
        private Timer settingsSaveTimer;

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                AppTheme.UnwatchSystemPreferences(this);
                components?.Dispose();
            }
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            components = new Container();
            var resources = new ComponentResourceManager(typeof(VibranceGUI));
            SuspendLayout();
            AppTheme.Configure(this);
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(720, 640);
            MinimumSize = new Size(650, 560);
            StartPosition = FormStartPosition.CenterScreen;
            Icon = (Icon)resources.GetObject("$this.Icon");
            Name = "VibranceGUI";
            Text = "vibranceGUI";
            toolTip = new ToolTip(components) { InitialDelay = 400, AutoPopDelay = 8000 };
            settingsSaveTimer = new Timer(components) { Interval = 750 };
            settingsSaveTimer.Tick += settingsSaveTimer_Tick;

            contextMenuStrip = new ContextMenuStrip(components) { RenderMode = ToolStripRenderMode.System };
            pauseToolStripMenuItem = new ToolStripMenuItem("Pause", null, buttonPause_Click) { Enabled = false };
            contextMenuStrip.Items.AddRange(new ToolStripItem[]
            {
                new ToolStripMenuItem("Show vibranceGUI", null, showToolStripMenuItem_Click),
                pauseToolStripMenuItem, new ToolStripSeparator(),
                new ToolStripMenuItem("Project on GitHub", null, projectToolStripMenuItem_Click) { Name = "projectToolStripMenuItem", Tag = AboutDialog.RepositoryUrl },
                new ToolStripMenuItem("About", null, aboutToolStripMenuItem_Click) { Name = "aboutToolStripMenuItem" },
                new ToolStripMenuItem("Exit", null, exitToolStripMenuItem_Click)
            });
            notifyIcon = new NotifyIcon(components)
            {
                ContextMenuStrip = contextMenuStrip, Icon = (Icon)resources.GetObject("notifyIcon.Icon"),
                Text = "vibranceGUI", BalloonTipTitle = "vibranceGUI", BalloonTipIcon = ToolTipIcon.Info
            };
            notifyIcon.MouseClick += notifyIcon_MouseClick;

            var scrollPanel = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
            var layout = new TableLayoutPanel { Dock = DockStyle.Top, Padding = new Padding(20), ColumnCount = 1, RowCount = 4 };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var header = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 2, Margin = new Padding(0, 0, 0, 24) };
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            header.Controls.Add(new HeaderLabel { Text = "vibranceGUI", Font = new Font("Segoe UI Semibold", 22F), AutoSize = true, Margin = Padding.Empty }, 0, 0);
            var subtitle = AppTheme.MutedLabel("Automatic vibrance for your games", flushText: true);
            subtitle.Margin = new Padding(0, 2, 0, 0);
            header.Controls.Add(subtitle, 0, 1);
            header.Controls.Add(new SpectrumBand { Size = new Size(168, 3), Margin = new Padding(0, 10, 0, 0) }, 0, 2);
            buttonPause = AppTheme.Button("Pause", buttonPause_Click);
            buttonPause.Anchor = AnchorStyles.Right;
            buttonPause.Margin = Padding.Empty;
            header.Controls.Add(buttonPause, 1, 0);
            header.SetRowSpan(buttonPause, 3);
            layout.Controls.Add(header, 0, 0);

            var settings = new QuietSection { Text = "Settings", Dock = DockStyle.Top, Padding = new Padding(0, 10, 0, 12), Margin = new Padding(0, 0, 0, 20) };
            var settingsLayout = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1, RowCount = 3, Margin = Padding.Empty };
            settingsLayout.Layout += (sender, args) =>
            {
                // GroupBox measures an unconstrained width. Use the laid-out content height so
                // wrapping follows the actual window width and does not steal the program list.
                int height = settingsLayout.Bottom + settings.Padding.Bottom;
                if (settings.Height != height) settings.Height = height;
            };
            settingsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (int row = 0; row < 3; row++) settingsLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var levelLayout = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 2, RowCount = 2, Margin = new Padding(0, 0, 0, 8) };
            levelLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            levelLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 76));
            levelLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            levelLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var levelTitle = new Label { Text = "Windows Vibrance Level", AutoSize = true, Margin = new Padding(0, 0, 0, 4) };
            levelLayout.Controls.Add(levelTitle, 0, 0);
            levelLayout.SetColumnSpan(levelTitle, 2);
            trackBarWindowsLevel = new TrackBar { Dock = DockStyle.Fill, Maximum = 100, TickStyle = TickStyle.None, Margin = Padding.Empty, AccessibleName = "Windows Vibrance Level", BackColor = Color.FromArgb(SystemColors.Control.ToArgb()) };
            trackBarWindowsLevel.ValueChanged += trackBarWindowsLevel_Scroll;
            labelWindowsLevel = new Label { AutoSize = true, Anchor = AnchorStyles.Right, Font = new Font("Consolas", 17F, FontStyle.Bold), Margin = Padding.Empty };
            levelLayout.Controls.Add(trackBarWindowsLevel, 0, 1);
            levelLayout.Controls.Add(labelWindowsLevel, 1, 1);
            settingsLayout.Controls.Add(levelLayout);

            var options = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true, Margin = new Padding(0, 0, 0, 12) };
            checkBoxAutostart = new CheckBox { Text = "Autostart vibranceGUI", AutoSize = true, Margin = new Padding(0, 0, 18, 10) };
            checkBoxPrimaryMonitorOnly = new CheckBox { Text = "Affect Primary Monitor only", AutoSize = true, Margin = new Padding(0, 0, 18, 10) };
            checkBoxNeverChangeResolutions = new CheckBox { Text = "Never change resolutions", AutoSize = true, Margin = new Padding(0, 0, 18, 10) };
            checkBoxAutostart.CheckedChanged += checkBoxAutostart_CheckedChanged;
            checkBoxPrimaryMonitorOnly.CheckedChanged += checkBoxPrimaryMonitorOnly_CheckedChanged;
            checkBoxNeverChangeResolutions.CheckedChanged += checkBoxNeverChangeResolutions_CheckedChanged;
            toolTip.SetToolTip(checkBoxAutostart, "Adds this executable to your Windows user's Run registry key (HKCU).\nStarts minimized in the tray at sign-in. Uncheck to remove it. No admin rights needed.");
            toolTip.SetToolTip(checkBoxPrimaryMonitorOnly, "When checking this, VibranceGUI will only change vibrance values on your primary monitor.");
            toolTip.SetToolTip(checkBoxNeverChangeResolutions, "When checking this, VibranceGUI will never change the resolution on any of your monitors.");
            options.Controls.AddRange(new Control[] { checkBoxAutostart, checkBoxPrimaryMonitorOnly, checkBoxNeverChangeResolutions });
            settingsLayout.Controls.Add(options);

            var appearance = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true, Margin = Padding.Empty };
            appearance.Controls.Add(new Label { Text = "Appearance", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 0, 12, 8) });
            comboBoxTheme = new ThemedComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 110, Anchor = AnchorStyles.Left, Margin = new Padding(0, 0, 18, 8), AccessibleName = "Appearance", Name = "comboBoxTheme" };
            foreach (ThemePreference preference in Enum.GetValues<ThemePreference>()) comboBoxTheme.Items.Add(preference);
            comboBoxTheme.SelectedIndexChanged += comboBoxTheme_SelectedIndexChanged;
            appearance.Controls.Add(comboBoxTheme);
            checkBoxPauseHotkey = new CheckBox { Text = "Enable pause hotkey", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 0, 12, 8) };
            checkBoxPauseHotkey.CheckedChanged += checkBoxPauseHotkey_CheckedChanged;
            appearance.Controls.Add(checkBoxPauseHotkey);
            buttonPauseHotkey = AppTheme.Button(PauseHotkey.Format(AppTheme.PauseHotkeyKeyData), buttonPauseHotkey_Click);
            buttonPauseHotkey.Name = "buttonPauseHotkey";
            buttonPauseHotkey.AccessibleName = "Change pause hotkey";
            buttonPauseHotkey.Anchor = AnchorStyles.Left;
            buttonPauseHotkey.Margin = new Padding(0, 0, 0, 8);
            appearance.Controls.Add(buttonPauseHotkey);
            settingsLayout.Controls.Add(appearance);
            settings.Controls.Add(settingsLayout);
            layout.Controls.Add(settings, 0, 1);

            var programs = new QuietSection { Text = "Program Settings", Dock = DockStyle.Fill, MinimumSize = new Size(0, 200), Padding = new Padding(0, 10, 0, 8), Margin = new Padding(0, 0, 0, 16) };
            var programsLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = Padding.Empty };
            programsLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            programsLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = new Padding(0, 0, 0, 12) };
            buttonProcessExplorer = AppTheme.Button("Add", buttonProcessExplorer_Click, primary: true);
            buttonAddProgram = AppTheme.Button("Add manually", buttonAddProgram_Click);
            buttonEditProgram = AppTheme.Button("Edit", listApplications_DoubleClick);
            buttonRemoveProgram = AppTheme.Button("Remove", buttonRemoveProgram_Click);
            toolTip.SetToolTip(buttonProcessExplorer, "Add a running program.");
            toolTip.SetToolTip(buttonAddProgram, "Choose a program executable.");
            actions.Controls.AddRange(new Control[] { buttonProcessExplorer, buttonAddProgram, buttonEditProgram, buttonRemoveProgram });
            programsLayout.Controls.Add(actions, 0, 0);
            listApplications = new ListView
            {
                Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HideSelection = false,
                MultiSelect = true, HeaderStyle = ColumnHeaderStyle.Nonclickable, BorderStyle = BorderStyle.FixedSingle,
                BackColor = SystemColors.Window, ForeColor = SystemColors.WindowText, AccessibleName = "Configured programs", Name = "listApplications", Margin = Padding.Empty
            };
            listApplications.Columns.Add("Programs", 300);
            listApplications.Columns.Add("Vibrance", 100);
            listApplications.Columns.Add("Resolution", 220);
            listApplications.ClientSizeChanged += (sender, args) =>
            {
                int width = listApplications.ClientSize.Width;
                int vibranceWidth = (int)(90 * listApplications.DeviceDpi / 96F);
                int resolutionWidth = (int)(width * 0.32F);
                listApplications.Columns[0].Width = Math.Max(0, width - vibranceWidth - resolutionWidth - 4);
                listApplications.Columns[1].Width = vibranceWidth;
                listApplications.Columns[2].Width = resolutionWidth;
            };
            listApplications.DoubleClick += listApplications_DoubleClick;
            listApplications.SelectedIndexChanged += listApplications_SelectedIndexChanged;
            listApplications.KeyDown += listApplications_KeyDown;
            programsLayout.Controls.Add(listApplications, 0, 1);
            programs.Controls.Add(programsLayout);
            layout.Controls.Add(programs, 0, 2);

            var status = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, Margin = Padding.Empty };
            status.Controls.Add(new Label { Text = "Observer status: ", AutoSize = true, Anchor = AnchorStyles.Left, Margin = Padding.Empty });
            statusLabel = new Label { Text = "Initializing...", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 0, 24, 0) };
            status.Controls.Add(statusLabel);
            layout.Controls.Add(status, 0, 3);
            var repositoryLink = new LinkLabel { Name = "linkProject", Text = "GitHub", Tag = AboutDialog.RepositoryUrl, AutoSize = true, Anchor = AnchorStyles.Left, LinkColor = AppTheme.LinkColor, ActiveLinkColor = AppTheme.LinkColor, VisitedLinkColor = AppTheme.LinkColor, Margin = new Padding(0, 0, 12, 0) };
            repositoryLink.LinkClicked += (sender, args) => projectToolStripMenuItem_Click(sender, args);
            status.Controls.Add(repositoryLink);
            var aboutButton = AppTheme.Button("About", aboutToolStripMenuItem_Click);
            aboutButton.Name = "buttonAbout";
            aboutButton.Anchor = AnchorStyles.Left;
            status.Controls.Add(aboutButton);
            layout.Layout += (sender, args) =>
            {
                int minimumHeight = layout.Padding.Vertical
                    + header.Height + header.Margin.Vertical
                    + settings.Height + settings.Margin.Vertical
                    + programs.MinimumSize.Height + programs.Margin.Vertical
                    + status.Height + status.Margin.Vertical;
                int height = Math.Max(scrollPanel.ClientSize.Height, minimumHeight);
                if (layout.Height != height) layout.Height = height;
            };
            scrollPanel.SizeChanged += (sender, args) => layout.PerformLayout();
            scrollPanel.Controls.Add(layout);
            Controls.Add(scrollPanel);
            Load += Form1_Load;
            Resize += Form1_Resize;
            FormClosing += Form1_FormClosing;
            ResumeLayout(true);
        }
    }
}
