using System;
using System.Threading;

namespace SpigenAudioCTRL.Infrastructure
{
    // Keeps one instance per user; a second launch asks the first to show its window.
    public sealed class SingleInstance : IDisposable
    {
        private const string MutexName = @"Local\SpigenAudioCTRL.Instance";
        private const string SignalName = @"Local\SpigenAudioCTRL.Activate";

        private readonly Mutex _mutex;
        private readonly EventWaitHandle _signal;
        private RegisteredWaitHandle? _registration;

        public bool IsFirst { get; }

        public SingleInstance()
        {
            _mutex = new Mutex(initiallyOwned: true, MutexName, out bool createdNew);
            _signal = new EventWaitHandle(false, EventResetMode.AutoReset, SignalName);
            IsFirst = createdNew;
        }

        public void ActivateFirstInstance() => _signal.Set();

        // The callback runs on a thread-pool thread.
        public void OnActivationRequested(Action callback)
        {
            _registration = ThreadPool.RegisterWaitForSingleObject(
                _signal, (_, _) => callback(), null, Timeout.Infinite, executeOnlyOnce: false);
        }

        public void Dispose()
        {
            _registration?.Unregister(null);
            if (IsFirst) _mutex.ReleaseMutex();
            _mutex.Dispose();
            _signal.Dispose();
        }
    }
}
