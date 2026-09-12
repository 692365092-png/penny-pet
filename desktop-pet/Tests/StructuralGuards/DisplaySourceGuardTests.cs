using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static PennyPet.Tests.SourceGuardText;

namespace PennyPet.Tests
{
    public sealed partial class InputAnimationBoundaryTests
    {
        [TestMethod]
        public void SideTabs_KeepTopMostAndOnlyRebuildForSplitChanges()
        {
            string source = ReadSource(
                "Features/StickyNotes/PetStickyWindowCoordinator.cs");
            string zOrder = Between(source, "private void ApplyNoteTabZOrder",
                "private void PositionNoteTabs");
            string position = Between(source, "private void PositionNoteTabs",
                "private void ShowStickyNotesManager");
            string tabs = ReadSource(
                "Features/StickyNotes/StickyNoteTabs.cs");
            string form = ReadSource("PetForm.cs");

            Assert.IsTrue(zOrder.Contains(".TopMost =") &&
                zOrder.Contains("BringToFront()") &&
                !zOrder.Contains("RaiseVisibleNotesAboveTabs") &&
                !zOrder.Contains("_noteWindows") &&
                !zOrder.Contains("_notes.GetAll"),
                "Side-tab chrome must own its TopMost policy without legacy Window routing.");
            Assert.IsTrue(tabs.Contains("ShowWithoutActivation") &&
                tabs.Contains("WS_EX_NOACTIVATE"),
                "Stable TopMost tabs must remain non-activating.");
            Assert.IsTrue(position.Contains("CalculateEdgeAwareLeftCount") &&
                position.Contains("_noteTabsSignature = String.Empty") &&
                position.Contains("RefreshNoteTabs();") &&
                position.Contains("ShowNear(petBounds, work)") &&
                position.Contains("petFacts.PhysicalBounds"),
                "Positioning must rebuild only an invalid split and otherwise reposition.");
            Assert.IsTrue(form.Contains("WmSettingChange") &&
                form.Contains("WmDisplayChange") &&
                form.Contains("WmDeviceChange") &&
                form.Contains("NotifyPotentialChange") &&
                form.Contains("DisplayTopologyRuntime") &&
                !form.Contains("BeginInvoke(new Action(PositionNoteTabs))") &&
                tabs.Contains("TopMost = true") &&
                tabs.Contains("BringToFront()") &&
                !tabs.Contains("Activate()"),
                "Display/work-area changes must revalidate non-activating tab chrome.");
        }

        [TestMethod]
        public void DynamicDisplayCoreContracts_RemainPlatformAndUiIndependent()
        {
            string topology = ReadSource(
                "Core/Display/DisplayTopologyModels.cs");
            string placement = ReadSource(
                "Core/Display/DisplayPlacementModels.cs");
            string rules = ReadSource(
                "Core/Display/DisplayTopologyRules.cs");
            string combined = topology + placement + rules;

            Assert.IsFalse(combined.Contains("using System.Windows") ||
                combined.Contains("IntPtr") ||
                combined.Contains("DllImport") ||
                combined.Contains("QueryDisplayConfig(") ||
                combined.Contains("GetDpiForWindow("),
                "DRT Core contracts must contain no Windows handle or UI dependency.");
            Assert.IsTrue(topology.Contains(
                    "IReadOnlyList<DisplaySurfaceSnapshot> Surfaces") &&
                topology.Contains(
                    "IReadOnlyList<DisplayTargetIdentity> Targets") &&
                placement.Contains("WindowPlacementPreference Preferred") &&
                placement.Contains("WindowFacts Effective"),
                "Topology collections must be immutable and preferred/effective placement must stay separate.");
            Assert.IsTrue(ReadSource("PetForm.cs").Contains(
                    "DisplayTopologyRuntime") &&
                ReadSource("PetForm.cs").Contains(
                    "NotifyPotentialChange") &&
                ReadSource("StickyUiHost.cs").Contains(
                    "SetCurrentTopology(") &&
                !ReadSource("StickyUiHost.cs").Contains(
                    "WindowsDisplayTopologyProvider"),
                "The host may hold the Pet-published topology but must never capture it.");
        }

        [TestMethod]
        public void Drt5_NativePlacementExecutor_OwnsTypedHiddenBootstrap()
        {
            string executor = ReadSource(
                "Infrastructure/Display/WindowsWindowPlacementExecutor.cs");
            string native = ReadSource(
                "Infrastructure/Display/NativeDisplayConfig.cs");

            Assert.IsTrue(executor.Contains("WindowInteropHelper") &&
                executor.Contains(".EnsureHandle()") &&
                executor.Contains("internal int GetDpiForWindow()") &&
                executor.Contains("internal bool SetWindowPosExact(") &&
                executor.Contains("internal void Show()") &&
                executor.Contains("WindowsWindowFactsReader.Capture("),
                "The executor must own the typed native placement bootstrap.");
            string hiddenMove = Between(executor,
                "internal bool MoveHiddenToSurface(PhysicalRect workArea)",
                "internal int GetDpiForWindow()");
            Assert.IsTrue(hiddenMove.Contains("SWP_NOACTIVATE") &&
                hiddenMove.Contains("SWP_NOZORDER") &&
                hiddenMove.Contains("SWP_NOSIZE"),
                "The hidden move must never activate, reorder or resize.");
            Assert.IsFalse(hiddenMove.Contains("SWP_SHOWWINDOW") ||
                hiddenMove.Contains("SW_SHOW"),
                "The hidden bootstrap move must never show the window.");
            Assert.IsTrue(native.Contains(
                    "static extern bool SetWindowPos(") &&
                native.Contains("static extern bool ShowWindow("),
                "SetWindowPos/ShowWindow must be declared as typed natives.");
        }

        [TestMethod]
        public void DrtCloseout_TopologyGenerationOwnedOnlyByRuntime()
        {
            string provider = ReadSource(
                "Infrastructure/Display/WindowsDisplayTopologyProvider.cs");
            string runtime = ReadSource(
                "Infrastructure/Display/DisplayTopologyRuntime.cs");
            string models = ReadSource(
                "Core/Display/DisplayTopologyModels.cs");

            Assert.IsTrue(provider.Contains(
                    "return new DisplayTopologySnapshot(0, surfaces)") &&
                runtime.Contains(".WithGeneration(") &&
                runtime.Contains("Generation++") &&
                models.Contains("WithGeneration(long generation)"),
                "Only DisplayTopologyRuntime may assign semantic generations.");
            Assert.IsFalse(provider.Contains("Generation++"),
                "The capture provider must not own semantic generation.");
        }

        [TestMethod]
        public void DrtCloseout_MirrorTargets0IsNotDurablePreferenceRule()
        {
            string reader = ReadSource(
                "Infrastructure/Display/WindowsWindowFactsReader.cs");
            string models = ReadSource(
                "Core/Display/DisplayTopologyModels.cs");

            Assert.IsTrue(reader.Contains(
                    "Targets[0] is NOT a durable") &&
                reader.Contains("DRT-6 must choose"),
                "Facts capture must document Targets[0] as an active-target hint only.");
            Assert.IsTrue(models.Contains(
                    "never a durable-preferred") &&
                models.Contains("QueryDisplayConfig enumeration order"),
                "Mirrored-surface target order must never become an identity rule.");
        }

        [TestMethod]
        public void Drt6_EffectiveRuntimeIsSeparateFromRepository()
        {
            string runtime = ReadSource(
                "Features/StickyNotes/StickyPlacementRuntime.cs");

            Assert.IsTrue(runtime.Contains(
                    "Dictionary<string, NotePlacementState> _states") &&
                runtime.Contains("internal bool TryUpdateEffective("),
                "Effective WindowFacts must live in runtime memory.");
            Assert.IsFalse(runtime.Contains("StickyNoteRepository") ||
                runtime.Contains("SaveAsync") ||
                runtime.Contains("SetWindowPos") ||
                runtime.Contains("WindowInteropHelper") ||
                runtime.Contains("StickyNoteWindow("),
                "The runtime store must not own persistence or UI objects.");
        }

        [TestMethod]
        public void Drt6Supplement_PetFactsAlignedWithSingleTopologySnapshot()
        {
            string coordinator = ReadSource(
                "Features/StickyNotes/PetStickyWindowCoordinator.cs");

            Assert.IsTrue(coordinator.Contains(
                    "DisplayTopologySnapshot topology = CurrentTopologySnapshot();") &&
                coordinator.Contains("CapturePetWindowFacts(topology)") &&
                coordinator.Contains(
                    "Handle, PetWindowFactsId,") &&
                coordinator.Contains("generation, sequence, topology"),
                "Pet facts must be captured against the same attempt topology.");
            string fallback = Between(coordinator,
                "private void ApplyLegacySpawnFallback",
                "private static void TraceSpawnPlacement");
            Assert.IsFalse(fallback.Contains("PreferredDisplayTargetKey"),
                "The legacy fallback must never fabricate a durable preference.");
        }

        [TestMethod]
        public void Drt7_FallbackPolicyIsPlatformIndependent()
        {
            string policy = ReadSource("Core/Display/FallbackDisplayPolicy.cs");

            Assert.IsFalse(policy.Contains("System.Windows") ||
                policy.Contains("IntPtr") ||
                policy.Contains("DllImport") ||
                policy.Contains("WindowsDisplay"),
                "The fallback policy must stay pure Core.");
            Assert.IsTrue(policy.Contains("PrimaryOrFirst()") &&
                policy.Contains("FindByRuntimeGdiName("),
                "The policy must fall back through Pet surface and primary.");
        }

        [TestMethod]
        public void Drt7_UserCommitBlocksReturnButReturnRestores()
        {
            var runtime = new StickyPlacementRuntime();
            WindowFacts facts = StickyGeometryAuthorityTests.Facts();
            Assert.IsTrue(runtime.TryUpdateEffective("note", facts, StickyGeometryAuthorityTests.Topology()));
            runtime.MarkTemporaryRehome("note", "display removed");
            Assert.IsTrue(runtime.IsTemporaryRehome("note"));
            runtime.MarkUserPlacementCommit("note");
            Assert.IsFalse(runtime.IsTemporaryRehome("note"));
            Assert.IsTrue(runtime.UserMovedSinceRehome("note"));
            runtime.MarkTemporaryRehome("note", "removed again");
            Assert.IsFalse(runtime.UserMovedSinceRehome("note"));
            runtime.MarkReturnedToPreferred("note");
            Assert.IsFalse(runtime.IsTemporaryRehome("note"));
            Assert.AreEqual(String.Empty, runtime.TemporaryReason("note"));
            Assert.AreSame(facts, runtime.GetEffective("note"));
        }

        [TestMethod]
        public void ReprojectRuntimeTransitionRequiresAppliedResult()
        {
            string coordinator = ReadSource(
                "Features/StickyNotes/PetStickyWindowCoordinator.cs");
            string apply = Between(coordinator,
                "private bool ApplyReprojectResult",
                "private static bool TryBuildPreference");
            Assert.IsTrue(apply.Contains("result.Facts == null") &&
                apply.Contains("WindowFactsVersionRules.Classify(noteId, result.Sequence,") &&
                apply.Contains("return true;"));
            Assert.IsTrue(coordinator.Contains(
                    "ApplyReprojectResult(result, noteId, snapshot)") &&
                coordinator.Contains(
                    "ApplyReprojectResult(result, rehomedNoteId, snapshot)"));
        }
    }
}
