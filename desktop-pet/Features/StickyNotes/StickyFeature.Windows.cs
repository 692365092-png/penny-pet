using System.Threading;

namespace PennyPet
{
    internal sealed partial class StickyFeature
    {
        internal StickyWorkspace Workspace { get; private set; }

        // The composition root attaches the Windows runtime after loading the
        // model. Start remains explicit, so attachment creates no HWNDs.
        internal StickyWorkspace AttachWorkspace(IStickyPetSurface surface,
            IStickyPresentation presentation, IStickyReminderActions reminders,
            SynchronizationContext context)
        {
            Workspace = new StickyWorkspace(this, surface, presentation, reminders, context);
            return Workspace;
        }
    }
}
