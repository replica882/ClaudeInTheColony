using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace ClaudeInTheColony {

    /// <summary>
    /// 所有端点。这里的每一行都跑在游戏主线程上（由 Bridge.Pump 调用），
    /// 碰 Grid / Components / Instance 都是安全的。
    /// </summary>
    internal static class Router {

        internal static string Dispatch(Job job) {
            switch (job.Path) {
                case "/state": return State();
                case "/map":   return Map(job);
                case "/raw":   return Raw(job);
                default:
                    job.Status = 404;
                    return "{\"error\":\"没有这个端点\",\"available\":[\"/ping\",\"/state\",\"/map\",\"/raw\"]}";
            }
        }

        // ───────────────────────── /state ─────────────────────────

        private static string State() {
            var sb = new StringBuilder(2048);
            sb.Append("{\n");

            string colony = null;
            if (SaveGame.Instance != null) colony = SaveGame.Instance.BaseName;
            sb.Append("  \"colony\": ").Append(Json.Str(colony)).Append(",\n");

            int cycle = GameClock.Instance != null ? GameClock.Instance.GetCycle() : -1;
            sb.Append("  \"cycle\": ").Append(cycle).Append(",\n");

            sb.Append("  \"world\": {\"w\": ").Append(Grid.WidthInCells)
              .Append(", \"h\": ").Append(Grid.HeightInCells).Append("},\n");

            // —— 复制体 ——
            sb.Append("  \"duplicants\": [\n");
            var live = Components.LiveMinionIdentities;
            bool first = true;
            if (live != null) {
                foreach (var m in live.Items) {
                    if (m == null) continue;
                    if (!first) sb.Append(",\n");
                    first = false;

                    int cell = Grid.PosToCell(m.gameObject);
                    int x, y;
                    Grid.CellToXY(cell, out x, out y);

                    sb.Append("    {\"name\": ").Append(Json.Str(m.GetProperName()))
                      .Append(", \"x\": ").Append(x)
                      .Append(", \"y\": ").Append(y);

                    var health = m.GetComponent<Health>();
                    if (health != null) {
                        sb.Append(", \"hp\": ").Append(Json.Num(health.hitPoints))
                          .Append(", \"hpMax\": ").Append(Json.Num(health.maxHitPoints));
                    }
                    sb.Append("}");
                }
            }
            sb.Append("\n  ]\n}");
            return sb.ToString();
        }

        // ───────────────────────── /map ─────────────────────────

        /// <summary>
        /// 一块矩形区域的格子数据。缺氧的一切问题最后都是格子问题：
        /// 这里有什么气体、多少压力、几度。
        /// 上限 40x40，再大 JSON 就没法看了。
        /// </summary>
        private static string Map(Job job) {
            int x0 = job.QInt("x", 0);
            int y0 = job.QInt("y", 0);
            int w  = Mathf.Clamp(job.QInt("w", 16), 1, 40);
            int h  = Mathf.Clamp(job.QInt("h", 16), 1, 40);

            var sb = new StringBuilder(w * h * 24 + 512);
            sb.Append("{\n");
            sb.Append("  \"origin\": {\"x\": ").Append(x0).Append(", \"y\": ").Append(y0)
              .Append(", \"w\": ").Append(w).Append(", \"h\": ").Append(h).Append("},\n");
            sb.Append("  \"note\": \"rows 从上往下（y 大的在前），跟你屏幕上看到的一致\",\n");

            // 元素名去重成图例，格子里只放短代号，否则 40x40 全是 \"Sandstone\"
            var legend = new Dictionary<string, string>();
            var codes  = new Dictionary<string, string>();

            var grid = new string[h][];
            var temp = new string[h][];
            var mass = new string[h][];

            for (int row = 0; row < h; row++) {
                int y = y0 + h - 1 - row;          // 从上往下
                grid[row] = new string[w];
                temp[row] = new string[w];
                mass[row] = new string[w];

                for (int col = 0; col < w; col++) {
                    int x = x0 + col;
                    if (!Grid.IsValidCell(Grid.XYToCell(x, y))) {
                        grid[row][col] = "-"; temp[row][col] = "null"; mass[row][col] = "null";
                        continue;
                    }
                    int cell = Grid.XYToCell(x, y);
                    var element = Grid.Element[cell];
                    string name = element != null ? element.id.ToString() : "Void";

                    string code;
                    if (!codes.TryGetValue(name, out code)) {
                        code = Code(name, codes);
                        codes[name] = code;
                        legend[code] = name;
                    }
                    grid[row][col] = code;
                    temp[row][col] = Json.Num(Grid.Temperature[cell] - 273.15f);   // 摄氏，人看的
                    mass[row][col] = Json.Num(Grid.Mass[cell]);
                }
            }

            sb.Append("  \"legend\": {");
            bool f1 = true;
            foreach (var kv in legend) {
                if (!f1) sb.Append(", ");
                f1 = false;
                sb.Append(Json.Str(kv.Key)).Append(": ").Append(Json.Str(kv.Value));
            }
            sb.Append("},\n");

            Rows(sb, "elements", grid, true);  sb.Append(",\n");
            Rows(sb, "tempC",    temp, false); sb.Append(",\n");
            Rows(sb, "massKg",   mass, false); sb.Append("\n}");
            return sb.ToString();
        }


        // ───────────────────────── /raw ─────────────────────────

        /// <summary>
        /// 给渲染器吃的原始格子数据。默认整张地图。
        ///
        /// 每格 10 字节，小端：
        ///   uint16  元素在 legend 里的下标
        ///   float32 温度（开尔文）
        ///   float32 质量（kg）
        /// 整张 256x384 的图约 1 MB，base64 后 1.3 MB —— 本机回环，无所谓。
        ///
        /// 刻意不在这里画图：渲染逻辑放外面，改配色不用重启游戏。
        /// </summary>
        private static string Raw(Job job) {
            int x0 = Mathf.Clamp(job.QInt("x", 0), 0, Grid.WidthInCells - 1);
            int y0 = Mathf.Clamp(job.QInt("y", 0), 0, Grid.HeightInCells - 1);
            int w  = Mathf.Clamp(job.QInt("w", Grid.WidthInCells),  1, Grid.WidthInCells  - x0);
            int h  = Mathf.Clamp(job.QInt("h", Grid.HeightInCells), 1, Grid.HeightInCells - y0);

            var names   = new List<string>();
            var states  = new List<string>();      // 气/液/固 —— 渲染器靠它决定哪张图画哪些格子
            var indexOf = new Dictionary<string, ushort>();

            var bytes = new byte[w * h * 10];
            int at = 0;

            // 行序：y 从大到小，跟屏幕上看到的一致
            for (int row = 0; row < h; row++) {
                int y = y0 + h - 1 - row;
                for (int col = 0; col < w; col++) {
                    int cell = Grid.XYToCell(x0 + col, y);

                    ushort idx = 0;
                    float tempK = 0f, massKg = 0f;

                    if (Grid.IsValidCell(cell)) {
                        var element = Grid.Element[cell];
                        string name = element != null ? element.id.ToString() : "Void";
                        if (!indexOf.TryGetValue(name, out idx)) {
                            idx = (ushort)names.Count;
                            indexOf[name] = idx;
                            names.Add(name);
                            states.Add(element == null ? "void"
                                     : element.IsGas    ? "gas"
                                     : element.IsLiquid ? "liquid"
                                     : element.IsSolid  ? "solid" : "other");
                        }
                        tempK  = Grid.Temperature[cell];
                        massKg = Grid.Mass[cell];
                    } else {
                        const string OOB = "OutOfBounds";
                        if (!indexOf.TryGetValue(OOB, out idx)) {
                            idx = (ushort)names.Count;
                            indexOf[OOB] = idx;
                            names.Add(OOB);
                            states.Add("void");
                        }
                    }

                    bytes[at++] = (byte)(idx & 0xFF);
                    bytes[at++] = (byte)(idx >> 8);
                    Buffer.BlockCopy(BitConverter.GetBytes(tempK),  0, bytes, at, 4); at += 4;
                    Buffer.BlockCopy(BitConverter.GetBytes(massKg), 0, bytes, at, 4); at += 4;
                }
            }

            var sb = new StringBuilder(bytes.Length * 2);
            sb.Append("{\n  \"origin\": {\"x\": ").Append(x0).Append(", \"y\": ").Append(y0)
              .Append(", \"w\": ").Append(w).Append(", \"h\": ").Append(h).Append("},\n");
            sb.Append("  \"format\": \"每格 10 字节小端：uint16 元素下标 / float32 开尔文 / float32 千克；行序 y 从大到小\",\n");
            sb.Append("  \"legend\": [");
            for (int i = 0; i < names.Count; i++) {
                if (i > 0) sb.Append(", ");
                sb.Append("{\"name\": ").Append(Json.Str(names[i]))
                  .Append(", \"state\": ").Append(Json.Str(states[i])).Append("}");
            }
            sb.Append("],\n");
            sb.Append("  \"data\": ").Append(Json.Str(Convert.ToBase64String(bytes))).Append("\n}");
            return sb.ToString();
        }

        private static void Rows(StringBuilder sb, string key, string[][] data, bool quote) {
            sb.Append("  \"").Append(key).Append("\": [\n");
            for (int r = 0; r < data.Length; r++) {
                sb.Append("    [");
                for (int c = 0; c < data[r].Length; c++) {
                    if (c > 0) sb.Append(",");
                    if (quote) sb.Append(Json.Str(data[r][c]));
                    else sb.Append(data[r][c]);
                }
                sb.Append(r == data.Length - 1 ? "]\n" : "],\n");
            }
            sb.Append("  ]");
        }

        /// <summary>把 Oxygen → O、SandStone → Sa 这样缩，冲突了就加字母。</summary>
        private static string Code(string name, Dictionary<string, string> taken) {
            for (int len = 1; len <= name.Length; len++) {
                string candidate = name.Substring(0, len);
                bool used = false;
                foreach (var v in taken.Values) if (v == candidate) { used = true; break; }
                if (!used) return candidate;
            }
            return name;
        }
    }
}
