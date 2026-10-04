using System.Runtime.InteropServices;
using System.Text.Json;
using vibrance.GUI.common;

internal static partial class CaptionChecks
{
    public static void Verify(Form[] windows, string outputDirectory)
    {
        var probes = windows.Select(window => new FrameProbe(window.Handle)).ToArray();
        var handles = windows.Select(window => window.Handle).ToArray();
        var bounds = windows.Select(window => (window.Bounds, window.ClientRectangle)).ToArray();
        var evidence = new List<object>();
        try
        {
            // Repeat the current mode too: there may be no later Windows palette
            // notification to repaint a caption left over from the previous pass.
            foreach (var theme in new[] { ThemePreference.Light, ThemePreference.Dark, ThemePreference.Light,
                ThemePreference.System, ThemePreference.Dark, ThemePreference.Light })
            {
                var counts = probes.Select(probe => probe.PaintCount).ToArray();
                IntPtr foreground = GetForegroundWindow();
                IntPtr focus = GetFocus();
                AppTheme.Apply(theme);
                // No DoEvents, delays, activation or resize before these assertions.
                for (int index = 0; index < windows.Length; index++)
                {
                    if (probes[index].PaintCount <= counts[index])
                        throw new InvalidOperationException($"{windows[index].GetType().Name} did not repaint its non-client frame synchronously for {theme}.");
                    if (windows[index].Handle != handles[index]
                        || (windows[index].Bounds, windows[index].ClientRectangle) != bounds[index])
                        throw new InvalidOperationException("Caption refresh recreated or moved/resized a window.");
                }
                if (GetForegroundWindow() != foreground || GetFocus() != focus)
                    throw new InvalidOperationException("Caption refresh changed activation or keyboard focus.");
                evidence.Add(new { Theme = theme.ToString(),
                    Frames = windows.Select((window, index) => new {
                        Window = window.GetType().Name,
                        SynchronousNonClientPaints = probes[index].PaintCount - counts[index]
                    }).ToArray() });
            }
            File.WriteAllText(Path.Combine(outputDirectory, "caption-refresh.json"),
                JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine("PASS: synchronous WM_NCPAINT processing across repeated Light/Dark/System changes; stable HWNDs, bounds, foreground and focus.");
        }
        finally { foreach (var probe in probes) probe.ReleaseHandle(); }
    }

    private sealed class FrameProbe : NativeWindow
    {
        internal int PaintCount { get; private set; }
        internal FrameProbe(IntPtr window) { AssignHandle(window); }
        protected override void WndProc(ref Message message)
        {
            base.WndProc(ref message);
            if (message.Msg == 0x0085) PaintCount++; // WM_NCPAINT completed.
        }
    }

    [LibraryImport("user32.dll")]
    private static partial IntPtr GetForegroundWindow();
    [LibraryImport("user32.dll")]
    private static partial IntPtr GetFocus();
}
