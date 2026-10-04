using System;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace vibrance.GUI.common
{
    internal static partial class StartupCommand
    {
        public static GraphicsAdapter? ParseAdapterOverride(string[] arguments)
        {
            int index = Array.FindIndex(arguments, argument => argument.Equals("--adapter", StringComparison.OrdinalIgnoreCase));
            if (index < 0) return null;
            if (index + 1 < arguments.Length && ParseAdapterValue(arguments[index + 1]) is GraphicsAdapter adapter)
                return adapter;
            throw new ArgumentException("--adapter requires nvidia or amd.");
        }

        public static string Build(string executablePath, GraphicsAdapter? adapterOverride, string existingCommand = null)
        {
            if (string.IsNullOrWhiteSpace(executablePath) || executablePath.Contains('"') || executablePath.Contains('\0'))
                throw new ArgumentException("The startup executable path is invalid.", nameof(executablePath));
            GraphicsAdapter? adapter = adapterOverride ?? ReadExistingOverride(existingCommand);
            string option = adapter switch
            {
                null => string.Empty,
                GraphicsAdapter.Amd => " --adapter amd",
                GraphicsAdapter.Nvidia => " --adapter nvidia",
                _ => throw new ArgumentException("--adapter requires nvidia or amd.", nameof(adapterOverride))
            };
            return "\"" + executablePath + "\" -minimized" + option;
        }

        private static GraphicsAdapter? ParseAdapterValue(string value) =>
            value.Equals("amd", StringComparison.OrdinalIgnoreCase) ? GraphicsAdapter.Amd
                : value.Equals("nvidia", StringComparison.OrdinalIgnoreCase) ? GraphicsAdapter.Nvidia : null;

        private static GraphicsAdapter? ReadExistingOverride(string command)
        {
            if (string.IsNullOrWhiteSpace(command) || command.Length > 32767 || command.Contains('\0')) return null;
            // Parse the quoted executable and arguments using Windows' rules. Only the
            // supported startup format can preserve a vendor; do not infer one from a path.
            using var buffer = CommandLineToArgvW(command.Trim(), out int count);
            if (buffer.IsInvalid || count < 1 || count > 4) return null;
            IntPtr pointers = buffer.DangerousGetHandle();
            string Argument(int index) => Marshal.PtrToStringUni(Marshal.ReadIntPtr(pointers, index * IntPtr.Size));
            if (string.IsNullOrWhiteSpace(Argument(0))) return null;
            bool minimized = false;
            GraphicsAdapter? adapter = null;
            for (int index = 1; index < count; index++)
            {
                string argument = Argument(index);
                if (argument.Equals("-minimized", StringComparison.OrdinalIgnoreCase) && !minimized)
                    minimized = true;
                else if (argument.Equals("--adapter", StringComparison.OrdinalIgnoreCase) && adapter == null
                    && index + 1 < count && ParseAdapterValue(Argument(++index)) is GraphicsAdapter choice)
                    adapter = choice;
                else
                    return null;
            }
            return adapter;
        }

        private sealed class CommandLineBuffer : SafeHandleZeroOrMinusOneIsInvalid
        {
            public CommandLineBuffer() : base(true) { }
            protected override bool ReleaseHandle() => LocalFree(handle) == IntPtr.Zero;
        }

        [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
        private static partial CommandLineBuffer CommandLineToArgvW(string commandLine, out int count);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        private static partial IntPtr LocalFree(IntPtr memory);
    }
}
