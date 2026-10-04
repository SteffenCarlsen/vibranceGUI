using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace vibrance.GUI.common
{
    public partial class VibranceSettings : Form
    {
        private readonly IVibranceProxy _v;
        private readonly ListViewItem _sender;
        private readonly Func<int, string> _resolveLabelLevel;
        private bool _loading = true;

        public VibranceSettings(IVibranceProxy v, int minValue, int maxValue, int defaultValue, ListViewItem sender, ApplicationSetting setting, List<ResolutionModeWrapper> supportedResolutionList, Func<int, string> resolveLabelLevel)
        {
            InitializeComponent();
            this.trackBarIngameLevel.Minimum = minValue;
            this.trackBarIngameLevel.Maximum = maxValue;
            this.trackBarIngameLevel.Value = Math.Clamp(defaultValue, minValue, maxValue);
            this._sender = sender;
            _resolveLabelLevel = resolveLabelLevel;
            this._v = v;
            labelIngameLevel.Text = _resolveLabelLevel(trackBarIngameLevel.Value);
            this.labelTitle.Text += $@"""{sender.Text}""";
            this.pictureBox.Image = this._sender.ListView.LargeImageList.Images[this._sender.ImageIndex];
            foreach (ResolutionModeWrapper resolution in supportedResolutionList ?? new List<ResolutionModeWrapper>())
                this.cBoxResolution.Items.Add(resolution);
            this.checkBoxResolution.Enabled = this.cBoxResolution.Items.Count > 0;
            // If the setting is new, we don't need to set the progress bar value
            if (setting != null)
            {
                // Sets the progress bar value to the Ingame Vibrance setting
                this.trackBarIngameLevel.Value = Math.Clamp(setting.IngameLevel, minValue, maxValue);
                this.cBoxResolution.SelectedItem = setting.ResolutionSettings;
                this.checkBoxResolution.Checked = setting.IsResolutionChangeNeeded && this.cBoxResolution.SelectedItem != null;
                // Necessary to reload the label which tells the percentage
                labelIngameLevel.Text = _resolveLabelLevel(trackBarIngameLevel.Value);
            }
            if (cBoxResolution.SelectedItem == null && cBoxResolution.Items.Count > 0) cBoxResolution.SelectedIndex = 0;
            _loading = false;
        }

        private void trackBarIngameLevel_Scroll(object sender, EventArgs e)
        {
            if (_loading) return;
            _v.SetVibranceIngameLevel(trackBarIngameLevel.Value);
            labelIngameLevel.Text = _resolveLabelLevel(trackBarIngameLevel.Value);
        }

        private void buttonSave_Click(object sender, EventArgs e)
        {
            if (checkBoxResolution.Checked && cBoxResolution.SelectedItem == null)
            {
                MessageBox.Show(this, "Select an available resolution before saving.", "vibranceGUI", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            this.DialogResult = DialogResult.OK;
            this.Close();
        }

        public ApplicationSetting GetApplicationSetting()
        {
            return new ApplicationSetting(_sender.Text, _sender.Tag.ToString(), this.trackBarIngameLevel.Value, 
                (ResolutionModeWrapper)this.cBoxResolution.SelectedItem, this.checkBoxResolution.Checked);
        }

        private void checkBoxResolution_CheckedChanged(object sender, EventArgs e)
        {
            this.cBoxResolution.Enabled = this.checkBoxResolution.Checked;
        }
    }
}
