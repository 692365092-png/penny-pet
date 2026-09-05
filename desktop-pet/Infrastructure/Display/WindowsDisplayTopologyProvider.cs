using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace PennyPet
{
    // Captures one immutable DisplayTopologySnapshot from the live Windows
    // virtual desktop. QueryDisplayConfig supplies the durable target
    // identities; EnumDisplayMonitors supplies physical bounds / work areas.
    // This class never moves windows and never writes persistence.
    internal sealed class WindowsDisplayTopologyProvider
    {
        private const int CaptureRetryLimit = 8;

        internal string LastCaptureError { get; private set; }

        internal DisplayTopologySnapshot Capture()
        {
            LastCaptureError = null;
            Dictionary<string, List<DisplayTargetIdentity>> targetsByGdi =
                new Dictionary<string, List<DisplayTargetIdentity>>(
                    StringComparer.OrdinalIgnoreCase);
            Dictionary<string, int> rotationByGdi =
                new Dictionary<string, int>(
                    StringComparer.OrdinalIgnoreCase);
            bool configPathsCaptured = TryCaptureConfigPaths(
                targetsByGdi, rotationByGdi);

            List<NativeDisplayMonitor> monitors = EnumerateMonitors();
            if (monitors.Count == 0)
            {
                LastCaptureError = "no display monitors";
                return null;
            }

            monitors.Sort(delegate(NativeDisplayMonitor left,
                NativeDisplayMonitor right)
            {
                return String.CompareOrdinal(left.Info.DeviceName,
                    right.Info.DeviceName);
            });

            List<DisplaySurfaceSnapshot> surfaces =
                new List<DisplaySurfaceSnapshot>(monitors.Count);
            int surfaceIndex = 0;
            foreach (NativeDisplayMonitor monitor in monitors)
            {
                surfaceIndex++;
                NativeDisplayMonitorInfo info = monitor.Info;
                PhysicalRect bounds = new PhysicalRect(info.Monitor.Left,
                    info.Monitor.Top,
                    info.Monitor.Right - info.Monitor.Left,
                    info.Monitor.Bottom - info.Monitor.Top);
                PhysicalRect workArea = new PhysicalRect(info.Work.Left,
                    info.Work.Top,
                    info.Work.Right - info.Work.Left,
                    info.Work.Bottom - info.Work.Top);
                bool primary = (info.Flags &
                    NativeDisplayConfig.MONITORINFOF_PRIMARY) != 0;

                List<DisplayTargetIdentity> targets;
                if (!configPathsCaptured ||
                    !targetsByGdi.TryGetValue(info.DeviceName,
                        out targets))
                {
                    targets = new List<DisplayTargetIdentity>
                    {
                        new DisplayTargetIdentity(
                            "ephemeral:enum:" +
                            (info.DeviceName ?? String.Empty).Trim(),
                            false, String.Empty, String.Empty, 0, 0, 0)
                    };
                }

                int rotation = 0;
                rotationByGdi.TryGetValue(info.DeviceName,
                    out rotation);
                double scale = monitor.Dpi > 0 ? monitor.Dpi / 96.0 : 1.0;

                surfaces.Add(new DisplaySurfaceSnapshot(
                    "surface-" + surfaceIndex,
                    info.DeviceName, bounds, workArea, primary,
                    RotationDegrees(rotation), targets, scale));
            }

            // Generation belongs to the topology runtime (DRT-3), which
            // decides when a semantic change has really occurred.
            return new DisplayTopologySnapshot(0, surfaces);
        }

        private static bool TryCaptureConfigPaths(
            Dictionary<string, List<DisplayTargetIdentity>> targetsByGdi,
            Dictionary<string, int> rotationByGdi)
        {
            uint flags = NativeDisplayConfig.QDC_ONLY_ACTIVE_PATHS |
                NativeDisplayConfig.QDC_VIRTUAL_MODE_AWARE;
            uint pathCount;
            uint modeCount;
            for (int attempt = 0; attempt < CaptureRetryLimit; attempt++)
            {
                int result = NativeDisplayConfig.GetDisplayConfigBufferSizes(
                    flags, out pathCount, out modeCount);
                if (result != NativeDisplayConfig.ERROR_SUCCESS)
                    return false;
                DisplayConfigPathInfo[] paths =
                    new DisplayConfigPathInfo[Math.Max(1, (int)pathCount)];
                DisplayConfigModeInfo[] modes =
                    new DisplayConfigModeInfo[Math.Max(1, (int)modeCount)];
                uint availablePaths = pathCount;
                uint availableModes = modeCount;
                result = NativeDisplayConfig.QueryDisplayConfig(flags,
                    ref availablePaths, paths, ref availableModes, modes,
                    IntPtr.Zero);
                if (result == NativeDisplayConfig.ERROR_SUCCESS)
                    return BuildTargetGroups(paths, availablePaths,
                        targetsByGdi, rotationByGdi);
                if (result != NativeDisplayConfig.ERROR_INSUFFICIENT_BUFFER)
                    return false;
            }
            return false;
        }

        private static bool BuildTargetGroups(DisplayConfigPathInfo[] paths,
            uint availablePaths,
            Dictionary<string, List<DisplayTargetIdentity>> targetsByGdi,
            Dictionary<string, int> rotationByGdi)
        {
            int captured = 0;
            int count = (int)Math.Min(availablePaths,
                (uint)(paths == null ? 0 : paths.Length));
            for (int index = 0; index < count; index++)
            {
                DisplayConfigPathInfo path = paths[index];
                if ((path.Flags & NativeDisplayConfig.DISPLAYCONFIG_PATH_ACTIVE)
                    == 0) continue;
                string gdiName = TryGetSourceName(path.SourceInfo);
                if (String.IsNullOrWhiteSpace(gdiName)) continue;

                DisplayTargetIdentity target = TryGetTargetIdentity(
                    path.TargetInfo);
                List<DisplayTargetIdentity> group;
                if (!targetsByGdi.TryGetValue(gdiName, out group))
                {
                    group = new List<DisplayTargetIdentity>();
                    targetsByGdi[gdiName] = group;
                }
                group.Add(target);
                rotationByGdi[gdiName] = path.TargetInfo.Rotation;
                captured++;
            }
            return captured > 0;
        }

        private static string TryGetSourceName(
            DisplayConfigPathSourceInfo source)
        {
            DisplayConfigSourceDeviceName name =
                new DisplayConfigSourceDeviceName();
            name.Header.Type =
                NativeDisplayConfig.DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME;
            name.Header.Size = Marshal.SizeOf(
                typeof(DisplayConfigSourceDeviceName));
            name.Header.AdapterId = source.AdapterId;
            name.Header.Id = source.Id;
            if (NativeDisplayConfig.DisplayConfigGetSourceDeviceName(
                ref name) != NativeDisplayConfig.ERROR_SUCCESS)
                return null;
            return name.ViewGdiDeviceName;
        }

        private static DisplayTargetIdentity TryGetTargetIdentity(
            DisplayConfigPathTargetInfo target)
        {
            DisplayConfigTargetDeviceName name =
                new DisplayConfigTargetDeviceName();
            name.Header.Type =
                NativeDisplayConfig.DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME;
            name.Header.Size = Marshal.SizeOf(
                typeof(DisplayConfigTargetDeviceName));
            name.Header.AdapterId = target.AdapterId;
            name.Header.Id = target.Id;
            if (NativeDisplayConfig.DisplayConfigGetTargetDeviceName(
                ref name) == NativeDisplayConfig.ERROR_SUCCESS &&
                !String.IsNullOrWhiteSpace(name.MonitorDevicePath))
            {
                return new DisplayTargetIdentity(
                    "mdp:" + name.MonitorDevicePath.Trim().ToUpperInvariant(),
                    true,
                    name.MonitorDevicePath.Trim(),
                    name.MonitorFriendlyDeviceName,
                    unchecked((ushort)name.EdidManufactureId),
                    unchecked((ushort)name.EdidProductCodeId),
                    unchecked((uint)(ushort)name.ConnectorInstance));
            }

            // Durable identity unavailable: runtime-only ephemeral key.
            // Ephemeral keys must never overwrite a durable preferred key.
            string luid = target.AdapterId.LowPart.ToString("x") + "-" +
                target.AdapterId.HighPart.ToString("x");
            return new DisplayTargetIdentity(
                "ephemeral:" + luid + ":" + target.Id,
                false, String.Empty, String.Empty, 0, 0, 0);
        }

        private sealed class NativeDisplayMonitor
        {
            internal NativeDisplayMonitorInfo Info;
            internal int Dpi;
        }

        private static List<NativeDisplayMonitor> EnumerateMonitors()
        {
            List<NativeDisplayMonitor> monitors =
                new List<NativeDisplayMonitor>();
            DisplayMonitorEnumProc callback =
                delegate(IntPtr hMonitor, IntPtr hdcMonitor,
                    ref NativeDisplayRect clip, IntPtr data)
                {
                    NativeDisplayMonitorInfo info =
                        new NativeDisplayMonitorInfo();
                    info.Size = Marshal.SizeOf(
                        typeof(NativeDisplayMonitorInfo));
                    if (NativeDisplayConfig.GetMonitorInfo(hMonitor,
                        ref info))
                    {
                        int dpiX;
                        int dpiY;
                        if (NativeDisplayConfig.GetDpiForMonitor(hMonitor,
                            NativeDisplayConfig.MDT_EFFECTIVE_DPI,
                            out dpiX, out dpiY) != 0) dpiX = 96;
                        monitors.Add(new NativeDisplayMonitor
                        {
                            Info = info,
                            Dpi = dpiX
                        });
                    }
                    return true;
                };
            NativeDisplayConfig.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero,
                callback, IntPtr.Zero);
            return monitors;
        }

        private static int RotationDegrees(int rotation)
        {
            switch (rotation)
            {
                case 1: return 0;
                case 2: return 90;
                case 3: return 180;
                case 4: return 270;
                default: return 0;
            }
        }
    }
}
