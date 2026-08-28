using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PennyPet.Tests
{
    [TestClass]
    public sealed class InputAnimationBoundaryTests
    {
        [TestMethod]
        public void Autosave_DoesNotRefreshSideTabs()
        {
            string source = ReadSource("Features/StickyNotes/PetStickyWindowCoordinator.cs");
            string handler = Between(source, "form.NoteChanged += delegate",
                "form.HeaderDragStarted");

            Assert.IsTrue(handler.Contains("_notes.SaveAsync();"),
                "NoteChanged must persist note data.");
            Assert.IsTrue(handler.Contains("RefreshMenuText();"),
                "NoteChanged must refresh menu text.");
            Assert.IsFalse(handler.Contains("RefreshNoteTabs();"),
                "Content autosave must not refresh side tabs.");
        }

        [TestMethod]
        public void OwnNoteTyping_StillTriggersAnimation()
        {
            string source = ReadSource("Features/StickyNotes/PetStickyWindowCoordinator.cs");
            string handler = Between(source, "form.TypingActivity += delegate",
                "form.CancelReminderRequested");

            Assert.IsTrue(handler.Contains("TriggerTypingAnimation();"),
                "Own-note typing should restore the typing animation.");
            Assert.IsFalse(handler.Contains("_typingSession = false"),
                "Own-note typing must not clear the animation session.");
        }

        [TestMethod]
        public void AnimationTick_DoesNotPauseForOwnNoteFocus()
        {
            string source = ReadSource("PetAnimationRuntime.cs");
            string tick = Between(source, "private void AnimationTick",
                "private int RuntimeFrameCount");

            Assert.IsFalse(tick.Contains("HasFocusedOwnNoteTextInput()"),
                "Animation must not pause merely because a note editor has focus.");
            Assert.IsFalse(tick.Contains("ShouldPauseOwnNoteAnimation"),
                "Animation must not pause for own-note IME composition.");
        }

        [TestMethod]
        public void AnimationRendering_DoesNotChangeWindowFocusOrActivation()
        {
            string runtime = ReadSource("PetAnimationRuntime.cs");
            string renderer = ReadSource("LayeredSpriteRenderer.cs");

            Assert.IsFalse(runtime.Contains(".Activate(") ||
                runtime.Contains(".Focus(") || runtime.Contains(".BringToFront("),
                "Animation runtime must not activate or focus windows.");
            Assert.IsFalse(renderer.Contains(".Activate(") ||
                renderer.Contains(".Focus(") || renderer.Contains(".BringToFront("),
                "Layered renderer must not activate or focus windows.");
        }

        [TestMethod]
        public void LayeredRenderer_DoesNotKeepGlobalBitmapHandleCache()
        {
            string source = ReadSource("LayeredSpriteRenderer.cs");
            Assert.IsFalse(source.Contains("Dictionary<Bitmap, IntPtr>") ||
                source.Contains("BitmapHandles"),
                "Layered renderer must not keep a global HBITMAP cache.");
        }

        [TestMethod]
        public void StartupPreload_IncludesTypingAnimationRows()
        {
            string source = ReadSource("PetAnimationRuntime.cs");
            string warmup = Between(source, "int[] warmRows =",
                "foreach (int row in warmRows)");

            Assert.IsTrue(warmup.Contains("WaitingRow") &&
                warmup.Contains("ThinkingRow"),
                "Typing animation rows must be preloaded during startup.");
        }

        private static string ReadSource(string relativePath)
        {
            string root = FindDesktopPetDirectory();
            string path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            return File.ReadAllText(path);
        }

        private static string Between(string text, string startMarker, string endMarker)
        {
            int start = text.IndexOf(startMarker, StringComparison.Ordinal);
            if (start < 0)
                throw new InvalidOperationException("Missing marker: " + startMarker);
            int end = text.IndexOf(endMarker, start + startMarker.Length,
                StringComparison.Ordinal);
            if (end < 0) end = text.Length;
            return text.Substring(start, end - start);
        }

        private static string FindDesktopPetDirectory()
        {
            DirectoryInfo current = new DirectoryInfo(AppContext.BaseDirectory);
            while (current != null)
            {
                if (File.Exists(Path.Combine(current.FullName,
                    "PennyPet.Windows.csproj")))
                    return current.FullName;
                current = current.Parent;
            }
            throw new DirectoryNotFoundException(
                "Could not locate the PennyPet Windows source directory.");
        }
    }
}
