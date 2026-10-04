using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Forms;
using vibrance.GUI.common;

internal static class HotkeyChecks
{
    private static readonly IntPtr Window = (IntPtr)101;
    private static readonly IntPtr OtherWindow = (IntPtr)202;
    private const Keys Custom = Keys.Control | Keys.Shift | Keys.K;

    public static void Run(string tempDirectory)
    {
        CheckKeyModel();
        CheckPreferences(tempDirectory);
        CheckRegistrationAndMessages();
        CheckReplacementRollback();
        CheckFailedCleanupRetainsOwnership();
        CheckAlreadyMissingRegistration();
        CheckDetachedWindowCleanup();
        Console.WriteLine("PASS: pause shortcut validation/preferences; transactional registration; collision and rollback; stale messages; failed cleanup ownership; already-missing release.");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void CheckKeyModel()
    {
        Assert(PauseHotkey.Default == (Keys.Control | Keys.Alt | Keys.V) && PauseHotkey.IsValid(PauseHotkey.Default),
            "The default pause shortcut is not valid Ctrl+Alt+V.");
        foreach (Keys key in new[] { Custom, Keys.Alt | Keys.Return, Keys.Control | Keys.D0, Keys.F1, Keys.F11, Keys.F24 })
            Assert(PauseHotkey.IsValid(key), "A supported pause chord was rejected: " + key);
        foreach (Keys key in new[] { Keys.None, Keys.V, Keys.Control, Keys.ControlKey, Keys.Control | Keys.LWin,
            Keys.Alt | Keys.LButton, Keys.Control | Keys.Sleep, Keys.F12, Keys.Control | Keys.F12,
            (Keys)255, Keys.Control | (Keys)0x00080000 | Keys.V })
            Assert(!PauseHotkey.IsValid(key), "An invalid, reserved or modifier-only pause chord was accepted: " + key);
        Assert(PauseHotkey.NativeModifiers(PauseHotkey.Default) == 0x4003 &&
            PauseHotkey.NativeModifiers(Custom) == 0x4006 && PauseHotkey.NativeModifiers(Keys.F1) == 0x4000,
            "Pause shortcut modifiers lost MOD_NOREPEAT or mapped Ctrl/Alt/Shift incorrectly.");
        Assert(PauseHotkey.Format(PauseHotkey.Default) == "Ctrl+Alt+V" &&
            PauseHotkey.Format(Keys.Control | Keys.Shift | Keys.D0) == "Ctrl+Shift+0" &&
            PauseHotkey.Format(Keys.Alt | Keys.Return) == "Alt+Enter" &&
            PauseHotkey.Format(Keys.Control | Keys.Escape) == "Ctrl+Esc" && PauseHotkey.Format(Keys.F24) == "F24",
            "Pause shortcut labels do not represent their saved chords.");
    }

    private static void CheckPreferences(string directory)
    {
        string path = Path.Combine(directory, "hotkey-appearance.json");
        var missing = AppTheme.ReadPreferences(path);
        Assert(missing.Theme == ThemePreference.System && !missing.EnablePauseHotkey &&
            missing.PauseHotkeyKeyData == PauseHotkey.Default, "Missing preferences changed the opt-in shortcut defaults.");

        File.WriteAllText(path, "{\"Theme\":2,\"EnablePauseHotkey\":true}");
        var legacy = AppTheme.ReadPreferences(path);
        Assert(legacy.Theme == ThemePreference.Dark && legacy.EnablePauseHotkey &&
            legacy.PauseHotkeyKeyData == PauseHotkey.Default, "Legacy appearance JSON lost its theme/enabled flag or default chord.");

        File.WriteAllText(path, JsonSerializer.Serialize(new
        {
            Theme = 999,
            EnablePauseHotkey = true,
            PauseHotkeyKeyData = (int)(Keys.Control | (Keys)0x00080000 | Keys.V)
        }));
        var normalized = AppTheme.ReadPreferences(path);
        Assert(normalized.Theme == ThemePreference.System && normalized.EnablePauseHotkey &&
            normalized.PauseHotkeyKeyData == PauseHotkey.Default, "Corrupt enum integers were not normalized without dropping the enabled flag.");
        File.WriteAllText(path, "{broken JSON");
        var corrupt = AppTheme.ReadPreferences(path);
        Assert(corrupt.Theme == ThemePreference.System && !corrupt.EnablePauseHotkey &&
            corrupt.PauseHotkeyKeyData == PauseHotkey.Default, "Malformed preferences did not recover safely.");

        AppTheme.Save(ThemePreference.Dark, true, Custom, path);
        var saved = AppTheme.ReadPreferences(path);
        Assert(saved.Theme == ThemePreference.Dark && saved.EnablePauseHotkey && saved.PauseHotkeyKeyData == Custom &&
            AppTheme.Preference == saved.Theme && AppTheme.EnablePauseHotkey && AppTheme.PauseHotkeyKeyData == Custom,
            "A custom shortcut did not roundtrip to disk and current preferences.");
        string originalJson = File.ReadAllText(path);
        bool rejected = false;
        try { AppTheme.Save(ThemePreference.Light, false, Keys.F12, path); }
        catch (ArgumentException) { rejected = true; }
        Assert(rejected && File.ReadAllText(path) == originalJson && AppTheme.Preference == ThemePreference.Dark &&
            AppTheme.EnablePauseHotkey && AppTheme.PauseHotkeyKeyData == Custom,
            "Saving an invalid shortcut changed the last good preferences.");

        // This fails after writing the temporary JSON, at the final replacement step.
        // It protects the promise that failed persistence does not become current settings.
        string directoryTarget = Path.Combine(directory, "hotkey-save-target-is-directory");
        Directory.CreateDirectory(directoryTarget);
        bool failed = false;
        try { AppTheme.Save(ThemePreference.Light, false, Keys.Control | Keys.P, directoryTarget); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { failed = true; }
        Assert(failed && Directory.Exists(directoryTarget) && AppTheme.Preference == ThemePreference.Dark &&
            AppTheme.EnablePauseHotkey && AppTheme.PauseHotkeyKeyData == Custom && File.ReadAllText(path) == originalJson,
            "Failed preference replacement falsely committed new in-memory settings or damaged the last good file.");
    }

    private static void CheckRegistrationAndMessages()
    {
        var native = new FakeNative();
        var binding = new PauseHotkeyBinding(native.Register, native.Unregister);
        Assert(!binding.TrySet(IntPtr.Zero, PauseHotkey.Default, out string error) && !string.IsNullOrWhiteSpace(error) &&
            native.Calls.Count == 0 && !binding.IsRegistered, "A zero HWND reached native hotkey registration.");
        Assert(!binding.TrySet(Window, Keys.F12, out error) && native.Calls.Count == 0 && !binding.IsRegistered,
            "An invalid shortcut reached native registration.");
        Assert(binding.TrySet(Window, PauseHotkey.Default, out error) && error == null && binding.IsRegistered &&
            binding.KeyData == PauseHotkey.Default && native.Registrations.Count == 1,
            "Initial hotkey registration failed or its ownership was lost.");
        int originalId = native.Calls.Last().Id;
        Assert(binding.Matches(HotkeyMessage(Window, originalId, PauseHotkey.Default)), "The active pause message was rejected.");
        int count = native.Calls.Count;
        Assert(binding.TrySet(Window, PauseHotkey.Default, out error) && native.Calls.Count == count,
            "Reapplying an identical binding made unnecessary native calls.");

        native.ExternalChords.Add(Chord(Custom));
        Assert(!binding.TrySet(Window, Custom, out error) && !string.IsNullOrWhiteSpace(error) &&
            binding.KeyData == PauseHotkey.Default && native.Registrations.Count == 1 &&
            native.Calls.Last().Kind == "register" && binding.Matches(HotkeyMessage(Window, originalId, PauseHotkey.Default)),
            "A colliding shortcut released the working binding.");
        native.ExternalChords.Clear();
        Assert(binding.TrySet(Window, Custom, out error) && error == null, "A free replacement chord failed.");
        var replacementCalls = native.Calls.TakeLast(2).ToArray();
        int activeId = replacementCalls[0].Id;
        Assert(replacementCalls[0].Kind == "register" && replacementCalls[0].Window == Window && activeId != originalId &&
            replacementCalls[1].Kind == "unregister" && replacementCalls[1].Window == Window && replacementCalls[1].Id == originalId &&
            native.Registrations.Count == 1 && binding.KeyData == Custom,
            "Replacement did not acquire a spare ID before releasing the old HWND/ID.");

        Assert(binding.Matches(HotkeyMessage(Window, activeId, Custom)), "The replacement's active message was rejected.");
        Assert(!binding.Matches(HotkeyMessage(Window, originalId, Custom)) &&
            !binding.Matches(HotkeyMessage(OtherWindow, activeId, Custom)) &&
            !binding.Matches(HotkeyMessage(Window, activeId, Keys.Control | Keys.Alt | Keys.K)) &&
            !binding.Matches(HotkeyMessage(Window, activeId, Keys.Control | Keys.Shift | Keys.V)) &&
            !binding.Matches(Message.Create(Window, 0x0311, (IntPtr)activeId, HotkeyPayload(Custom))) &&
            !binding.Matches(Message.Create(Window, 0x0312, (IntPtr)(0x100000000L | (uint)activeId), HotkeyPayload(Custom))),
            "Stale IDs, windows, modifiers, keys or other Windows messages could toggle pause.");
        Assert(binding.TryClear(out error) && error == null && !binding.IsRegistered && binding.KeyData == Keys.None &&
            native.Registrations.Count == 0 && !binding.Matches(HotkeyMessage(Window, activeId, Custom)),
            "Disabling the shortcut retained a registration or accepted its queued message.");
        count = native.Calls.Count;
        Assert(binding.TryClear(out error) && native.Calls.Count == count, "Repeated cleanup called Windows after ownership was cleared.");
    }

    private static void CheckReplacementRollback()
    {
        var native = new FakeNative();
        var binding = new PauseHotkeyBinding(native.Register, native.Unregister);
        Assert(binding.TrySet(Window, PauseHotkey.Default, out _), "Rollback setup failed.");
        int oldId = native.Calls.Last().Id;
        native.FailUnregister(Window, oldId, 5);
        Assert(!binding.TrySet(Window, Custom, out string error) && !string.IsNullOrWhiteSpace(error),
            "Replacement claimed success despite failure to release the old binding.");
        var attempts = native.Calls.TakeLast(3).ToArray();
        int candidateId = attempts[0].Id;
        Assert(attempts[0].Kind == "register" && attempts[1].Kind == "unregister" && attempts[1].Id == oldId &&
            attempts[2].Kind == "unregister" && attempts[2].Id == candidateId && candidateId != oldId &&
            native.Registrations.Count == 1 && native.Registrations.ContainsKey((Window, oldId)) &&
            binding.KeyData == PauseHotkey.Default && binding.Matches(HotkeyMessage(Window, oldId, PauseHotkey.Default)) &&
            !binding.Matches(HotkeyMessage(Window, candidateId, Custom)),
            "A failed replacement did not roll back its candidate and preserve the old active binding.");
        Assert(binding.TryClear(out _) && native.Registrations.Count == 0, "Rollback cleanup leaked the retained old binding.");
    }

    private static void CheckFailedCleanupRetainsOwnership()
    {
        var native = new FakeNative();
        var binding = new PauseHotkeyBinding(native.Register, native.Unregister);
        Assert(binding.TrySet(Window, PauseHotkey.Default, out _), "Failed-cleanup setup failed.");
        int oldId = native.Calls.Last().Id;
        int candidateId = oldId == 1 ? 2 : 1;
        native.FailUnregister(Window, oldId, 5);
        native.FailUnregister(Window, candidateId, 5);
        Assert(!binding.TrySet(Window, Custom, out string error) && !string.IsNullOrWhiteSpace(error) &&
            native.Registrations.Count == 2 && binding.KeyData == PauseHotkey.Default &&
            binding.Matches(HotkeyMessage(Window, oldId, PauseHotkey.Default)) &&
            !binding.Matches(HotkeyMessage(Window, candidateId, Custom)),
            "Failed old release plus failed rollback forgot a native registration or activated the candidate.");

        native.FailUnregister(Window, oldId, 5);
        native.FailUnregister(Window, candidateId, 5);
        Assert(!binding.TryClear(out error) && !string.IsNullOrWhiteSpace(error) && native.Registrations.Count == 2 && binding.IsRegistered,
            "Failed cleanup falsely relinquished native ownership.");
        int registrationsBefore = native.Calls.Count(call => call.Kind == "register");
        native.FailUnregister(Window, candidateId, 5);
        Assert(!binding.TrySet(Window, Keys.Control | Keys.Alt | Keys.M, out error) &&
            native.Calls.Count(call => call.Kind == "register") == registrationsBefore && native.Registrations.Count == 2,
            "A new binding reused the spare ID before the failed inactive registration was released.");
        Assert(binding.TryClear(out error) && error == null && native.Registrations.Count == 0 && !binding.IsRegistered,
            "Recovered cleanup did not release all retained registrations.");
        Assert(binding.TrySet(Window, Keys.Control | Keys.Alt | Keys.M, out error) && native.Registrations.Count == 1 &&
            binding.KeyData == (Keys.Control | Keys.Alt | Keys.M), "A successfully cleaned ID could not be reused.");
        Assert(binding.TryClear(out _), "Final cleanup failed.");
    }

    private static void CheckAlreadyMissingRegistration()
    {
        var native = new FakeNative();
        var binding = new PauseHotkeyBinding(native.Register, native.Unregister);
        Assert(binding.TrySet(Window, PauseHotkey.Default, out _), "Already-missing setup failed.");
        int id = native.Calls.Last().Id;
        native.Registrations.Remove((Window, id)); // Windows already removed it, e.g. with a destroyed HWND.
        Assert(binding.TryClear(out string error) && error == null && !binding.IsRegistered &&
            native.Calls.Last().Kind == "unregister" && Marshal.GetLastPInvokeError() == 1419,
            "ERROR_HOTKEY_NOT_REGISTERED did not confirm that native ownership was gone.");
        Assert(binding.TrySet(Window, PauseHotkey.Default, out _) && binding.IsRegistered,
            "Confirmed absence left the ID blocked.");
        id = native.Calls.Last().Id;
        native.Registrations.Remove((Window, id));
        Assert(binding.TrySet(Window, Custom, out error) && error == null && binding.KeyData == Custom && native.Registrations.Count == 1,
            "Replacing an already-missing old registration failed instead of accepting its absence.");
        Assert(binding.TryClear(out _), "Already-missing final cleanup failed.");
    }

    private static void CheckDetachedWindowCleanup()
    {
        var native = new FakeNative();
        var binding = new PauseHotkeyBinding(native.Register, native.Unregister);
        Assert(binding.TrySet(Window, PauseHotkey.Default, out _), "Window-detach setup failed.");
        int oldId = native.Calls.Last().Id;
        native.FailUnregister(Window, oldId, 5);
        Assert(!binding.TryClear(out _), "Window-detach setup did not retain the failed registration.");
        binding.DetachWindow();
        Assert(!binding.IsRegistered && binding.KeyData == Keys.None && native.Registrations.Count == 1 &&
            !binding.Matches(HotkeyMessage(Window, oldId, PauseHotkey.Default)),
            "A destroyed HWND remained active or its uncertain native ownership was discarded.");
        int registrationsBefore = native.Calls.Count(call => call.Kind == "register");
        native.FailUnregister(Window, oldId, 5);
        Assert(!binding.TrySet(OtherWindow, PauseHotkey.Default, out string error) && !string.IsNullOrWhiteSpace(error) &&
            !binding.IsRegistered && native.Registrations.Count == 1 &&
            native.Calls.Count(call => call.Kind == "register") == registrationsBefore,
            "New-HWND rebinding ignored unresolved old ownership or falsely restored active dispatch.");
        native.Registrations.Remove((Window, oldId)); // Destroyed HWND removed its hotkey; cleanup now returns 1419.
        Assert(binding.TrySet(OtherWindow, PauseHotkey.Default, out error) && error == null && binding.IsRegistered &&
            native.Registrations.Count == 1 && binding.Matches(HotkeyMessage(OtherWindow, native.Calls.Last().Id, PauseHotkey.Default)) &&
            !binding.Matches(HotkeyMessage(Window, oldId, PauseHotkey.Default)),
            "ERROR_HOTKEY_NOT_REGISTERED did not allow safe rebinding to the new HWND.");
        Assert(binding.TryClear(out _), "Detached-window cleanup failed.");

        // TrySet must retire an old HWND even when OnHandleDestroyed has not called DetachWindow yet.
        Assert(binding.TrySet(Window, PauseHotkey.Default, out _), "Implicit-detach setup failed.");
        oldId = native.Calls.Last().Id;
        native.FailUnregister(Window, oldId, 5);
        Assert(!binding.TrySet(OtherWindow, PauseHotkey.Default, out error) && !binding.IsRegistered &&
            !binding.Matches(HotkeyMessage(Window, oldId, PauseHotkey.Default)) && native.Registrations.Count == 1,
            "A changed HWND kept the stale active binding after cleanup failed.");
        Assert(binding.TrySet(OtherWindow, PauseHotkey.Default, out error) && error == null && binding.IsRegistered &&
            native.Registrations.Count == 1, "A recovered old-HWND cleanup did not permit rebinding.");
        Assert(binding.TryClear(out _), "Implicit-detach final cleanup failed.");
    }

    private static (uint Modifiers, uint Key) Chord(Keys keys) =>
        (PauseHotkey.NativeModifiers(keys) & 0xF, (uint)(keys & Keys.KeyCode));
    private static IntPtr HotkeyPayload(Keys keys)
    {
        var chord = Chord(keys);
        return (IntPtr)(long)(chord.Modifiers | chord.Key << 16);
    }
    private static Message HotkeyMessage(IntPtr window, int id, Keys keys) =>
        Message.Create(window, 0x0312, (IntPtr)id, HotkeyPayload(keys));

    private sealed record NativeCall(string Kind, IntPtr Window, int Id, uint Modifiers = 0, uint Key = 0);
    private sealed class FakeNative
    {
        public Dictionary<(IntPtr Window, int Id), (uint Modifiers, uint Key)> Registrations { get; } = new();
        public HashSet<(uint Modifiers, uint Key)> ExternalChords { get; } = new();
        public List<NativeCall> Calls { get; } = new();
        private readonly Dictionary<(IntPtr Window, int Id), Queue<int>> _unregisterFailures = new();

        public void FailUnregister(IntPtr window, int id, int code)
        {
            if (!_unregisterFailures.TryGetValue((window, id), out var failures))
                _unregisterFailures[(window, id)] = failures = new Queue<int>();
            failures.Enqueue(code);
        }

        public int Register(IntPtr window, int id, uint modifiers, uint key)
        {
            Calls.Add(new NativeCall("register", window, id, modifiers, key));
            var chord = (modifiers & 0xF, key);
            if (Registrations.ContainsKey((window, id)) || ExternalChords.Contains(chord) || Registrations.Values.Contains(chord))
                return Fail(1409);
            Registrations.Add((window, id), chord);
            Marshal.SetLastPInvokeError(0);
            return 1;
        }

        public int Unregister(IntPtr window, int id)
        {
            Calls.Add(new NativeCall("unregister", window, id));
            if (_unregisterFailures.TryGetValue((window, id), out var failures) && failures.Count > 0)
                return Fail(failures.Dequeue());
            if (!Registrations.Remove((window, id))) return Fail(1419);
            Marshal.SetLastPInvokeError(0);
            return 1;
        }

        private static int Fail(int code) { Marshal.SetLastPInvokeError(code); return 0; }
    }
}
