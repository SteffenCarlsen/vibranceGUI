using System.ComponentModel;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Forms;

internal static partial class DropdownChecks
{
    private const uint PositionFlags = 0x0010 | 0x0004 | 0x0200; // NOACTIVATE | NOZORDER | NOOWNERZORDER.

    internal static void Verify(ComboBox combo, string output)
    {
        if (!combo.IsHandleCreated || combo.IsDisposed)
            throw new InvalidOperationException("Native dropdown verification needs an existing task-owned ComboBox HWND.");
        if (combo.DrawMode != DrawMode.OwnerDrawFixed || combo.Items.Count < 2 || combo.Items.Count > 128 || combo.SelectedIndex < 0)
            throw new InvalidOperationException("Native dropdown verification needs a fixed owner-draw list with 2–128 items and a selection.");

        IntPtr originalHandle = combo.Handle;
        int originalSelection = combo.SelectedIndex;
        bool originalEnabled = combo.Enabled;
        var info = new ComboBoxInfo { Size = (uint)Marshal.SizeOf<ComboBoxInfo>() };
        Check(GetComboBoxInfo(originalHandle, ref info), "GetComboBoxInfo");
        if (info.List == IntPtr.Zero || info.Combo != originalHandle)
            throw new InvalidOperationException("GetComboBoxInfo did not expose this ComboBox's borrowed native list HWND.");
        GetWindowThreadProcessId(info.List, out uint processId);
        if (processId != Environment.ProcessId || IsWindowVisible(info.List) != 0)
            throw new InvalidOperationException("Dropdown verification refuses a visible list or a list owned by another process.");
        Check(GetWindowRect(info.List, out NativeRectangle originalBounds), "GetWindowRect");

        int itemHeight = (int)SendMessageW(info.List, 0x01A1, IntPtr.Zero, IntPtr.Zero); // LB_GETITEMHEIGHT.
        if (itemHeight <= 0 || itemHeight > 256)
            throw new InvalidOperationException("The hidden native dropdown did not report a usable item height.");
        int width = Math.Clamp(Math.Max(combo.Width, combo.DropDownWidth), 64, 4096);
        int height = checked(itemHeight * combo.Items.Count + 4);
        var draws = new List<(int Index, DrawItemState State)>();
        DrawItemEventHandler observe = (_, args) =>
        {
            if (args.Index >= 0 && (args.State & DrawItemState.ComboBoxEdit) == 0)
                draws.Add((args.Index, args.State));
        };
        combo.DrawItem += observe;
        bool moved = false;
        try
        {
            Check(SetWindowPos(info.List, IntPtr.Zero, -32000, -32000, width, height, PositionFlags), "SetWindowPos(hidden dropdown)");
            moved = true;
            Check(GetClientRect(info.List, out NativeRectangle client), "GetClientRect");
            if (client.Right <= 0 || client.Bottom <= 0 || IsWindowVisible(info.List) != 0)
                throw new InvalidOperationException("The resized native dropdown has no hidden, drawable client area.");

            using var bitmap = new Bitmap(client.Right, client.Bottom);
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.Clear(Color.Magenta); // Preserve unmistakable evidence when hidden WM_PRINT cannot paint.
                IntPtr dc = graphics.GetHdc(); // Borrowed from this bitmap's Graphics, not a desktop/window DC.
                try
                {
                    // No CHECKVISIBLE, SHOWWINDOW, dropdown-opening, focus, activation or capture.
                    SendMessageW(info.List, 0x0317, dc, (IntPtr)(0x0004 | 0x0008)); // WM_PRINT, CLIENT | ERASEBKGND.
                }
                finally { graphics.ReleaseHdc(dc); }
            }
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
            bitmap.Save(output, ImageFormat.Png);
            File.WriteAllText(Path.ChangeExtension(output, ".json"), JsonSerializer.Serialize(new
            {
                Combo = combo.Name,
                originalSelection,
                originalEnabled,
                ItemHeight = itemHeight,
                Font = combo.Font.ToString(),
                ClientWidth = client.Right,
                ClientHeight = client.Bottom,
                Draws = draws.Select(row => new { row.Index, State = row.State.ToString() }).ToArray(),
                Hidden = IsWindowVisible(info.List) == 0
            }, new JsonSerializerOptions { WriteIndented = true }));

            if (draws.Count == 0)
                throw new InvalidOperationException("Native dropdown rendering limitation: hidden list HWND WM_PRINT did not invoke any ComboBox owner-draw rows. The PNG contains the actual capture; no visible popup was opened.");
            var selected = draws.FirstOrDefault(row => (row.State & DrawItemState.Selected) != 0);
            var ordinary = draws.FirstOrDefault(row => (row.State & DrawItemState.Selected) == 0);
            if (!draws.Any(row => (row.State & DrawItemState.Selected) != 0) ||
                !draws.Any(row => (row.State & DrawItemState.Selected) == 0))
                throw new InvalidOperationException("Native dropdown rendering limitation: the hidden list did not paint both selected and unselected rows; inspect the captured PNG/Draws JSON.");

            VerifyRow(combo, info.List, bitmap, selected.Index, selected.State, itemHeight);
            VerifyRow(combo, info.List, bitmap, ordinary.Index, ordinary.State, itemHeight);
        }
        finally
        {
            combo.DrawItem -= observe;
            if (moved)
                Check(SetWindowPos(info.List, IntPtr.Zero, originalBounds.Left, originalBounds.Top,
                    originalBounds.Right - originalBounds.Left, originalBounds.Bottom - originalBounds.Top, PositionFlags),
                    "Restore hidden dropdown bounds");
            if (combo.IsDisposed || combo.Handle != originalHandle || combo.SelectedIndex != originalSelection ||
                combo.Enabled != originalEnabled || IsWindowVisible(info.List) != 0)
                throw new InvalidOperationException("Native dropdown verification changed its ComboBox HWND, selection, enabled state or popup visibility.");
        }
    }

    private static void VerifyRow(ComboBox combo, IntPtr list, Bitmap bitmap, int index, DrawItemState state, int itemHeight)
    {
        var bounds = new NativeRectangle();
        if (SendMessageRect(list, 0x0198, (IntPtr)index, ref bounds).ToInt64() == -1) // LB_GETITEMRECT.
            throw new InvalidOperationException("The native dropdown could not provide its painted item bounds.");
        bool disabled = !combo.Enabled || (state & (DrawItemState.Disabled | DrawItemState.Grayed)) != 0;
        bool selected = !disabled && (state & DrawItemState.Selected) != 0;
        Color expectedBackground = selected ? SystemColors.Highlight : SystemColors.Window;
        Color expectedText = disabled ? SystemColors.GrayText
            : selected ? !SystemInformation.HighContrast && Application.IsDarkModeEnabled ? Color.White : SystemColors.HighlightText
            : SystemColors.WindowText;
        int left = Math.Max(0, bounds.Left);
        int right = Math.Min(bitmap.Width, bounds.Right);
        int top = Math.Max(0, bounds.Top);
        int bottom = Math.Min(bitmap.Height, bounds.Bottom);
        if (right - left < 16 || bottom - top < 6 || bounds.Bottom - bounds.Top != itemHeight)
            throw new InvalidOperationException("The hidden native dropdown's owner-draw row bounds are clipped or disagree with its item height.");
        Color background = bitmap.GetPixel(right - 8, top + (bottom - top) / 2);
        if (ColorDistance(background, expectedBackground) > 6)
            throw new InvalidOperationException($"Native dropdown row {index} ({(selected ? "selected" : "unselected")}) background is {background}, expected {expectedBackground}.");
        if (!disabled && Contrast(expectedText, expectedBackground) < 4.45)
            throw new InvalidOperationException($"Native dropdown row {index} has insufficient system text/background contrast.");

        string text = combo.GetItemText(combo.Items[index]) ?? string.Empty;
        Size measured = TextRenderer.MeasureText(text, combo.Font, new Size(right - left, itemHeight),
            TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
        int textRight = Math.Min(right - 3, left + measured.Width + 3);
        int textPixels = 0;
        for (int y = top + 2; y < bottom - 2; y++)
            for (int x = left + 3; x < textRight; x++)
            {
                Color pixel = bitmap.GetPixel(x, y);
                if (ColorDistance(pixel, expectedText) <= 48 && ColorDistance(pixel, expectedBackground) >= 30)
                    textPixels++;
            }
        if (!string.IsNullOrWhiteSpace(text) && textPixels < 8)
            throw new InvalidOperationException($"Native dropdown row {index} painted too few readable text pixels ({textPixels}) near {expectedText}; inspect the actual popup capture.");
    }

    private static int ColorDistance(Color first, Color second) =>
        Math.Max(Math.Abs(first.R - second.R), Math.Max(Math.Abs(first.G - second.G), Math.Abs(first.B - second.B)));
    private static double Contrast(Color first, Color second)
    {
        static double Linear(byte channel)
        {
            double value = channel / 255.0;
            return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
        }
        static double Luminance(Color value) => 0.2126 * Linear(value.R) + 0.7152 * Linear(value.G) + 0.0722 * Linear(value.B);
        double left = Luminance(first), right = Luminance(second);
        return (Math.Max(left, right) + 0.05) / (Math.Min(left, right) + 0.05);
    }
    private static void Check(int result, string operation)
    {
        if (result == 0) throw new Win32Exception(Marshal.GetLastPInvokeError(), operation + " failed.");
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRectangle { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    private struct ComboBoxInfo
    {
        public uint Size;
        public NativeRectangle ItemBounds, ButtonBounds;
        public uint ButtonState;
        public IntPtr Combo, Item, List;
    }
    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial int GetComboBoxInfo(IntPtr combo, ref ComboBoxInfo info);
    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial int GetWindowRect(IntPtr window, out NativeRectangle bounds);
    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial int GetClientRect(IntPtr window, out NativeRectangle bounds);
    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial int SetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
    [LibraryImport("user32.dll")]
    private static partial int IsWindowVisible(IntPtr window);
    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [LibraryImport("user32.dll")]
    private static partial IntPtr SendMessageW(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    [LibraryImport("user32.dll", EntryPoint = "SendMessageW")]
    private static partial IntPtr SendMessageRect(IntPtr window, uint message, IntPtr wParam, ref NativeRectangle lParam);
}
