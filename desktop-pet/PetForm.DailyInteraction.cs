namespace PennyPet
{
    internal sealed partial class PetForm
    {
        private bool ShowConversationMessage(PetMessageKind kind, string text)
        {
            if (_exiting || IsDisposed || Disposing) return false;
            string font = KeyboardOverlayForm.TextFontFamilyName;
            float size = KeyboardOverlayForm.TextFontSizePoints(_settings.KeyOverlayScalePercent);
            return _bubbleCoordinator.Show(kind == PetMessageKind.SmallTalk
                ? PetBubbleRequest.SmallTalk(text, font, size)
                : PetBubbleRequest.DailyGreeting(text, font, size));
        }
    }
}
