using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static PennyPet.Tests.SourceGuardText;

namespace PennyPet.Tests
{
    // Source structure evidence only; does not prove runtime behavior.
    [TestCategory("ArchitectureSourceBoundary")]
    [TestClass]
    public sealed partial class InputAnimationBoundaryTests
    {
        [TestMethod]
        public void StartupLoading_UsesBootstrapOnlyEmbeddedVisual()
        {
            string loading = ReadSource("StartupLoadingForm.cs");
            string loadingThread = ReadSource(
                "StartupLoadingThreadHost.cs");
            string host = ReadSource("PennyApplicationHost.cs");
            string animation = ReadSource("PetAnimationRuntime.cs");
            string startup = ReadSource("PetStartupCoordinator.cs");

            Assert.IsTrue(loading.Contains("PennyPet.Startup.Loading") &&
                loading.Contains("GetManifestResourceStream(ResourceName)") &&
                loading.Contains("StartupPetPlacementSnapshot") &&
                loading.Contains("CalculateImageBounds(source.Size, size)") &&
                loading.Contains("canvas.Height - height") &&
                loading.Contains("graphics.Clear(Color.Transparent)"),
                "Loading must use its embedded asset on a bottom-aligned proportional canvas sized by the startup placement snapshot.");
            Assert.IsFalse(loading.Contains("PetArtPackage") ||
                loading.Contains("StickyUiHost") ||
                loading.Contains("StickyUiThreadHost") ||
                loading.Contains("StickyNoteRepository") ||
                loading.Contains("StickyHostedRuntime") ||
                loading.Contains("WpfApplicationHost") ||
                loading.Contains("PetForm.") ||
                loading.Contains("PetSettings") ||
                loading.Contains("Screen.") ||
                loading.Contains("ResolveLocation") ||
                loading.Contains("HasLocation"),
                "Bootstrap loading must only project the immutable placement snapshot and never read settings, screens or sticky state.");
            int showLoading = host.IndexOf("loading.Start(startupPlacement);",
                StringComparison.Ordinal);
            int constructPet = host.IndexOf("new PetForm(preloadedSettings)",
                StringComparison.Ordinal);
            Assert.IsTrue(showLoading >= 0 && constructPet > showLoading,
                "The loading form must be shown before PetForm construction.");
            int captureTopology = host.IndexOf(
                "new WindowsDisplayTopologyProvider().Capture()",
                StringComparison.Ordinal);
            int resolvePlacement = host.IndexOf(
                "ResolveStartupPetPlacement(preloadedSettings",
                StringComparison.Ordinal);
            Assert.IsTrue(captureTopology >= 0 &&
                resolvePlacement > captureTopology && showLoading > resolvePlacement,
                "The startup placement snapshot must be resolved from one captured topology before the loading thread starts.");
            Assert.IsTrue(loadingThread.Contains("new Thread(") &&
                loadingThread.Contains(
                    "SetApartmentState(ApartmentState.STA)") &&
                loadingThread.Contains("IsBackground = true") &&
                loadingThread.Contains("Application.Run(form)") &&
                loadingThread.Contains("form.BeginInvoke(") &&
                loadingThread.Contains("_ready.Set()"),
                "Loading must own a responsive message loop on a dedicated STA.");
            Assert.IsFalse(loadingThread.Contains("new PetForm") ||
                loadingThread.Contains("PetArtPackage") ||
                loadingThread.Contains("StickyUiHost") ||
                loadingThread.Contains("StickyNoteRepository") ||
                loadingThread.Contains("PetSettings"),
                "The loading thread must own only bootstrap presentation and never read settings.");
            string closeLoading = Between(loadingThread,
                "internal void Close()", "internal void BringToFront()");
            string postLoading = Between(loadingThread,
                "private void Post(", "public void Dispose()");
            Assert.IsTrue(closeLoading.Contains("Post(") &&
                postLoading.Contains("form.BeginInvoke(") &&
                loadingThread.Contains(
                    "_ready.WaitOne(ReadyTimeoutMilliseconds)") &&
                loadingThread.Contains(
                    "_exited.WaitOne(ExitTimeoutMilliseconds)") &&
                !loadingThread.Contains("_exited.WaitOne()"),
                "Close must marshal to loading STA and both waits must be bounded.");
            Assert.IsTrue(host.Contains("pet.StartupReady += delegate") &&
                host.Contains("loading.Close();") &&
                host.Contains("loading.BringToFront();") &&
                !host.Contains("Application.DoEvents()"),
                "StartupReady must close loading without DoEvents workarounds.");
            int releaseFrame = startup.IndexOf(
                "_startupDisplaySuppressed = false;", StringComparison.Ordinal);
            int renderFrame = startup.IndexOf("RenderCurrentFrame();",
                StringComparison.Ordinal);
            Assert.IsTrue(animation.Contains(
                    "if (_startupDisplaySuppressed || !IsHandleCreated") &&
                startup.Contains("_startupUiReady, _startupArtReady") &&
                releaseFrame >= 0 && renderFrame > releaseFrame,
                "Normal Pet frames must remain suppressed until startup readiness.");
        }
    }
}
