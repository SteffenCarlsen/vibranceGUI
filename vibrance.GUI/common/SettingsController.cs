using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Xml;
using System.Xml.Serialization;
using vibrance.GUI.NVIDIA;

namespace vibrance.GUI.common
{
    internal sealed class SettingsController : ISettingsController
    {
        [DllImport("kernel32.dll", EntryPoint = "GetPrivateProfileStringW", CharSet = CharSet.Unicode)]
        private static extern uint ReadIni(string section, string key, string defaultValue,
            [Out] char[] value, uint size, string fileName);

        [DllImport("kernel32.dll", EntryPoint = "WritePrivateProfileStringW", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool WriteIni(string section, string key, string value, string fileName);

        private static readonly object SaveLock = new object();
        private const string Section = "Settings";
        private readonly string _directory;
        private readonly string _fileName;
        private readonly string _applicationFile;
        public string LastError { get; private set; }

        public SettingsController() : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "vibranceGUI")) { }

        internal SettingsController(string directory)
        {
            _directory = Path.GetFullPath(directory);
            _fileName = Path.Combine(_directory, "vibranceGUI.ini");
            _applicationFile = Path.Combine(_directory, "applicationData.xml");
        }

        public bool SetVibranceSettings(string windowsLevel, string affectPrimaryMonitorOnly,
            string neverSwitchResolution, List<ApplicationSetting> applicationSettings)
        {
            lock (SaveLock)
            {
                string temporaryFile = _applicationFile + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    LastError = null;
                    Directory.CreateDirectory(_directory);
                    // Serialize completely before replacing the last readable profile file.
                    using (var writer = XmlWriter.Create(temporaryFile, new XmlWriterSettings { Indent = true }))
                        new XmlSerializer(typeof(List<ApplicationSetting>)).Serialize(writer,
                            applicationSettings ?? new List<ApplicationSetting>());
                    if (File.Exists(_applicationFile))
                        File.Replace(temporaryFile, _applicationFile, _applicationFile + ".bak");
                    else
                        File.Move(temporaryFile, _applicationFile);
                    bool saved = WriteIni(Section, "inactiveValue", windowsLevel, _fileName)
                        & WriteIni(Section, "affectPrimaryMonitorOnly", affectPrimaryMonitorOnly, _fileName)
                        & WriteIni(Section, "neverSwitchResolution", neverSwitchResolution, _fileName);
                    if (!saved) LastError = "Windows could not write the desktop settings file.";
                    return saved;
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is InvalidOperationException)
                {
                    LastError = ex.Message;
                    return false;
                }
                finally
                {
                    try { if (File.Exists(temporaryFile)) File.Delete(temporaryFile); }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
            }
        }

        public bool SetVibranceSetting(string key, string value)
        {
            lock (SaveLock)
            {
                try
                {
                    Directory.CreateDirectory(_directory);
                    return WriteIni(Section, key, value, _fileName);
                }
                catch (IOException) { return false; }
                catch (UnauthorizedAccessException) { return false; }
            }
        }

        public bool BackupUnreadableProfiles()
        {
            try
            {
                if (File.Exists(_applicationFile))
                    File.Copy(_applicationFile, _applicationFile + "." + Guid.NewGuid().ToString("N") + ".corrupt");
                return true;
            }
            catch (IOException ex) { LastError = ex.Message; return false; }
            catch (UnauthorizedAccessException ex) { LastError = ex.Message; return false; }
        }

        private string ReadValue(string key, string defaultValue)
        {
            var buffer = new char[64];
            int length = (int)ReadIni(Section, key, defaultValue, buffer, (uint)buffer.Length, _fileName);
            return new string(buffer, 0, length);
        }

        public void ReadVibranceSettings(GraphicsAdapter graphicsAdapter, out int vibranceWindowsLevel,
            out bool affectPrimaryMonitorOnly, out bool neverSwitchResolution,
            out List<ApplicationSetting> applicationSettings)
        {
            int defaultLevel = graphicsAdapter == GraphicsAdapter.Amd ? 100 : NvidiaDynamicVibranceProxy.NvapiDefaultLevel;
            int maximum = graphicsAdapter == GraphicsAdapter.Amd ? 300 : NvidiaDynamicVibranceProxy.NvapiMaxLevel;
            vibranceWindowsLevel = int.TryParse(ReadValue("inactiveValue", defaultLevel.ToString()), out int level)
                && level >= 0 && level <= maximum ? level : defaultLevel;
            affectPrimaryMonitorOnly = bool.TryParse(ReadValue("affectPrimaryMonitorOnly", "false"), out bool primary) && primary;
            neverSwitchResolution = bool.TryParse(ReadValue("neverSwitchResolution", "false"), out bool never) && never;
            applicationSettings = new List<ApplicationSetting>();
            LastError = null;
            if (!File.Exists(_applicationFile)) return;
            try
            {
                using var reader = XmlReader.Create(_applicationFile,
                    new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
                var profiles = (List<ApplicationSetting>)new XmlSerializer(typeof(List<ApplicationSetting>)).Deserialize(reader);
                applicationSettings = (profiles ?? new List<ApplicationSetting>())
                    .Where(profile => profile != null && !string.IsNullOrWhiteSpace(profile.FileName))
                    .GroupBy(profile => profile.FileName, StringComparer.OrdinalIgnoreCase)
                    .Select(group => group.First()).ToList();
                foreach (var profile in applicationSettings)
                {
                    profile.IngameLevel = Math.Clamp(profile.IngameLevel, 0, maximum);
                    if (profile.ResolutionSettings == null) profile.IsResolutionChangeNeeded = false;
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is InvalidOperationException || ex is XmlException)
            {
                LastError = "Could not read applicationData.xml: " + ex.Message;
            }
        }
    }
}
