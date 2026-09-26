using System;
using System.Threading.Tasks;
using static PennyPet.PetAnimationController;

namespace PennyPet
{
    internal sealed partial class InteractionRuntime
    {
        private readonly PetPokeBurstTracker _pokeBurstTracker = new PetPokeBurstTracker();

        internal async Task PokeAsync(ConversationRuntime conversation,
            Func<bool> protectedMessage, Func<bool> showEasterEgg)
        {
            if (_stopped || _exiting) return;
            DateTime nowUtc = DateTime.UtcNow;
            if (_pokeBurstTracker.RegisterPoke(nowUtc))
            {
                _pokeGeneration++;
                conversation.InvalidatePending();
                if (!protectedMessage() && !ReminderAttentionActive && showEasterEgg())
                    StartPoke(FailedRow, PetInteractionAnimationKind.EasterEgg, nowUtc);
                return;
            }

            int generation = _pokeGeneration;
            DateTimeOffset localNow = DateTimeOffset.Now;
            if (conversation.IsOpeningEligible(localNow) && !protectedMessage())
                StartPoke(NotificationRow, PetInteractionAnimationKind.Notification, nowUtc);
            ConversationAnimation animation = await conversation.HandlePetPokedAsync(localNow);
            if (_stopped || _exiting || generation != _pokeGeneration || protectedMessage()) return;
            // Publication can await weather. Schedule from acceptance time,
            // never from an old click timestamp that has already elapsed.
            nowUtc = DateTime.UtcNow;
            switch (animation)
            {
                case ConversationAnimation.Notification:
                    StartPoke(NotificationRow, PetInteractionAnimationKind.Notification, nowUtc);
                    break;
                case ConversationAnimation.Guitar:
                    StartPoke(WaitingRow, PetInteractionAnimationKind.OrdinaryPoke, nowUtc, true);
                    break;
                case ConversationAnimation.Hover:
                    StartPoke(HoverRow, PetInteractionAnimationKind.OrdinaryPoke, nowUtc, true);
                    break;
                case ConversationAnimation.Ordinary:
                    StartPoke(PickRandomManualAnimationRow(_random, Row),
                        PetInteractionAnimationKind.OrdinaryPoke, nowUtc);
                    break;
            }
        }
    }
}
