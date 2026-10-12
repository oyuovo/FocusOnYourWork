using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace FocusOnYourWork.Core
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
            if (!(raw["Version"] is int) || (int)raw["Version"] < 1 || (int)raw["Version"] > 3) throw new NotSupportedException("不支持此存档版本，原文件保持不变。");
            int version = (int)raw["Version"];
            foreach (string field in new [] { version < 3 ? "IslandName" : "SectName", "WalletUnits", "TotalEffectiveMs", "History" })
                if (!raw.ContainsKey(field)) throw new InvalidOperationException("存档缺少必要字段。");
            // Only import work and cultivation progress. Retired island fields never enter the current model.
            if (version < 3) {
                raw["SectName"] = Object.Equals(raw["IslandName"], "我的专注岛") ? "我的宗门" : raw["IslandName"];
                raw.Remove("IslandName"); raw.Remove("Theme"); raw.Remove("Buildings");
            }
            SaveData data = serializer.ConvertToType<SaveData>(raw);
            data.Version = 3;
            if (version == 1) { data.Sect = new SectData(); }
            else {
                var sectRaw = raw.ContainsKey("Sect") ? raw["Sect"] as Dictionary<string, object> : null;
                if (sectRaw == null) throw new InvalidOperationException("存档缺少宗门数据。");
                foreach (string field in new [] { "Capacity", "MountainLevel", "DuplicateStreak", "PendingDrawId", "SceneTheme", "UiTheme", "Roster", "Draws", "Memorials" })
                    if (!sectRaw.ContainsKey(field)) throw new InvalidOperationException("宗门存档缺少必要字段。");
                RequireRows(sectRaw["Roster"], new [] { "Id", "CultivationMs", "Active", "JoinedUtc", "AscendedUtc", "LastTask" });
                RequireRows(sectRaw["Draws"], new [] { "Id", "CharacterId", "Duplicate", "Choice" });
            }
            if (data.History == null || data.WalletUnits < 0 || data.TotalEffectiveMs < 0 ||
                String.IsNullOrWhiteSpace(data.SectName) || data.SectName.Length > 20 ||
                data.History.Any(x => x == null || String.IsNullOrEmpty(x.Id) || x.EffectiveMs < 0 || x.RewardUnits < 0 || x.Distractions < 0) ||
                data.History.Select(x => x.Id).Distinct().Count() != data.History.Count)
                throw new InvalidOperationException("存档内容无效。");
            if (data.Pending != null && (String.IsNullOrEmpty(data.Pending.Id) || data.Pending.TargetMs < 300000 ||
                data.Pending.TargetMs > 7200000 || data.Pending.EffectiveMs < 0 || data.Pending.EffectiveMs > data.Pending.TargetMs))
                throw new InvalidOperationException("会话检查点无效。");
            SectGame.Validate(data.Sect);
            return data;
        }

        private static void RequireRows(object value, string[] fields)
        {
            var rows = value as object[];
            if (rows == null) throw new InvalidOperationException("宗门记录格式无效。");
            foreach (object row in rows) {
                var data = row as Dictionary<string, object>;
                if (data == null || fields.Any(field => !data.ContainsKey(field))) throw new InvalidOperationException("宗门记录缺少必要字段，不能重置已有进度。");
            }
        }

        // Commit a copy first. Failed disk writes must not spend materials or mutate live progress.
        public static void Update(SaveData live, Action<SaveData> change, Action<SaveData> persist)
        {
            var serializer = new JavaScriptSerializer { MaxJsonLength = 16 * 1024 * 1024 };
            SaveData copy = Parse(serializer.Serialize(live));
            change(copy);
            SectGame.Validate(copy.Sect);
            persist(copy);
            live.Version = copy.Version; live.Sect = copy.Sect;
            live.SectName = copy.SectName;
            live.WalletUnits = copy.WalletUnits; live.TotalEffectiveMs = copy.TotalEffectiveMs;
            live.History = copy.History; live.Pending = copy.Pending;
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
