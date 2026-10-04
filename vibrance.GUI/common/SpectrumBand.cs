using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace vibrance.GUI.common
{
    internal sealed class SpectrumBand : Control
    {
        internal SpectrumBand()
        {
            TabStop = false;
            AccessibleRole = AccessibleRole.None;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        }

        protected override void OnPaint(PaintEventArgs args)
        {
            if (Width <= 0 || Height <= 0) return;
            if (SystemInformation.HighContrast)
            {
                using var solid = new SolidBrush(SystemColors.Highlight);
                args.Graphics.FillRectangle(solid, ClientRectangle);
                return;
            }
            using var spectrum = new LinearGradientBrush(ClientRectangle, Color.Empty, Color.Empty, 0F);
            spectrum.InterpolationColors = new ColorBlend
            {
                Colors = new[] { Color.FromArgb(77, 168, 229), Color.FromArgb(133, 114, 216), Color.FromArgb(218, 113, 148), Color.FromArgb(226, 159, 90), Color.FromArgb(178, 197, 102) },
                Positions = new[] { 0F, 0.25F, 0.5F, 0.75F, 1F }
            };
            args.Graphics.FillRectangle(spectrum, ClientRectangle);
        }
    }
}
