using vibrance.GUI.common;

internal static class ResolutionModeChecks
{
    private const string Display = "CHECK_DISPLAY";
    private const uint FixedOutputField = 0x20000000;

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    public static void Run()
    {
        var stretch = Mode(Dmdfo.Stretch);
        var center = new ResolutionModeWrapper(Mode(Dmdfo.Center));
        Assert(!ResolutionHelper.MatchesRequestedMode(center, stretch),
            "A scaling-only Center request still matches the current Stretch mode.");
        Assert(ResolutionHelper.MatchesRequestedMode(center, Mode(Dmdfo.Center)),
            "An identical complete resolution request no longer matches.");

        var identical = new NativeBoundary(center, Mode(Dmdfo.Center));
        Assert(identical.Change(center, out bool attempted) && !attempted &&
            identical.Changes.Count == 0 && identical.Enumerations == 0 && identical.Reads == 1,
            "An identical mode unnecessarily tested or applied a display change.");

        // Explicit opposite scaling must fail readback; Windows Default can represent identity/newer scaling.
        foreach (var expected in new[]
        {
            (Requested: Dmdfo.Center, Achieved: Dmdfo.Center, Success: true),
            (Requested: Dmdfo.Center, Achieved: Dmdfo.Default, Success: true),
            (Requested: Dmdfo.Center, Achieved: Dmdfo.Stretch, Success: false),
            (Requested: Dmdfo.Default, Achieved: Dmdfo.Stretch, Success: true)
        })
        {
            var requested = new ResolutionModeWrapper(Mode(expected.Requested));
            var native = new NativeBoundary(requested, stretch, Mode(expected.Achieved));
            bool changed = native.Change(requested, out attempted);
            Assert(changed == expected.Success && attempted && native.Reads == 2 && native.Enumerations == 1,
                $"Scaling request {expected.Requested} with readback {expected.Achieved} produced the wrong result.");
            Assert(native.Changes.Select(x => x.Flags).SequenceEqual(new[]
                { ChangeDisplaySettingsFlags.CdsTest, ChangeDisplaySettingsFlags.CdsFullscreen }),
                "A scaling-only request did not test then apply the temporary mode.");
            Assert(native.Changes.All(x => (x.Mode.dmFields & FixedOutputField) != 0 &&
                x.Mode.dmDisplayFixedOutput == (uint)expected.Requested),
                "An accepted scaling request lost its field or scaling value.");
            Assert(native.Failures.Count == (expected.Success ? 0 : 1),
                "Scaling readback failure reporting differs from the actual result.");
        }

        var fallback = new NativeBoundary(center, Mode(Dmdfo.Stretch, 1920, 1080), Mode(Dmdfo.Default));
        fallback.Results.Enqueue(DispChange.DispChangeBadmode);
        fallback.Results.Enqueue(DispChange.DispChangeSuccessful);
        fallback.Results.Enqueue(DispChange.DispChangeSuccessful);
        Assert(fallback.Change(center, out attempted) && attempted && fallback.Failures.Count == 0,
            "Unsupported fixed-output fallback rejected a successful core mode with Default readback.");
        Assert(fallback.Changes.Select(x => x.Flags).SequenceEqual(new[]
            { ChangeDisplaySettingsFlags.CdsTest, ChangeDisplaySettingsFlags.CdsTest, ChangeDisplaySettingsFlags.CdsFullscreen }),
            "Unsupported scaling did not retry the mode test before applying.");
        Assert((fallback.Changes[0].Mode.dmFields & FixedOutputField) != 0 &&
            fallback.Changes[0].Mode.dmDisplayFixedOutput == (uint)Dmdfo.Center &&
            fallback.Changes.Skip(1).All(x => (x.Mode.dmFields & FixedOutputField) == 0 &&
                x.Mode.dmDisplayFixedOutput == 0),
            "Fixed-output fallback did not clear both the native field flag and its value.");
        Assert(fallback.Changes.All(x => x.Mode.dmPelsWidth == 1280 && x.Mode.dmPelsHeight == 720 &&
            x.Mode.dmBitsPerPel == 32 && x.Mode.dmDisplayFrequency == 60) &&
            center.DmDisplayFixedOutput == (uint)Dmdfo.Center,
            "Fallback lost the requested core mode or modified the saved scaling preference.");

        foreach (bool rejectScaling in new[] { false, true })
        {
            var mismatched = new NativeBoundary(center, stretch, Mode(Dmdfo.Default, 1024, 720));
            if (rejectScaling)
            {
                mismatched.Results.Enqueue(DispChange.DispChangeBadmode);
                mismatched.Results.Enqueue(DispChange.DispChangeSuccessful);
                mismatched.Results.Enqueue(DispChange.DispChangeSuccessful);
            }
            Assert(!mismatched.Change(center, out attempted) && attempted &&
                mismatched.Failures.SequenceEqual(new[] { DispChange.DispChangeFailed }),
                "Scaling normalization or fallback hid a failed core-mode readback.");
        }

        var rejected = new NativeBoundary(center, stretch);
        rejected.Results.Enqueue(DispChange.DispChangeBadmode);
        rejected.Results.Enqueue(DispChange.DispChangeBadmode);
        Assert(!rejected.Change(center, out attempted) && !attempted && rejected.Changes.Count == 2 &&
            rejected.Changes.All(x => x.Flags == ChangeDisplaySettingsFlags.CdsTest) && rejected.Reads == 1,
            "Two rejected tests incorrectly applied a mode or claimed modeset ownership.");

        Console.WriteLine("PASS: scaling-only mode changes; complete-mode skip; native test/apply/readback; Default normalization; unsupported-scaling fallback; failed core readback; rejected-test ownership.");
    }

    private static Devmode Mode(Dmdfo scaling, uint width = 1280, uint height = 720) => new()
    {
        dmFields = FixedOutputField,
        dmPelsWidth = width,
        dmPelsHeight = height,
        dmBitsPerPel = 32,
        dmDisplayFrequency = 60,
        dmDisplayFixedOutput = (uint)scaling
    };

    private sealed class NativeBoundary
    {
        private readonly Queue<Devmode> _readbacks;
        private readonly ResolutionModeWrapper _supported;
        public readonly Queue<DispChange> Results = new();
        public readonly List<(Devmode Mode, ChangeDisplaySettingsFlags Flags)> Changes = new();
        public readonly List<DispChange> Failures = new();
        public int Reads { get; private set; }
        public int Enumerations { get; private set; }

        public NativeBoundary(ResolutionModeWrapper supported, params Devmode[] readbacks)
        {
            _supported = supported;
            _readbacks = new Queue<Devmode>(readbacks);
        }

        public bool Change(ResolutionModeWrapper requested, out bool attempted) =>
            ResolutionHelper.ChangeResolutionEx(requested, Display, out attempted,
                ReadCurrent, Enumerate, ChangeNative, Failures.Add);

        private bool ReadCurrent(out Devmode mode, string device)
        {
            Assert(device == Display, "Resolution readback queried another display.");
            Reads++;
            mode = _readbacks.Dequeue();
            return true;
        }

        private List<ResolutionModeWrapper> Enumerate(string device)
        {
            Assert(device == Display, "Supported-mode enumeration queried another display.");
            Enumerations++;
            // The supported core mode can enumerate Default even for an explicit scaling intent.
            return new List<ResolutionModeWrapper>
            {
                new(Mode(Dmdfo.Default, _supported.DmPelsWidth, _supported.DmPelsHeight))
            };
        }

        private DispChange ChangeNative(string device, ref Devmode mode, IntPtr window,
            ChangeDisplaySettingsFlags flags, IntPtr parameter)
        {
            Assert(device == Display && window == IntPtr.Zero && parameter == IntPtr.Zero,
                "The native change boundary targeted another display or passed unexpected pointers.");
            Changes.Add((mode, flags));
            return Results.Count == 0 ? DispChange.DispChangeSuccessful : Results.Dequeue();
        }
    }
}
