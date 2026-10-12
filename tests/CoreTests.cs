using System;
using System.IO;
using System.Linq;
using FocusOnYourWork.Core;

internal static partial class CoreTests
{
    private static int passed;
    private static int failed;
    private static string testDir;
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void Test(string name, Action body)
    {
        try { body(); passed++; Console.WriteLine("PASS " + name); }
        catch (Exception ex) { failed++; Console.WriteLine("FAIL " + name + ": " + ex.Message); }
    }
    private static void Throws<T>(Action body) where T : Exception
    {
        try { body(); } catch (T) { return; }
        throw new Exception("Expected " + typeof(T).Name);
    }
    private static SessionEngine Session(bool challenge) { return new SessionEngine("Read a chapter", 5, challenge); }
    private static void Run(SessionEngine session, int milliseconds, Presence presence)
    {
        while (milliseconds > 0) { int step = Math.Min(milliseconds, 500); session.Tick(step, presence); milliseconds -= step; }
    }
    private static BrowserMessage Page(string url, int tab, bool focused)
    {
        return new BrowserMessage { Type = "status", Instance = "edge-instance", Url = url, TabId = tab, Focused = focused };
    }
    private static SaveStore Store()
    {
        return new SaveStore(Path.Combine(testDir, Guid.NewGuid().ToString("N")));
    }

    public static int Main(string[] args)
    {
        testDir = args.Length > 0 ? args[0] : Path.Combine(Path.GetTempPath(), "FocusOnYourWork.Tests");
        Test("allowed endpoints earn exact elapsed time", delegate {
            var s = Session(false); s.Tick(0, Presence.Allowed); Run(s, 1500, Presence.Allowed);
            Check(s.Data.EffectiveMs == 1500, "timing mismatch");
        });
        Test("away and transition intervals never earn", delegate {
            var s = Session(false); s.Tick(0, Presence.Allowed); s.Tick(500, Presence.Away); Run(s, 20000, Presence.Away);
            s.Tick(500, Presence.Allowed); Check(s.Data.EffectiveMs == 0, "away credited");
            s.Tick(500, Presence.Allowed); Check(s.Data.EffectiveMs == 500, "return failed");
        });
        Test("10 second grace preserves challenge", delegate {
            var s = Session(true); s.Tick(0, Presence.Away); Run(s, 9500, Presence.Away); s.Tick(500, Presence.Allowed);
            Check(s.Data.BonusEligible && s.Data.Distractions == 0, "boundary wrong");
        });
        Test("beyond grace loses bonus once per departure", delegate {
            var s = Session(true); s.Tick(0, Presence.Away); Run(s, 12000, Presence.Away);
            Check(!s.Data.BonusEligible && s.Data.Distractions == 1, "not counted once");
            Run(s, 20000, Presence.Away); Check(s.Data.Distractions == 1, "repeat penalty");
        });
        Test("return after more than grace counts a departure", delegate {
            var s = Session(true); s.Tick(0, Presence.Away); Run(s, 10000, Presence.Away); s.Tick(500, Presence.Allowed);
            Check(s.Data.Distractions == 1, "return edge");
        });
        Test("second departure counts separately", delegate {
            var s = Session(false); s.Tick(0, Presence.Away); Run(s, 11000, Presence.Away);
            s.Tick(0, Presence.Allowed); s.Tick(0, Presence.Away); Run(s, 11000, Presence.Away);
            Check(s.Data.Distractions == 2, "episode count");
        });
        Test("unknown evidence cancels proof without distraction", delegate {
            var s = Session(true); s.Tick(0, Presence.Allowed); s.Tick(500, Presence.Unknown);
            Check(s.Data.EffectiveMs == 0 && !s.Data.BonusEligible && s.Data.Distractions == 0, "unknown handling");
        });
        Test("sleep sized gap cannot mint materials", delegate {
            var s = Session(true); s.Tick(0, Presence.Allowed); s.Tick(3600000, Presence.Allowed);
            Check(s.Data.EffectiveMs == 0 && !s.Data.BonusEligible, "gap credited");
        });
        Test("negative interval not credited", delegate {
            var s = Session(true); s.Tick(0, Presence.Allowed); s.Tick(-1, Presence.Allowed);
            Check(s.Data.EffectiveMs == 0 && !s.Data.BonusEligible, "negative time");
        });
        Test("pause freezes and resume does not backfill", delegate {
            var s = Session(true); s.Tick(0, Presence.Allowed); Run(s, 1000, Presence.Allowed); s.Pause();
            Run(s, 60000, Presence.Allowed); s.Resume(); s.Tick(500, Presence.Allowed); s.Tick(500, Presence.Allowed);
            Check(s.Data.EffectiveMs == 1500 && !s.Data.BonusEligible, "pause timing");
        });
        Test("completion caps target and grants fixed 20 percent", delegate {
            var s = Session(true); s.Tick(0, Presence.Allowed); Run(s, 300500, Presence.Allowed);
            var save = new SaveData(); var r = Game.Settle(save, s.Data, false);
            Check(s.Complete && r.EffectiveMs == 300000 && r.RewardUnits == 360000 && r.ChallengeWon, "bonus mismatch");
        });
        Test("early end does not grant completion bonus", delegate {
            var s = Session(true); s.Tick(0, Presence.Allowed); Run(s, 30000, Presence.Allowed);
            Check(Game.Settle(new SaveData(), s.Data, false).RewardUnits == 30000, "early bonus");
        });
        Test("settlement is idempotent and clears checkpoint", delegate {
            var s = Session(false); s.Tick(0, Presence.Allowed); Run(s, 1000, Presence.Allowed);
            var save = new SaveData { Pending = s.Data }; Game.Settle(save, s.Data, false); Game.Settle(save, s.Data, false);
            Check(save.WalletUnits == 1000 && save.History.Count == 1 && save.Pending == null, "duplicate award");
        });
        Test("fractional minutes survive separate sessions", delegate {
            var save = new SaveData(); for (int i = 0; i < 2; i++) {
                var s = Session(false); s.Tick(0, Presence.Allowed); Run(s, 30000, Presence.Allowed); Game.Settle(save, s.Data, false);
            }
            Check(save.WalletUnits == 60000, "rounding loss");
        });
        Test("recovery preserves base only even at completion", delegate {
            var s = Session(true); s.Tick(0, Presence.Allowed); Run(s, 300000, Presence.Allowed);
            var r = Game.Settle(new SaveData(), s.Data, true); Check(r.RewardUnits == 300000 && !r.ChallengeWon, "recovery bonus");
        });
        Test("invalid task and duration rejected", delegate {
            Throws<ArgumentException>(delegate { new SessionEngine(" ", 25, false); });
            Throws<ArgumentException>(delegate { new SessionEngine("task", 0, false); });
        });
        Test("save roundtrip retains sub-minute precision", delegate {
            var store = Store(); var save = new SaveData { WalletUnits = 12500, Pending = Session(true).Data };
            store.Save(save); var loaded = store.Load(); Check(loaded.WalletUnits == 12500 && loaded.Pending.Id == save.Pending.Id, "roundtrip");
        });
        Test("restart recovery can only settle once", delegate {
            var store = Store(); var s = Session(true); s.Tick(0, Presence.Allowed); Run(s, 7500, Presence.Allowed);
            store.Save(new SaveData { Pending = s.Data }); var loaded = store.Load(); Game.Settle(loaded, loaded.Pending, true); store.Save(loaded);
            var again = store.Load(); Check(again.Pending == null && again.WalletUnits == 7500 && again.History.Count == 1, "restart replay");
        });
        Test("corrupt primary recovers backup and keeps damaged file", delegate {
            string dir = Path.Combine(testDir, Guid.NewGuid().ToString("N")); var store = new SaveStore(dir);
            store.Save(new SaveData { WalletUnits = 100 }); store.Save(new SaveData { WalletUnits = 200 });
            File.WriteAllText(Path.Combine(dir, "island.json"), "not-json");
            Check(store.Load().WalletUnits == 100 && store.Warning != null, "backup missing");
            Check(Directory.GetFiles(dir, "*.damaged-*").Length == 1, "corrupt not preserved");
        });
        Test("unknown schema refuses to load", delegate {
            Throws<NotSupportedException>(delegate { SaveStore.Parse("{\"Version\":99}"); });
        });
        Test("same selected foreground page matches", delegate {
            var b = new BrowserTracker(); b.Select(Page("https://example.com/read?a=1#p", 2, true), 100);
            Check(b.Evaluate(600, true) == Presence.Allowed, "page mismatch");
        });
        Test("same tab navigating elsewhere is away", delegate {
            var b = new BrowserTracker(); b.Select(Page("https://example.com/read", 2, true), 100);
            b.Update(Page("https://example.com/video", 2, true), 600); Check(b.Evaluate(700, true) == Presence.Away, "navigation ignored");
        });
        Test("query and fragment changes are different pages", delegate {
            Check(BrowserTracker.CanonicalUrl("https://example.com/?a=1#one") != BrowserTracker.CanonicalUrl("https://example.com/?a=2#one"), "query lost");
            Check(BrowserTracker.CanonicalUrl("https://example.com/#one") != BrowserTracker.CanonicalUrl("https://example.com/#two"), "route lost");
        });
        Test("same URL different tab is away", delegate {
            var b = new BrowserTracker(); b.Select(Page("https://example.com/", 2, true), 100);
            b.Update(Page("https://example.com/", 3, true), 200); Check(b.Evaluate(300, true) == Presence.Away, "tab ignored");
        });
        Test("foreground and browser focus both required", delegate {
            var b = new BrowserTracker(); b.Select(Page("https://example.com/", 2, true), 100);
            Check(b.Evaluate(200, false) == Presence.Away, "OS focus ignored");
            b.Update(Page("https://example.com/", 2, false), 200); Check(b.Evaluate(300, true) == Presence.Away, "browser focus ignored");
        });
        Test("stale data and disconnect are unknown", delegate {
            var b = new BrowserTracker(); b.Select(Page("https://example.com/", 2, true), 100);
            Check(b.Evaluate(3000, true) == Presence.Unknown, "stale data credited");
            var p = Page("https://example.com/", 2, true); p.Type = "disconnect"; b.Update(p, 3100);
            Check(b.Evaluate(3200, true) == Presence.Unknown, "disconnect ignored");
        });
        Test("another browser instance cannot replace evidence", delegate {
            var b = new BrowserTracker(); b.Select(Page("https://example.com/", 2, true), 100);
            var p = Page("https://example.com/", 2, true); p.Instance = "other-profile"; b.Update(p, 2600);
            Check(b.Evaluate(2700, true) == Presence.Unknown, "instance mixed");
        });
        Test("browser internal URLs are rejected", delegate {
            Throws<ArgumentException>(delegate { new BrowserTracker().Select(Page("edge://settings", 2, true), 0); });
        });
        Test("initial browser warmup does not cancel challenge", delegate {
            var s = Session(true); Run(s, 4000, Presence.Unknown); s.Tick(0, Presence.Allowed); Run(s, 1000, Presence.Allowed);
            Check(s.Data.BonusEligible && s.Data.EffectiveMs == 1000, "warmup counted as interruption");
        });
        Test("known different foreground remains away on stale browser evidence", delegate {
            var b = new BrowserTracker(); b.Select(Page("https://example.com/", 2, true), 0);
            Check(b.Evaluate(60000, false) == Presence.Away, "known departure was hidden");
        });
        Test("valid but incomplete JSON must not reset progress", delegate {
            Throws<InvalidOperationException>(delegate { SaveStore.Parse("{}"); });
            Throws<InvalidOperationException>(delegate { SaveStore.Parse("{\"Version\":1}"); });
        });
        Test("successful rename commits both disk and live state", delegate {
            var save = new SaveData(); var store = Store();
            SaveStore.Update(save, delegate(SaveData copy) { copy.SectName = "云隐宗"; }, store.Save);
            Check(save.SectName == "云隐宗" && store.Load().SectName == "云隐宗", "commit incomplete");
        });
        Test("failed rename preserves original name", delegate {
            var save = new SaveData();
            Throws<IOException>(delegate {
                SaveStore.Update(save, delegate(SaveData copy) { copy.SectName = "云隐宗"; }, delegate { throw new IOException(); });
            });
            Check(save.SectName == "我的宗门", "name changed despite failure");
        });
        Test("native frame handles Unicode byte lengths and consecutive messages", delegate {
            using (var stream = new MemoryStream()) {
                Framing.Write(stream, "{\"Title\":\"中文阅读\"}"); Framing.Write(stream, "{\"Ok\":true}"); stream.Position = 0;
                Check(Framing.Read(stream) == "{\"Title\":\"中文阅读\"}", "UTF8 framing");
                Check(Framing.Read(stream) == "{\"Ok\":true}" && Framing.Read(stream) == null, "frame boundaries");
            }
        });
        Test("native frame rejects partial header and oversized length", delegate {
            Throws<EndOfStreamException>(delegate { Framing.Read(new MemoryStream(new byte[] { 1, 0 })); });
            Throws<InvalidDataException>(delegate { Framing.Read(new MemoryStream(BitConverter.GetBytes(Framing.MaxBytes + 1))); });
        });
        Test("native frame rejects truncated payload", delegate {
            using (var stream = new MemoryStream()) {
                byte[] prefix = BitConverter.GetBytes(8); stream.Write(prefix, 0, 4); stream.WriteByte(123); stream.Position = 0;
                Throws<EndOfStreamException>(delegate { Framing.Read(stream); });
            }
        });
        RunSectTests();
        Console.WriteLine(String.Format("RESULT: {0} passed, {1} failed", passed, failed));
        return failed == 0 ? 0 : 1;
    }
}
