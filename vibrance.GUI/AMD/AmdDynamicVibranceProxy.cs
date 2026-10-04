using System;
using System.Collections.Generic;
using vibrance.GUI.AMD.vendor;
using vibrance.GUI.common;

namespace vibrance.GUI.AMD
{
    public sealed class AmdDynamicVibranceProxy : DisplayVibranceController
    {
        public AmdDynamicVibranceProxy(IAmdAdapter adapter, List<ApplicationSetting> applications,
            Dictionary<string, Tuple<ResolutionModeWrapper, List<ResolutionModeWrapper>>> resolutions)
            : base(Initialize(adapter), GraphicsAdapter.Amd, 100, applications, resolutions) { }

        private static IAmdAdapter Initialize(IAmdAdapter adapter) { adapter.Init(); return adapter; }
    }
}
