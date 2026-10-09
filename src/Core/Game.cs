using System;
using System.Linq;

namespace FocusIsland.Core
{
    public static class Game
    {
        public static SessionRecord Settle(SaveData save, SessionData session, bool recovered)
        {
            if (session == null || save.History.Any(x => x.Id == session.Id)) return null;
            bool won = !recovered && session.Challenge && session.BonusEligible && session.EffectiveMs >= session.TargetMs;
            long time = Math.Max(0, Math.Min(session.EffectiveMs, session.TargetMs));
            long reward = time + (won ? time / 5 : 0);
            SessionRecord record = new SessionRecord {
                Id = session.Id, Task = session.Task, StartedUtc = session.StartedUtc, EndedUtc = DateTime.UtcNow.ToString("o"),
                EffectiveMs = time, RewardUnits = reward, Distractions = session.Distractions, ChallengeWon = won, Recovered = recovered
            };
            save.WalletUnits = checked(save.WalletUnits + reward);
            save.TotalEffectiveMs = checked(save.TotalEffectiveMs + time);
            save.History.Add(record);
            if (save.Pending != null && save.Pending.Id == session.Id) save.Pending = null;
            return record;
        }

        public static void Build(SaveData save, int plot, string kind, string color)
        {
            if (save.Pending != null) throw new InvalidOperationException("请先结束专注，再建设小岛。");
            if (plot < 0 || plot >= Rules.PlotCount) throw new ArgumentException("请选择岛上的地块。");
            if (save.Buildings.Any(x => x.Plot == plot)) throw new InvalidOperationException("这个位置已有建筑，可免费换色或移动。");
            if (color != "sage" && color != "clay") throw new ArgumentException("请选择有效配色。");
            long price = Rules.Price(kind);
            if (save.WalletUnits < price) throw new InvalidOperationException("材料还差 " + Rules.Materials(price - save.WalletUnits) + "，下次专注继续积累。");
            save.WalletUnits -= price;
            save.Buildings.Add(new Building { Plot = plot, Kind = kind, Color = color });
        }

        public static void Move(SaveData save, int from, int to)
        {
            if (save.Pending != null) throw new InvalidOperationException("请先结束专注，再移动建筑。");
            Building source = save.Buildings.FirstOrDefault(x => x.Plot == from);
            if (source == null || to < 0 || to >= Rules.PlotCount) throw new ArgumentException("请选择有效建筑与位置。");
            if (save.Buildings.Any(x => x.Plot == to)) throw new InvalidOperationException("请选择一个空位置。");
            source.Plot = to;
        }
    }
}
