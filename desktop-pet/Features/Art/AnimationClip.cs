using System;
using System.Drawing;

namespace PennyPet
{
    internal sealed class AnimationClip : IDisposable
    {
        internal AnimationClip(string source, Bitmap[] frames, int[] durations)
        {
            Source = source;
            Frames = frames;
            Durations = durations;
        }

        internal readonly string Source;
        internal readonly Bitmap[] Frames;
        internal readonly int[] Durations;

        internal int FrameCount
        {
            get { return Frames == null ? 0 : Frames.Length; }
        }

        internal int FrameDuration(int frame)
        {
            if (Durations == null || Durations.Length == 0) return 40;
            int index = Math.Max(0, frame) % Durations.Length;
            return Durations[index];
        }

        public void Dispose()
        {
            if (Frames == null) return;
            foreach (Bitmap frame in Frames)
                if (frame != null) frame.Dispose();
        }
    }
}
