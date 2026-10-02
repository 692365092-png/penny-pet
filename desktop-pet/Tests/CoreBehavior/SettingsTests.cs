using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PennyPet.Tests
{
    public sealed partial class CoreBehaviorTests
    {
        [TestMethod]
        public void SettingRules_NormalizePersistedValuesWithoutUiTypes()
        {
            Assert.AreEqual(50, PetSettingRules.NormalizePetScalePercent(47));
            Assert.AreEqual(100, PetSettingRules.NormalizePetScalePercent(104));
            Assert.AreEqual(160, PetSettingRules.NormalizePetScalePercent(156));
            Assert.AreEqual(200, PetSettingRules.NormalizePetScalePercent(207));

            Assert.AreEqual(60,
                PetSettingRules.NormalizeKeyboardTextScalePercent(55));
            Assert.AreEqual(100,
                PetSettingRules.NormalizeKeyboardTextScalePercent(100));
            Assert.AreEqual(150,
                PetSettingRules.NormalizeKeyboardTextScalePercent(140));
        }

        [TestMethod]
        public void SettingsCodec_RoundTripsCurrentFormatAndReminders()
        {
            PetSettingsData source = new PetSettingsData
            {
                HasLocation = true,
                X = -120,
                Y = 340,
                StartupPreferenceInitialized = true,
                StartAtLogin = false,
                ScalePercent = 170,
                ShowKeyOverlay = true,
                KeyboardPrivacyNoticeAccepted = true,
                KeyOverlayScalePercent = 150,
                SilentMode = true,
                DailyContentEnabled = false,
                SolarTermEnabled = false,
                WeatherEnabled = true,
                WeatherLocationName = "武汉",
                WeatherLocationAdmin1 = "湖北",
                WeatherLocationCountry = "中国",
                WeatherLatitude = 30.5928,
                WeatherLongitude = 114.3055,
                WeatherTimezone = "Asia/Shanghai",
                ZodiacSign = ZodiacSign.Scorpio,
                LastDailyBriefingDate = "20350405"
            };
            source.Reminders.Add(new ReminderItem(
                new DateTime(2035, 4, 5, 6, 7, 8, DateTimeKind.Utc),
                "喝水", "note-42", 24F, true));

            List<string> serialized = PetSettingsCodec.Serialize(source);
            PetSettingsData restored = PetSettingsCodec.Parse(serialized);
            int dailyDateLines = 0;
            int zodiacLines = 0;
            foreach (string line in serialized)
            {
                if (line.StartsWith("LastDailyBriefingDate=",
                    StringComparison.Ordinal)) dailyDateLines++;
                if (line.StartsWith("ZodiacSign=",
                    StringComparison.Ordinal)) zodiacLines++;
            }

            Assert.IsTrue(restored.HasLocation);
            Assert.AreEqual(-120, restored.X);
            Assert.AreEqual(340, restored.Y);
            Assert.IsFalse(restored.StartAtLogin);
            Assert.AreEqual(170, restored.ScalePercent);
            Assert.IsTrue(restored.ShowKeyOverlay);
            Assert.IsTrue(restored.KeyboardPrivacyNoticeAccepted);
            Assert.AreEqual(150, restored.KeyOverlayScalePercent);
            Assert.IsTrue(restored.SilentMode);
            Assert.IsFalse(restored.DailyContentEnabled);
            Assert.IsFalse(restored.SolarTermEnabled);
            Assert.IsTrue(restored.WeatherEnabled);
            Assert.AreEqual("武汉", restored.WeatherLocationName);
            Assert.AreEqual("湖北", restored.WeatherLocationAdmin1);
            Assert.AreEqual("中国", restored.WeatherLocationCountry);
            Assert.AreEqual(30.5928, restored.WeatherLatitude, 0.000001);
            Assert.AreEqual(114.3055, restored.WeatherLongitude, 0.000001);
            Assert.AreEqual("Asia/Shanghai", restored.WeatherTimezone);
            Assert.AreEqual(ZodiacSign.Scorpio, restored.ZodiacSign);
            Assert.AreEqual("20350405", restored.LastDailyBriefingDate);
            Assert.AreEqual(1, dailyDateLines);
            Assert.AreEqual(1, zodiacLines);
            Assert.AreEqual(1, restored.Reminders.Count);
            Assert.AreEqual("喝水", restored.Reminders[0].Text);
            Assert.AreEqual("note-42", restored.Reminders[0].SourceNoteId);
            Assert.AreEqual(480, restored.Reminders[0].FontSizeTwips);
            Assert.IsTrue(restored.Reminders[0].PreAlertEnabled);
        }

        [TestMethod]
        public void SettingsCodec_LoadsLegacyReminderAndNormalizesScales()
        {
            DateTime deadline = new DateTime(2036, 2, 3, 4, 5, 6,
                DateTimeKind.Utc);
            string encodedText = Convert.ToBase64String(
                Encoding.UTF8.GetBytes("旧提醒"));

            PetSettingsData restored = PetSettingsCodec.Parse(new string[]
            {
                "StartWithWindows=0",
                "ScalePercent=47",
                "KeyOverlayScalePercent=140",
                "ReminderUtcTicks=" + deadline.Ticks,
                "ReminderTextBase64=" + encodedText
            });

            Assert.IsFalse(restored.StartAtLogin);
            Assert.AreEqual(50, restored.ScalePercent);
            Assert.AreEqual(150, restored.KeyOverlayScalePercent);
            Assert.IsTrue(restored.DailyContentEnabled);
            Assert.IsTrue(restored.SolarTermEnabled);
            Assert.IsFalse(restored.WeatherEnabled);
            Assert.AreEqual(ZodiacSign.None, restored.ZodiacSign);
            Assert.AreEqual(1, restored.Reminders.Count);
            Assert.AreEqual("旧提醒", restored.Reminders[0].Text);
            Assert.AreEqual(deadline, restored.Reminders[0].DeadlineUtc);
        }

        [TestMethod]
        public void SettingsCodec_RejectsContentWithNoKnownFields()
        {
            bool rejected = false;
            try
            {
                PetSettingsCodec.Parse(new string[] { "unknown=value" });
            }
            catch (InvalidDataException)
            {
                rejected = true;
            }
            Assert.IsTrue(rejected);
            Assert.IsFalse(new PetSettingsData().StartAtLogin);
            Assert.IsTrue(new PetSettingsData().DailyContentEnabled);
            Assert.IsTrue(new PetSettingsData().SolarTermEnabled);
            Assert.IsFalse(new PetSettingsData().WeatherEnabled);
            Assert.AreEqual(ZodiacSign.None,
                new PetSettingsData().ZodiacSign);
        }

        [TestMethod]
        public void SettingsData_CopyFromPreservesDailyContentPreferences()
        {
            PetSettingsData source = new PetSettingsData
            {
                DailyContentEnabled = false,
                SolarTermEnabled = false,
                WeatherEnabled = true,
                WeatherLocationName = "香港",
                WeatherLocationAdmin1 = "香港",
                WeatherLocationCountry = "中国",
                WeatherLatitude = 22.3193,
                WeatherLongitude = 114.1694,
                WeatherTimezone = "Asia/Hong_Kong",
                ZodiacSign = ZodiacSign.Taurus,
                LastDailyBriefingDate = "20350908"
            };
            PetSettingsData target = new PetSettingsData();

            target.CopyFrom(source);

            Assert.IsFalse(target.DailyContentEnabled);
            Assert.IsFalse(target.SolarTermEnabled);
            Assert.IsTrue(target.WeatherEnabled);
            Assert.AreEqual("香港", target.WeatherLocationName);
            Assert.AreEqual(22.3193, target.WeatherLatitude, 0.000001);
            Assert.AreEqual("Asia/Hong_Kong", target.WeatherTimezone);
            Assert.AreEqual(ZodiacSign.Taurus, target.ZodiacSign);
            Assert.AreEqual("20350908", target.LastDailyBriefingDate);
        }

        [TestMethod]
        public void SettingsCodec_AlmanacDefaultsTrueAndRoundTripsFalse()
        {
            Assert.IsTrue(new PetSettingsData().AlmanacEnabled);
            Assert.IsTrue(PetSettingsCodec.Parse(new[] { "DailyContentEnabled=1" })
                .AlmanacEnabled);

            PetSettingsData disabled = new PetSettingsData
            {
                AlmanacEnabled = false
            };
            Assert.IsFalse(PetSettingsCodec.Parse(
                PetSettingsCodec.Serialize(disabled)).AlmanacEnabled);

            PetSettingsData target = new PetSettingsData();
            target.CopyFrom(disabled);
            Assert.IsFalse(target.AlmanacEnabled);
        }

        [TestMethod]
        public void SettingsCodec_NormalizesInvalidAndRoundTripsPisces()
        {
            PetSettingsData pisces = new PetSettingsData
            {
                ZodiacSign = ZodiacSign.Pisces
            };
            Assert.AreEqual(ZodiacSign.Pisces, PetSettingsCodec.Parse(
                PetSettingsCodec.Serialize(pisces)).ZodiacSign);
            Assert.AreEqual(ZodiacSign.None, PetSettingsCodec.Parse(
                new string[] { "ZodiacSign=999" }).ZodiacSign);
            Assert.AreEqual(ZodiacSign.None, PetSettingsCodec.Parse(
                new string[] { "ZodiacSign=Scorpio" }).ZodiacSign);
            Assert.AreEqual(ZodiacSign.None,
                PetSettingRules.NormalizeZodiacSign((ZodiacSign)(-1)));
        }

        [TestMethod]
        public void SettingsCodec_InvalidWeatherLocationFailsClosed()
        {
            string encodedName = Convert.ToBase64String(
                Encoding.UTF8.GetBytes("武汉"));
            string encodedTimezone = Convert.ToBase64String(
                Encoding.UTF8.GetBytes("Asia/Shanghai"));
            PetSettingsData restored = PetSettingsCodec.Parse(new[]
            {
                "WeatherEnabled=1",
                "WeatherLocationNameBase64=" + encodedName,
                "WeatherLatitude=200",
                "WeatherLongitude=114.3055",
                "WeatherTimezoneBase64=" + encodedTimezone
            });

            Assert.IsFalse(restored.WeatherEnabled);
        }
    }
}
