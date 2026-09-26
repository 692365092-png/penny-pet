using Microsoft.VisualStudio.TestTools.UnitTesting;
using static PennyPet.Tests.StickySessionTopologyContractTests;

namespace PennyPet.Tests
{
    [TestClass]
    public sealed class KeyboardPrivacyArchitectureTests
    {
        [TestMethod]
        public void HookAndUiDelivery_ContainNoAutomationOrWaiting()
        {
            foreach (string file in new[] { "GlobalKeyboardActivity.cs", "KeyboardFocusSnapshot.cs", "PetKeyboardOverlayCoordinator.cs" })
            {
                string source = ReadSource("Features/KeyboardOverlay/" + file);
                Assert.IsFalse(source.Contains("AutomationElement."));
                Assert.IsFalse(source.Contains("SensitiveInputDetector."));
                Assert.IsFalse(source.Contains(".Wait("));
                Assert.IsFalse(source.Contains(".Join("));
            }
        }
        [TestMethod]
        public void Worker_IsSingleMtaAndDoesNotOwnAWindow()
        {
            string source = ReadSource("Features/KeyboardOverlay/KeyboardPrivacyWorker.cs");
            StringAssert.Contains(source, "SetApartmentState(ApartmentState.MTA)");
            Assert.IsFalse(source.Contains("ThreadPool"));
            Assert.IsFalse(source.Contains("Task.Run"));
            Assert.IsFalse(source.Contains("System.Windows"));
            string offer = SliceMethod(source, "internal void Offer(");
            Assert.IsFalse(offer.Contains("new Thread"));
            StringAssert.Contains(source, "_stillCurrent(input.FocusSnapshot)");
        }
        [TestMethod]
        public void Inspector_RequiresSupportedPasswordEvidenceAndExactNativeElement()
        {
            string source = ReadSource("Features/KeyboardOverlay/SensitiveInputDetector.cs");
            StringAssert.Contains(source, "AutomationElement.IsPasswordProperty, true");
            StringAssert.Contains(source, "password as bool?");
            StringAssert.Contains(source, "focused.Current.NativeWindowHandle");
            Assert.IsFalse(source.Contains("automationInspected || nativeInspected"));
        }
    }
}
