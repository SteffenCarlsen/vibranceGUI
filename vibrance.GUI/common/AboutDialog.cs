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
            ClientSize = new Size(460, 280);
            MinimumSize = new Size(420, 1);
            AutoScroll = true;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            Icon = (Icon)new ComponentResourceManager(typeof(VibranceGUI)).GetObject("$this.Icon");
            Text = "About vibranceGUI";

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Top, Padding = new Padding(16), ColumnCount = 1, RowCount = 8,
                AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (int row = 0; row < 8; row++) layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.Controls.Add(new Label
            {
                Text = "vibranceGUI", AutoSize = true, Dock = DockStyle.Top,
                Font = new Font("Segoe UI", 18F, FontStyle.Bold), Margin = Padding.Empty
            }, 0, 0);
            var version = new Label { AutoSize = true, Text = "Version " + (typeof(AboutDialog).Assembly
                    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                    ?? typeof(AboutDialog).Assembly.GetName().Version?.ToString()) };
            version.Name = "labelVersion";
            version.Dock = DockStyle.Top;
            version.Margin = new Padding(0, 3, 0, 12);
            version.MaximumSize = new Size(428, 0);
            layout.Controls.Add(version, 0, 1);
            layout.Controls.Add(CreateLink("Upstream repository: SteffenCarlsen/vibranceGUI", "linkRepository", RepositoryUrl, openLink, 16), 0, 2);

            var creator = new FlowLayoutPanel
            {
                AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Top,
                WrapContents = true, Margin = new Padding(0, 0, 0, 8)
            };
            creator.Controls.Add(new Label { Text = "Originally created by", AutoSize = true, Margin = new Padding(0, 0, 8, 0) });
            creator.Controls.Add(CreateLink("juv / juvlarN", "linkOriginalDeveloper", OriginalDeveloperUrl, openLink));
            layout.Controls.Add(creator, 0, 3);
            layout.Controls.Add(CreateLink("Original project: juv/vibranceGUI", "linkOriginalProject", OriginalProjectUrl, openLink, 8), 0, 4);
            var amdCredit = new Label { Text = "Original AMD implementation: juRiiir3.", AutoSize = true };
            amdCredit.Dock = DockStyle.Top;
            amdCredit.Margin = new Padding(0, 0, 0, 12);
            layout.Controls.Add(amdCredit, 0, 5);
            layout.Controls.Add(CreateLink("Support the original developer", "linkOriginalSupport", OriginalSupportUrl, openLink), 0, 6);

            var actions = new FlowLayoutPanel
            {
                AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft,
                Margin = new Padding(0, 16, 0, 0)
            };
            var close = AppTheme.Button("Close", (sender, args) => Close());
            close.Name = "buttonClose";
            close.DialogResult = DialogResult.Cancel;
            actions.Controls.Add(close);
            layout.Controls.Add(actions, 0, 7);
            AcceptButton = close;
            CancelButton = close;
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
                foreach (Control control in layout.Controls)
                    if (control is Label label) label.MaximumSize = new Size(width, 0);
                FitHeight();
            };
            layout.Layout += (sender, args) => FitHeight();
            Controls.Add(layout);
            Shown += (sender, args) => FitHeight();
        }

        private static LinkLabel CreateLink(string text, string name, string url, Action<string> openLink, int bottomMargin = 0)
        {
            var link = new LinkLabel
            {
                Name = name, Text = text, Tag = url, AutoSize = true, MaximumSize = new Size(428, 0),
                LinkColor = AppTheme.LinkColor, ActiveLinkColor = AppTheme.LinkColor,
                VisitedLinkColor = AppTheme.LinkColor, Margin = new Padding(0, 0, 0, bottomMargin)
            };
            link.LinkClicked += (sender, args) => openLink(url);
            return link;
        }
    }
}
