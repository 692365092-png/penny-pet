using System;
using System.Collections.Generic;

namespace PennyPet
{
    // Selects at most three semantic sentences, then delegates only their
    // final punctuation/particle to PetSentenceEndingPolicy.
    internal static class DailyBriefingComposer
    {
        internal static string Compose(DayPart dayPart, DateTime localDate,
            DailyBriefingContent content)
        {
            return ComposeSentences(localDate,
                SelectSentences(dayPart, content));
        }

        internal static DailyBriefingSentence[] SelectSentences(
            DayPart dayPart, DailyBriefingContent content)
        {
            List<DailyBriefingSentence> selected =
                new List<DailyBriefingSentence>(3);
            selected.Add(new DailyBriefingSentence(
                DailyContentRules.GreetingBodyFor(dayPart),
                PetSentenceContentKind.Greeting,
                DailyContentRules.GreetingIntentFor(dayPart),
                "GREETING-" + dayPart));
            foreach (DailyBriefingSentence supplementary in
                SelectSupplementary(content))
            {
                if (selected.Count == 3) break;
                selected.Add(supplementary);
            }
            return selected.ToArray();
        }

        internal static DailyBriefingSentence[] SelectSupplementary(
            DailyBriefingContent content)
        {
            if (content == null) return new DailyBriefingSentence[0];
            List<DailyBriefingSentence> selected =
                new List<DailyBriefingSentence>(2);
            if (content.BirthdayKind != PetBirthdayKind.None &&
                content.BirthdayLine != null)
            {
                selected.Add(EntrySentence(content.BirthdayLine,
                    PetSentenceContentKind.Birthday));
                if (selected.Count == 2) return selected.ToArray();
            }
            if (content.SolarTerm.HasValue)
            {
                selected.Add(SolarSentence(content.SolarTerm.Value));
                if (selected.Count < 2 && content.Weather != null)
                    selected.Add(WeatherSentence(content.Weather));
                else if (selected.Count < 2 && content.Almanac != null)
                    selected.Add(AlmanacSentence(content.Almanac));
                return selected.ToArray();
            }
            if (selected.Count < 2 && content.Weather != null)
            {
                selected.Add(WeatherSentence(content.Weather));
                if (selected.Count < 2 && content.Almanac != null)
                    selected.Add(AlmanacSentence(content.Almanac));
                return selected.ToArray();
            }
            if (selected.Count < 2 && content.Almanac != null)
            {
                selected.Add(AlmanacSentence(content.Almanac));
                return selected.ToArray();
            }
            if (selected.Count < 2 && content.CuratedLine != null)
                selected.Add(EntrySentence(content.CuratedLine,
                    PetSentenceContentKind.Curated));
            if (selected.Count < 2 && content.ZodiacLine != null)
                selected.Add(EntrySentence(content.ZodiacLine,
                    PetSentenceContentKind.Zodiac));
            return selected.ToArray();
        }

        internal static string ComposeSentences(DateTime localDate,
            DailyBriefingSentence[] sentences)
        {
            if (sentences == null || sentences.Length == 0)
                return String.Empty;
            if (sentences.Length > 3)
                throw new ArgumentException(
                    "Daily briefing cannot exceed three sentences.",
                    "sentences");
            string[] lines = new string[sentences.Length];
            for (int i = 0; i < sentences.Length; i++)
            {
                DailyBriefingSentence sentence = sentences[i];
                PetSentenceRole role = sentences.Length == 1
                    ? PetSentenceRole.Single : i == 0
                        ? PetSentenceRole.Opening
                        : i == sentences.Length - 1
                            ? PetSentenceRole.Closing
                            : PetSentenceRole.Middle;
                lines[i] = PetSentenceEndingPolicy.Apply(sentence.Body,
                    new PetSentenceEndingContext(role, sentence.Intent,
                        sentence.Kind, sentence.StableContentId, localDate));
            }
            return String.Join("\n", lines);
        }

        private static DailyBriefingSentence SolarSentence(
            SolarTermInfo solarTerm)
        {
            return new DailyBriefingSentence("今天是" +
                solarTerm.ChineseName, PetSentenceContentKind.Solar,
                PetSentenceIntent.Gentle, "SOLAR-" + solarTerm.Term);
        }

        private static DailyBriefingSentence WeatherSentence(
            WeatherDailySelection weather)
        {
            return new DailyBriefingSentence(weather.Text,
                PetSentenceContentKind.Weather,
                WeatherIntent(weather.Meaning), weather.VariantId);
        }

        private static PetSentenceIntent WeatherIntent(WeatherMeaning meaning)
        {
            switch (meaning)
            {
                case WeatherMeaning.Snow:
                case WeatherMeaning.RainAndWind:
                case WeatherMeaning.RainAndCooling:
                case WeatherMeaning.HeavyRain:
                case WeatherMeaning.Hot:
                case WeatherMeaning.Cold:
                    return PetSentenceIntent.Serious;
                case WeatherMeaning.Warming:
                    return PetSentenceIntent.Statement;
                default:
                    return PetSentenceIntent.Gentle;
            }
        }

        private static DailyBriefingSentence AlmanacSentence(
            AlmanacDailySelection almanac)
        {
            PetSentenceIntent intent = !almanac.IsYi ||
                almanac.Topic == AlmanacTopic.ConservativeDay
                    ? PetSentenceIntent.Serious
                    : PetSentenceIntent.Gentle;
            return new DailyBriefingSentence(almanac.Text,
                PetSentenceContentKind.Almanac, intent,
                "ALMANAC-" + almanac.VariantId);
        }

        private static DailyBriefingSentence EntrySentence(
            DailyLineEntry entry, PetSentenceContentKind kind)
        {
            return new DailyBriefingSentence(entry.Text, kind,
                PetSentenceIntent.Gentle, entry.Id);
        }
    }
}
