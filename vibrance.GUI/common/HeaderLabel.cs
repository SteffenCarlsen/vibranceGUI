using System.Windows.Forms;

namespace vibrance.GUI.common
{
    internal sealed class HeaderLabel : Label
    {
        protected override void OnPaint(PaintEventArgs args)
        {
            // Native Label adds different overhang padding at different font sizes.
            // Draw at the shared content edge while retaining Label layout/accessibility.
            TextRenderer.DrawText(args.Graphics, Text, Font, ClientRectangle,
                Enabled ? ForeColor : System.Drawing.SystemColors.GrayText,
                TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine |
                TextFormatFlags.PreserveGraphicsClipping | TextFormatFlags.PreserveGraphicsTranslateTransform);
        }
    }
}
