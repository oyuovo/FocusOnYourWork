using System;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;
using FocusOnYourWork.Core;

internal static partial class CoreTests
{
    private static SaveData Sect(int people)
    {
        var save = new SaveData { WalletUnits = 10000 * Rules.UnitsPerMaterial };
        SectGame.ChooseStarter(save, "sword");
        for (int i = 1; i < people; i++) SectGame.Draw(save, "recruit-" + i, i);
        return save;
    }
    private static string Json(SaveData save) { return new JavaScriptSerializer().Serialize(save); }
    private static void Advance(SaveData save, long ms)
    { SectGame.Progress(save.Sect, ms, "2026-10-10T00:00:00Z", "完成一章阅读"); }
    private static void RunSectTests()
    {
        Test("starter is free and cannot be chosen twice", delegate {
            var s = new SaveData(); SectGame.ChooseStarter(s, "snow");
            Check(s.WalletUnits == 0 && s.Sect.Roster.Single().Active, "starter cost or slot");
            Throws<InvalidOperationException>(delegate { SectGame.ChooseStarter(s, "herb"); });
        });
        Test("ascension requires precisely 99 hours and keeps its permanent record", delegate {
            var s = Sect(1); Advance(s, SectGame.AscensionMs - 1);
            Check(!SectGame.ThemeUnlocked(s.Sect, "sword") && s.Sect.Roster[0].Active, "early ascension");
            Advance(s, 1); var c = s.Sect.Roster[0];
            Check(c.CultivationMs == SectGame.AscensionMs && c.AscendedUtc != null && !c.Active && SectGame.ThemeUnlocked(s.Sect, "sword"), "missing ascension");
            string stamp = c.AscendedUtc; Advance(s, SectGame.AscensionMs);
            Check(c.AscendedUtc == stamp && c.CultivationMs == SectGame.AscensionMs && c.LastTask == "完成一章阅读", "record changed");
            SectGame.Validate(s.Sect);
        });
        Test("realm milestones use cumulative effective time", delegate {
            long[] hours = { 1, 6, 15, 30, 48, 66, 84, 99 };
            string[] names = { "筑基", "金丹", "元婴", "化神", "炼虚", "合体", "大乘", "飞升" };
            for (int i = 0; i < hours.Length; i++) {
                long ms = hours[i] * 3600000;
                Check(SectGame.NextRealmMs(ms - 1) == ms && SectGame.Realm(ms) == names[i] && SectGame.Realm(ms - 1) != names[i], "realm boundary");
            }
        });
        Test("three slots do not shorten an individual 99 hour threshold", delegate {
            var s = Sect(3); SectGame.UpgradeHousing(s); SectGame.UpgradeHousing(s);
            Advance(s, SectGame.AscensionMs - 1);
            Check(s.Sect.Roster.All(x => x.CultivationMs == SectGame.AscensionMs - 1 && x.AscendedUtc == null), "parallel speedup");
            Advance(s, 1); Check(s.Sect.Roster.All(x => x.AscendedUtc != null), "parallel finish"); SectGame.Validate(s.Sect);
        });
        Test("auto replacement only receives interval after predecessor ascends", delegate {
            var s = Sect(3); s.Sect.Roster[0].CultivationMs = SectGame.AscensionMs - 1000;
            SectGame.PrioritizeWaiting(s, "star"); Advance(s, 2500);
            Check(s.Sect.Roster.Single(x => x.Id == "star").CultivationMs == 1500, "replacement got whole interval");
            Check(s.Sect.Roster.Single(x => x.Id == "herb").CultivationMs == 0, "waiting grew"); SectGame.Validate(s.Sect);
        });
        Test("simultaneous ascensions fill two slots without double credit", delegate {
            var s = Sect(4); SectGame.UpgradeHousing(s);
            foreach (var c in s.Sect.Roster.Where(x => x.Active)) c.CultivationMs = SectGame.AscensionMs - 500;
            Advance(s, 1500);
            Check(s.Sect.Roster.Count(x => x.AscendedUtc != null) == 2 && s.Sect.Roster.Where(x => x.Active).All(x => x.CultivationMs == 1000), "slot redistribution");
            SectGame.Validate(s.Sect);
        });
        Test("upgrades spend fixed prices and never add cultivation time", delegate {
            var s = Sect(3); long before = s.WalletUnits;
            SectGame.UpgradeHousing(s); SectGame.UpgradeHousing(s); SectGame.UpgradeMountain(s); SectGame.UpgradeMountain(s);
            Check(s.WalletUnits == before - 1980 * Rules.UnitsPerMaterial && s.Sect.Roster.All(x => x.Active && x.CultivationMs == 0), "upgrade balance");
            Throws<InvalidOperationException>(delegate { SectGame.UpgradeHousing(s); });
            Throws<InvalidOperationException>(delegate { SectGame.UpgradeMountain(s); });
        });
        Test("insufficient materials do not change buildings or pity", delegate {
            var s = Sect(1); s.WalletUnits = SectGame.DrawCost - 1; string before = Json(s);
            Throws<InvalidOperationException>(delegate { SectGame.Draw(s, "poor", 1); });
            Throws<InvalidOperationException>(delegate { SectGame.UpgradeHousing(s); });
            Throws<InvalidOperationException>(delegate { SectGame.UpgradeMountain(s); });
            Check(Json(s) == before, "failed spend mutated save");
        });
        Test("worst case recruitment completes six people within 25 paid draws", delegate {
            var s = Sect(1); long start = s.WalletUnits;
            for (int person = 1; person < 6; person++) {
                for (int i = 0; i < 4; i++) Check(SectGame.Draw(s, person + "-duplicate-" + i, 0).Duplicate, "duplicate receipt");
                string id = person + "-pity";
                var r = SectGame.Draw(s, id, 0); Check(r.Choice && r.CharacterId == null, "fifth draw not selectable");
                SectGame.Validate(s.Sect);
                SectGame.ChoosePity(s, id, SectGame.Characters[person].Id); SectGame.Validate(s.Sect);
            }
            Check(s.Sect.Roster.Count == 6 && s.Sect.Draws.Count == 25 && start - s.WalletUnits == 2760 * Rules.UnitsPerMaterial, "guarantee or refund budget");
            string before = Json(s); Throws<InvalidOperationException>(delegate { SectGame.Draw(s, "full", 1); });
            Check(Json(s) == before, "full pool spent materials");
        });
        Test("natural new character resets pity and same request cannot charge twice", delegate {
            var s = Sect(1); SectGame.Draw(s, "dup", 0); SectGame.Draw(s, "new", 1);
            long balance = s.WalletUnits; SectGame.Draw(s, "new", 2);
            Check(s.Sect.DuplicateStreak == 0 && s.Sect.Roster.Count == 2 && s.WalletUnits == balance, "reset or idempotence");
        });
        Test("paid pity survives restart and selection is idempotent", delegate {
            var s = Sect(1); var store = Store();
            for (int i = 0; i < 4; i++) SectGame.Draw(s, "dup" + i, 0);
            SaveStore.Update(s, x => SectGame.Draw(x, "pity", 3), store.Save);
            s = store.Load(); long balance = s.WalletUnits;
            Throws<InvalidOperationException>(delegate { SectGame.Draw(s, "blocked", 2); });
            SaveStore.Update(s, x => SectGame.ChoosePity(x, "pity", "herb"), store.Save);
            SaveStore.Update(s, x => SectGame.ChoosePity(x, "pity", "herb"), store.Save);
            Check(s.WalletUnits == balance && s.Sect.Roster.Count == 2 && store.Load().Sect.PendingDrawId == null, "pity replay");
            Throws<InvalidOperationException>(delegate { SectGame.ChoosePity(s, "pity", "star"); });
        });
        Test("failed pity persistence preserves paid choice for retry", delegate {
            var s = Sect(1);
            for (int i = 0; i < 5; i++) SectGame.Draw(s, "d" + i, 0);
            string before = Json(s);
            Throws<IOException>(delegate { SaveStore.Update(s, x => SectGame.ChoosePity(x, "d4", "snow"), delegate { throw new IOException("disk full"); }); });
            Check(Json(s) == before && s.Sect.PendingDrawId == "d4", "lost paid choice");
        });
        Test("failed recruitment rolls back funds receipts and people together", delegate {
            var s = Sect(1); string before = Json(s);
            Throws<IOException>(delegate { SaveStore.Update(s, x => SectGame.Draw(x, "fail", 1), delegate { throw new IOException(); }); });
            Check(Json(s) == before, "partial recruitment");
        });
        Test("ascended character remains owned when drawn again", delegate {
            var s = Sect(1); Advance(s, SectGame.AscensionMs); long balance = s.WalletUnits;
            Check(SectGame.Draw(s, "return", 0).Duplicate && s.Sect.Roster.Count == 1 && s.WalletUnits == balance - SectGame.DrawCost + SectGame.DuplicateRefund, "ascended rejoined");
        });
        Test("challenge rewards materials but never accelerates cultivation", delegate {
            var s = Sect(1); var engine = Session(true); engine.Tick(0, Presence.Allowed); Run(engine, 300000, Presence.Allowed);
            long balance = s.WalletUnits; Game.Settle(s, engine.Data, false);
            Check(s.WalletUnits - balance == 360000 && s.Sect.Roster[0].CultivationMs == 300000 && s.TotalEffectiveMs == 300000, "challenge speedup");
        });
        Test("away unknown paused and missed samples cannot grow characters", delegate {
            var s = Sect(1); var e = Session(false); e.Tick(0, Presence.Allowed); Run(e, 1000, Presence.Allowed);
            Run(e, 10000, Presence.Away); Run(e, 5000, Presence.Unknown); e.Tick(3600000, Presence.Allowed);
            e.Pause(); Run(e, 5000, Presence.Allowed); e.Resume(); e.Tick(0, Presence.Allowed); Run(e, 1000, Presence.Allowed);
            Game.Settle(s, e.Data, false); Check(s.Sect.Roster[0].CultivationMs == 2000, "unproven time credited");
        });
        Test("ascension settlement failure preserves checkpoint and retries once", delegate {
            var s = Sect(2); s.Sect.Roster[0].CultivationMs = SectGame.AscensionMs - 500;
            var e = Session(true); e.Tick(0, Presence.Allowed); Run(e, 1500, Presence.Allowed); s.Pending = e.Data;
            string before = Json(s); var store = Store(); store.Save(s);
            Throws<IOException>(delegate { SaveStore.Update(s, x => Game.Settle(x, e.Data, false), delegate { throw new IOException(); }); });
            Check(Json(s) == before && store.Load().Sect.Roster[0].AscendedUtc == null, "half committed ascension");
            SaveStore.Update(s, x => Game.Settle(x, e.Data, false), store.Save);
            SaveStore.Update(s, x => Game.Settle(x, e.Data, false), store.Save);
            var loaded = store.Load(); Check(loaded.Pending == null && loaded.History.Count == 1 && loaded.Sect.Roster[1].CultivationMs == 1000 && SectGame.ThemeUnlocked(loaded.Sect, "sword"), "retry duplicated award");
        });
        Test("stale checkpoint already in history is cleared without credit", delegate {
            var s = Sect(1); var e = Session(false); e.Data.EffectiveMs = 1000;
            Game.Settle(s, e.Data, false); s.Pending = e.Data; long balance = s.WalletUnits;
            Check(Game.Settle(s, e.Data, true) == null && s.Pending == null && s.WalletUnits == balance && s.Sect.Roster[0].CultivationMs == 1000, "stale checkpoint");
        });
        Test("recovered session grows only durable base time without offline catchup", delegate {
            var s = Sect(1); var e = Session(true); e.Data.EffectiveMs = 300000; s.Pending = e.Data;
            var store = Store(); store.Save(s); s = store.Load(); long balance = s.WalletUnits;
            SaveStore.Update(s, x => Game.Settle(x, x.Pending, true), store.Save);
            Check(s.WalletUnits - balance == 300000 && s.Sect.Roster[0].CultivationMs == 300000 && s.History[0].Recovered, "recovery grew extra");
        });
        Test("scene and UI unlock independently from spent materials", delegate {
            var s = Sect(2); Throws<InvalidOperationException>(delegate { SectGame.SetTheme(s, "herb", false); });
            Advance(s, SectGame.AscensionMs); SectGame.SetTheme(s, "sword", true);
            Check(s.Sect.SceneTheme == "default" && s.Sect.UiTheme == "sword", "themes coupled");
            SectGame.SetTheme(s, "sword", false); SectGame.SetTheme(s, "default", true);
            Check(s.Sect.SceneTheme == "sword", "scene reset by UI");
        });
        Test("memorials require unlocked character and mountain capacity", delegate {
            var s = Sect(3); Advance(s, SectGame.AscensionMs * 3);
            Throws<InvalidOperationException>(delegate { SectGame.ToggleMemorial(s, "sword"); });
            SectGame.UpgradeMountain(s); SectGame.ToggleMemorial(s, "sword");
            Throws<InvalidOperationException>(delegate { SectGame.ToggleMemorial(s, "herb"); });
            SectGame.UpgradeMountain(s); SectGame.ToggleMemorial(s, "herb"); SectGame.ToggleMemorial(s, "sword");
            Check(s.Sect.Memorials.SequenceEqual(new [] { "herb" }) && SectGame.ThemeUnlocked(s.Sect, "sword"), "removal revoked reward");
        });
        Test("sect management is blocked until current focus settles", delegate {
            var s = Sect(2); s.Pending = Session(false).Data;
            Throws<InvalidOperationException>(delegate { SectGame.Draw(s, "blocked", 2); });
            Throws<InvalidOperationException>(delegate { SectGame.UpgradeHousing(s); });
            Throws<InvalidOperationException>(delegate { SectGame.SetTheme(s, "default", false); });
            Throws<InvalidOperationException>(delegate { SectGame.PrioritizeWaiting(s, "herb"); });
        });
        Test("v1 imports work but removes retired island data on save", delegate {
            var old = new SaveData { WalletUnits = 1234567, TotalEffectiveMs = 999999999, SectName = "云隐宗" };
            var e = Session(true); e.Data.EffectiveMs = 12345; old.Pending = e.Data;
            string original = LegacyJson(old, 1); string dir = Path.Combine(testDir, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "island.json"), original);
            var store = new SaveStore(dir); var migrated = store.Load();
            Check(migrated.Version == 3 && migrated.Sect.Roster.Count == 0 && migrated.WalletUnits == old.WalletUnits && migrated.TotalEffectiveMs == old.TotalEffectiveMs && migrated.SectName == "云隐宗", "migration lost work");
            SaveStore.Update(migrated, x => Game.Settle(x, x.Pending, true), store.Save);
            Check(File.ReadAllText(Path.Combine(dir, "island.json.bak")) == original, "atomic backup missing");
            Check(!Json(migrated).Contains("Buildings") && !Json(migrated).Contains("IslandName") && !Json(migrated).Contains("\"Theme\":"), "retired fields remain");
            SectGame.ChooseStarter(migrated, "herb"); store.Save(migrated);
            Check(store.Load().Sect.Roster[0].CultivationMs == 0, "retroactive XP");
        });
        Test("v2 imports cultivation draws pending choice skins and work unchanged", delegate {
            var s = Sect(1); for (int i = 0; i < 4; i++) SectGame.Draw(s, "old-duplicate-" + i, 0);
            SectGame.Draw(s, "old-choice", 0); Advance(s, SectGame.AscensionMs);
            SectGame.SetTheme(s, "sword", true); s.Pending = Session(false).Data;
            s.History.Add(new SessionRecord { Id = "past", EffectiveMs = 300000, RewardUnits = 300000 });
            var migrated = SaveStore.Parse(LegacyJson(s, 2));
            Check(Json(migrated) == Json(s), "cultivation or work changed during import");
        });
        Test("legacy island fields are discarded without validating retired rules", delegate {
            var s = SaveStore.Parse(LegacyJson(new SaveData { SectName = "我的专注岛" }, 1).Replace("\"Buildings\":[]", "\"Buildings\":\"unused\""));
            Check(s.SectName == "我的宗门" && s.Version == 3, "default name not migrated");
        });
        Test("current save requires its sect name and versioned progress", delegate {
            Throws<InvalidOperationException>(delegate { SaveStore.Parse(Json(new SaveData()).Replace("\"SectName\":", "\"MissingName\":")); });
            var json = Json(Sect(1));
            Throws<InvalidOperationException>(delegate { SaveStore.Parse(json.Replace("\"Sect\":", "\"MissingSect\":")); });
            Throws<InvalidOperationException>(delegate { SaveStore.Parse(LegacyJson(Sect(1), 2).Replace("\"Sect\":", "\"MissingSect\":")); });
        });
        Test("missing progress cannot silently reset a character", delegate {
            var s = Sect(1); Advance(s, 1234);
            Throws<InvalidOperationException>(delegate { SaveStore.Parse(Json(s).Replace("\"CultivationMs\":1234,", "")); });
            Throws<InvalidOperationException>(delegate { SaveStore.Parse(Json(s).Replace("\"Roster\":", "\"MissingRoster\":")); });
        });
        Test("receipt inconsistency and corrupt pity counter are rejected", delegate {
            var s = Sect(1); SectGame.Draw(s, "duplicate", 0); s.Sect.DuplicateStreak = 0;
            Throws<InvalidOperationException>(delegate { SaveStore.Parse(Json(s)); });
            s.Sect.DuplicateStreak = 1; s.Sect.Draws[0].Duplicate = false;
            Throws<InvalidOperationException>(delegate { SaveStore.Parse(Json(s)); });
        });
        Test("future schema and invalid ascension never overwrite current save", delegate {
            var s = Sect(1); s.Sect.Roster[0].CultivationMs = SectGame.AscensionMs;
            Throws<InvalidOperationException>(delegate { SaveStore.Parse(Json(s)); });
            s.Version = 4; Throws<NotSupportedException>(delegate { SaveStore.Parse(Json(s)); });
        });
    }

    private static string LegacyJson(SaveData data, int version)
    {
        var serializer = new JavaScriptSerializer();
        var raw = (System.Collections.Generic.Dictionary<string, object>)serializer.DeserializeObject(Json(data));
        raw["Version"] = version; raw["IslandName"] = raw["SectName"]; raw.Remove("SectName");
        raw["Theme"] = "sage"; raw["Buildings"] = new object[0];
        if (version == 1) raw.Remove("Sect");
        return serializer.Serialize(raw);
    }
}
