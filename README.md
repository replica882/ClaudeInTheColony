# ClaudeInTheColony

把 Claude 接进《缺氧》(Oxygen Not Included)。

游戏里跑一个 mod，外面包一层 MCP，让 AI 能真的看见殖民地、并最终能动手。

> 搜遍 GitHub，ONI 没有任何 http / websocket / remote-control 类的 mod。
> Minecraft 那边已经有好几个 MCP mod 了，缺氧这边是空的。这是第一个。

## 现在能做什么（v0.1）

进游戏时、以及之后每 ~5 秒，往
`~/Library/Application Support/unity.Klei.Oxygen Not Included/claude/state.json`
写一份殖民地快照：

```json
{
  "reason": "tick",
  "wallClock": "2026-08-25 01:30:00",
  "colony": "笨笨",
  "cycle": 1,
  "duplicantCount": 3,
  "duplicants": ["...", "...", "..."]
}
```

单向（游戏 → 外部）。这一版的目的是把「Mac 上能不能编出被游戏加载的 DLL」这条最不确定的链路先跑通。

## 构建

```bash
./build.sh
```

编译并部署到 `mods/dev/ClaudeInTheColony/`。之后重启游戏，在主菜单 MODS 里启用。

验证是否加载成功：

```bash
grep ClaudeInTheColony ~/Library/Logs/Klei/"Oxygen Not Included"/Player.log
```

## macOS 上的坑

ONI modding 的社区资料（[Cairath 的指南](https://github.com/Cairath/Oxygen-Not-Included-Modding)、
[peterhaneve/ONIMods](https://github.com/peterhaneve/ONIMods)）全部默认 Windows。
Mac 的差异都在这里：

| | Windows | macOS |
|---|---|---|
| 游戏程序集 | `OxygenNotIncluded_Data/Managed` | `OxygenNotIncluded.app/Contents/Resources/Data/Managed` |
| mod 安装目录 | `Documents\Klei\OxygenNotIncluded\mods\` | `~/Library/Application Support/unity.Klei.Oxygen Not Included/mods/` |
| 日志 | `AppData\LocalLow\Klei\...` | `~/Library/Logs/Klei/Oxygen Not Included/Player.log` |

其他几条：

- **peterhaneve 的 `Directory.Build.props` 在 Mac 上用不了** —— 它靠读 Windows 注册表定位游戏。Mac 得在 csproj 里直接写 `HintPath`。
- **`System.DateTime` 会被撞掉。** ONI 的 `Assembly-CSharp` 里有自己的 `DateTime` 类型，直接写 `DateTime.Now` 报 `CS0117`。用全限定名 `System.DateTime.Now`。
- **`mod_info.yaml` 必须 UTF-8 无 BOM**，带签名游戏直接拒绝加载。`APIVersion: 2` 必填。
- 引用游戏 DLL 时全部加 `Private="false"`，否则会把游戏程序集拷进输出目录。
- 构建目标 `netstandard2.1`。

已验证环境：macOS 25.5.0 (arm64) / ONI Build `U59-744825-V` / Unity 6000.3.5f2 / .NET SDK 8.0.401

## 架构

```
ONI 进程 (Mono)
 └ ClaudeInTheColony.dll  (Harmony)
    ├ 后台线程 HTTP server            ← v0.2
    └ Game.Update 上的主线程指令队列   ← v0.2
             ↑ HTTP
      MCP server (Python)             ← v0.2
             ↑
           Claude
```

**关键约束**：Unity 是单线程的。从 HTTP 线程直接碰游戏对象必崩。
所有请求都要排队，等主线程那一帧执行完再回填结果。

## 路线图

- [x] **v0.1 打通** — 编译链、加载链、状态落盘
- [ ] **v0.2 眼睛** — HTTP server + 主线程队列 + MCP 包装；地图查询（每格元素/温度/气压）
- [ ] **v0.3 嘴** — 游戏内通知，Claude 可以在右上角说话
- [ ] **v0.4 手** — 挖掘 / 拆除 / 优先级 / 暂停加速
- [ ] **v0.5 真·手** — 建造、布线、管道

## 许可

MIT
