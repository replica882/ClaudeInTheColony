using System.Globalization;
using System.Text;

namespace ClaudeInTheColony {

    /// <summary>
    /// 手搓 JSON。不用 Newtonsoft 是为了躲开游戏自带那份的版本冲突
    /// （构建时已经因为它吃过 MSB3277 警告）。我们只往外写，不需要解析器。
    /// </summary>
    internal static class Json {

        internal static string Str(string s) {
            if (s == null) return "null";
            var sb = new StringBuilder(s.Length + 8);
            sb.Append('"');
            foreach (char c in s) {
                switch (c) {
                    case '"':  sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n");  break;
                    case '\r': break;
                    case '\t': sb.Append("\\t");  break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
            return sb.ToString();
        }

        /// <summary>保留一位小数就够了，温度气压不需要十五位有效数字撑爆输出。</summary>
        internal static string Num(float f) {
            if (float.IsNaN(f) || float.IsInfinity(f)) return "null";
            return f.ToString("0.#", CultureInfo.InvariantCulture);
        }

        internal static string Num(double d) {
            return d.ToString("0.#", CultureInfo.InvariantCulture);
        }
    }
}
