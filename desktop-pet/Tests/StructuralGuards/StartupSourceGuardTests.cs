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
            string artResources = ReadSource("PennyPet.ArtResources.targets");
            string selfTests = ReadSource("SelfTestRunner.cs");
            string releaseSmoke = ReadSource("test-release.ps1");

            Assert.IsFalse(host.Contains("StartupLoadingThreadHost") ||
                host.Contains("loading.Start(") ||
                host.Contains("loading.BringToFront(") ||
                host.Contains("WaitOne(") ||
                artResources.Contains("PennyPet.Startup.Loading") ||
                selfTests.Contains("StartupLoading") ||
                releaseSmoke.Contains("PennyPet.Startup.Loading"),
                "The retired loading STA, loading visual/resource and its validation contract must not remain in the product pipeline.");
            Assert.IsTrue(host.Contains("pet.ShellReady += delegate") &&
                host.Contains("Task.Run(delegate") &&
                host.Contains("StickyFeature.PrepareLoad()") &&
                host.Contains("pet.AttachPreparedStickyRuntime(prepared)"),
                "PennyApplicationHost must own background runtime preparation after the shell is ready.");

            int constructor = form.IndexOf(
                "internal PetForm(PetSettings preloadedSettings)",
                StringComparison.Ordinal);
            int createParams = form.IndexOf(
                "protected override CreateParams CreateParams",
                constructor, StringComparison.Ordinal);
            string constructorBody = form.Substring(
                constructor, createParams - constructor);
            Assert.IsFalse(constructorBody.Contains("StickyFeature.Load(") ||
                constructorBody.Contains("StickyStore.Load(") ||
                constructorBody.Contains("GetForecastAsync("),
                "PetForm construction must not scan Sticky data or wait for weather.");
            Assert.IsTrue(constructorBody.Contains(
                    "_art.PreloadRow(IdleRow)") &&
                !constructorBody.Contains("PreloadRow(HoverRow)") &&
                !constructorBody.Contains("PreloadRow(FailedRow)"),
                "Only the mandatory idle clip may be synchronously prepared by the shell.");

            Assert.IsTrue(feature.Contains("PrepareLoad()") &&
                feature.Contains("PublishPreparedLoad(") &&
                composition.Contains(
                    "StickyFeature.PublishPreparedLoad(prepared)") &&
                composition.Contains(
                    "SynchronizationContext.Current"),
                "Sticky I/O preparation must be detached while publication captures the Pet STA owner context.");

            Assert.IsTrue(startup.Contains(
                    "StartupWorkPhase.WaitForStickyRuntime") &&
                startup.Contains(
                    "if (_notes == null || _stickyWorkspace == null") &&
                startup.Contains(
                    "EventHandler backgroundReady = StartupBackgroundReady") &&
                startup.Contains("EventHandler ready = ShellReady"),
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
