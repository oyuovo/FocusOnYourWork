using System;
using System.Threading;

namespace FocusOnYourWork.Desktop
{
    // A named event sleeps in the thread pool until another launch asks to show the window.
    public sealed class SingleInstance : IDisposable
    {
        private readonly Mutex mutex;
        private readonly string eventName;
        private EventWaitHandle activation;
        private RegisteredWaitHandle registration;
        public bool IsPrimary { get; private set; }

        public SingleInstance(string identity)
        {
            // Keep the existing mutex identity to prevent two versions writing the same save.
            mutex = new Mutex(false, "Local\\FocusIsland.v1." + identity);
            eventName = "Local\\FocusOnYourWork.Activate." + identity;
            try { IsPrimary = mutex.WaitOne(0); }
            catch (AbandonedMutexException) { IsPrimary = true; }
            if (IsPrimary) activation = new EventWaitHandle(false, EventResetMode.AutoReset, eventName);
        }

        public void Listen(Action show)
        {
            if (!IsPrimary || registration != null) throw new InvalidOperationException("Activation listener already started or not owned.");
            registration = ThreadPool.RegisterWaitForSingleObject(activation, delegate { show(); }, null, Timeout.Infinite, false);
        }

        public bool ActivateExisting()
        {
            for (int attempt = 0; attempt < 20; attempt++) {
                EventWaitHandle other;
                if (EventWaitHandle.TryOpenExisting(eventName, out other)) {
                    using (other) { other.Set(); } return true;
                }
                Thread.Sleep(100);
            }
            return false;
        }

        public void Dispose()
        {
            if (registration != null) registration.Unregister(null);
            if (activation != null) activation.Dispose();
            if (IsPrimary) mutex.ReleaseMutex();
            mutex.Dispose();
        }
    }
}
