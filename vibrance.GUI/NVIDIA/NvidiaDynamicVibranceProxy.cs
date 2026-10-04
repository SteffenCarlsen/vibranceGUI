using System;
using System.Collections.Generic;
using vibrance.GUI.common;

namespace vibrance.GUI.NVIDIA
{
    internal sealed class NvidiaDynamicVibranceProxy : DisplayVibranceController
    {
        public const int NvapiMaxPhysicalGpus = 64;
        public const int NvapiMaxLevel = 63;
        public const int NvapiDefaultLevel = 0;

        public NvidiaDynamicVibranceProxy(List<ApplicationSetting> applications,
            Dictionary<string, Tuple<ResolutionModeWrapper, List<ResolutionModeWrapper>>> resolutions)
            : base(new NvidiaDisplayBackend(), GraphicsAdapter.Nvidia, NvapiDefaultLevel, applications, resolutions) { }
    }
}
