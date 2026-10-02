using System;
using System.Windows.Forms;

namespace PennyPet
{
    internal sealed partial class PetForm
    {
        private void OnRawMouseEnter()
        {
            _interaction.MouseEnter(DateTime.UtcNow);
        }

        private void OnRawMouseLeave()
        {
            _interaction.MouseLeave(DateTime.UtcNow, !Bounds.Contains(Cursor.Position));
        }

        private void InteractionHoverChanged()
        {
            if (_interaction.StableMouseInside && !_interaction.HoverSuppressed && !_exiting)
                ShowOrUpdateHoverBubble();
            else HideHoverBubble();
        }
    }
}
