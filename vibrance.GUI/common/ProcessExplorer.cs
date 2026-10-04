using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace vibrance.GUI.common
{
    public partial class ProcessExplorer : Form
    {
        private readonly VibranceGUI _vibranceGui;
        private CancellationTokenSource _reloadCancellation;
        private bool _closing;

        public ProcessExplorer(VibranceGUI vibranceGui, bool initializeProcesses = true)
        {
            _vibranceGui = vibranceGui;
            InitializeComponent();
            if (initializeProcesses) Shown += async (sender, args) => await ReloadProcesses();
            else
            {
                foreach (string program in new[] { "Counter-Strike 2", "VALORANT" })
                {
                    using var icon = VibranceGUI.ExtractProgramIcon(null);
                    iconList.Images.Add(icon);
                    listView.Items.Add(new ListViewItem(new[] { program, @"C:\Games\" + program + @"\game.exe" }, iconList.Images.Count - 1));
                }
                labelStatus.Text = "Preview — 2 programs found.";
                button.Enabled = false;
            }
            FormClosing += (sender, args) => { _closing = true; _reloadCancellation?.Cancel(); };
        }

        private static List<ProcessExplorerEntry> GetAllProcesses(CancellationToken cancellation)
        {
            var entries = new List<ProcessExplorerEntry>();
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Process process in Process.GetProcesses())
            {
                using (process)
                {
                    if (cancellation.IsCancellationRequested) continue;
                    try
                    {
                        if (process.Id == Environment.ProcessId || process.MainWindowHandle == IntPtr.Zero) continue;
                        // The app now runs as x64, so the standard API also reads x64 game paths.
                        string path = process.MainModule?.FileName;
                        if (!string.IsNullOrEmpty(path) && paths.Add(path))
                            entries.Add(new ProcessExplorerEntry(path, VibranceGUI.ExtractProgramIcon(path), process.ProcessName));
                    }
                    catch (Exception ex) when (ex is Win32Exception || ex is InvalidOperationException || ex is NotSupportedException)
                    {
                        // A protected or already exited process can still be added manually.
                    }
                }
            }
            return entries.OrderBy(entry => entry.ProcessName, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private async Task ReloadProcesses()
        {
            if (_reloadCancellation != null || _closing) return;
            button.Enabled = false;
            buttonAdd.Enabled = false;
            labelStatus.Text = "Loading processes...";
            ClearProcessEntries();
            var cancellation = new CancellationTokenSource();
            _reloadCancellation = cancellation;
            try
            {
                List<ProcessExplorerEntry> entries = await Task.Run(() => GetAllProcesses(cancellation.Token));
                if (_closing || IsDisposed || cancellation.IsCancellationRequested)
                {
                    foreach (var entry in entries) entry.Icon.Dispose();
                    return;
                }
                listView.BeginUpdate();
                foreach (var entry in entries)
                {
                    iconList.Images.Add(entry.Icon);
                    var item = new ListViewItem(new[] { entry.ProcessName, entry.Path }, iconList.Images.Count - 1) { Tag = entry };
                    listView.Items.Add(item);
                }
                listView.EndUpdate();
                labelStatus.Text = entries.Count == 0 ? "No programs found. You can add an executable manually." : $"{entries.Count} programs found. Select a program to add.";
            }
            catch (Exception ex)
            {
                if (!_closing && !IsDisposed) labelStatus.Text = "Processes could not be loaded: " + ex.Message;
            }
            finally
            {
                _reloadCancellation = null;
                cancellation.Dispose();
                if (!_closing && !IsDisposed) button.Enabled = true;
            }
        }

        private void ClearProcessEntries()
        {
            if (listView == null || listView.IsDisposed) return;
            foreach (ListViewItem item in listView.Items) (item.Tag as ProcessExplorerEntry)?.Icon?.Dispose();
            listView.Items.Clear();
            iconList.Images.Clear();
        }
        private void listView_DoubleClick(object sender, EventArgs e)
        {
            if (_closing || listView.SelectedItems.Count != 1) return;
            if (listView.SelectedItems[0].Tag is not ProcessExplorerEntry entry) return;
            Hide();
            _vibranceGui.AddProgramExtern(entry);
            Close();
        }
        private async void button_Click(object sender, EventArgs e) => await ReloadProcesses();
    }
}
