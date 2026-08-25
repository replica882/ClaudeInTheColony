using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace ClaudeInTheColony {

    /// <summary>一次请求。后台线程造它，主线程填 Result，后台线程再取走。</summary>
    internal sealed class Job {
        internal string Method = "GET";
        internal string Path = "/";
        internal readonly Dictionary<string, string> Query = new Dictionary<string, string>();
        internal string Body = "";
        internal string Result;
        internal int Status = 200;
        internal readonly ManualResetEventSlim Done = new ManualResetEventSlim(false);

        internal string Q(string key, string fallback = null) {
            string v;
            return Query.TryGetValue(key, out v) ? v : fallback;
        }
        internal int QInt(string key, int fallback) {
            int v;
            return int.TryParse(Q(key), out v) ? v : fallback;
        }
    }

    /// <summary>
    /// 只听 127.0.0.1 的极简 HTTP 服务。
    ///
    /// 为什么不用 HttpListener：Mono 上它的行为在各平台不一致，而我们只要
    /// GET/POST + JSON，自己解析二十行就够，少一个不确定性。
    ///
    /// 线程模型（整个 mod 最要命的地方）：
    ///   后台线程收请求 → 入队 → 阻塞等待
    ///   主线程 Game.Update → Pump() 排空队列 → 碰游戏对象 → 填结果 → 放行
    /// 后台线程永远不碰任何 Unity/ONI 对象。碰了就是随机崩溃。
    /// </summary>
    internal static class Bridge {

        internal const int PORT = 7373;
        private const int TIMEOUT_MS = 5000;

        private static TcpListener listener;
        private static readonly ConcurrentQueue<Job> pending = new ConcurrentQueue<Job>();
        private static volatile bool running;

        internal static void Start() {
            if (running) return;
            try {
                listener = new TcpListener(IPAddress.Loopback, PORT);
                listener.Start();
                running = true;
                new Thread(AcceptLoop) { IsBackground = true, Name = "ClaudeBridge" }.Start();
                Log.Info("桥开了 → http://127.0.0.1:" + PORT + "（只听本机）");
            } catch (Exception e) {
                Log.Error("桥开不起来：" + e.Message);
            }
        }

        internal static void Stop() {
            running = false;
            try { if (listener != null) listener.Stop(); } catch { }
            listener = null;
        }

        /// <summary>主线程每帧调一次。队列空的时候几乎不花钱。</summary>
        internal static void Pump() {
            Job job;
            while (pending.TryDequeue(out job)) {
                try {
                    job.Result = Router.Dispatch(job);
                } catch (Exception e) {
                    job.Status = 500;
                    job.Result = "{\"error\":" + Json.Str(e.Message) + "}";
                    Log.Warn("处理 " + job.Path + " 出错：" + e.Message);
                }
                job.Done.Set();
            }
        }

        private static void AcceptLoop() {
            while (running) {
                try {
                    var client = listener.AcceptTcpClient();
                    ThreadPool.QueueUserWorkItem(delegate { Serve(client); });
                } catch (Exception) {
                    if (running) Thread.Sleep(100);
                }
            }
        }

        private static void Serve(TcpClient client) {
            try {
                using (client)
                using (var stream = client.GetStream()) {
                    client.ReceiveTimeout = 3000;
                    client.SendTimeout = 3000;

                    var job = Parse(stream);
                    if (job == null) { Write(stream, 400, "{\"error\":\"bad request\"}"); return; }

                    // /ping 不碰游戏对象，后台线程直接答，主菜单里也能用
                    if (job.Path == "/ping") {
                        Write(stream, 200, "{\"ok\":true,\"mod\":\"ClaudeInTheColony\",\"version\":\"0.2.0\"}");
                        return;
                    }

                    pending.Enqueue(job);

                    if (!job.Done.Wait(TIMEOUT_MS)) {
                        // 主线程没来取 —— 多半是还在主菜单，Game.Update 根本没跑
                        Write(stream, 503,
                            "{\"error\":\"主线程没有响应。游戏可能还在主菜单，没进存档。\"}");
                        return;
                    }
                    Write(stream, job.Status, job.Result);
                }
            } catch (Exception) {
                // 客户端断了之类，安静收场
            }
        }

        private static Job Parse(NetworkStream stream) {
            var head = new StringBuilder(512);
            var one = new byte[1];
            int consecutiveNewlines = 0;

            // 一个字节一个字节读到 \r\n\r\n。请求头很小，不值得为它写缓冲区管理。
            while (head.Length < 8192) {
                if (stream.Read(one, 0, 1) <= 0) return null;
                char c = (char)one[0];
                head.Append(c);
                if (c == '\n') { if (++consecutiveNewlines == 2) break; }
                else if (c != '\r') consecutiveNewlines = 0;
            }

            var lines = head.ToString().Split('\n');
            if (lines.Length == 0) return null;

            var parts = lines[0].Trim().Split(' ');
            if (parts.Length < 2) return null;

            var job = new Job { Method = parts[0].ToUpperInvariant() };

            var url = parts[1];
            int q = url.IndexOf('?');
            if (q >= 0) {
                job.Path = url.Substring(0, q);
                foreach (var pair in url.Substring(q + 1).Split('&')) {
                    if (pair.Length == 0) continue;
                    int eq = pair.IndexOf('=');
                    if (eq < 0) job.Query[Unescape(pair)] = "";
                    else job.Query[Unescape(pair.Substring(0, eq))] = Unescape(pair.Substring(eq + 1));
                }
            } else {
                job.Path = url;
            }

            int contentLength = 0;
            for (int i = 1; i < lines.Length; i++) {
                var line = lines[i].Trim();
                if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                    int.TryParse(line.Substring(15).Trim(), out contentLength);
            }

            if (contentLength > 0 && contentLength < 1 << 20) {
                var buf = new byte[contentLength];
                int got = 0;
                while (got < contentLength) {
                    int n = stream.Read(buf, got, contentLength - got);
                    if (n <= 0) break;
                    got += n;
                }
                job.Body = Encoding.UTF8.GetString(buf, 0, got);
            }
            return job;
        }

        private static string Unescape(string s) {
            try { return Uri.UnescapeDataString(s.Replace("+", " ")); }
            catch { return s; }
        }

        private static void Write(NetworkStream stream, int status, string body) {
            var payload = Encoding.UTF8.GetBytes(body ?? "");
            var header = Encoding.UTF8.GetBytes(
                "HTTP/1.1 " + status + " " + (status == 200 ? "OK" : "Error") + "\r\n" +
                "Content-Type: application/json; charset=utf-8\r\n" +
                "Content-Length: " + payload.Length + "\r\n" +
                "Connection: close\r\n\r\n");
            stream.Write(header, 0, header.Length);
            stream.Write(payload, 0, payload.Length);
            stream.Flush();
        }
    }
}
