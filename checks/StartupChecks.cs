using vibrance.GUI.common;

internal static class StartupChecks
{
    private const string NewExecutable = @"C:\New folder\æøå-日本語\vibrance.GUI.exe";
    private const string AutomaticCommand = "\"" + NewExecutable + "\" -minimized";

    public static void Run()
    {
        CheckLaunchArguments();
        CheckRegistrationAndRepair();
        CheckInvalidExistingCommands();
        Console.WriteLine("PASS: explicit adapter parsing; automatic startup remains automatic; quoted-path repair preserves valid vendor overrides.");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void CheckLaunchArguments()
    {
        Assert(StartupCommand.ParseAdapterOverride(Array.Empty<string>()) == null
            && StartupCommand.ParseAdapterOverride(new[] { "-minimized" }) == null,
            "Automatic/minimized launch was pinned to a selected vendor.");
        Assert(StartupCommand.ParseAdapterOverride(new[] { "--ADAPTER", "AMD", "-minimized" }) == GraphicsAdapter.Amd
            && StartupCommand.ParseAdapterOverride(new[] { "-minimized", "--adapter", "nvidia" }) == GraphicsAdapter.Nvidia,
            "Explicit launch vendor or case-insensitive parsing was lost.");
        // Keep the existing first-occurrence CLI behavior; do not change unrelated argument handling.
        Assert(StartupCommand.ParseAdapterOverride(new[] { "--adapter", "amd", "--adapter", "intel" }) == GraphicsAdapter.Amd,
            "The first explicit adapter no longer determines a launch.");
        foreach (string[] arguments in new[]
        {
            new[] { "--adapter" }, new[] { "--adapter", "" },
            new[] { "--adapter", "intel" }, new[] { "--adapter", "-minimized" }
        })
        {
            bool rejected = false;
            try { StartupCommand.ParseAdapterOverride(arguments); }
            catch (ArgumentException ex) { rejected = ex.Message == "--adapter requires nvidia or amd."; }
            Assert(rejected, "A missing/invalid explicit adapter changed its launch error behavior.");
        }
    }

    private static void CheckRegistrationAndRepair()
    {
        Assert(StartupCommand.Build(NewExecutable, null) == AutomaticCommand,
            "A fresh automatic Run entry added an adapter override.");
        foreach (GraphicsAdapter adapter in new[] { GraphicsAdapter.Amd, GraphicsAdapter.Nvidia })
        {
            string expected = AutomaticCommand + " --adapter " + adapter.ToString().ToLowerInvariant();
            string registered = StartupCommand.Build(NewExecutable, adapter);
            Assert(registered == expected && StartupCommand.Build(NewExecutable, null, registered) == registered,
                "Explicit registration or unchanged-command refresh lost its adapter.");
        }
        foreach (string oldCommand in new[]
        {
            @"""C:\Old folder\vibrance.GUI.exe"" -minimized --adapter amd",
            @"""C:\Old folder\vibrance.GUI.exe"" --adapter AMD -MINIMIZED",
            @"""C:\Old folder\vibrance.GUI.exe"" ""--adapter"" ""amd"" ""-minimized""",
            @"C:\Old\vibrance.GUI.exe --adapter amd",
            "  \"C:\\Old folder\\vibrance.GUI.exe\"\t--adapter\tamd  "
        })
        {
            Assert(StartupCommand.Build(NewExecutable, null, oldCommand) == AutomaticCommand + " --adapter amd",
                "Repairing a quoted/moved executable lost the existing AMD override: " + oldCommand);
            Assert(StartupCommand.Build(NewExecutable, GraphicsAdapter.Nvidia, oldCommand) == AutomaticCommand + " --adapter nvidia",
                "The current explicit NVIDIA choice did not supersede the previous AMD Run entry.");
        }
        Assert(StartupCommand.Build(NewExecutable, GraphicsAdapter.Amd,
            @"""C:\Old\vibrance.GUI.exe"" --adapter nvidia") == AutomaticCommand + " --adapter amd",
            "The current explicit AMD choice did not supersede the previous NVIDIA Run entry.");
    }

    private static void CheckInvalidExistingCommands()
    {
        foreach (string oldCommand in new[]
        {
            "", @"""C:\Tools --adapter amd\vibrance.GUI.exe"" -minimized",
            @"""C:\Old folder\vibrance.GUI.exe"" --adapter",
            @"""C:\Old folder\vibrance.GUI.exe"" --adapter intel",
            @"""C:\Old folder\vibrance.GUI.exe"" --adapter amd --diagnostics",
            @"""C:\Old folder\vibrance.GUI.exe"" --adapter amd --adapter nvidia",
            @"""C:\Old folder\vibrance.GUI.exe"" -minimized -minimized --adapter amd"
        })
            Assert(StartupCommand.Build(NewExecutable, null, oldCommand) == AutomaticCommand,
                "An invalid/unknown existing command guessed a vendor or read one from its path: " + oldCommand);
    }
}
