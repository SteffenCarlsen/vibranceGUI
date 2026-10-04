using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace vibrance.GUI.common
{
    internal static class PauseHotkey
    {
        public const Keys Default = Keys.Control | Keys.Alt | Keys.V;

        public static bool IsValid(Keys keyData)
        {
            Keys key = keyData & Keys.KeyCode;
            Keys modifiers = keyData & Keys.Modifiers;
            if ((modifiers & ~(Keys.Control | Keys.Alt | Keys.Shift)) != 0
                || !Enum.IsDefined(key) || (int)key <= 0 || (int)key > 254) return false;
            if (key is Keys.ControlKey or Keys.ShiftKey or Keys.Menu or Keys.LControlKey or Keys.RControlKey
                or Keys.LShiftKey or Keys.RShiftKey or Keys.LMenu or Keys.RMenu or Keys.LWin or Keys.RWin
                or Keys.Apps or Keys.Sleep or Keys.LButton or Keys.RButton or Keys.MButton
                or Keys.XButton1 or Keys.XButton2 or Keys.Cancel or Keys.F12) return false;
            return modifiers != Keys.None || key >= Keys.F1 && key <= Keys.F24;
        }

        public static uint NativeModifiers(Keys keyData) => 0x4000u
            | (keyData.HasFlag(Keys.Alt) ? 1u : 0u)
            | (keyData.HasFlag(Keys.Control) ? 2u : 0u)
            | (keyData.HasFlag(Keys.Shift) ? 4u : 0u);

        public static string Format(Keys keyData)
        {
            var parts = new List<string>();
            if (keyData.HasFlag(Keys.Control)) parts.Add("Ctrl");
            if (keyData.HasFlag(Keys.Alt)) parts.Add("Alt");
            if (keyData.HasFlag(Keys.Shift)) parts.Add("Shift");
            Keys key = keyData & Keys.KeyCode;
            parts.Add(key switch
            {
                Keys.Return => "Enter",
                Keys.Escape => "Esc",
                >= Keys.D0 and <= Keys.D9 => ((int)key - (int)Keys.D0).ToString(),
                _ => key.ToString()
            });
            return string.Join("+", parts);
        }
    }

    // The HWNDs are borrowed. Retain every successful native registration until
    // Windows confirms release, including a failed rollback's inactive candidate.
    internal sealed partial class PauseHotkeyBinding
    {
        private sealed record Registration(IntPtr Window, int Id, Keys KeyData);
        private readonly Dictionary<int, Registration> _registrations = new();
        private readonly Func<IntPtr, int, uint, uint, int> _register;
        private readonly Func<IntPtr, int, int> _unregister;
        private Registration _active;

        public PauseHotkeyBinding(Func<IntPtr, int, uint, uint, int> register = null,
            Func<IntPtr, int, int> unregister = null)
        {
            _register = register ?? RegisterHotKey;
            _unregister = unregister ?? UnregisterHotKey;
        }

        public bool IsRegistered => _active != null;
        public Keys KeyData => _active?.KeyData ?? Keys.None;
        public void DetachWindow() => _active = null; // Keep uncertain records for cleanup; the old HWND cannot receive shortcuts.

        public bool TrySet(IntPtr window, Keys keyData, out string error)
        {
            error = null;
            if (window == IntPtr.Zero) { error = "The pause shortcut window is unavailable."; return false; }
            if (!PauseHotkey.IsValid(keyData)) { error = "Choose Ctrl, Alt or Shift with a key, or F1–F24. F12 is reserved by Windows."; return false; }
            if (_active != null && _active.Window != window) DetachWindow();
            if (!ReleaseInactive(out error)) return false;
            if (_active?.Window == window && _active.KeyData == keyData) return true;
            int candidateId = _active?.Id == 1 ? 2 : 1;
            if (_register(window, candidateId, PauseHotkey.NativeModifiers(keyData), (uint)(keyData & Keys.KeyCode)) == 0)
            {
                error = PauseHotkey.Format(keyData) + " could not be registered. Another program may be using it.\n\n"
                    + new Win32Exception(Marshal.GetLastPInvokeError()).Message;
                return false;
            }
            var candidate = new Registration(window, candidateId, keyData);
            _registrations.Add(candidateId, candidate);
            if (_active != null && !Release(_active, out error))
            {
                string releaseError = error;
                if (!Release(candidate, out string rollbackError)) releaseError += "\n" + rollbackError;
                error = releaseError;
                return false;
            }
            _active = candidate;
            return true;
        }

        public bool TryClear(out string error)
        {
            error = null;
            foreach (var registration in _registrations.Values.ToArray())
            {
                if (!Release(registration, out string failure)) error = failure;
                else if (_active == registration) _active = null;
            }
            return error == null;
        }

        public bool Matches(Message message)
        {
            if (_active == null || message.Msg != 0x0312 || message.HWnd != _active.Window
                || message.WParam.ToInt64() != _active.Id) return false;
            uint payload = unchecked((uint)message.LParam.ToInt64());
            return (payload & 0xFFFF) == (PauseHotkey.NativeModifiers(_active.KeyData) & 0xF)
                && (payload >> 16) == (uint)(_active.KeyData & Keys.KeyCode);
        }

        private bool ReleaseInactive(out string error)
        {
            error = null;
            foreach (var registration in _registrations.Values.Where(item => item != _active).ToArray())
                if (!Release(registration, out error)) return false;
            return true;
        }

        private bool Release(Registration registration, out string error)
        {
            error = null;
            if (_unregister(registration.Window, registration.Id) == 0)
            {
                int code = Marshal.GetLastPInvokeError();
                if (code != 1419) // ERROR_HOTKEY_NOT_REGISTERED also confirms it is absent.
                {
                    error = "Could not release " + PauseHotkey.Format(registration.KeyData) + ": " + new Win32Exception(code).Message;
                    return false;
                }
            }
            _registrations.Remove(registration.Id);
            return true;
        }

        [LibraryImport("user32.dll", SetLastError = true)]
        private static partial int RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
        [LibraryImport("user32.dll", SetLastError = true)]
        private static partial int UnregisterHotKey(IntPtr window, int id);
    }
}
