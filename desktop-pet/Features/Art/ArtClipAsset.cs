using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace PennyPet
{
    // One actual clip, shared by all row aliases. Decoding and bitmap disposal
    // happen outside the gate; the gate protects task identity and publication.
    internal sealed class ArtClipAsset : IDisposable
    {
        private readonly object _gate = new object();
        private readonly Func<AnimationClip> _decode;
        private readonly Func<DateTime> _utcNow;
        private AnimationClip _ready;
        private TaskCompletionSource<AnimationClip> _completion;
        private int _attempts;
        private DateTime _retryAfterUtc;
        private bool _disposed;

        internal ArtClipAsset(Func<AnimationClip> decode, AnimationClip ready = null,
            Func<DateTime> utcNow = null)
        {
            _decode = decode;
            _utcNow = utcNow ?? (() => DateTime.UtcNow);
            _ready = ready;
            if (ready != null)
            {
                _completion = NewCompletion();
                _completion.SetResult(ready);
            }
        }

        internal AnimationClip Ready { get { return Volatile.Read(ref _ready); } }

        internal Task<AnimationClip> LoadAsync()
        {
            lock (_gate)
            {
                if (_disposed)
                {
                    if (_completion == null || !_completion.Task.IsCanceled)
                    {
                        _completion = NewCompletion();
                        _completion.SetCanceled();
                    }
                    return _completion.Task;
                }
                if (_completion != null && (!_completion.Task.IsFaulted ||
                    _attempts >= 2 || _utcNow() < _retryAfterUtc))
                    return _completion.Task;
                _completion = NewCompletion();
                var completion = _completion;
                _attempts++;
                ThreadPool.QueueUserWorkItem(_ => Decode(completion));
                return completion.Task;
            }
        }

        private static TaskCompletionSource<AnimationClip> NewCompletion()
        {
            var completion = new TaskCompletionSource<AnimationClip>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            // Interactive requests are fire-and-forget, while reminders await
            // the same task. Observe faults without changing either result.
            completion.Task.ContinueWith(task => { var observed = task.Exception; },
                CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted |
                TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            return completion;
        }

        private void Decode(TaskCompletionSource<AnimationClip> completion)
        {
            AnimationClip clip = null;
            try
            {
                clip = _decode();
                if (clip == null || clip.FrameCount == 0)
                    throw new InvalidDataException("美术状态没有可播放帧。");
                lock (_gate)
                {
                    if (_disposed) return;
                    Volatile.Write(ref _ready, clip);
                    completion.TrySetResult(clip);
                    clip = null; // Ownership has transferred to the asset.
                }
            }
            catch (Exception error)
            {
                lock (_gate)
                {
                    _retryAfterUtc = _utcNow().AddSeconds(1);
                    completion.TrySetException(error);
                }
            }
            finally
            {
                if (clip != null) clip.Dispose();
            }
        }

        public void Dispose()
        {
            AnimationClip clip;
            lock (_gate)
            {
                if (_disposed) return;
                _disposed = true;
                clip = Interlocked.Exchange(ref _ready, null);
                if (_completion != null) _completion.TrySetCanceled();
            }
            if (clip != null) clip.Dispose();
        }
    }
}
