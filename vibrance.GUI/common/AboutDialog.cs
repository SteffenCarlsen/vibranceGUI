using System;
using System.ComponentModel;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;

namespace vibrance.GUI.common
{
    internal class AboutDialog : Form
    {
        internal const string RepositoryUrl = "https://github.com/SteffenCarlsen/vibranceGUI";
        internal const string OriginalProjectUrl = "https://github.com/juv/vibranceGUI";
        internal const string OriginalDeveloperUrl = "https://github.com/juv";
        internal const string OriginalSupportUrl = "https://www.paypal.com/cgi-bin/webscr?cmd=_s-xclick&hosted_button_id=JDQFNKNNEW356";

        internal AboutDialog(Action<string> openLink)
        {
            ArgumentNullException.ThrowIfNull(openLink);
            AppTheme.Configure(this);
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(500, 360);
            MinimumSize = new Size(420, 400);
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            Icon = (Icon)new ComponentResourceManager(typeof(VibranceGUI)).GetObject("$this.Icon");
            Text = "About vibranceGUI";

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, Padding = new Padding(24), ColumnCount = 1, RowCount = 9
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (int row = 0; row < 7; row++) layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.Controls.Add(new Label
            {
                Text = "vibranceGUI", AutoSize = true, Dock = DockStyle.Top,
                Font = new Font("Segoe UI", 20F, FontStyle.Bold), Margin = Padding.Empty
            }, 0, 0);
            layout.Controls.Add(new Label
            {
                Name = "labelVersion", Text = "Version " + (typeof(AboutDialog).Assembly
                    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                    ?? typeof(AboutDialog).Assembly.GetName().Version?.ToString()),
                AutoSize = true, Dock = DockStyle.Top, Margin = new Padding(0, 4, 0, 18)
            }, 0, 1);
            layout.Controls.Add(CreateLink("Upstream repository: SteffenCarlsen/vibranceGUI", "linkRepository", RepositoryUrl, openLink, 20), 0, 2);

            var creator = new FlowLayoutPanel
            {
                AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Top,
                WrapContents = true, Margin = new Padding(0, 0, 0, 8)
            };
            creator.Controls.Add(new Label { Text = "Originally created by", AutoSize = true, Margin = new Padding(0, 0, 8, 0) });
            creator.Controls.Add(CreateLink("juv / juvlarN", "linkOriginalDeveloper", OriginalDeveloperUrl, openLink));
            layout.Controls.Add(creator, 0, 3);
            layout.Controls.Add(CreateLink("Original project: juv/vibranceGUI", "linkOriginalProject", OriginalProjectUrl, openLink, 8), 0, 4);
            layout.Controls.Add(new Label
            {
                Text = "Original AMD implementation: juRiiir3.", AutoSize = true,
                Dock = DockStyle.Top, Margin = new Padding(0, 0, 0, 18)
            }, 0, 5);
            layout.Controls.Add(CreateLink("Support the original developer", "linkOriginalSupport", OriginalSupportUrl, openLink), 0, 6);

            var actions = new FlowLayoutPanel
            {
                AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft,
                Margin = Padding.Empty
            };
            var close = AppTheme.Button("Close", (sender, args) => Close());
            close.Name = "buttonClose";
            close.DialogResult = DialogResult.Cancel;
            actions.Controls.Add(close);
            layout.Controls.Add(actions, 0, 8);
            AcceptButton = close;
            CancelButton = close;
            Controls.Add(layout);
        }

        private static LinkLabel CreateLink(string text, string name, string url, Action<string> openLink, int bottomMargin = 0)
        {
            var link = new LinkLabel
            {
                Name = name, Text = text, Tag = url, AutoSize = true,
                LinkColor = AppTheme.LinkColor, ActiveLinkColor = AppTheme.LinkColor,
                VisitedLinkColor = AppTheme.LinkColor, Margin = new Padding(0, 0, 0, bottomMargin)
            };
            link.LinkClicked += (sender, args) => openLink(url);
            return link;
        }
    }
}
