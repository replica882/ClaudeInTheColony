using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace ClaudeInTheColony {

    /// <summary>
    /// v0.1 的眼睛：把殖民地状态写成 JSON 落到磁盘，外面的我直接读文件。
    /// 只在主线程被调用（Harmony 挂在 Game.OnSpawn / Game.Update 上）。
    /// </summary>
    internal static class Eyes {

        private static string dir;
        private static bool broken;   // 出过一次错就闭嘴，别刷屏

        internal static string Dir {
            get {
                if (dir == null) {
                    dir = Path.Combine(Application.persistentDataPath, "claude");
                    Directory.CreateDirectory(dir);
                }
                return dir;
            }
        }

        internal static void Dump(string reason) {
            if (broken) return;
            try {
                var sb = new StringBuilder(1024);
                sb.Append("{\n");
                Field(sb, "reason", reason);
                Field(sb, "wallClock", System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

                // —— 殖民地 ——
                string baseName = null;
                try { baseName = SaveGame.Instance != null ? SaveGame.Instance.BaseName : null; } catch { }
                Field(sb, "colony", baseName);

                int cycle = -1;
                try { if (GameClock.Instance != null) cycle = GameClock.Instance.GetCycle(); } catch { }
                Num(sb, "cycle", cycle);

                // —— 复制体 ——
                var names = new List<string>();
                try {
                    var live = Components.LiveMinionIdentities;
                    if (live != null) {
                        foreach (var m in live.Items) {
                            if (m == null) continue;
                            names.Add(m.GetProperName());
                        }
                    }
                } catch (Exception e) { Log.Warn("读复制体失败：" + e.Message); }

                Num(sb, "duplicantCount", names.Count);
                sb.Append("  \"duplicants\": [");
                for (int i = 0; i < names.Count; i++) {
                    if (i > 0) sb.Append(", ");
                    sb.Append('"').Append(Esc(names[i])).Append('"');
                }
                sb.Append("]\n}");

                File.WriteAllText(Path.Combine(Dir, "state.json"), sb.ToString(), new UTF8Encoding(false));
            } catch (Exception e) {
                broken = true;
                Log.Error("Eyes.Dump 炸了，之后不再尝试：" + e);
            }
        }

        private static void Field(StringBuilder sb, string k, string v) {
            sb.Append("  \"").Append(k).Append("\": ");
            if (v == null) sb.Append("null"); else sb.Append('"').Append(Esc(v)).Append('"');
            sb.Append(",\n");
        }

        private static void Num(StringBuilder sb, string k, int v) {
            sb.Append("  \"").Append(k).Append("\": ").Append(v).Append(",\n");
        }

        private static string Esc(string s) {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"")
                    .Replace("\n", "\\n").Replace("\r", "").Replace("\t", "\\t");
        }
    }
}
