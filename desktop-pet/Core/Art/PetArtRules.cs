using System;
using System.Collections.Generic;
using System.IO;

namespace PennyPet
{
    internal static class PetArtRules
    {
        internal static PetArtRenderSettings NormalizeRenderSettings(
            PetArtRenderSettings value)
        {
            PetArtRenderSettings result = value ?? new PetArtRenderSettings();
            if (String.IsNullOrWhiteSpace(result.fit)) result.fit = "contain";
            result.anchorX = Clamp(result.anchorX, 0.0, 1.0, 0.5);
            result.anchorY = Clamp(result.anchorY, 0.0, 1.0, 1.0);
            if (result.scale <= 0.0) result.scale = 1.0;
            if (result.minimumFrameMs <= 0) result.minimumFrameMs = 20;
            if (result.maximumFrameMs < result.minimumFrameMs)
                result.maximumFrameMs = Math.Max(1000,
                    result.minimumFrameMs);
            return result;
        }

        internal static string ResolveTerminalStateName(PetArtManifest manifest,
            string stateName)
        {
            if (manifest == null || manifest.states == null)
                throw new InvalidDataException("美术清单没有 states。");

            HashSet<string> resolving = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            string current = stateName;
            while (true)
            {
                if (!resolving.Add(current))
                    throw new InvalidDataException(
                        "美术状态 alias 形成循环：" + stateName);
                PetArtStateDefinition definition;
                if (!manifest.states.TryGetValue(current, out definition) ||
                    definition == null)
                {
                    current = String.IsNullOrWhiteSpace(manifest.fallbackState)
                        ? "idle" : manifest.fallbackState.Trim();
                    continue;
                }
                if (String.IsNullOrWhiteSpace(definition.alias)) return current;
                current = definition.alias.Trim();
            }
        }

        internal static int DefaultFrameDuration(
            PetArtStateDefinition definition)
        {
            return definition != null && definition.defaultFrameMs > 0
                ? definition.defaultFrameMs : 40;
        }

        internal static int NormalizeFrameDuration(int milliseconds,
            PetArtStateDefinition definition, PetArtRenderSettings render)
        {
            if (render == null)
                throw new ArgumentNullException(nameof(render));
            double speed = definition == null || definition.speed <= 0.0
                ? 1.0 : definition.speed;
            int adjusted = (int)Math.Round(milliseconds / speed);
            return Math.Max(render.minimumFrameMs,
                Math.Min(render.maximumFrameMs, adjusted));
        }

        private static double Clamp(double value, double minimum,
            double maximum, double fallback)
        {
            if (Double.IsNaN(value) || Double.IsInfinity(value)) return fallback;
            return Math.Max(minimum, Math.Min(maximum, value));
        }
    }
}
