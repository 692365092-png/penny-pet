using System;
using System.Collections.Generic;
using System.Globalization;

namespace PennyPet
{
    internal static class AlmanacDailySelector
    {
        private const int CandidateSalt = 37;
        private const int RouteSalt = 101;
        private const int FullWordingSalt = 131;
        private const int FramingSalt = 211;
        private const int CoreSalt = 307;

        internal static AlmanacDailySelection Select(AlmanacDayInfo day,
            DateTimeOffset localNow)
        {
            if (day == null || day.Year != localNow.Year ||
                day.Month != localNow.Month || day.Day != localNow.Day)
                return null;
            string[] recognized;
            string[] suppressed;
            List<Candidate> everyday;
            List<Candidate> cultural;
            List<Candidate> conservative;
            Analyze(day, out everyday, out cultural, out conservative,
                out recognized, out suppressed);
            List<Candidate> available = everyday.Count > 0
                ? everyday : cultural.Count > 0 ? cultural : conservative;
            if (available.Count == 0) return null;
            long seed = DateNumber(day.Year, day.Month, day.Day);
            foreach (Candidate candidate in available)
                seed = Mix(seed, (int)candidate.Topic * CandidateSalt +
                    (candidate.IsYi ? 1 : 0));
            Candidate selected = available[PositiveModulo(seed,
                available.Count)];
            return CreateWording(day, selected);
        }

        internal static void DescribeTopics(AlmanacDayInfo day,
            out string[] recognizedTopics, out string[] suppressedTopics)
        {
            List<Candidate> everyday;
            List<Candidate> cultural;
            List<Candidate> conservative;
            Analyze(day, out everyday, out cultural, out conservative,
                out recognizedTopics, out suppressedTopics);
        }

        private static void Analyze(AlmanacDayInfo day,
            out List<Candidate> everyday, out List<Candidate> cultural,
            out List<Candidate> conservative, out string[] recognized,
            out string[] suppressed)
        {
            everyday = new List<Candidate>();
            cultural = new List<Candidate>();
            conservative = new List<Candidate>();
            List<string> recognizedList = new List<string>();
            List<string> suppressedList = new List<string>();
            if (day == null)
            {
                recognized = recognizedList.ToArray();
                suppressed = suppressedList.ToArray();
                return;
            }
            Dictionary<AlmanacTopic, TopicTerms> topics =
                new Dictionary<AlmanacTopic, TopicTerms>();
            AddTerms(day.Yi, true, topics);
            AddTerms(day.Ji, false, topics);
            List<AlmanacTopic> ordered = new List<AlmanacTopic>(topics.Keys);
            ordered.Sort();
            foreach (AlmanacTopic topic in ordered)
            {
                TopicTerms terms = topics[topic];
                if (terms.Yi.Count > 0)
                    recognizedList.Add(topic + ":Yi");
                if (terms.Ji.Count > 0)
                    recognizedList.Add(topic + ":Ji");
                if (terms.Yi.Count > 0 && terms.Ji.Count > 0)
                {
                    suppressedList.Add(topic + ":YiJiConflict");
                    continue;
                }
                if (terms.Yi.Count > 0)
                {
                    Candidate candidate = new Candidate(topic,
                        terms.Yi.Min, true);
                    if (AlmanacSemanticCatalog.IsEverydayYi(topic))
                        everyday.Add(candidate);
                    else if (AlmanacSemanticCatalog.IsCultural(topic, true))
                        cultural.Add(candidate);
                    else if (topic == AlmanacTopic.ConservativeDay)
                        conservative.Add(candidate);
                    continue;
                }
                if (terms.Ji.Count == 0) continue;
                Candidate jiCandidate = new Candidate(topic,
                    terms.Ji.Min, false);
                if (AlmanacSemanticCatalog.IsCultural(topic, false))
                    cultural.Add(jiCandidate);
                else if (topic == AlmanacTopic.ConservativeDay)
                    conservative.Add(jiCandidate);
                else
                    suppressedList.Add(topic + ":JiNotEligible");
            }
            recognized = recognizedList.ToArray();
            suppressed = suppressedList.ToArray();
        }

        private static void AddTerms(IReadOnlyList<string> rawTerms,
            bool isYi, Dictionary<AlmanacTopic, TopicTerms> topics)
        {
            foreach (string rawTerm in rawTerms)
            {
                AlmanacTopic topic;
                if (!AlmanacSemanticCatalog.TryMap(rawTerm, out topic))
                    continue;
                TopicTerms terms;
                if (!topics.TryGetValue(topic, out terms))
                {
                    terms = new TopicTerms();
                    topics.Add(topic, terms);
                }
                (isYi ? terms.Yi : terms.Ji).Add(rawTerm.Trim());
            }
        }

        private static AlmanacDailySelection CreateWording(
            AlmanacDayInfo day, Candidate candidate)
        {
            long seed = Mix(DateNumber(day.Year, day.Month, day.Day),
                (int)candidate.Topic * 2 + (candidate.IsYi ? 1 : 0));
            AlmanacWordingVariant[] full =
                AlmanacWordingCatalog.GetFullVariants(candidate.Topic,
                    candidate.IsYi);
            DailyLineEntry[] framings = AlmanacWordingCatalog.GetFramings(
                candidate.Topic, candidate.IsYi);
            DailyLineEntry[] cores = AlmanacWordingCatalog.GetCores(
                candidate.Topic, candidate.IsYi);
            bool composed = framings.Length > 0 && cores.Length > 0 &&
                PositiveModulo(Mix(seed, RouteSalt), 100) < 80;
            if (!composed)
            {
                AlmanacWordingVariant wording = SelectStable(full,
                    Mix(seed, FullWordingSalt));
                return new AlmanacDailySelection(candidate.Topic,
                    candidate.SourceTerm, candidate.IsYi, wording.Id,
                    wording.FramingId, wording.Id, wording.Text);
            }

            DailyLineEntry framing = SelectStable(framings,
                Mix(seed, FramingSalt));
            DailyLineEntry core = SelectStable(cores, Mix(seed, CoreSalt));
            string text = String.Format(CultureInfo.InvariantCulture,
                framing.Text, core.Text);
            string variantId = framing.Id + "+" + core.Id;
            return new AlmanacDailySelection(candidate.Topic,
                candidate.SourceTerm, candidate.IsYi, variantId,
                framing.Id, core.Id, text);
        }

        private static AlmanacWordingVariant SelectStable(
            AlmanacWordingVariant[] source, long seed)
        {
            List<AlmanacWordingVariant> ordered =
                new List<AlmanacWordingVariant>(source);
            ordered.Sort(delegate(AlmanacWordingVariant left,
                AlmanacWordingVariant right)
            {
                return StringComparer.Ordinal.Compare(left.Id, right.Id);
            });
            return ordered[PositiveModulo(seed, ordered.Count)];
        }

        private static DailyLineEntry SelectStable(DailyLineEntry[] source,
            long seed)
        {
            List<DailyLineEntry> ordered = new List<DailyLineEntry>(source);
            ordered.Sort(delegate(DailyLineEntry left, DailyLineEntry right)
            {
                return StringComparer.Ordinal.Compare(left.Id, right.Id);
            });
            return ordered[PositiveModulo(seed, ordered.Count)];
        }

        private static long DateNumber(int year, int month, int day)
        {
            return year * 372L + month * 31L + day;
        }

        private static long Mix(long seed, int salt)
        {
            return unchecked(seed * 1103515245L + salt * 12345L);
        }

        private static int PositiveModulo(long value, int divisor)
        {
            long result = value % divisor;
            return (int)(result < 0 ? result + divisor : result);
        }

        private sealed class TopicTerms
        {
            internal readonly SortedSet<string> Yi =
                new SortedSet<string>(StringComparer.Ordinal);
            internal readonly SortedSet<string> Ji =
                new SortedSet<string>(StringComparer.Ordinal);
        }

        private sealed class Candidate
        {
            internal Candidate(AlmanacTopic topic, string sourceTerm,
                bool isYi)
            {
                Topic = topic;
                SourceTerm = sourceTerm;
                IsYi = isYi;
            }

            internal AlmanacTopic Topic { get; private set; }
            internal string SourceTerm { get; private set; }
            internal bool IsYi { get; private set; }
        }
    }
}
