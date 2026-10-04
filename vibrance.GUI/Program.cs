using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Windows.Forms;
using vibrance.GUI.AMD;
using vibrance.GUI.AMD.vendor;
using vibrance.GUI.common;
using vibrance.GUI.NVIDIA;

namespace vibrance.GUI
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            using var mutex = new Mutex(true, "vibranceGUI~Mutex", out bool ownsMutex);
            ApplicationConfiguration.Initialize();
            if (!ownsMutex)
            {
                MessageBox.Show("You can run vibranceGUI only once at a time!", "vibranceGUI",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return 1;
            }
            try
            {
                var adapter = GraphicsAdapterHelper.GetAdapter();
                if (adapter == GraphicsAdapter.Unknown)
                {
                    MessageBox.Show("No attached display with supported NVIDIA digital vibrance or AMD saturation control was found. " +
                        "Keep your integrated graphics and chipset drivers installed. Check which GPU your monitor is connected to. " +
                        "You can run --diagnostics <file.json> for a read-only report.\n\n" + GraphicsAdapterHelper.LastError, "vibranceGUI",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return 2;
                }
                Func<List<ApplicationSetting>, Dictionary<string, Tuple<ResolutionModeWrapper, List<ResolutionModeWrapper>>>, IVibranceProxy> factory =
                    (profiles, resolutions) => CreateProxy(adapter, profiles, resolutions);
                using var window = adapter == GraphicsAdapter.Amd
                    ? new VibranceGUI(factory, 100, 0, 300, 100, value => value.ToString() + "%")
                    : new VibranceGUI(factory, NvidiaDynamicVibranceProxy.NvapiDefaultLevel, 0,
                        NvidiaDynamicVibranceProxy.NvapiMaxLevel, NvidiaDynamicVibranceProxy.NvapiDefaultLevel,
                        value => NvidiaVibranceValueWrapper.Find(value).Percentage);
                if (args.Contains("-minimized", StringComparer.OrdinalIgnoreCase))
                {
                    window.WindowState = FormWindowState.Minimized;
                    window.SetAllowVisible(false);
                }
                window.Text += $" ({adapter.ToString().ToUpperInvariant()}, {Application.ProductVersion})";
                Application.Run(window);
                return 0;
            }
            catch (Exception ex)
            {
                VibranceGUI.Log(ex);
                MessageBox.Show("vibranceGUI could not start: " + ex.Message, "vibranceGUI",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 3;
            }
            finally
            {
                mutex.ReleaseMutex();
            }
        }



        internal static IVibranceProxy CreateProxy(GraphicsAdapter adapter, List<ApplicationSetting> profiles,
            Dictionary<string, Tuple<ResolutionModeWrapper, List<ResolutionModeWrapper>>> resolutions)
        {
            return adapter == GraphicsAdapter.Amd
                ? new AmdDynamicVibranceProxy(Environment.Is64BitProcess ? new AmdAdapter64() : (IAmdAdapter)new AmdAdapter32(), profiles, resolutions)
                : new NvidiaDynamicVibranceProxy(profiles, resolutions);
        }



    }
}
