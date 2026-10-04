using System;
using Microsoft.Win32;

namespace vibrance.GUI.common
{
    internal sealed class RegistryController : IRegistryController
    {
        private const string RunKey = "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run";

        public bool RegisterProgram(string appName, string pathToExe)
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(RunKey, true);
                if (key == null) return false;
                key.SetValue(appName, pathToExe, RegistryValueKind.String);
                return true;
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException || ex is System.Security.SecurityException || ex is System.IO.IOException) { return false; }
        }

        public bool UnregisterProgram(string appName)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKey, true);
                key?.DeleteValue(appName, false);
                return true;
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException || ex is System.Security.SecurityException || ex is System.IO.IOException) { return false; }
        }

        public bool IsProgramRegistered(string appName)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKey);
                return key?.GetValue(appName) != null;
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException || ex is System.Security.SecurityException || ex is System.IO.IOException) { return false; }
        }

        public bool IsStartupPathUnchanged(string appName, string pathToExe)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKey);
                return string.Equals(key?.GetValue(appName) as string, pathToExe, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException || ex is System.Security.SecurityException || ex is System.IO.IOException) { return false; }
        }
    }
}
