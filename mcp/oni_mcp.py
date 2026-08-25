#!/usr/bin/env python3
# /// script
# requires-python = ">=3.10"
# dependencies = ["mcp>=1.2.0"]
# ///
"""
ClaudeInTheColony 的 MCP 侧。

把游戏里那个 mod 开的本地 HTTP 桥，包成 Claude 能直接调的工具。
桥只听 127.0.0.1:7373，不出本机。

跑法（uv 会自动准备依赖）：
    uv run mcp/oni_mcp.py

注册进 Claude Code：
    claude mcp add oni -- uv run --script /绝对路径/mcp/oni_mcp.py
"""

import json
import urllib.error
import urllib.request

from mcp.server.fastmcp import FastMCP

BASE = "http://127.0.0.1:7373"
TIMEOUT = 8

mcp = FastMCP("oni")

# 必须显式绕开系统代理。开着 Clash 之类的机场时，http_proxy 环境变量会让
# urllib 把发往 127.0.0.1 的请求也交给代理，代理连不上游戏进程，回 502 ——
# 报错长得跟"游戏没开"一模一样，能查很久。空的 ProxyHandler 直连。
_direct = urllib.request.build_opener(urllib.request.ProxyHandler({}))


def _get(path: str) -> str:
    try:
        with _direct.open(BASE + path, timeout=TIMEOUT) as r:
            return r.read().decode("utf-8")
    except urllib.error.HTTPError as e:
        body = e.read().decode("utf-8", "replace")
        try:
            return json.dumps(json.loads(body), ensure_ascii=False, indent=2)
        except Exception:
            return f"游戏返回 HTTP {e.code}：{body}"
    except urllib.error.URLError as e:
        return (
            f"连不上游戏里的桥（{BASE}）：{e.reason}\n"
            "排查顺序：\n"
            "  1. 缺氧开着吗\n"
            "  2. 主菜单 MODS 里 Claude In The Colony 打开了吗\n"
            "  3. 装的是 v0.2 以上吗（v0.1 没有桥）\n"
            "  4. 进存档了吗（主菜单里主线程不跑，只有 /ping 能答）"
        )
    except Exception as e:
        return f"出错了：{e!r}"


@mcp.tool()
def oni_ping() -> str:
    """确认游戏里的桥还活着。主菜单里也能用——它不碰游戏世界。"""
    return _get("/ping")


@mcp.tool()
def oni_state() -> str:
    """
    当前殖民地全景：名字、周期数、地图尺寸，
    以及每个复制体的名字 / 坐标 / 血量。

    想知道"现在什么情况"就先调这个。
    """
    return _get("/state")


@mcp.tool()
def oni_map(x: int, y: int, w: int = 16, h: int = 16) -> str:
    """
    读一块矩形区域的格子数据 —— 缺氧的一切最后都是格子问题。

    每格给三样：元素（图例缩写 + legend 对照表）、温度（摄氏）、质量（kg）。
    气体的质量就是气压，固体的质量就是矿藏量。

    参数:
        x, y: 区域左下角的格子坐标（(0,0) 在地图左下）
        w, h: 宽高，各自上限 40。40x40 已经是一屏的量了。

    返回的 rows 从上往下排，跟屏幕上看到的一致。
    先用 oni_state 拿到 world.w / world.h 再决定看哪里。
    """
    return _get(f"/map?x={x}&y={y}&w={w}&h={h}")


if __name__ == "__main__":
    mcp.run()
