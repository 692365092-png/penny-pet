using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PennyPet.Tests
{
    public sealed partial class CoreBehaviorTests
    {
        [TestMethod]
        public void Startup_settings_save_only_when_snapshot_is_dirty()
        {
            int writes = 0;
            bool failWrites = false;
            PetSettings settings = new PetSettings(delegate(SettingsWriteRequest request)
            {
                writes++;
                return failWrites
                    ? PersistenceResult.Failure(new System.IO.IOException("expected"))
                    : PersistenceResult.Success();
            });

            Assert.IsFalse(settings.SaveIfChangedAsync(),
                "A freshly loaded/default snapshot must not be rewritten just because startup ran.");
            Assert.AreEqual(0, writes);

            settings.ScalePercent = 150;
            Assert.IsTrue(settings.SaveIfChangedAsync(),
                "A startup normalization/change must enqueue one settings write.");
            Assert.IsTrue(settings.WaitForPendingSaves().Succeeded);
            Assert.AreEqual(1, writes);
            Assert.IsFalse(settings.SaveIfChangedAsync(),
                "The same already-queued snapshot must not be written twice.");

            failWrites = true;
            settings.KeyOverlayScalePercent = 120;
            Assert.IsTrue(settings.SaveIfChangedAsync());
            Assert.IsFalse(settings.WaitForPendingSaves().Succeeded);
            Assert.IsTrue(settings.HasUnsavedChanges);
            Assert.AreEqual(2, writes);

            failWrites = false;
            Assert.IsTrue(settings.SaveIfChangedAsync(),
                "A failed write is still dirty and must retry even when the model did not change again.");
            Assert.IsTrue(settings.WaitForPendingSaves().Succeeded);
            Assert.IsFalse(settings.HasUnsavedChanges);
            Assert.AreEqual(3, writes);
        }
    }
}
