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
            // A diagnostic run never starts monitoring or changes display settings.
            if (args.Contains("--diagnostics", StringComparer.OrdinalIgnoreCase))
                return WriteDiagnostics(args);

            using var mutex = new Mutex(true, "vibranceGUI~Mutex", out bool ownsMutex);
            ApplicationConfiguration.Initialize();
            AppTheme.Initialize();
            if (!ownsMutex)
            {
                MessageBox.Show("You can run vibranceGUI only once at a time!", "vibranceGUI",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return 1;
            }
            try
            {
                GraphicsAdapter? adapterOverride = StartupCommand.ParseAdapterOverride(args);
                var adapter = adapterOverride ?? GraphicsAdapterHelper.GetAdapter();
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
                    ? new VibranceGUI(factory, 100, 0, 300, 100, value => value.ToString() + "%", startupAdapterOverride: adapterOverride)
                    : new VibranceGUI(factory, NvidiaDynamicVibranceProxy.NvapiDefaultLevel, 0,
                        NvidiaDynamicVibranceProxy.NvapiMaxLevel, NvidiaDynamicVibranceProxy.NvapiDefaultLevel,
                        value => NvidiaVibranceValueWrapper.Find(value).Percentage, startupAdapterOverride: adapterOverride);
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

        private static int WriteDiagnostics(string[] args)
        {
            try
            {
                int index = Array.FindIndex(args, argument => argument.Equals("--diagnostics", StringComparison.OrdinalIgnoreCase));
                string output = index + 1 < args.Length && !args[index + 1].StartsWith("-")
                    ? args[index + 1] : Path.Combine(Environment.CurrentDirectory, "gpu-diagnostics.json");
                var backends = new List<object>();
                foreach (var adapter in new[] { GraphicsAdapter.Nvidia, GraphicsAdapter.Amd })
                {
                    IVibranceProxy proxy = null;
                    try
                    {
                        proxy = CreateProxy(adapter, new List<ApplicationSetting>(),
                            new Dictionary<string, Tuple<ResolutionModeWrapper, List<ResolutionModeWrapper>>>());
                        var info = proxy.GetVibranceInfo();
                        backends.Add(new { Adapter = adapter.ToString(), Initialized = info.isInitialized,
                            Error = proxy.InitializationError, info.szGpuName, info.activeOutput,
                            DisplayCount = info.activeOutput });
                    }
                    catch (Exception ex) { backends.Add(new { Adapter = adapter.ToString(), Initialized = false, Error = ex.Message }); }
                    finally { proxy?.UnloadLibraryEx(); }
                }
                var report = new
                {
                    Timestamp = DateTimeOffset.Now,
                    Version = Application.ProductVersion,
                    OS = Environment.OSVersion.ToString(),
                    ProcessArchitecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
                    Runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
                    SelectedAdapter = GraphicsAdapterHelper.GetAdapter().ToString(),
                    Displays = Screen.AllScreens.Select(screen => new { screen.DeviceName, screen.Primary, screen.Bounds }),
                    Backends = backends,
                    ReadOnly = true
                };
                output = Path.GetFullPath(output);
                Directory.CreateDirectory(Path.GetDirectoryName(output));
                File.WriteAllText(output, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.Message);
                return 3;
            }
        }

    }
}
