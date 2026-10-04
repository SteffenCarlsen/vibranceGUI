using System.Drawing;
using System.Windows.Forms;

namespace vibrance.GUI.common
{
    // Retain native grouping/accessibility, with a lighter visual hierarchy.
    internal sealed class QuietSection : GroupBox
    {
        internal QuietSection()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        }

        protected override void OnPaint(PaintEventArgs args)
        {
            if (SystemInformation.HighContrast) { base.OnPaint(args); return; }
            args.Graphics.Clear(BackColor);
            using var heading = new Font(Font, FontStyle.Bold);
            var textSize = TextRenderer.MeasureText(args.Graphics, Text, heading, Size.Empty, TextFormatFlags.NoPrefix);
            TextRenderer.DrawText(args.Graphics, Text, heading, new Point(Padding.Left, 0), ForeColor, TextFormatFlags.NoPrefix);
            int start = Padding.Left + textSize.Width + (int)(12 * DeviceDpi / 96F);
            int end = Width - Padding.Right - 1;
            if (start >= end) return;
            using var rule = new Pen(SystemColors.ControlDark);
            args.Graphics.DrawLine(rule, start, textSize.Height / 2, end, textSize.Height / 2);
        }
    }
}
