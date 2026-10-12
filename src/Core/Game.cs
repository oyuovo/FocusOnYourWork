using System;
using System.Linq;

namespace FocusOnYourWork.Core
{
    public static class Game
    {
        public static SessionRecord Settle(SaveData save, SessionData session, bool recovered)
        {
            if (session == null) return null;
            if (save.History.Any(x => x.Id == session.Id)) {
                if (save.Pending != null && save.Pending.Id == session.Id) save.Pending = null;
                return null;
            }
            bool won = !recovered && session.Challenge && session.BonusEligible && session.EffectiveMs >= session.TargetMs;
            long time = Math.Max(0, Math.Min(session.EffectiveMs, session.TargetMs));
            long reward = time + (won ? time / 5 : 0);
            SessionRecord record = new SessionRecord {
                Id = session.Id, Task = session.Task, StartedUtc = session.StartedUtc, EndedUtc = DateTime.UtcNow.ToString("o"),
                EffectiveMs = time, RewardUnits = reward, Distractions = session.Distractions, ChallengeWon = won, Recovered = recovered
            };
            save.WalletUnits = checked(save.WalletUnits + reward);
            save.TotalEffectiveMs = checked(save.TotalEffectiveMs + time);
            SectGame.Progress(save.Sect, time, record.EndedUtc, session.Task);
            save.History.Add(record);
            if (save.Pending != null && save.Pending.Id == session.Id) save.Pending = null;
            return record;
        }

    }
}
