using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace FocusIsland.Core
{
    public sealed class SaveStore
    {
        private readonly string path;
        public string Warning { get; private set; }
        public SaveStore(string directory)
        {
            Directory.CreateDirectory(directory);
            path = Path.Combine(directory, "island.json");
        }

        public SaveData Load()
        {
            if (!File.Exists(path)) return new SaveData();
            try { return Parse(File.ReadAllText(path, Encoding.UTF8)); }
            catch (NotSupportedException) { throw; }
            catch (Exception ex)
            {
                if (!(ex is IOException || ex is ArgumentException || ex is InvalidOperationException)) throw;
                if (File.Exists(path + ".bak"))
                {
                    SaveData backup = Parse(File.ReadAllText(path + ".bak", Encoding.UTF8));
                    File.Copy(path, path + ".damaged-" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff"), false);
                    // Restore before the next Replace so the good backup is not replaced by corrupt data.
                    File.Copy(path + ".bak", path, true);
                    Warning = "主存档损坏，已恢复上一份备份，并保留损坏文件。";
                    return backup;
                }
                throw new InvalidOperationException("存档无法读取，原文件已保留。请备份后检查，程序不会覆盖它。", ex);
            }
        }

        public static SaveData Parse(string json)
        {
            var serializer = new JavaScriptSerializer { MaxJsonLength = 16 * 1024 * 1024 };
            var raw = serializer.DeserializeObject(json) as Dictionary<string, object>;
            if (raw == null || !raw.ContainsKey("Version")) throw new InvalidOperationException("存档缺少版本信息。");
            if (!(raw["Version"] is int) || (int)raw["Version"] != 1) throw new NotSupportedException("不支持此存档版本，原文件保持不变。");
            foreach (string field in new [] { "IslandName", "Theme", "WalletUnits", "TotalEffectiveMs", "Buildings", "History" })
                if (!raw.ContainsKey(field)) throw new InvalidOperationException("存档缺少必要字段。");
            SaveData data = serializer.ConvertToType<SaveData>(raw);
            if (data.Buildings == null || data.History == null || data.WalletUnits < 0 || data.TotalEffectiveMs < 0 ||
                String.IsNullOrWhiteSpace(data.IslandName) || data.IslandName.Length > 20 || (data.Theme != "sage" && data.Theme != "clay") ||
                data.Buildings.Any(x => x == null) || data.History.Any(x => x == null || String.IsNullOrEmpty(x.Id) || x.EffectiveMs < 0 || x.RewardUnits < 0 || x.Distractions < 0) ||
                data.History.Select(x => x.Id).Distinct().Count() != data.History.Count ||
                data.Buildings.Count > Rules.PlotCount || data.Buildings.Select(x => x.Plot).Distinct().Count() != data.Buildings.Count)
                throw new InvalidOperationException("存档内容无效。");
            foreach (Building b in data.Buildings)
                if (b.Plot < 0 || b.Plot >= Rules.PlotCount || (b.Color != "sage" && b.Color != "clay") || Rules.Price(b.Kind) < 0)
                    throw new InvalidOperationException("建筑存档无效。");
            if (data.Pending != null && (String.IsNullOrEmpty(data.Pending.Id) || data.Pending.TargetMs < 300000 ||
                data.Pending.TargetMs > 7200000 || data.Pending.EffectiveMs < 0 || data.Pending.EffectiveMs > data.Pending.TargetMs))
                throw new InvalidOperationException("会话检查点无效。");
            return data;
        }

        // Commit a copy first. Failed disk writes must not spend materials or mutate the live island.
        public static void Update(SaveData live, Action<SaveData> change, Action<SaveData> persist)
        {
            var serializer = new JavaScriptSerializer { MaxJsonLength = 16 * 1024 * 1024 };
            SaveData copy = Parse(serializer.Serialize(live));
            change(copy);
            persist(copy);
            live.IslandName = copy.IslandName; live.Theme = copy.Theme;
            live.WalletUnits = copy.WalletUnits; live.TotalEffectiveMs = copy.TotalEffectiveMs;
            live.Buildings = copy.Buildings; live.History = copy.History; live.Pending = copy.Pending;
        }

        public void Save(SaveData data)
        {
            string json = new JavaScriptSerializer { MaxJsonLength = 16 * 1024 * 1024 }.Serialize(data);
            string temp = path + ".tmp";
            byte[] bytes = new UTF8Encoding(false).GetBytes(json);
            using (FileStream f = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                f.Write(bytes, 0, bytes.Length);
                f.Flush(true);
            }
            if (File.Exists(path)) File.Replace(temp, path, path + ".bak");
            else File.Move(temp, path);
        }
    }
}
