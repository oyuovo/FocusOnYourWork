using System;

namespace FocusIsland.Core
{
    // Receives monotonic elapsed intervals. Neither wall-clock jumps nor missed callbacks earn time.
    public sealed class SessionEngine
    {
        public SessionData Data { get; private set; }
        public Presence Current { get; private set; }
        public bool Complete { get { return Data.EffectiveMs >= Data.TargetMs; } }
        public bool Settled { get; set; }
        private long awayMs;
        private bool awayCounted;

        public SessionEngine(string task, int minutes, bool challenge)
        {
            if (String.IsNullOrWhiteSpace(task) || task.Trim().Length > 100) throw new ArgumentException("任务名称需要 1–100 个字符。");
            if (minutes < 5 || minutes > 120) throw new ArgumentException("专注时长为 5–120 分钟。");
            Data = new SessionData {
                Id = Guid.NewGuid().ToString("N"), Task = task.Trim(), StartedUtc = DateTime.UtcNow.ToString("o"),
                TargetMs = minutes * 60000L, Challenge = challenge, BonusEligible = challenge
            };
            Current = Presence.Unknown;
        }

        public void Tick(long elapsedMs, Presence next)
        {
            if (Settled || Complete || Data.Paused) { Current = Presence.Unknown; return; }
            if (elapsedMs < 0 || elapsedMs > Rules.MaxSampleMs)
            {
                Interrupt(); Current = Presence.Unknown; return;
            }
            if (next == Presence.Unknown)
            {
                // Waiting for the very first valid observation is not a break in a streak.
                if (Data.EffectiveMs > 0 || Current != Presence.Unknown) Interrupt();
                Current = next; return;
            }
            // The entire interval must have allowed endpoints. Never credit a stale observation.
            if (next == Presence.Allowed && Current == Presence.Allowed)
                Data.EffectiveMs += Math.Min(elapsedMs, Data.TargetMs - Data.EffectiveMs);

            if (Current == Presence.Away)
            {
                awayMs += elapsedMs;
                if (awayMs > Rules.GraceMs && !awayCounted)
                {
                    Data.Distractions++;
                    Data.BonusEligible = false;
                    awayCounted = true;
                }
            }
            if (next != Presence.Away) { awayMs = 0; awayCounted = false; }
            Current = next;
        }

        public void Pause() { Data.Paused = true; Interrupt(); }
        public void Resume() { Data.Paused = false; Current = Presence.Unknown; awayMs = 0; awayCounted = false; }
        public void Interrupt()
        {
            Data.BonusEligible = false;
            Current = Presence.Unknown;
            awayMs = 0;
            awayCounted = false;
        }
    }
}
