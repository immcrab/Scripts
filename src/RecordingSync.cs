using System;
using System.Runtime.CompilerServices;
using Liv.Lck;
using UnityEngine;

namespace Scripts
{
    /// <summary>
    /// Optional sync with the game's built-in LIV LCK camera (the in-game recording/streaming camera).
    /// LckService exposes OnRecordingStarted/Paused/Resumed/Stopped events; when SyncWithRecording is on,
    /// starting a recording begins the script from the top, pausing/stopping the recording pauses it.
    ///
    /// Only reads events; never starts, stops or configures recordings. Events are queued and applied on the
    /// main thread. All LCK types are touched inside NoInlining methods so a missing/changed LCK assembly
    /// only disables sync instead of breaking the mod.
    /// </summary>
    internal sealed class RecordingSync : IDisposable
    {
        private enum Pending { None, Started, Paused, Resumed, Stopped }

        public string StatusText { get; private set; } = "LCK camera: searching...";
        public bool Available { get; private set; }

        private object _service;
        private float _nextPoll;
        private bool _broken;
        private volatile int _pending;

        public void Tick()
        {
            if (!_broken && Time.unscaledTime >= _nextPoll)
            {
                _nextPoll = Time.unscaledTime + 2f;
                try { Poll(); }
                catch (Exception e)
                {
                    _broken = true;
                    Available = false;
                    StatusText = "LCK camera not supported in this version";
                    Plugin.Log.LogWarning("Recording sync disabled: " + e.Message);
                }
            }

            var p = (Pending)System.Threading.Interlocked.Exchange(ref _pending, 0);
            if (p == Pending.None || !Plugin.SyncWithRecording.Value || Plugin.Prompter == null) return;

            switch (p)
            {
                case Pending.Started: Plugin.Prompter.StartTake(); break;
                case Pending.Resumed: Plugin.Prompter.Play(); break;
                case Pending.Paused:
                case Pending.Stopped: Plugin.Prompter.Pause(); break;
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void Poll()
        {
            LckResult<LckService> r = LckService.GetService();
            LckService svc = r != null && r.Success ? r.Result : null;
            if (ReferenceEquals(svc, _service)) return;

            Unsubscribe();
            _service = svc;
            Available = svc != null;
            if (svc == null)
            {
                StatusText = "LCK camera: not active yet";
                return;
            }

            svc.OnRecordingStarted += OnStarted;
            svc.OnRecordingPaused += OnPaused;
            svc.OnRecordingResumed += OnResumed;
            svc.OnRecordingStopped += OnStopped;
            StatusText = "Built-in LCK camera: ready";
            Plugin.Log.LogInfo("Recording sync: subscribed to the LCK camera service.");
        }

        private void OnStarted(LckResult r) { if (r == null || r.Success) _pending = (int)Pending.Started; }
        private void OnPaused(LckResult r) { if (r == null || r.Success) _pending = (int)Pending.Paused; }
        private void OnResumed(LckResult r) { if (r == null || r.Success) _pending = (int)Pending.Resumed; }
        private void OnStopped(LckResult r) { if (r == null || r.Success) _pending = (int)Pending.Stopped; }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void Unsubscribe()
        {
            if (_service is LckService svc)
            {
                svc.OnRecordingStarted -= OnStarted;
                svc.OnRecordingPaused -= OnPaused;
                svc.OnRecordingResumed -= OnResumed;
                svc.OnRecordingStopped -= OnStopped;
            }
            _service = null;
        }

        public void Dispose()
        {
            try { if (!_broken) Unsubscribe(); }
            catch { /* shutting down */ }
        }
    }
}
