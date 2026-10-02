using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PennyPet.Tests
{
    public sealed partial class CoreBehaviorTests
    {

        [TestMethod]
        public void PetArtRules_ResolveAliasesAndNormalizeTimingWithoutBitmaps()
        {
            PetArtManifest manifest = new PetArtManifest
            {
                fallbackState = "idle",
                states = new Dictionary<string, PetArtStateDefinition>(
                    StringComparer.OrdinalIgnoreCase)
                {
                    { "idle", new PetArtStateDefinition() },
                    { "waiting", new PetArtStateDefinition { alias = "idle" } }
                }
            };
            Assert.AreEqual("idle",
                PetArtRules.ResolveTerminalStateName(manifest, "waiting"));
            Assert.AreEqual("idle",
                PetArtRules.ResolveTerminalStateName(manifest, "missing"));

            PetArtRenderSettings render =
                PetArtRules.NormalizeRenderSettings(new PetArtRenderSettings
                {
                    anchorX = Double.NaN,
                    anchorY = 2.0,
                    minimumFrameMs = 25,
                    maximumFrameMs = 100
                });
            Assert.AreEqual("contain", render.fit);
            Assert.AreEqual(0.5, render.anchorX);
            Assert.AreEqual(1.0, render.anchorY);
            Assert.AreEqual(50, PetArtRules.NormalizeFrameDuration(100,
                new PetArtStateDefinition { speed = 2.0 }, render));
            Assert.AreEqual(25, PetArtRules.NormalizeFrameDuration(5,
                null, render));
        }
    }
}
