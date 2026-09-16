using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PennyPet.Tests
{
    [TestClass]
    public sealed class PetSettingsPlacementCodecTests
    {
        [TestMethod]
        public void LegacySettings_KeepRawFallbackWithoutInventingPreference()
        {
            PetSettingsData value = PetSettingsCodec.Parse(new[]
            {
                "HasLocation=1", "X=-1200", "Y=80", "ScalePercent=150"
            });
            Assert.IsTrue(value.HasLocation);
            Assert.AreEqual(-1200, value.X);
            Assert.AreEqual(80, value.Y);
            Assert.AreEqual(150, value.ScalePercent);
            Assert.AreEqual(string.Empty, value.PetPreferredTargetKey);
            Assert.AreEqual(0, value.PetPreferredLocalLogicalX);
            Assert.AreEqual(0, value.PetPreferredLocalLogicalY);
        }

        [TestMethod]
        public void PreferredPlacement_RoundTripsIndependentlyOfRawFallback()
        {
            PetSettingsData original = new PetSettingsData
            {
                HasLocation = true, X = -2460, Y = -100,
                PetPreferredTargetKey = "mdp:monitor=左屏",
                PetPreferredLocalLogicalX = 50,
                PetPreferredLocalLogicalY = -25
            };
            PetSettingsData value = PetSettingsCodec.Parse(
                PetSettingsCodec.Serialize(original));
            Assert.AreEqual(original.PetPreferredTargetKey,
                value.PetPreferredTargetKey);
            Assert.AreEqual(50, value.PetPreferredLocalLogicalX);
            Assert.AreEqual(-25, value.PetPreferredLocalLogicalY);
            Assert.AreEqual(-2460, value.X);
            Assert.AreEqual(-100, value.Y);
        }

        [TestMethod]
        public void CopyFrom_CopiesPreferenceAndNormalizesNullKey()
        {
            PetSettingsData original = new PetSettingsData
            {
                PetPreferredTargetKey = "mdp:b",
                PetPreferredLocalLogicalX = 60,
                PetPreferredLocalLogicalY = 90
            };
            PetSettingsData value = new PetSettingsData();
            value.CopyFrom(original);
            Assert.AreEqual("mdp:b", value.PetPreferredTargetKey);
            Assert.AreEqual(60, value.PetPreferredLocalLogicalX);
            Assert.AreEqual(90, value.PetPreferredLocalLogicalY);
            original.PetPreferredTargetKey = null;
            value.CopyFrom(original);
            Assert.AreEqual(string.Empty, value.PetPreferredTargetKey);
        }
    }
}
