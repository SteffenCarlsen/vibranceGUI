using System;
using System.Drawing;
using System.Windows.Forms;

namespace vibrance.GUI.common
{
    internal class ThemedComboBox : ComboBox
    {
        internal ThemedComboBox()
        {
            DrawMode = DrawMode.OwnerDrawFixed;
            RefreshThemeColors();
        }

        internal void RefreshThemeColors()
        {
            if (IsDisposed || Disposing) return;
            // Explicit RGB values replace native brushes cached under the previous palette.
            BackColor = Color.FromArgb(SystemColors.Window.ToArgb());
            ForeColor = Color.FromArgb(SystemColors.WindowText.ToArgb());
            Invalidate();
        }

        protected override void OnSystemColorsChanged(EventArgs args)
        {
            base.OnSystemColorsChanged(args);
            RefreshThemeColors();
        }

        protected override void OnDrawItem(DrawItemEventArgs args)
        {
            bool closedField = (args.State & DrawItemState.ComboBoxEdit) != 0;
            bool disabled = !Enabled || (args.State & (DrawItemState.Disabled | DrawItemState.Grayed)) != 0;
            bool selected = !closedField && !disabled && (args.State & DrawItemState.Selected) != 0;
            Color background = closedField ? SystemColors.Control
                : selected ? SystemColors.Highlight : SystemColors.Window;
            Color selectedText = Application.IsDarkModeEnabled && !SystemInformation.HighContrast
                ? Color.White : SystemColors.HighlightText;
            Color foreground = disabled ? SystemColors.GrayText
                : closedField ? SystemColors.ControlText
                : selected ? selectedText : SystemColors.WindowText;

            using (var brush = new SolidBrush(background))
                args.Graphics.FillRectangle(brush, args.Bounds);
            string text = args.Index >= 0 && args.Index < Items.Count
                ? GetItemText(Items[args.Index]) : Text;
            var flags = TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter
                | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis;
            if (RightToLeft == RightToLeft.Yes) flags |= TextFormatFlags.RightToLeft | TextFormatFlags.Right;
            TextRenderer.DrawText(args.Graphics, text, args.Font ?? Font, args.Bounds, foreground, flags);
            if ((args.State & DrawItemState.Focus) != 0 && (args.State & DrawItemState.NoFocusRect) == 0)
                ControlPaint.DrawFocusRectangle(args.Graphics, args.Bounds, foreground, background);

            base.OnDrawItem(args);
        }
    }
}
