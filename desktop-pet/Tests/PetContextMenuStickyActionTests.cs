using Microsoft.VisualStudio.TestTools.UnitTesting;
using static PennyPet.Tests.StickySessionTopologyContractTests;

namespace PennyPet.Tests
{
    // Source guard only; does not prove native menu interaction/placement.
    [TestClass]
    [TestCategory("ArchitectureSourceBoundary")]
    public sealed class PetContextMenuStickyActionTests
    {
        [TestMethod]
        public void TileAllUsesExactProductActionWithoutManagerOnlyCommands()
        {
            string menu = ReadSource("PetContextMenu.cs");
            Assert.IsTrue(menu.Contains("平铺全部便利贴到当前屏幕"));
            Assert.IsTrue(menu.Contains("_commands.TileAllNotes();"));
            Assert.IsTrue(menu.Contains("Menu.Items.Add(TileAllNotesItem)"));
            string form = ReadSource("PetForm.cs");
            string wire = SliceMethod(form, "menuCommands.TileAllNotes = delegate");
            Assert.IsTrue(wire.Contains("QueueStickyWindowAction("));
            Assert.IsTrue(wire.Contains("ExpandAndTileAllStickyNotesToPetScreen,"));
            foreach (string text in new[] { "收起全部", "展开全部", "完整恢复" })
                Assert.IsFalse(menu.Contains(text), text);
        }
    }
}
