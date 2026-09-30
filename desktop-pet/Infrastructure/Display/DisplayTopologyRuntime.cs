using System;
using System.Collections.Generic;

namespace PennyPet
{
    // Turns "topology may have changed" hints into one settled immutable
    // snapshot. Generation advances only on a semantic change; reordered
    // surfaces do not count. No window movement happens here.
    internal sealed class DisplayTopologyRuntime : IDisposable
    {
        internal const int QuietDelayMilliseconds = 150;
        internal const int SettleWindowMilliseconds = 750;
        internal const int CaptureFailureTraceLimit = 8;
        internal const int CaptureRetryLimit = 3;
        private const int CaptureRetryDelayMilliseconds = 250;

        private readonly Func<DisplayTopologySnapshot> _capture;
        private readonly object _gate = new object();
        private readonly System.Windows.Forms.Timer _quietTimer;
        private readonly System.Windows.Forms.Timer _settleTimer;
        private DateTime _firstHintUtc;
        private bool _hintActive;
        private bool _capturePending;
        private int _captureRetriesRemaining;
        private bool _disposed;
        private int _captureFailureTraceCount;

        internal event Action<string, DisplayTopologySnapshot> TopologyChanged;

        internal DisplayTopologyRuntime(
            Func<DisplayTopologySnapshot> capture)
        {
            _capture = capture ?? throw new ArgumentNullException(nameof(capture));
            _quietTimer = new System.Windows.Forms.Timer();
            _quietTimer.Interval = QuietDelayMilliseconds;
            _quietTimer.Tick += delegate { TryCapture(); };
            _settleTimer = new System.Windows.Forms.Timer();
            _settleTimer.Interval = SettleWindowMilliseconds;
            _settleTimer.Tick += delegate { TryCapture(); };
        }

        internal long Generation { get; private set; }
        internal int CaptureCount { get; private set; }
        internal string LastReason { get; private set; }
        internal DisplayTopologySnapshot Current { get; private set; }

        internal void CaptureInitial()
        {
            lock (_gate)
            {
                if (_disposed) return;
                LastReason = "initial";
                _captureRetriesRemaining = CaptureRetryLimit;
            }
            DisplayTopologySnapshot snapshot = SafeCapture();
            if (snapshot == null) { ScheduleCaptureRetry(); return; }
            DisplayTopologySnapshot owned;
            lock (_gate)
            {
                Generation = 0;
                Current = snapshot.WithGeneration(Generation);
                owned = Current;
            }
            Raise("initial", owned);
        }

        internal void NotifyPotentialChange(string reason)
        {
            lock (_gate)
            {
                if (_disposed) return;
                LastReason = reason ?? String.Empty;
                _capturePending = true;
                _captureRetriesRemaining = CaptureRetryLimit;
                _quietTimer.Interval = QuietDelayMilliseconds;
                if (!_hintActive)
                {
                    _hintActive = true;
                    _firstHintUtc = DateTime.UtcNow;
                    _settleTimer.Start();
                }
                _quietTimer.Stop();
                _quietTimer.Start();
                if (DateTime.UtcNow - _firstHintUtc >=
                    TimeSpan.FromMilliseconds(SettleWindowMilliseconds))
                    TryCapture();
            }
        }

        internal void FlushPendingForTest()
        {
            TryCapture();
        }

        internal static bool SemanticEquals(
            DisplayTopologySnapshot left, DisplayTopologySnapshot right)
        {
            if (Object.ReferenceEquals(left, right)) return true;
            if (left == null || right == null) return false;
            IReadOnlyList<DisplaySurfaceSnapshot> leftSurfaces =
                left.Surfaces;
            IReadOnlyList<DisplaySurfaceSnapshot> rightSurfaces =
                right.Surfaces;
            if (leftSurfaces.Count != rightSurfaces.Count) return false;
            List<DisplaySurfaceSnapshot> sortedLeft =
                new List<DisplaySurfaceSnapshot>(leftSurfaces);
            List<DisplaySurfaceSnapshot> sortedRight =
                new List<DisplaySurfaceSnapshot>(rightSurfaces);
            sortedLeft.Sort(SurfaceComparison);
            sortedRight.Sort(SurfaceComparison);
            for (int index = 0; index < sortedLeft.Count; index++)
            {
                DisplaySurfaceSnapshot a = sortedLeft[index];
                DisplaySurfaceSnapshot b = sortedRight[index];
                if (!String.Equals(a.RuntimeGdiName, b.RuntimeGdiName,
                    StringComparison.OrdinalIgnoreCase)) return false;
                if (!RectEquals(a.Bounds, b.Bounds)) return false;
                if (!RectEquals(a.WorkArea, b.WorkArea)) return false;
                if (a.IsPrimary != b.IsPrimary) return false;
                if (a.RotationDegrees != b.RotationDegrees) return false;
                if (Math.Abs(a.Scale - b.Scale) > 0.0001) return false;
                if (!TargetsEqual(a.Targets, b.Targets)) return false;
            }
            return true;
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed) return;
                _disposed = true;
                _quietTimer.Stop();
                _settleTimer.Stop();
                _quietTimer.Dispose();
                _settleTimer.Dispose();
            }
        }

        private static int SurfaceComparison(
            DisplaySurfaceSnapshot left, DisplaySurfaceSnapshot right)
        {
            return String.CompareOrdinal(left.RuntimeGdiName,
                right.RuntimeGdiName);
        }

        private static bool TargetsEqual(
            IReadOnlyList<DisplayTargetIdentity> left,
            IReadOnlyList<DisplayTargetIdentity> right)
        {
            if (left.Count != right.Count) return false;
            List<DisplayTargetIdentity> sortedLeft =
                new List<DisplayTargetIdentity>(left);
            List<DisplayTargetIdentity> sortedRight =
                new List<DisplayTargetIdentity>(right);
            sortedLeft.Sort(TargetComparison);
            sortedRight.Sort(TargetComparison);
            for (int index = 0; index < sortedLeft.Count; index++)
            {
                DisplayTargetIdentity a = sortedLeft[index];
                DisplayTargetIdentity b = sortedRight[index];
                if (!String.Equals(a.StableKey, b.StableKey,
                    StringComparison.OrdinalIgnoreCase)) return false;
                if (a.IsDurable != b.IsDurable) return false;
            }
            return true;
        }

        private static int TargetComparison(
            DisplayTargetIdentity left, DisplayTargetIdentity right)
        {
            return String.CompareOrdinal(left.StableKey, right.StableKey);
        }

        private static bool RectEquals(PhysicalRect left, PhysicalRect right)
        {
            return left.Left == right.Left && left.Top == right.Top &&
                left.Width == right.Width && left.Height == right.Height;
        }

        private void ScheduleCaptureRetry()
        {
            lock (_gate)
            {
                if (_disposed || _captureRetriesRemaining == 0) return;
                _captureRetriesRemaining--;
                _capturePending = true;
                _quietTimer.Interval = CaptureRetryDelayMilliseconds;
                _quietTimer.Start();
            }
        }

        private void TryCapture()
        {
            lock (_gate)
            {
                if (_disposed || !_capturePending) return;
                _capturePending = false;
                _quietTimer.Stop();
                _settleTimer.Stop();
                _hintActive = false;
            }
            DisplayTopologySnapshot snapshot = SafeCapture();
            if (snapshot == null) { ScheduleCaptureRetry(); return; }
            string reason;
            DisplayTopologySnapshot owned = null;
            lock (_gate)
            {
                if (_disposed) return;
                _captureRetriesRemaining = 0;
                if (SemanticEquals(Current, snapshot))
                {
                    return;
                }
                Generation = Current == null ? 0 : Generation + 1;
                Current = snapshot.WithGeneration(Generation);
                owned = Current;
                reason = LastReason;
            }
            Raise(reason, owned);
        }

        private DisplayTopologySnapshot SafeCapture()
        {
            try
            {
                lock (_gate) CaptureCount++;
                return _capture();
            }
            catch
            {
                // Retries and diagnostic output are independently bounded;
                // a failed capture never replaces the last valid snapshot.
                lock (_gate)
                {
                    if (_captureFailureTraceCount < CaptureFailureTraceLimit)
                    {
                        _captureFailureTraceCount++;
                        DisplayDiagnostics.Trace("TopologyCaptured",
                            "capture failed (attempt " +
                            _captureFailureTraceCount + ")");
                    }
                }
                return null;
            }
        }

        private void Raise(string reason, DisplayTopologySnapshot snapshot)
        {
            Action<string, DisplayTopologySnapshot> handler;
            lock (_gate) handler = TopologyChanged;
            if (handler == null) return;
            handler(reason, snapshot);
        }
    }
}
