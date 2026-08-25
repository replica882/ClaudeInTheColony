# ClaudeInTheColony

把 Claude 接进《缺氧》(Oxygen Not Included)。

游戏里跑一个 mod，外面包一层 MCP，让 AI 能真的看见殖民地、并最终能动手。

> 搜遍 GitHub，ONI 没有任何 http / websocket / remote-control 类的 mod。
> Minecraft 那边已经有好几个 MCP mod 了，缺氧这边是空的。这是第一个。

## 现在能做什么（v0.2）

mod 在游戏进程里开一个只听 `127.0.0.1:7373` 的 HTTP 服务：

| 端点 | 作用 |
|---|---|
| `GET /ping` | 桥还活着吗。不碰游戏世界，主菜单里也能答 |
| `GET /state` | 殖民地名、周期、地图尺寸、每个复制体的名字/坐标/血量 |
| `GET /map?x=&y=&w=&h=` | 一块区域每格的元素、温度（℃）、质量（kg）。上限 40×40 |

外面用 `mcp/oni_mcp.py` 包成 MCP 工具：

```bash
claude mcp add oni -- uv run --script /绝对路径/ClaudeInTheColony/mcp/oni_mcp.py
```

（`uv` 会照 PEP 723 的内联声明自己准备依赖，不用建虚拟环境。）

也可以完全不经过 MCP，直接问：

```bash
curl -s 127.0.0.1:7373/state | python3 -m json.tool
curl -s "127.0.0.1:7373/map?x=100&y=100&w=20&h=12"
```

另外每 ~5 秒仍会往
`~/Library/Application Support/unity.Klei.Oxygen Not Included/claude/state.json`
落一份快照，作为 HTTP 之外的旁路。

## 构建

```bash
./build.sh
```

编译并部署到 `mods/dev/ClaudeInTheColony/`。之后重启游戏，在主菜单 MODS 里启用。

验证是否加载成功：

```bash
grep ClaudeInTheColony ~/Library/Logs/Klei/"Oxygen Not Included"/Player.log
```

## 如果你开着代理

桥听的是 `127.0.0.1`，但只要设了 `http_proxy` 环境变量（Clash / 各种机场），
`curl` 和 Python 的 `urllib` **都会把发往本机的请求也交给代理**，代理连不上
游戏进程，回你一个 502 —— 报错长得跟"游戏没开"一模一样，能查很久。

```bash
curl -s --noproxy '*' 127.0.0.1:7373/ping     # 手动测试要加 --noproxy
```

`mcp/oni_mcp.py` 里已经用空的 `ProxyHandler` 强制直连，不受环境变量影响。

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
    ├ 后台线程 HTTP server (127.0.0.1:7373)
    └ Game.Update 上的主线程指令队列
             ↑ HTTP
      MCP server (Python, mcp/oni_mcp.py)
             ↑
           Claude
```

**关键约束**：Unity 是单线程的。从 HTTP 线程直接碰游戏对象必崩。
所有请求都要排队，等主线程那一帧执行完再回填结果。

## 路线图

- [x] **v0.1 打通** — 编译链、加载链、状态落盘
- [x] **v0.2 眼睛** — HTTP server + 主线程队列 + MCP 包装；地图查询（每格元素/温度/气压）
- [ ] **v0.3 嘴** — 游戏内通知，Claude 可以在右上角说话
- [ ] **v0.4 手** — 挖掘 / 拆除 / 优先级 / 暂停加速
- [ ] **v0.5 真·手** — 建造、布线、管道

## 许可

MIT
