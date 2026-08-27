using System.Collections.Generic;
using System.Reflection;

namespace PennyPet
{
    [Obfuscation(Exclude = false, Feature = "-rename", ApplyToMembers = true)]
    internal sealed class PetArtManifest
    {
        public int schemaVersion { get; set; }
        public string displayName { get; set; }
        public string fallbackState { get; set; }
        public PetArtRenderSettings render { get; set; }
        public Dictionary<string, PetArtStateDefinition> states { get; set; }
    }

    [Obfuscation(Exclude = false, Feature = "-rename", ApplyToMembers = true)]
    internal sealed class PetArtRenderSettings
    {
        public string fit { get; set; }
        public double anchorX { get; set; }
        public double anchorY { get; set; }
        public double scale { get; set; }
        public int offsetX { get; set; }
        public int offsetY { get; set; }
        public int minimumFrameMs { get; set; }
        public int maximumFrameMs { get; set; }
        public bool innerOutline { get; set; }
    }

    [Obfuscation(Exclude = false, Feature = "-rename", ApplyToMembers = true)]
    internal sealed class PetArtStateDefinition
    {
        public string file { get; set; }
        public string folder { get; set; }
        public string alias { get; set; }
        public int[] durationsMs { get; set; }
        public int defaultFrameMs { get; set; }
        public double speed { get; set; }
        public double renderScale { get; set; }
        public double renderScaleY { get; set; }
        public double renderOffsetX { get; set; }
        public double renderOffsetY { get; set; }
    }
}
