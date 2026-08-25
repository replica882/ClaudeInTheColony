#!/usr/bin/env python3
# /// script
# requires-python = ">=3.10"
# dependencies = ["pillow", "numpy"]
# ///
"""
把缺氧的格子数据画成一张"给狗看的图"。

游戏里那个 mod 只吐原始数据（/raw），画图全在这里 —— 所以调配色、加图层
都不用重启游戏。

    uv run tools/render.py                      # 整张地图三联图
    uv run tools/render.py --x 100 --y 80 --w 60 --h 40 --scale 8
    uv run tools/render.py --ascii --x 110 --y 90 --w 40 --h 24
    uv run tools/render.py --demo               # 不连游戏，用假数据自测

三联图 = 元素 / 温度 / 质量。温度这一张是重点：游戏里要专门切图层才看得到，
而缺氧一半的死法都跟热有关。
"""

import argparse
import base64
import json
import struct
import sys
import urllib.request
import zlib

import numpy as np
from PIL import Image, ImageDraw, ImageFont


def load_font(size=13):
    """PIL 自带字体没有中文，标题会变方块。macOS 上借苹方。"""
    for path in ("/System/Library/Fonts/PingFang.ttc",
                 "/System/Library/Fonts/STHeiti Light.ttc",
                 "/System/Library/Fonts/Helvetica.ttc"):
        try:
            return ImageFont.truetype(path, size)
        except Exception:
            pass
    return ImageFont.load_default()


FONT = load_font(13)
FONT_SM = load_font(11)


GAS_NAMES = {"Oxygen", "ContaminatedOxygen", "CarbonDioxide", "Hydrogen",
             "Methane", "ChlorineGas", "Steam", "SourGas", "Vacuum"}
LIQUID_NAMES = {"Water", "DirtyWater", "Petroleum", "CrudeOil", "Magma",
                "Brine", "SaltWater", "LiquidOxygen", "Ethanol", "MoltenGlass"}


def guess_state(name):
    """给 demo 数据和老格式兜底；真实数据里相态由 mod 直接给。"""
    if name in GAS_NAMES:
        return "void" if name == "Vacuum" else "gas"
    if name in LIQUID_NAMES:
        return "liquid"
    if name in ("OutOfBounds", "Void"):
        return "void"
    return "solid"


def parse_legend(legend):
    """mod v0.3 起 legend 是 [{name,state}]；更早是纯字符串数组。"""
    names, states = [], []
    for e in legend:
        if isinstance(e, dict):
            names.append(e["name"])
            states.append(e.get("state") or guess_state(e["name"]))
        else:
            names.append(e)
            states.append(guess_state(e))
    return names, states

BASE = "http://127.0.0.1:7373"
# 开着机场时 http_proxy 会让本机请求也走代理，必须强制直连
_direct = urllib.request.build_opener(urllib.request.ProxyHandler({}))

# 手调的配色。没列到的元素按名字 hash 出一个稳定颜色，不会撞太厉害。
ELEMENT_COLORS = {
    "Vacuum":              (12, 12, 18),
    "OutOfBounds":         (0, 0, 0),
    "Void":                (0, 0, 0),
    "Neutronium":          (28, 24, 36),
    # 气体
    "Oxygen":              (150, 205, 255),
    "ContaminatedOxygen":  (150, 190, 120),
    "CarbonDioxide":       (90, 88, 96),
    "Hydrogen":            (220, 170, 235),
    "Methane":             (215, 195, 110),
    "ChlorineGas":         (200, 225, 130),
    "Steam":               (235, 235, 240),
    "SourGas":             (170, 200, 150),
    # 液体
    "Water":               (60, 130, 215),
    "DirtyWater":          (95, 120, 90),
    "Petroleum":           (60, 45, 40),
    "CrudeOil":            (45, 35, 30),
    "Magma":               (255, 110, 40),
    "Brine":               (80, 140, 180),
    "SaltWater":           (70, 150, 200),
    "LiquidOxygen":        (120, 180, 240),
    # 固体
    "SandStone":           (200, 175, 130),
    "Sand":                (225, 205, 155),
    "Granite":             (150, 145, 150),
    "IgneousRock":         (110, 100, 100),
    "SedimentaryRock":     (170, 150, 125),
    "Obsidian":            (55, 50, 60),
    "Dirt":                (140, 105, 75),
    "Clay":                (165, 120, 95),
    "Algae":               (105, 185, 110),
    "SlimeMold":           (110, 150, 70),
    "Coal":                (55, 52, 55),
    "Copper":              (200, 125, 70),
    "CopperOre":           (185, 115, 65),
    "Iron":                (175, 90, 80),
    "IronOre":             (160, 85, 75),
    "GoldAmalgam":         (215, 180, 90),
    "Abyssalite":          (95, 80, 130),
    "Ice":                 (195, 225, 245),
    "Snow":                (225, 240, 250),
    "BleachStone":         (200, 215, 170),
    "Fertilizer":          (130, 105, 70),
    "Rust":                (185, 110, 70),
    "Salt":                (225, 220, 215),
    "Wolframite":          (120, 110, 105),
}

# ASCII 模式用的单字符代号
ELEMENT_CHARS = {
    "Vacuum": ".", "OutOfBounds": " ", "Void": " ", "Neutronium": "#",
    "Oxygen": "o", "ContaminatedOxygen": "p", "CarbonDioxide": "c",
    "Hydrogen": "h", "Methane": "m", "ChlorineGas": "l", "Steam": "s",
    "Water": "W", "DirtyWater": "P", "Magma": "M", "CrudeOil": "O",
    "SandStone": "S", "Sand": "n", "Granite": "G", "IgneousRock": "I",
    "SedimentaryRock": "D", "Dirt": "d", "Clay": "y", "Algae": "a",
    "SlimeMold": "L", "Coal": "C", "Copper": "u", "CopperOre": "u",
    "Iron": "r", "IronOre": "r", "Abyssalite": "A", "Ice": "i",
}


def stable_color(name: str) -> tuple:
    """没配色的元素给一个稳定的、不太丑的颜色。"""
    h = zlib.crc32(name.encode())
    return (80 + (h & 0x7F), 80 + ((h >> 8) & 0x7F), 80 + ((h >> 16) & 0x7F))


def fetch(path: str) -> dict:
    with _direct.open(BASE + path, timeout=20) as r:
        return json.loads(r.read().decode("utf-8"))


def decode(payload: dict):
    """/raw 的 base64 → (元素下标, 摄氏温度, 千克) 三个二维数组。"""
    o = payload["origin"]
    w, h = o["w"], o["h"]
    buf = base64.b64decode(payload["data"])
    n = w * h

    idx = np.frombuffer(buf, dtype="<u2").reshape(-1, 5)[:, 0].astype(np.int32)
    rest = np.frombuffer(buf, dtype="<u1").reshape(n, 10)[:, 2:].copy()
    floats = rest.view("<f4").reshape(n, 2)

    names, states = parse_legend(payload["legend"])
    return (idx.reshape(h, w),
            (floats[:, 0] - 273.15).reshape(h, w),
            floats[:, 1].reshape(h, w),
            names, states, o)


def demo_data():
    """假数据：底下岩石、中间氧气、左上一坨水、右下一个热源。"""
    w, h = 120, 80
    legend = ["Vacuum", "Oxygen", "SandStone", "Water", "Magma", "CarbonDioxide"]
    idx = np.full((h, w), 1, dtype=np.int32)
    idx[h // 2:, :] = 2
    idx[10:26, 8:30] = 3
    idx[h - 12:, w - 26:] = 4
    idx[h // 2 - 8:h // 2, :] = 5

    temp = np.full((h, w), 22.0, dtype=np.float32)
    temp[h - 12:, w - 26:] = 1400.0
    yy, xx = np.mgrid[0:h, 0:w]
    d = np.sqrt((yy - (h - 6)) ** 2 + (xx - (w - 13)) ** 2)
    temp += np.clip(600 - d * 14, 0, 600)
    temp[10:26, 8:30] = 4.0

    mass = np.full((h, w), 1.8, dtype=np.float32)
    mass[h // 2:, :] = 1200.0
    mass[10:26, 8:30] = 1000.0
    mass[h // 2 - 8:h // 2, :] = 3.2          # CO2 那条带气压偏高
    mass[2:8, 60:100] = 0.08                  # 右上角一块接近真空
    names, states = parse_legend(legend)
    return idx, temp, mass, names, states, {"x": 0, "y": 0, "w": w, "h": h}


# ─────────────────────────── 画图 ───────────────────────────

def elements_rgb(idx, legend):
    lut = np.array([ELEMENT_COLORS.get(n, stable_color(n)) for n in legend], dtype=np.uint8)
    return lut[np.clip(idx, 0, len(legend) - 1)]


def temp_rgb(temp):
    """
    缺氧的温度尺度不是线性的 —— 20 度和 30 度天差地别，800 度和 900 度都一样是灾难。
    所以锚在几个有实际意义的点上分段：结冰 / 舒适 / 作物上限 / 烫 / 熔岩。
    """
    stops = [(-50, (40, 60, 150)), (0, (90, 160, 230)), (20, (110, 200, 140)),
             (30, (230, 220, 110)), (60, (235, 150, 60)), (125, (215, 60, 50)),
             (500, (255, 240, 235))]
    t = np.clip(np.nan_to_num(temp), stops[0][0], stops[-1][0])
    out = np.zeros(t.shape + (3,), dtype=np.uint8)
    for (a, ca), (b, cb) in zip(stops, stops[1:]):
        m = (t >= a) & (t <= b)
        if not m.any():
            continue
        f = ((t[m] - a) / (b - a))[:, None]
        out[m] = (np.array(ca) * (1 - f) + np.array(cb) * f).astype(np.uint8)
    return out


def pressure_rgb(mass, idx, states):
    """
    只画气体的气压，固液体压成暗色只留地形轮廓。

    把固体质量和气体质量放同一个色阶是错的：岩石一格一两千公斤，气体一格
    一两公斤，对数下来全糊成一片。而且没人关心岩石多重 —— 要看的是哪里
    气压低到复制体会窒息（<0.1kg），哪里高压顶得住。
    """
    state_arr = np.array([{"gas": 0, "liquid": 1, "solid": 2}.get(s, 3)
                          for s in states], dtype=np.int8)
    kind = state_arr[np.clip(idx, 0, len(states) - 1)]

    out = np.zeros(mass.shape + (3,), dtype=np.uint8)
    out[kind == 2] = (34, 34, 40)        # 固体：地形轮廓
    out[kind == 1] = (52, 30, 70)        # 液体：暗紫（避开低压的蓝）
    out[kind == 3] = (0, 0, 0)           # 界外

    # 气体：0kg 黑 → 0.1 危险蓝 → 1.5 正常绿 → 5 黄 → 20+ 红
    stops = [(0.0, (10, 10, 16)), (0.1, (40, 70, 160)), (0.6, (60, 160, 190)),
             (1.5, (90, 200, 120)), (4.0, (225, 210, 100)), (10.0, (230, 130, 60)),
             (30.0, (215, 60, 55))]
    g = kind == 0
    if g.any():
        m = np.clip(np.nan_to_num(mass[g]), 0, stops[-1][0])
        col = np.zeros((m.size, 3))
        for (a, ca), (b, cb) in zip(stops, stops[1:]):
            sel = (m >= a) & (m <= b)
            if not sel.any():
                continue
            f = ((m[sel] - a) / (b - a))[:, None]
            col[sel] = np.array(ca) * (1 - f) + np.array(cb) * f
        out[g] = col.astype(np.uint8)
    return out


def panel(rgb, scale, title, origin, ticks=True):
    h, w = rgb.shape[:2]
    im = Image.fromarray(rgb, "RGB").resize((w * scale, h * scale), Image.NEAREST)
    pad_l, pad_t = 46, 22
    canvas = Image.new("RGB", (im.width + pad_l + 8, im.height + pad_t + 20), (18, 18, 22))
    canvas.paste(im, (pad_l, pad_t))
    d = ImageDraw.Draw(canvas)
    d.text((pad_l, 6), title, fill=(235, 235, 235), font=FONT)

    if ticks:
        step = max(10, (w // 8 // 10) * 10)
        for gx in range(0, w, step):
            px = pad_l + gx * scale
            d.line([(px, pad_t), (px, pad_t + im.height)], fill=(255, 255, 255, 40), width=1)
            d.text((px + 2, pad_t + im.height + 4), str(origin["x"] + gx), fill=(150, 150, 160), font=FONT_SM)
        for gy in range(0, h, step):
            py = pad_t + gy * scale
            world_y = origin["y"] + h - 1 - gy
            d.line([(pad_l, py), (pad_l + im.width, py)], fill=(255, 255, 255, 40), width=1)
            d.text((4, py + 2), str(world_y), fill=(150, 150, 160), font=FONT_SM)
    return canvas


def compose(idx, temp, mass, legend, states, origin, scale, out):
    counts = np.bincount(idx.ravel(), minlength=len(legend))
    top = np.argsort(counts)[::-1][:14]

    panels = [
        panel(elements_rgb(idx, legend), scale, "元素", origin),
        panel(temp_rgb(temp), scale, f"温度  {temp.min():.0f}℃ ~ {temp.max():.0f}℃", origin),
        panel(pressure_rgb(mass, idx, states), scale,
              "气压（只画气体，固液体压暗留轮廓）", origin),
    ]
    gap = 14
    W = sum(p.width for p in panels) + gap * (len(panels) - 1)
    H = max(p.height for p in panels) + 130
    sheet = Image.new("RGB", (W, H), (18, 18, 22))
    x = 0
    for p in panels:
        sheet.paste(p, (x, 0))
        x += p.width + gap

    d = ImageDraw.Draw(sheet)
    y0 = max(p.height for p in panels) + 10
    d.text((8, y0), "图例（按占地面积）", fill=(235, 235, 235), font=FONT)
    total = idx.size
    for i, e in enumerate(top):
        name = legend[e]
        col, row = i // 5, i % 5
        bx, by = 8 + col * 240, y0 + 18 + row * 20
        d.rectangle([bx, by, bx + 14, by + 14],
                    fill=ELEMENT_COLORS.get(name, stable_color(name)))
        d.text((bx + 20, by + 1), f"{name}  {counts[e] / total * 100:.1f}%",
               fill=(210, 210, 215), font=FONT_SM)

    sheet.save(out)
    return sheet.size


def ascii_map(idx, temp, mass, legend, states, origin):
    lines = []
    h, w = idx.shape
    lines.append(f"区域 x={origin['x']}..{origin['x']+w-1}  y={origin['y']}..{origin['y']+h-1}"
                 f"（上面是 y 大的一头）")
    for r in range(h):
        row = "".join(ELEMENT_CHARS.get(legend[i], "?") for i in idx[r])
        lines.append(f"{origin['y'] + h - 1 - r:>4} |{row}|")
    used = sorted({legend[i] for i in np.unique(idx)})
    lines.append("")
    lines.append("字符表: " + "  ".join(f"{ELEMENT_CHARS.get(n,'?')}={n}" for n in used))
    lines.append(f"温度: 最低 {temp.min():.1f}℃  最高 {temp.max():.1f}℃  中位 {np.median(temp):.1f}℃")
    gas = np.array([s == "gas" for s in states])[np.clip(idx, 0, len(states) - 1)]
    if gas.any():
        lines.append(f"气压(仅气体): 最低 {mass[gas].min():.2f}kg  最高 {mass[gas].max():.2f}kg  "
                     f"中位 {np.median(mass[gas]):.2f}kg")
    return "\n".join(lines)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--x", type=int, default=0)
    ap.add_argument("--y", type=int, default=0)
    ap.add_argument("--w", type=int, default=0, help="0 = 整张地图")
    ap.add_argument("--h", type=int, default=0)
    ap.add_argument("--scale", type=int, default=3)
    ap.add_argument("--out", default="oni.png")
    ap.add_argument("--ascii", action="store_true")
    ap.add_argument("--demo", action="store_true")
    a = ap.parse_args()

    if a.demo:
        idx, temp, mass, legend, states, origin = demo_data()
    else:
        q = f"/raw?x={a.x}&y={a.y}"
        if a.w: q += f"&w={a.w}"
        if a.h: q += f"&h={a.h}"
        try:
            idx, temp, mass, legend, states, origin = decode(fetch(q))
        except Exception as e:
            sys.exit(f"连不上游戏：{e}\n（缺氧开着吗？mod 是 v0.2+ 吗？进存档了吗？）")

    if a.ascii:
        print(ascii_map(idx, temp, mass, legend, states, origin))
    else:
        size = compose(idx, temp, mass, legend, states, origin, a.scale, a.out)
        print(f"画好了 {a.out}  {size[0]}x{size[1]}  "
              f"区域 {origin['w']}x{origin['h']} @({origin['x']},{origin['y']})")


if __name__ == "__main__":
    main()
