using HarmonyLib;
using KMod;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace ClaudeInTheColony {

    /// <summary>mod 入口。游戏发现这个类就会实例化它。</summary>
    public sealed class ClaudeMod : UserMod2 {

        public override void OnLoad(Harmony harmony) {
            Log.Info("狗进来了。OnLoad — build against U59-744825");

            // 不用 PatchAll：目标方法万一改名会整个 mod 挂掉。
            // 手动 patch + 逐个兜底，找不到就只警告，不影响加载。
            TryPatch(harmony, typeof(Game), "OnSpawn",
                     nameof(Hooks.AfterGameSpawn));
            TryPatch(harmony, typeof(Game), "Update",
                     nameof(Hooks.AfterGameUpdate));

            Log.Info("patch 阶段结束");
        }

        public override void OnAllModsLoaded(Harmony harmony, IReadOnlyList<Mod> mods) {
            base.OnAllModsLoaded(harmony, mods);
            Log.Info("所有 mod 加载完毕，共 " + (mods == null ? 0 : mods.Count) + " 个");
        }

        private static void TryPatch(Harmony harmony, Type target, string method, string postfix) {
            try {
                var original = AccessTools.Method(target, method);
                if (original == null) {
                    Log.Warn("找不到 " + target.Name + "." + method + "，跳过");
                    return;
                }
                harmony.Patch(original,
                    postfix: new HarmonyMethod(AccessTools.Method(typeof(Hooks), postfix)));
                Log.Info("已挂上 " + target.Name + "." + method);
            } catch (Exception e) {
                Log.Warn("patch " + target.Name + "." + method + " 失败：" + e.Message);
            }
        }
    }

    /// <summary>Harmony 的落点。这里跑在游戏主线程上，碰游戏对象是安全的。</summary>
    public static class Hooks {

        private static int frame;

        public static void AfterGameSpawn() {
            Log.Info("Game.OnSpawn — 我在基地里了");
            Eyes.Dump("spawn");
        }

        public static void AfterGameUpdate() {
            // 每 ~300 帧（5 秒左右）看一眼，别把磁盘写爆
            if (++frame < 300) return;
            frame = 0;
            Eyes.Dump("tick");
        }
    }

    internal static class Log {
        private const string TAG = "[ClaudeInTheColony] ";
        public static void Info(string m)  { Debug.Log(TAG + m); }
        public static void Warn(string m)  { Debug.LogWarning(TAG + m); }
        public static void Error(string m) { Debug.LogError(TAG + m); }
    }
}
