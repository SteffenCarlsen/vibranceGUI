using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace vibrance.GUI.common
{
    partial class ProcessExplorer
    {
        private IContainer components;
        private ListView listView;
        private ImageList iconList;
        private Button button, buttonAdd;
        private Label labelStatus;

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _closing = true;
                _reloadCancellation?.Cancel();
                ClearProcessEntries();
                components?.Dispose();
            }
            base.Dispose(disposing);
        }
        private void InitializeComponent()
        {
            components = new Container();
            var resources = new ComponentResourceManager(typeof(ProcessExplorer));
            AppTheme.Configure(this);
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(780, 460);
            MinimumSize = new Size(580, 360);
            StartPosition = FormStartPosition.CenterParent;
            Icon = (Icon)resources.GetObject("$this.Icon");
            Text = "vibranceGUI Process Explorer";
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(24) };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var header = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, ColumnCount = 2, Margin = new Padding(0, 0, 0, 16) };
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            header.Controls.Add(new Label { Text = "Add a running program", AutoSize = true, Font = new Font("Segoe UI", 18F, FontStyle.Bold), Margin = Padding.Empty }, 0, 0);
            button = AppTheme.Button("Reload Processes", button_Click);
            header.Controls.Add(button, 1, 0);
            layout.Controls.Add(header, 0, 0);
            iconList = new ImageList(components) { ImageSize = new Size(24, 24), ColorDepth = ColorDepth.Depth32Bit };
            listView = new ListView
            {
                Dock = DockStyle.Fill, View = View.Details, SmallImageList = iconList, FullRowSelect = true,
                HideSelection = false, MultiSelect = false, HeaderStyle = ColumnHeaderStyle.Nonclickable,
                BackColor = SystemColors.Window, ForeColor = SystemColors.WindowText, AccessibleName = "Running programs", Margin = new Padding(0, 0, 0, 16)
            };
            listView.Columns.Add("Programs", 210);
            listView.Columns.Add("Full Path", 470);
            listView.DoubleClick += listView_DoubleClick;
            listView.SelectedIndexChanged += (sender, args) => buttonAdd.Enabled = listView.SelectedItems.Count == 1;
            listView.KeyDown += (sender, args) => { if (args.KeyCode == Keys.Enter) { listView_DoubleClick(sender, args); args.Handled = true; } };
            layout.Controls.Add(listView, 0, 1);
            var footer = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, ColumnCount = 2, Margin = Padding.Empty };
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            labelStatus = new Label { Text = "Loading processes...", AutoSize = true, Anchor = AnchorStyles.Left, Margin = Padding.Empty };
            footer.Controls.Add(labelStatus, 0, 0);
            buttonAdd = AppTheme.Button("Add", listView_DoubleClick);
            buttonAdd.Enabled = false;
            footer.Controls.Add(buttonAdd, 1, 0);
            layout.Controls.Add(footer, 0, 2);
            Controls.Add(layout);
            AcceptButton = buttonAdd;
        }
    }
}
