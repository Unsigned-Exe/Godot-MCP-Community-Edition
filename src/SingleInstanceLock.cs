#nullable enable
using System;
using System.Threading;

namespace MCP_Godot4
{
    /// <summary>
    /// Enforces strict single-instance lifecycle for Godot MCP Hub.
    /// Uses per-user kernel mutexes and show signaling to prevent duplicate tray agents or windows.
    /// </summary>
    public sealed class SingleInstanceLock : IDisposable
    {
        private Mutex? _mutex;
        private EventWaitHandle? _showEvent;
        private RegisteredWaitHandle? _waitHandleRegistration;
        private readonly bool _hasOwnership;

        public bool IsAcquired => _hasOwnership;

        private SingleInstanceLock(Mutex mutex, EventWaitHandle? showEvent, RegisteredWaitHandle? waitReg, bool hasOwnership)
        {
            _mutex = mutex;
            _showEvent = showEvent;
            _waitHandleRegistration = waitReg;
            _hasOwnership = hasOwnership;
        }

        public static SingleInstanceLock? TryAcquire(string appKey, bool isSecondarySilent = false, Action? onShowRequested = null)
        {
            string mutexName = $"Local\\{appKey}-Instance-{Environment.UserName}";
            string eventName = $"Local\\{appKey}-ShowEvent-{Environment.UserName}";

            Mutex? mutex = null;
            bool createdNew;

            try
            {
                mutex = new Mutex(true, mutexName, out createdNew);
            }
            catch (AbandonedMutexException)
            {
                createdNew = true;
            }

            if (!createdNew)
            {
                if (!isSecondarySilent)
                {
                    try
                    {
                        if (EventWaitHandle.TryOpenExisting(eventName, out var existingEvent))
                        {
                            existingEvent.Set();
                            existingEvent.Dispose();
                        }
                    }
                    catch { }
                }

                mutex?.Dispose();
                return null;
            }

            EventWaitHandle? showEvent = null;
            RegisteredWaitHandle? waitReg = null;

            try
            {
                showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, eventName);

                if (onShowRequested != null)
                {
                    waitReg = ThreadPool.RegisterWaitForSingleObject(showEvent, (state, timedOut) =>
                    {
                        try
                        {
                            onShowRequested();
                        }
                        catch { }
                    }, null, -1, false);
                }
            }
            catch { }

            return new SingleInstanceLock(mutex!, showEvent, waitReg, true);
        }

        public void Dispose()
        {
            try
            {
                _waitHandleRegistration?.Unregister(null);
                _showEvent?.Dispose();

                if (_hasOwnership && _mutex != null)
                {
                    try
                    {
                        _mutex.ReleaseMutex();
                    }
                    catch { }
                    _mutex.Dispose();
                }
            }
            catch { }
        }
    }
}
