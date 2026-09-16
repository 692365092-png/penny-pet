using System;
using System.Collections.Generic;

namespace PennyPet.Tests
{
    internal sealed class SyntheticDisplayScenario
    {
        private readonly Dictionary<string, int> _dpiBySurfaceId;

        internal SyntheticDisplayScenario(
            DisplayTopologySnapshot topology,
            Dictionary<string, int> dpiBySurfaceId)
        {
            Topology = topology ??
                throw new ArgumentNullException(nameof(topology));
            _dpiBySurfaceId = dpiBySurfaceId ??
                throw new ArgumentNullException(nameof(dpiBySurfaceId));
        }

        internal DisplayTopologySnapshot Topology { get; private set; }

        internal int DpiFor(DisplaySurfaceSnapshot surface)
        {
            if (surface == null)
                throw new ArgumentNullException(nameof(surface));
            int dpi;
            if (!_dpiBySurfaceId.TryGetValue(
                surface.RuntimeSurfaceId, out dpi))
                throw new InvalidOperationException(
                    "Synthetic surface DPI is missing: " +
                    surface.RuntimeSurfaceId);
            return dpi;
        }
    }

    internal static class DynamicNDisplayScenarioFactory
    {
        private static readonly int[] Widths =
        {
            1024, 1280, 1366, 1600, 1920, 2560, 3440, 3840
        };

        private static readonly int[] Heights =
        {
            720, 768, 900, 1080, 1200, 1440, 1600, 2160
        };

        private static readonly int[] Dpis =
        {
            96, 120, 144, 168, 192, 216
        };

        internal static SyntheticDisplayScenario Create(
            int seed, long generation)
        {
            Random random = new Random(seed);
            int count = random.Next(1, 17);
            int primaryIndex = random.Next(count);

            int[] widths = new int[count];
            int[] heights = new int[count];
            int[] dpis = new int[count];
            int[] lefts = new int[count];
            int[] tops = new int[count];

            for (int index = 0; index < count; index++)
            {
                widths[index] = Widths[random.Next(Widths.Length)];
                heights[index] = Heights[random.Next(Heights.Length)];
                dpis[index] = Dpis[random.Next(Dpis.Length)];
                tops[index] = random.Next(-1200, 1201);
            }

            lefts[primaryIndex] = 0;
            for (int index = primaryIndex - 1; index >= 0; index--)
                lefts[index] = lefts[index + 1] - widths[index];
            for (int index = primaryIndex + 1; index < count; index++)
                lefts[index] = lefts[index - 1] + widths[index - 1];

            List<DisplaySurfaceSnapshot> surfaces =
                new List<DisplaySurfaceSnapshot>(count);
            Dictionary<string, int> dpiBySurfaceId =
                new Dictionary<string, int>(
                    StringComparer.OrdinalIgnoreCase);

            for (int index = 0; index < count; index++)
            {
                int insetLeft = random.Next(0, 33);
                int insetRight = random.Next(0, 33);
                int insetTop = random.Next(0, 65);
                int insetBottom = random.Next(0, 97);
                int workWidth = Math.Max(200,
                    widths[index] - insetLeft - insetRight);
                int workHeight = Math.Max(200,
                    heights[index] - insetTop - insetBottom);

                string surfaceId =
                    "synthetic-surface-" + seed + "-" + index;
                string gdi = "\\\\.\\DISPLAY" + (index + 1);
                string durableKey =
                    "mdp:synthetic:" + seed + ":" + index;

                List<DisplayTargetIdentity> targets =
                    new List<DisplayTargetIdentity>();
                targets.Add(new DisplayTargetIdentity(durableKey, true,
                    "path:" + durableKey, "Synthetic " + index, 0, 0,
                    (uint)index));

                if (random.Next(0, 5) == 0)
                {
                    string aliasKey = durableKey + ":alias";
                    targets.Add(new DisplayTargetIdentity(aliasKey, true,
                        "path:" + aliasKey, "Synthetic alias " + index,
                        0, 0, (uint)(1000 + index)));
                }

                DisplaySurfaceSnapshot surface =
                    new DisplaySurfaceSnapshot(
                        surfaceId, gdi,
                        new PhysicalRect(lefts[index], tops[index],
                            widths[index], heights[index]),
                        new PhysicalRect(lefts[index] + insetLeft,
                            tops[index] + insetTop,
                            workWidth, workHeight),
                        index == primaryIndex,
                        Rotation(random),
                        targets,
                        dpis[index] / 96.0);

                surfaces.Add(surface);
                dpiBySurfaceId[surfaceId] = dpis[index];
            }

            Shuffle(surfaces, random);

            return new SyntheticDisplayScenario(
                new DisplayTopologySnapshot(generation, surfaces),
                dpiBySurfaceId);
        }

        internal static DisplayTopologySnapshot ReorderOnly(
            SyntheticDisplayScenario scenario, int seed,
            long generation)
        {
            if (scenario == null)
                throw new ArgumentNullException(nameof(scenario));
            List<DisplaySurfaceSnapshot> surfaces =
                new List<DisplaySurfaceSnapshot>(
                    scenario.Topology.Surfaces);
            Shuffle(surfaces, new Random(seed));
            return new DisplayTopologySnapshot(generation, surfaces);
        }

        internal static DisplayTopologySnapshot RemoveSurface(
            SyntheticDisplayScenario scenario,
            DisplaySurfaceSnapshot removed, long generation)
        {
            if (scenario == null)
                throw new ArgumentNullException(nameof(scenario));
            if (removed == null)
                throw new ArgumentNullException(nameof(removed));

            List<DisplaySurfaceSnapshot> remaining =
                new List<DisplaySurfaceSnapshot>();
            foreach (DisplaySurfaceSnapshot surface
                in scenario.Topology.Surfaces)
            {
                if (!String.Equals(surface.RuntimeSurfaceId,
                    removed.RuntimeSurfaceId,
                    StringComparison.OrdinalIgnoreCase))
                    remaining.Add(surface);
            }

            if (remaining.Count == 0)
                throw new InvalidOperationException(
                    "Synthetic removal needs at least one remaining surface.");

            bool hasPrimary = false;
            foreach (DisplaySurfaceSnapshot surface in remaining)
                if (surface.IsPrimary) { hasPrimary = true; break; }
            if (!hasPrimary)
                remaining[0] = CloneWithPrimary(remaining[0], true);

            return new DisplayTopologySnapshot(generation, remaining);
        }

        internal static LogicalRect RandomLocalLogicalRect(Random random)
        {
            return new LogicalRect
            {
                X = random.Next(-5000, 5001),
                Y = random.Next(-5000, 5001),
                Width = random.Next(160, 801),
                Height = random.Next(120, 901)
            };
        }

        internal static int NextDpi(Random random)
        {
            return Dpis[random.Next(Dpis.Length)];
        }

        private static int Rotation(Random random)
        {
            switch (random.Next(4))
            {
                case 1: return 90;
                case 2: return 180;
                case 3: return 270;
                default: return 0;
            }
        }

        private static DisplaySurfaceSnapshot CloneWithPrimary(
            DisplaySurfaceSnapshot source, bool primary)
        {
            return new DisplaySurfaceSnapshot(
                source.RuntimeSurfaceId, source.RuntimeGdiName,
                source.Bounds, source.WorkArea, primary,
                source.RotationDegrees, source.Targets, source.Scale);
        }

        private static void Shuffle<T>(IList<T> items, Random random)
        {
            for (int index = items.Count - 1; index > 0; index--)
            {
                int other = random.Next(index + 1);
                T value = items[index];
                items[index] = items[other];
                items[other] = value;
            }
        }
    }
}
