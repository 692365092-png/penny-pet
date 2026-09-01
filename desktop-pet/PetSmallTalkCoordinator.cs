using System;

namespace PennyPet
{
    // Owns the minimal in-process SmallTalk lifecycle without knowing UI.
    internal sealed class PetSmallTalkCoordinator
    {
        private static readonly string[] DefaultPhrases =
        {
            "需要我帮什么忙吗？",
            "我在呢～",
            "怎么啦？",
            "今天想做点什么？",
            "要不要休息一下？"
        };

        private readonly Func<bool> _silentMode;
        private readonly Func<string, bool> _show;
        private readonly Random _random;
        private int _lastPhraseIndex = -1;
        private DateTime _lastShownUtc = DateTime.MinValue;

        internal PetSmallTalkCoordinator(Func<bool> silentMode,
            Func<string, bool> show, Random random = null)
        {
            _silentMode = silentMode ??
                throw new ArgumentNullException("silentMode");
            _show = show ?? throw new ArgumentNullException("show");
            _random = random ?? new Random();
        }

        internal bool HandlePetPoked(DateTime nowUtc)
        {
            if (PetMessagePolicy.ShouldSuppress(PetMessageKind.SmallTalk,
                _silentMode())) return false;
            if (!PetSmallTalkPolicy.ShouldAttempt(_lastShownUtc, nowUtc,
                _random.Next(100))) return false;
            int phraseIndex = PetSmallTalkPolicy.NextPhraseIndex(
                _lastPhraseIndex, _random.Next(DefaultPhrases.Length),
                DefaultPhrases.Length);
            if (!_show(DefaultPhrases[phraseIndex])) return false;
            _lastShownUtc = nowUtc;
            _lastPhraseIndex = phraseIndex;
            return true;
        }
    }
}
