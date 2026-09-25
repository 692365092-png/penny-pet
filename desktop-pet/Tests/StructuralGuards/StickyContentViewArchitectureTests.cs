using Microsoft.VisualStudio.TestTools.UnitTesting;
using static PennyPet.Tests.StickySessionTopologyContractTests;

namespace PennyPet.Tests
{
    [TestClass]
    public sealed class StickyContentViewArchitectureTests
    {
        [TestMethod]
        public void WindowShell_CreatesOnlySelectedContentAndContainsNoExerciseScripts()
        {
            string shell = ReadSource("Features/StickyNotes/StickyNoteWpf.cs");
            StringAssert.Contains(shell, "private readonly StickyContentView _contentView");
            StringAssert.Contains(shell, "new StickyTextContentView(this)");
            StringAssert.Contains(shell, "new StickyTodoContentView(this)");
            StringAssert.Contains(shell, "new StickyScheduleContentView(this)");
            Assert.IsFalse(shell.Contains("new WC.RichTextBox"));
            Assert.IsFalse(shell.Contains("Exercise"));
            Assert.IsFalse(shell.Contains("new WC.ListBox"));
        }

        [TestMethod]
        public void ListViews_DoNotCreateRichEditorOrLinkTimer()
        {
            foreach (string file in new[] { "StickyContentView.cs", "StickyTodoContentView.cs", "StickyScheduleContentView.cs" })
            {
                string source = ReadSource("Features/StickyNotes/" + file);
                Assert.IsFalse(source.Contains("new WC.RichTextBox"));
                Assert.IsFalse(source.Contains("_linkRefreshTimer"));
                Assert.IsFalse(source.Contains("InstalledFontNames()"));
            }
        }

        [TestMethod]
        public void TextView_RetainsCompositionHandlersAndDisposesOwnedTimer()
        {
            string source = ReadSource("Features/StickyNotes/StickyTextContentView.cs");
            StringAssert.Contains(source, "AddPreviewTextInputStartHandler");
            StringAssert.Contains(source, "RemovePreviewTextInputStartHandler");
            StringAssert.Contains(source, "AddPreviewTextInputUpdateHandler");
            StringAssert.Contains(source, "RemovePreviewTextInputUpdateHandler");
            StringAssert.Contains(source, "_linkRefreshTimer.Stop()");
            StringAssert.Contains(source, "_editorTextCompositionActive");
        }
    }
}
