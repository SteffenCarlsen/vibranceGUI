using System.Runtime.InteropServices;
using vibrance.GUI.common;
using vibrance.GUI.NVIDIA;
using vibrance.GUI.AMD.vendor;

internal static class RuntimeChecks
{
    private const string GamePath = @"C:\Games\Game.exe";
    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    public static void Run()
    {
        Assert(Marshal.SizeOf<NvDisplayDvcInfo>() == 16, "Legacy NVAPI DVC ABI changed.");
        Assert(Marshal.SizeOf<NvDisplayDvcInfoEx>() == 20, "Extended NVAPI DVC ABI changed.");
        Assert(Marshal.SizeOf<AmdAdapter.AdapterInfo>() == 1572, "AMD AdapterInfo ABI changed.");
        Assert(Marshal.SizeOf<Devmode>() == 220, "DEVMODEW ABI changed.");
        Assert(NvidiaDisplayBackend.MapLevel(0, 0, 100) == 0 && NvidiaDisplayBackend.MapLevel(63, 0, 100) == 100,
            "Legacy NVIDIA settings do not map to native neutral/maximum.");
        Assert(NvidiaDisplayBackend.MapLevel(0, 50, 100) == 50 && NvidiaDisplayBackend.MapLevel(63, 50, 100) == 100,
            "NVIDIA native neutral range was ignored.");
        var backend = new RecordingBackend();
        var game = new ApplicationSetting("Game", GamePath, 50, null, false);
        var controller = new TestController(backend, new List<ApplicationSetting> { game });
        controller.SetApplicationSettings(new List<ApplicationSetting> { game });
        controller.SetVibranceWindowsLevel(0);
        controller.SetAffectPrimaryMonitorOnly(true);
        controller.SetNeverSwitchResolution(true);
        Assert(backend.Writes.Count == 0, "Initialization wrote placeholder desktop colors.");
        controller.SetShouldRun(true);
        controller.ApplyForeground("Game", "DISPLAY_B", GamePath);
        Assert(backend.Writes.SequenceEqual(new[] { ("DISPLAY_A", 50) }), "Primary-only scope targeted the game monitor instead of the primary display.");
        controller.ApplyForeground("Game", "DISPLAY_B", GamePath);
        Assert(backend.Writes.Count == 1, "Duplicate foreground event repeated a color call.");
        controller.ApplyForeground("Game", "DISPLAY_B", @"C:\Unrelated\Game.exe");
        Assert(backend.Writes.Last() == ("DISPLAY_A", 0), "An unrelated same-name executable received the profile.");
        backend.Writes.Clear();
        controller.ApplyForeground("Game", "DISPLAY_B", GamePath);
        controller.ApplyForeground("Explorer", "DISPLAY_B", @"C:\Windows\explorer.exe");
        Assert(backend.Writes.SequenceEqual(new[] { ("DISPLAY_A", 50), ("DISPLAY_A", 0) }), "Focus on another monitor stranded color or touched an unowned display.");
        backend.Writes.Clear();
        controller.SetAffectPrimaryMonitorOnly(false);
        controller.ApplyForeground("Game", "DISPLAY_A", GamePath);
        Assert(backend.Writes.SequenceEqual(new[] { ("DISPLAY_A", 50), ("DISPLAY_B", 50) }), "All-monitor scope did not apply both displays.");
        controller.SetShouldRun(false);
        Assert(backend.Writes.TakeLast(2).SequenceEqual(new[] { ("DISPLAY_A", 0), ("DISPLAY_B", 0) }), "Pause did not restore modified displays.");
        int pausedCount = backend.Writes.Count;
        controller.ApplyForeground("Game", "DISPLAY_A", GamePath);
        Assert(backend.Writes.Count == pausedCount, "Paused monitoring wrote colors.");
        controller.SetShouldRun(true);
        controller.ApplyForeground("Game", "DISPLAY_A", GamePath);
        controller.SetApplicationSettings(new List<ApplicationSetting>());
        controller.ApplyForeground("Game", "DISPLAY_A", GamePath);
        Assert(backend.Writes.TakeLast(2).All(write => write.Item2 == 0), "Removing the last profile stranded colors.");
        controller.SetApplicationSettings(new List<ApplicationSetting> { game });
        controller.ApplyForeground("Game", "DISPLAY_A", GamePath);
        backend.FailDesktopRestore = true;
        controller.ApplyForeground("Explorer", "DISPLAY_B", @"C:\Windows\explorer.exe");
        int failedCount = backend.Writes.Count;
        backend.FailDesktopRestore = false;
        controller.ApplyForeground("Explorer", "DISPLAY_B", @"C:\Windows\explorer.exe");
        Assert(backend.Writes.Count == failedCount + 2 && backend.Writes.TakeLast(2).All(write => write.Item2 == 0), "Failed restores weren't retained and retried.");
        controller.ApplyForeground("Game", "DISPLAY_A", GamePath);
        controller.HandleDvcExit();
        Assert(backend.Writes.TakeLast(2).All(write => write.Item2 == 0), "Exit did not restore owned displays.");
        Assert(controller.UnloadLibraryEx() && backend.Disposed, "Runtime cleanup did not release backend.");
        CheckPendingState(game);
        CheckResolutionOwnership();
        CheckResolutionWithoutColorTargets();
        Console.WriteLine("PASS: GPU ABI/scales; no startup write; exact path matching; primary/all monitor scope; duplicate events; cross-monitor restore; pause; last-profile removal; failed-restore retry; exit.");
    }

    private static void CheckPendingState(ApplicationSetting game)
    {
        var backend = new RecordingBackend();
        var controller = new TestController(backend, new List<ApplicationSetting> { game });
        controller.SetVibranceWindowsLevel(12);
        controller.SetAffectPrimaryMonitorOnly(true);
        Assert(backend.Writes.Count == 0, "Saved settings configuration wrote colors before activation.");
        controller.SetShouldRun(true);
        controller.ApplyForeground("Explorer", "DISPLAY_B", @"C:\Windows\explorer.exe");
        Assert(backend.Writes.SequenceEqual(new[] { ("DISPLAY_A", 12) }), "Saved desktop level was not applied on activation.");
        controller.SetShouldRun(false);
        controller.SetVibranceWindowsLevel(17);
        controller.SetShouldRun(true);
        controller.ApplyForeground("Explorer", "DISPLAY_B", @"C:\Windows\explorer.exe");
        Assert(backend.Writes.Last() == ("DISPLAY_A", 17), "Paused desktop level did not apply on resume.");
        controller.SetVibranceWindowsLevel(0);
        controller.SetAffectPrimaryMonitorOnly(false);
        controller.ApplyForeground("Game", "DISPLAY_A", GamePath);
        backend.FailDesktopRestore = true;
        controller.SetAffectPrimaryMonitorOnly(true);
        controller.ApplyForeground("Game", "DISPLAY_A", GamePath);
        backend.FailDesktopRestore = false;
        int beforeRetry = backend.Writes.Count;
        controller.ApplyForeground("Game", "DISPLAY_A", GamePath);
        Assert(backend.Writes.Skip(beforeRetry).Contains(("DISPLAY_B", 0)), "Changing scope cached a failed secondary restore.");
        Assert(DisplayVibranceController.MatchesProcess(game, "Game", null), "Protected process name fallback was removed.");
        controller.SetShouldRun(false);
        controller.UnloadLibraryEx();
    }

    private static void CheckResolutionOwnership()
    {
        var original = Mode(1920, 1080, 144);
        var gameMode = new ResolutionModeWrapper(Mode(1280, 720, 120));
        var modes = new FakeModes { Current = original };
        var profile = new ApplicationSetting("Game", GamePath, 50, gameMode, true);
        var colorOnly = new ApplicationSetting("Other", @"C:\Games\Other.exe", 40, null, false);
        var backend = new RecordingBackend();
        var controller = new ResolutionController(backend, new List<ApplicationSetting> { profile, colorOnly }, modes, original);
        controller.SetVibranceWindowsLevel(0);
        controller.SetShouldRun(true);
        controller.ApplyForeground("Other", "DISPLAY_A", colorOnly.FileName);
        Assert(modes.ReadCount == 0 && modes.Calls.Count == 0, "A vibrance-only profile touched resolution APIs.");
        controller.ApplyForeground("Game", "DISPLAY_A", GamePath);
        Assert(modes.Current.dmPelsWidth == 1280, "Game resolution was not applied.");
        controller.ApplyForeground("Other", "DISPLAY_A", colorOnly.FileName);
        Assert(modes.Current.dmPelsWidth == 1920 && modes.Current.dmDisplayFrequency == 144, "Game-to-game color-only transition stranded temporary resolution.");
        controller.ApplyForeground("Game", "DISPLAY_A", GamePath);
        controller.SetNeverSwitchResolution(true);
        Assert(modes.Current.dmPelsWidth == 1920, "Never change resolutions stranded an already owned mode.");
        controller.SetNeverSwitchResolution(false);
        modes.RejectAndChangeExternally = true;
        controller.ApplyForeground("Game", "DISPLAY_A", GamePath);
        int afterRejection = modes.Calls.Count;
        controller.ApplyForeground("Explorer", "DISPLAY_A", @"C:\Windows\explorer.exe");
        Assert(modes.Calls.Count == afterRejection && modes.Current.dmPelsWidth == 3840,
            "A rejected mode test claimed and reverted a concurrent external/game resolution.");
        modes.Current = original;
        modes.RejectAndChangeExternally = false;
        modes.PartialApplyFailure = true;
        controller.ApplyForeground("Game", "DISPLAY_A", GamePath);
        modes.PartialApplyFailure = false;
        modes.FailRestore = true;
        controller.ApplyForeground("Explorer", "DISPLAY_A", @"C:\Windows\explorer.exe");
        Assert(modes.Current.dmPelsWidth == 1280, "Failed restore fixture unexpectedly changed resolution.");
        modes.FailRestore = false;
        controller.ApplyForeground("Explorer", "DISPLAY_A", @"C:\Windows\explorer.exe");
        Assert(modes.Current.dmPelsWidth == 1920 && modes.Current.dmDisplayFrequency == 144,
            "Partial apply baseline or failed resolution restore was not retained for retry.");
        controller.UnloadLibraryEx();
        Console.WriteLine("PASS: saved desktop activation; paused edits; scope-change retry; process fallback; no unowned resolution changes; rejected-test/external-mode race; partial apply and failed-restore retry.");
    }

    private static void CheckResolutionWithoutColorTargets()
    {
        var original = Mode(1920, 1080, 144);
        var modes = new FakeModes { Current = original };
        var profile = new ApplicationSetting("Game", GamePath, 50,
            new ResolutionModeWrapper(Mode(1280, 720, 120)), true);
        var backend = new RecordingBackend("DISPLAY_B");
        var controller = new ResolutionController(backend, new List<ApplicationSetting> { profile },
            modes, original, resolutionDisplay: "DISPLAY_B");
        Assert(controller.GetVibranceInfo().isInitialized, "The supported secondary output did not initialize the backend.");
        controller.SetVibranceWindowsLevel(0);
        controller.SetAffectPrimaryMonitorOnly(true);
        controller.SetShouldRun(true);
        controller.ApplyForeground("Game", "DISPLAY_B", GamePath);
        Assert(modes.Current.dmPelsWidth == 1280 && modes.Devices.SequenceEqual(new[] { "DISPLAY_B" }),
            "No qualifying primary color target suppressed the secondary display's resolution profile.");
        Assert(backend.Writes.Count == 0, "Primary-only scope changed colors on the supported secondary display.");
        controller.ApplyForeground("Game", "DISPLAY_B", GamePath);
        Assert(modes.Calls.Count == 1, "Repeated foreground events reapplied an unchanged resolution.");

        controller.SetAffectPrimaryMonitorOnly(false);
        controller.ApplyForeground("Game", "DISPLAY_B", GamePath);
        Assert(backend.Writes.Single() == ("DISPLAY_B", 50), "All-monitor scope did not apply the supported display's color.");
        backend.FailDesktopRestore = true;
        controller.SetAffectPrimaryMonitorOnly(true);
        controller.ApplyForeground("Game", "DISPLAY_B", GamePath);
        backend.FailDesktopRestore = false;
        controller.ApplyForeground("Game", "DISPLAY_B", GamePath);
        Assert(backend.Writes.TakeLast(2).All(write => write == ("DISPLAY_B", 0)) && modes.Calls.Count == 1,
            "Scope changes did not retry color restoration while retaining the independent game resolution.");

        modes.FailRestore = true;
        controller.ApplyForeground("Explorer", "DISPLAY_A", @"C:\Windows\explorer.exe");
        Assert(modes.Current.dmPelsWidth == 1280, "The failed restore fixture unexpectedly restored the mode.");
        modes.FailRestore = false;
        controller.ApplyForeground("Explorer", "DISPLAY_A", @"C:\Windows\explorer.exe");
        Assert(modes.Current.dmPelsWidth == 1920 && modes.Devices.All(device => device == "DISPLAY_B"),
            "The captured secondary-display mode was not retained and restored after failure.");
        int reads = modes.ReadCount, writes = modes.Calls.Count;
        controller.SetNeverSwitchResolution(true);
        controller.ApplyForeground("Game", "DISPLAY_B", GamePath);
        Assert(modes.ReadCount == reads && modes.Calls.Count == writes,
            "Never change resolutions made resolution calls with no qualifying color target.");
        Assert(controller.UnloadLibraryEx(), "Mixed-output resolution cleanup left an owned change behind.");
        Console.WriteLine("PASS: independent resolution profiles with no color targets; duplicate suppression; scope changes; failed restoration retry; global resolution opt-out.");
    }

    private static Devmode Mode(uint width, uint height, uint refresh) => new()
    { dmPelsWidth = width, dmPelsHeight = height, dmBitsPerPel = 32, dmDisplayFrequency = refresh };

    private sealed class ResolutionController : DisplayVibranceController
    {
        public ResolutionController(RecordingBackend backend, List<ApplicationSetting> profiles, FakeModes modes, Devmode original,
            string resolutionDisplay = "DISPLAY_A")
            : base(backend, GraphicsAdapter.Nvidia, 0, profiles,
                new Dictionary<string, Tuple<ResolutionModeWrapper, List<ResolutionModeWrapper>>>
                { [resolutionDisplay] = Tuple.Create(new ResolutionModeWrapper(original), new List<ResolutionModeWrapper>()) },
                subscribeToForegroundEvents: false, primaryDisplayName: "DISPLAY_A",
                readResolution: modes.Read, changeResolution: modes.Change) { }
    }

    private sealed class FakeModes
    {
        public Devmode Current;
        public bool RejectAndChangeExternally, PartialApplyFailure, FailRestore;
        public int ReadCount;
        public List<ResolutionModeWrapper> Calls = new();
        public List<string> Devices = new();
        public Devmode? Read(string _) { ReadCount++; return Current; }
        public bool Change(ResolutionModeWrapper requested, string device, out bool attempted)
        {
            Calls.Add(requested);
            Devices.Add(device);
            attempted = false;
            if (RejectAndChangeExternally) { Current = Mode(3840, 2160, 60); return false; }
            attempted = true;
            if (FailRestore && requested.DmPelsWidth == 1920) return false;
            Current = Mode(requested.DmPelsWidth, requested.DmPelsHeight, requested.DmDisplayFrequency);
            return !PartialApplyFailure;
        }
    }

    private sealed class TestController : DisplayVibranceController
    {
        public TestController(RecordingBackend backend, List<ApplicationSetting> profiles)
            : base(backend, GraphicsAdapter.Nvidia, 0, profiles,
                new Dictionary<string, Tuple<ResolutionModeWrapper, List<ResolutionModeWrapper>>>(),
                subscribeToForegroundEvents: false, primaryDisplayName: "DISPLAY_A") { }
    }

    private sealed class RecordingBackend : IDisplayVibranceBackend
    {
        public RecordingBackend(params string[] displays) => DisplayNames = displays.Length == 0
            ? new[] { "DISPLAY_A", "DISPLAY_B" } : displays;
        public IReadOnlyList<string> DisplayNames { get; }
        public string GpuName => "Regression fake";
        public string InitializationError => "";
        public List<(string, int)> Writes { get; } = new();
        public bool FailDesktopRestore { get; set; }
        public bool Disposed { get; private set; }
        public bool SetLevel(string deviceName, int level)
        {
            Writes.Add((deviceName, level));
            return !(FailDesktopRestore && level == 0);
        }
        public void Dispose() => Disposed = true;
        public void InvalidateCache() { }
    }
}
