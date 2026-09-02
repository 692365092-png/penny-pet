using System;
using System.Windows.Forms;

namespace PennyPet
{
    internal sealed partial class PetForm
    {
        private bool _stableMouseInside;
        private bool _hoverSuppressedUntilStableLeave;
        private DateTime _hoverEnterCandidateUtc;
        private DateTime _hoverLeaveCandidateUtc;
        private Timer _hoverStabilityTimer;

        private void OnRawMouseEnter()
        {
            _hoverLeaveCandidateUtc = default(DateTime);
            _hoverEnterCandidateUtc = DateTime.UtcNow;
            EnsureHoverStabilityTimer();
        }

        private void OnRawMouseLeave()
        {
            _hoverEnterCandidateUtc = default(DateTime);
            if (!Bounds.Contains(Cursor.Position))
            {
                CommitStableLeave();
                return;
            }
            _hoverLeaveCandidateUtc = DateTime.UtcNow;
            EnsureHoverStabilityTimer();
        }

        private void EnsureHoverStabilityTimer()
        {
            if (_hoverStabilityTimer == null)
            {
                _hoverStabilityTimer = new Timer();
                _hoverStabilityTimer.Interval = 40;
                _hoverStabilityTimer.Tick += HoverStabilityTick;
            }
            if (!_hoverStabilityTimer.Enabled) _hoverStabilityTimer.Start();
        }

        private void HoverStabilityTick(object sender, EventArgs e)
        {
            DateTime nowUtc = DateTime.UtcNow;
            bool pending = false;
            if (_hoverEnterCandidateUtc != default(DateTime))
            {
                if (PetHoverStabilityRules.ShouldCommitEnter(
                    _hoverEnterCandidateUtc, nowUtc))
                    CommitStableEnter();
                else
                    pending = true;
            }
            if (_hoverLeaveCandidateUtc != default(DateTime))
            {
                if (PetHoverStabilityRules.ShouldCommitLeave(
                    _hoverLeaveCandidateUtc, nowUtc))
                    CommitStableLeave();
                else
                    pending = true;
            }
            if (!pending && _hoverStabilityTimer != null)
                _hoverStabilityTimer.Stop();
        }

        private void CommitStableEnter()
        {
            _hoverEnterCandidateUtc = default(DateTime);
            if (_stableMouseInside) return;
            _stableMouseInside = true;
            QueueArtPreload(HoverRow);
            if (!_hoverSuppressedUntilStableLeave)
                ShowOrUpdateHoverBubble();
        }

        private void CommitStableLeave()
        {
            _stableMouseInside = false;
            _hoverEnterCandidateUtc = default(DateTime);
            _hoverLeaveCandidateUtc = default(DateTime);
            if (_hoverStabilityTimer != null) _hoverStabilityTimer.Stop();
            HideHoverBubble();
            _hoverSuppressedUntilStableLeave = false;
        }

        private void DisposeHoverRuntime()
        {
            _stableMouseInside = false;
            if (_hoverStabilityTimer != null)
            {
                _hoverStabilityTimer.Stop();
                _hoverStabilityTimer.Dispose();
            }
        }
    }
}
