using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static PennyPet.Tests.SourceGuardText;

namespace PennyPet.Tests
{
    [TestCategory("ArchitectureSourceBoundary")]
    [TestClass]
    public sealed partial class InputAnimationBoundaryTests
    {
        [TestMethod]
        public void R20_StartupShowsShellBeforeStickyRuntimeComposition()
        {
            string host = ReadSource("PennyApplicationHost.cs");
            string form = ReadSource("PetForm.cs");
            string composition = ReadSource("PetRuntimeComposition.cs");
            string startup = ReadSource("PetStartupCoordinator.cs");
            string feature = ReadSource(
                "Features/StickyNotes/StickyFeature.cs");

            Assert.IsFalse(host.Contains("StartupLoadingThreadHost") ||
                host.Contains("loading.Start(") ||
                host.Contains("loading.BringToFront(") ||
                host.Contains("WaitOne("),
                "The retired loading STA, loading visual/resource and its validation contract must not remain in the product pipeline.");
            Assert.IsTrue(host.Contains("ShellReady") &&
                host.Contains("BeginRuntimeComposition") &&
                host.Contains("StickyFeature.PrepareLoad()") &&
                host.Contains("pet.AttachPreparedStickyRuntime(prepared)"),
                "PennyApplicationHost must own background runtime preparation after the shell is ready.");

            Assert.IsFalse(form.Contains("StickyFeature.Load(") ||
                form.Contains("StickyStore.Load(") ||
                form.Contains("GetForecastAsync("),
                "PetForm construction must not synchronously own Sticky or weather I/O.");

            Assert.IsTrue(feature.Contains("PrepareLoad()") &&
                feature.Contains("PublishPreparedLoad(") &&
                composition.Contains(
                    "StickyFeature.PublishPreparedLoad(prepared)") &&
                composition.Contains(
                    "SynchronizationContext.Current"),
                "Sticky I/O preparation must be detached while publication captures the Pet STA owner context.");

            Assert.IsTrue(startup.Contains(
                    "StartupWorkPhase.WaitForStickyRuntime") &&
                startup.Contains("_notes == null") &&
                startup.Contains("StartupBackgroundReady") &&
                startup.Contains("ShellReady"),
                "Shell readiness and background Sticky restore completion must be independent event boundaries.");
            Assert.IsFalse(startup.Contains(
                    "!_startupUiReady || !_startupArtReady"),
                "Sticky first-render completion must not gate the interactive Pet shell.");
            Assert.IsTrue(composition.Contains(
                    "if (IsExitingForComposition) return;") &&
                host.Contains(
                    "pet.IsDisposed || pet.Disposing"),
                "Late background completion must not resurrect windows after shutdown.");
        }
    }
}
