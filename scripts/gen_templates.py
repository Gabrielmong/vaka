"""Generates src/SensorPanel/templates/*.json (+ manifest.json). Run: python scripts/gen_templates.py

The JSON files are what ships; edit the templates here and regenerate rather than hand-editing the JSON."""
import json, os

OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'src', 'SensorPanel', 'templates')
_group = [None]


def group(name): _group[0] = name


def base(type_, x, y, w=0, h=0, **kw):
    e = {'type': type_, 'x': round(x, 2), 'y': round(y, 2), 'w': round(w, 2), 'h': round(h, 2)}
    if _group[0]: e['group'] = _group[0]
    e.update({k: v for k, v in kw.items() if v is not None})
    return e


def rect(x, y, w, h, fill=None, radius=0, stroke=None, sw=1, gradient=None, opacity=None):
    return base('rect', x, y, w, h, fill=fill, radius=radius, stroke=stroke, strokeWidth=sw, gradient=gradient, opacity=opacity)


def ellipse(x, y, w, h, fill=None, stroke=None, sw=1, gradient=None, opacity=None):
    return base('ellipse', x, y, w, h, fill=fill, stroke=stroke, strokeWidth=sw, gradient=gradient, opacity=opacity)


def line(x, y, x2, y2, color, width=1, round_cap=False, opacity=None):
    return base('line', x, y, x2=x2, y2=y2, color=color, width=width, roundCap=round_cap, opacity=opacity)


def text(x, y, s, size=16, color='#ffffff', font='Segoe UI', weight=400, align='left', w=0, value=None, th=None, opacity=None):
    return base('text', x, y, w, 0, text=s, font=font, size=size, weight=weight, align=align, color=color,
                value=value, thresholds=th, opacity=opacity)


def bar(x, y, w, h, value, color, track='#2a2a2a', mn=0, mx=100, radius=0, vertical=False, th=None, gradient=None):
    return base('bar', x, y, w, h, value=value, min=mn, max=mx, color=color, track=track, radius=radius,
                vertical=vertical, thresholds=th, gradient=gradient)


def ring(x, y, d, value, color, track='#2a2a2a', mn=0, mx=100, thickness=12, start=135, sweep=270, cap=True, th=None, gradient=None,
         opacity=None):
    return base('ring', x, y, d, d, value=value, min=mn, max=mx, color=color, track=track, thickness=thickness,
                startAngle=start, sweep=sweep, roundCap=cap, thresholds=th, gradient=gradient, opacity=opacity)


def graph(x, y, w, h, value, color, mn=0, mx=100, duration=60, width=2, fill=None, bg=None, grid=0, grid_color=None,
          auto=False, gradient=None):
    return base('graph', x, y, w, h, value=value, min=mn, max=mx, color=color, duration=duration, width=width, fill=fill,
                background=bg, gridLines=grid, gridColor=grid_color, autoScale=auto, gradient=gradient)


def lin(angle, *colors):
    return {'kind': 'linear', 'angle': angle, 'colors': list(colors)}


def glow(color, alpha):
    """Radial gradient from color at the centre to fully transparent at the edge."""
    return {'kind': 'radial', 'colors': [f'{color}{alpha:02x}', f'{color}00']}


TEMP_TH = [{'gte': 80, 'color': '#ffb347'}, {'gte': 90, 'color': '#ff5a5a'}]
LOAD_TH = [{'gte': 85, 'color': '#ffb347'}, {'gte': 95, 'color': '#ff5a5a'}]

TEMPLATES = []


def template(id_, name, description, shows, background):
    def deco(fn):
        TEMPLATES.append((id_, name, description, shows, background, fn))
        return fn
    return deco


# ─────────────────────────────────────────────────────────────── 1. Duo
@template('duo', 'Duo', 'Two clean cards for CPU and GPU with RAM, power and the time underneath.',
          ['CPU', 'GPU', 'RAM', 'VRAM', 'Power', 'FPS', 'Clock'], '#0b0f14')
def duo():
    S, W = [], []
    card, track, label = '#141b24', '#232d3a', '#6b7a8c'
    for gx, who, accent in ((16, 'cpu', '#4da3ff'), (408, 'gpu', '#58d68d')):
        group(f'{who.upper()} card')
        S += [rect(gx, 16, 376, 300, card, 14),
              rect(gx + 20, 30, 28, 4, accent, 2),
              text(gx + 20, 42, who.upper(), 13, label, 'Bahnschrift', 600),
              text(gx + 20, 62, f'${who}.name', 18, '#e8eef5', weight=600),
              text(gx + 20, 210, 'LOAD', 12, label, 'Bahnschrift', 600),
              text(gx + 20, 266, 'CLOCK', 12, label, 'Bahnschrift', 600),
              text(gx + 200, 266, 'POWER', 12, label, 'Bahnschrift', 600)]
        W += [text(gx + 20, 104, f'{{{who}.temp:0}}°', 72, '#ffffff', 'Bahnschrift', 600, value=f'{who}.temp', th=TEMP_TH),
              graph(gx + 200, 104, 156, 72, f'{who}.load', accent, fill=accent + '33', width=2),
              text(gx + 20, 206, f'{{{who}.load:0}}%', 18, '#e8eef5', 'Bahnschrift', 600, 'right', 336),
              bar(gx + 20, 232, 336, 10, f'{who}.load', accent, track, radius=5, th=LOAD_TH),
              text(gx + 20, 286, f'{{{who}.clock:0.00}} GHz', 22, '#e8eef5', 'Bahnschrift'),
              text(gx + 200, 286, f'{{{who}.power:0}} W', 22, '#e8eef5', 'Bahnschrift')]
    group('Footer')
    S += [rect(16, 332, 768, 132, card, 14),
          text(36, 352, 'RAM', 12, label, 'Bahnschrift', 600),
          text(296, 352, 'VRAM', 12, label, 'Bahnschrift', 600)]
    W += [text(36, 376, '{ram.used:0.0} GB', 22, '#e8eef5', 'Bahnschrift'),
          bar(36, 416, 230, 10, 'ram.load', '#4da3ff', track, radius=5, th=LOAD_TH),
          text(296, 376, '{vram.used:0.0} / {vram.total:0} GB', 22, '#e8eef5', 'Bahnschrift'),
          bar(296, 416, 230, 10, 'vram.load', '#58d68d', track, radius=5, th=LOAD_TH),
          text(560, 350, '{time:HH:mm}', 48, '#ffffff', 'Bahnschrift', 600, 'right', 204),
          text(560, 406, '{date:ddd d MMM}', 16, label, 'Bahnschrift', 400, 'right', 204),
          text(560, 434, '{system.power:0} W  ·  {fps:0} FPS', 16, '#e8eef5', 'Bahnschrift', 400, 'right', 204)]
    return S, W


# ─────────────────────────────────────────────────────────────── 2. Gauges
@template('gauges', 'Gauges', 'Four neon rings for CPU and GPU temperature and load, with a side column for the time and memory.',
          ['CPU', 'GPU', 'RAM', 'VRAM', 'FPS', 'Clock'], '#000000')
def gauges():
    S, W = [], []
    track, label, F = '#161a1f', '#7c8591', 'Bahnschrift'
    cells = [  # (cx, top, key, title, ring colour, max-min, unit format, line under)
        (150, 0, 'cpu.temp', 'CPU TEMP', '#00e5ff', (20, 100), '{cpu.temp:0}°', '{cpu.clock:0.00} GHz'),
        (430, 0, 'gpu.temp', 'GPU TEMP', '#ff3df2', (20, 100), '{gpu.temp:0}°', '{gpu.clock:0.00} GHz'),
        (150, 240, 'cpu.load', 'CPU LOAD', '#00ff9c', (0, 100), '{cpu.load:0}%', '{cpu.power:0} W'),
        (430, 240, 'gpu.load', 'GPU LOAD', '#ffe600', (0, 100), '{gpu.load:0}%', '{gpu.power:0} W'),
    ]
    group('Gauges')
    S += [line(290, 24, 290, 456, '#15191e', 2), line(24, 240, 556, 240, '#15191e', 2)]
    for cx, top, key, title, color, (mn, mx), fmt, under in cells:
        d, x, y = 168, cx - 84, top + 18
        group(title.title())
        S += [text(x, y + 132, title, 12, label, F, 600, 'center', d)]
        W += [ring(x, y, d, key, color, track, mn, mx, 14, th=TEMP_TH if 'temp' in key else None),
              text(x, y + 64, fmt, 46, '#ffffff', F, 600, 'center', d),
              text(cx - 100, y + 186, under, 18, '#cfd6de', F, 400, 'center', 200)]
    group('Side')
    S += [rect(576, 16, 208, 448, '#0a0c0f', 16, '#1c2128', 1),
          line(596, 136, 764, 136, '#1c2128', 1),
          text(596, 156, 'CPU', 11, label, F, 600),
          text(596, 172, '$cpu.name', 13, '#cfd6de'),
          text(596, 204, 'GPU', 11, label, F, 600),
          text(596, 220, '$gpu.name', 13, '#cfd6de'),
          line(596, 252, 764, 252, '#1c2128', 1),
          text(596, 270, 'RAM', 11, label, F, 600),
          text(596, 344, 'VRAM', 11, label, F, 600)]
    W += [text(596, 36, '{time:HH:mm}', 54, '#ffffff', F, 600),
          text(596, 100, '{date:ddd d MMM}', 15, label, F),
          text(596, 288, '{ram.used:0.0} GB', 22, '#ffffff', F),
          bar(596, 320, 168, 6, 'ram.load', '#00ff9c', track, radius=3, th=LOAD_TH),
          text(596, 362, '{vram.used:0.0} GB', 22, '#ffffff', F),
          bar(596, 394, 168, 6, 'vram.load', '#ffe600', track, radius=3, th=LOAD_TH),
          text(596, 426, '{fps:0} FPS', 18, '#00e5ff', F, 600)]
    return S, W


# ─────────────────────────────────────────────────────────────── 3. Trends
@template('trends', 'Trends', 'Terminal-green history graphs of CPU and GPU load and temperature over the last minute.',
          ['CPU', 'GPU', 'RAM', 'VRAM', 'Power', 'FPS', 'Clock'], '#040806')
def trends():
    S, W = [], []
    G, F, dim, panel, edge = '#39ff88', 'Consolas', '#1f6b43', '#07110b', '#123a24'
    cells = [(16, 16, 'cpu.load', 'CPU LOAD', '{cpu.load:0}%', 0, 100), (408, 16, 'gpu.load', 'GPU LOAD', '{gpu.load:0}%', 0, 100),
             (16, 220, 'cpu.temp', 'CPU TEMP', '{cpu.temp:0} °C', 20, 100), (408, 220, 'gpu.temp', 'GPU TEMP', '{gpu.temp:0} °C', 20, 100)]
    for x, y, key, title, fmt, mn, mx in cells:
        group(title.title())
        S += [rect(x, y, 376, 192, panel, 6, edge, 1),
              text(x + 14, y + 14, title, 13, dim, F, 700),
              text(x + 14, y + 32, '$cpu.name' if key.startswith('cpu') else '$gpu.name', 11, dim, F)]
        W += [text(x + 14, y + 10, fmt, 24, G, F, 700, 'right', 348, value=key, th=TEMP_TH if 'temp' in key else LOAD_TH),
              graph(x + 14, y + 56, 348, 122, key, G, mn, mx, 60, 2, G + '1f', grid=3, grid_color=G + '1a')]
    group('Status line')
    S += [rect(16, 424, 768, 40, panel, 6, edge, 1),
          text(30, 436, '>', 16, dim, F, 700)]
    W += [text(48, 436, '{time:HH:mm:ss}   RAM {ram.used:0.0} GB   VRAM {vram.used:0.0} GB   PWR {system.power:0} W   FPS {fps:0}',
               16, G, F)]
    return S, W


# ─────────────────────────────────────────────────────────────── 4. Gaming HUD
@template('hud', 'Gaming HUD', 'A huge FPS counter with 1% lows and frame time, plus the GPU and CPU numbers that matter in game.',
          ['FPS', 'Frame time', 'GPU', 'CPU', 'Clock'], '#000000')
def hud():
    S, W = [], []
    R, F, grey, dark = '#ff2d3d', 'Bahnschrift', '#8a8a8a', '#2a080c'
    group('Header')
    S += [rect(0, 0, 800, 4, R),
          text(24, 20, 'GAME MODE', 15, R, F, 700)]
    S += [line(150 + i * 16, 36, 162 + i * 16, 20, R, 6, opacity=round(0.9 - i * 0.14, 2)) for i in range(6)]
    W += [text(576, 18, '{time:HH:mm}', 22, '#ffffff', F, 600, 'right', 200)]
    group('FPS')
    S += [text(24, 200, 'FPS', 20, R, F, 700),
          text(24, 240, '1% LOW', 12, grey, F, 600),
          text(200, 240, 'FRAME TIME', 12, grey, F, 600),
          text(24, 304, 'FRAME TIME · LAST 60 S', 11, grey, F, 600),
          line(424, 64, 404, 464, R, 2, opacity=0.35)]
    W += [text(20, 62, '{fps:0}', 150, '#ffffff', F, 700, value='fps',
               th=None),
          text(24, 258, '{fps.low1:0}', 36, '#ffffff', F, 600),
          text(200, 258, '{fps.frametime:0.0} ms', 36, '#ffffff', F, 600),
          graph(24, 322, 360, 138, 'fps.frametime', R, 0, 50, 60, 2, R + '26', grid=2, grid_color=R + '22', auto=True)]
    rows = [('gpu.temp', 'GPU TEMP', '{gpu.temp:0}°', 20, 100), ('gpu.load', 'GPU LOAD', '{gpu.load:0}%', 0, 100),
            ('gpu.power', 'GPU POWER', '{gpu.power:0} W', 0, 350), ('cpu.load', 'CPU LOAD', '{cpu.load:0}%', 0, 100)]
    for i, (key, title, fmt, mn, mx) in enumerate(rows):
        x, y = 456, 72 + i * 92
        group(title.title())
        S += [text(x, y, title, 13, grey, F, 600)]
        W += [text(x, y - 8, fmt, 30, '#ffffff', F, 600, 'right', 320,
                   value=key, th=TEMP_TH if 'temp' in key else None),
              bar(x, y + 34, 320, 16, key, R, dark, mn, mx)]
        # Slanted cuts turn the bar into angled segments (drawn over it, so they live with the widgets).
        W += [line(x + 4 + s * 20, y + 52, x + 12 + s * 20, y + 32, '#000000', 4) for s in range(16)]
    group('GPU name')
    S += [text(456, 444, '$gpu.name', 14, grey, F)]
    return S, W


# ─────────────────────────────────────────────────────────────── 5. Cooling
def _fan_icon(x, y, color, hub):
    """A 64 px fan: frame, ring, four blades and a hub."""
    cx, cy = x + 32, y + 32
    return [rect(x - 4, y - 4, 72, 72, None, 12, '#1d4560', 1),
            ellipse(x, y, 64, 64, None, color, 2),
            ellipse(cx - 6, y + 6, 12, 22, color, opacity=0.8), ellipse(cx - 6, cy + 4, 12, 22, color, opacity=0.8),
            ellipse(x + 6, cy - 6, 22, 12, color, opacity=0.8), ellipse(cx + 4, cy - 6, 22, 12, color, opacity=0.8),
            ellipse(cx - 7, cy - 7, 14, 14, hub, color, 2)]


@template('cooling', 'Cooling', 'Every temperature and every fan: CPU, GPU, hotspot, board and disk, plus up to six fans and the pump.',
          ['Temperatures', 'Fans', 'Pump', 'Clock'], '#06121c')
def cooling():
    S, W = [], []
    ICE, F, card, label, track = '#7fd4ff', 'Bahnschrift', '#0b1d2b', '#6f9bb8', '#12324a'
    group('Temperatures')
    S += [rect(16, 16, 284, 448, card, 14),
          text(36, 36, 'TEMPERATURES', 13, ICE, F, 700)]
    for i, (key, title) in enumerate([('cpu.temp', 'CPU'), ('gpu.temp', 'GPU'), ('gpu.hotspot', 'GPU HOTSPOT'),
                                      ('board.temp', 'BOARD'), ('disk.temp', 'SYSTEM DISK')]):
        y = 76 + i * 76
        S += [text(36, y + 8, title, 13, label, F, 600)]
        W += [text(36, y, f'{{{key}:0}}°', 32, '#ffffff', F, 600, 'right', 244, value=key, th=TEMP_TH),
              bar(36, y + 40, 244, 6, key, ICE, track, 20, 100, radius=3, th=TEMP_TH)]
    tiles = [(f'fan.{n}', f'FAN {n}', 2500) for n in range(1, 7)] + [('pump', 'PUMP', 5000)]
    for i, (key, title, mx) in enumerate(tiles):
        tx, ty = 316 + (i % 4) * 120, 16 + (i // 4) * 230
        group(title.title())
        S += [rect(tx, ty, 108, 218, card, 12)]
        S += _fan_icon(tx + 22, ty + 22, ICE if key != 'pump' else '#a78bff', card)
        S += [text(tx, ty + 104, title, 12, label, F, 600, 'center', 108),
              text(tx, ty + 160, 'RPM', 11, label, F, 400, 'center', 108)]
        W += [text(tx, ty + 124, f'{{{key}:0}}', 26, '#ffffff', F, 600, 'center', 108),
              bar(tx + 14, ty + 188, 80, 8, key, ICE if key != 'pump' else '#a78bff', track, 0, mx, radius=4)]
    group('Clock')
    tx, ty = 316 + 3 * 120, 246
    S += [rect(tx, ty, 108, 218, card, 12),
          text(tx, ty + 150, 'TOTAL', 11, label, F, 600, 'center', 108)]
    W += [text(tx, ty + 30, '{time:HH:mm}', 30, '#ffffff', F, 600, 'center', 108),
          text(tx, ty + 72, '{date:ddd d}', 15, label, F, 400, 'center', 108),
          text(tx, ty + 168, '{system.power:0} W', 20, ICE, F, 600, 'center', 108)]
    return S, W


# ─────────────────────────────────────────────────────────────── 6. Clock
@template('clock', 'Clock', 'A calm cream clock face with the date, and a thin strip of CPU and GPU numbers.',
          ['Clock', 'Date', 'CPU', 'GPU'], '#f4efe6')
def clock():
    S, W = [], []
    INK, F, soft, rule = '#2b2b2b', 'Segoe UI Variable', '#8a8174', '#d8d0c3'
    group('Time')
    W += [text(0, 64, '{time:HH:mm}', 190, INK, F, 300, 'center', 800),
          text(0, 262, '{date:dddd d MMMM}', 30, '#6b645a', F, 400, 'center', 800)]
    group('Strip')
    S += [line(80, 334, 720, 334, rule, 1)]
    cols = [('cpu.temp', 'CPU', '{cpu.temp:0}°', 20, 100), ('cpu.load', 'CPU LOAD', '{cpu.load:0}%', 0, 100),
            ('gpu.temp', 'GPU', '{gpu.temp:0}°', 20, 100), ('gpu.load', 'GPU LOAD', '{gpu.load:0}%', 0, 100)]
    for i, (key, title, fmt, mn, mx) in enumerate(cols):
        x = 80 + i * 160
        S += [text(x, 362, title, 13, soft, F, 600, 'center', 160)]
        W += [text(x, 386, fmt, 34, INK, F, 400, 'center', 160, value=key, th=TEMP_TH if 'temp' in key else None),
              bar(x + 40, 440, 80, 3, key, INK, '#e2d9cb', mn, mx, radius=1.5)]
    return S, W


# ─────────────────────────────────────────────────────────────── 7. Power
@template('power', 'Power', 'Estimated whole-system power in big amber digits, with two-minute CPU and GPU power graphs.',
          ['Power', 'CPU', 'GPU', 'FPS', 'Clock'], '#0d0a05')
def power():
    S, W = [], []
    A, A2, F, label, card, light = '#ffb000', '#ff7a1a', 'Bahnschrift', '#8c7b5e', '#17110a', '#fff4dc'
    group('System power')
    S += [text(32, 32, 'SYSTEM POWER', 14, A, F, 700),
          text(32, 54, 'ESTIMATED AT THE WALL', 11, label, F, 600)]
    W += [text(26, 90, '{system.power:0} W', 112, A, F, 600)]
    group('Breakdown')
    for i, (key, title, fmt, color, mx) in enumerate([('cpu.power', 'CPU', '{cpu.power:0} W', A, 200),
                                                       ('gpu.power', 'GPU', '{gpu.power:0} W', A2, 350)]):
        x = 32 + i * 180
        S += [text(x, 250, title, 12, label, F, 600)]
        W += [text(x, 270, fmt, 34, light, F, 600),
              bar(x, 316, 150, 6, key, color, '#2a2010', 0, mx, radius=3)]
    group('Footer')
    S += [line(32, 352, 392, 352, '#2a2010', 1),
          text(32, 372, 'FPS', 12, label, F, 600),
          text(212, 372, 'TIME', 12, label, F, 600)]
    W += [text(32, 392, '{fps:0}', 40, light, F, 600),
          text(212, 392, '{time:HH:mm}', 40, light, F, 600),
          text(212, 440, '{date:ddd d MMM}', 14, label, F)]
    for i, (key, title, color) in enumerate([('cpu.power', 'CPU POWER · 2 MIN', A), ('gpu.power', 'GPU POWER · 2 MIN', A2)]):
        y = 16 + i * 232
        group(title.split(' ·')[0].title())
        S += [rect(424, y, 360, 216, card, 12),
              text(440, y + 16, title, 12, '#a3741a', F, 600)]
        W += [text(440, y + 12, f'{{{key}:0}} W', 20, light, F, 600, 'right', 328),
              graph(440, y + 48, 328, 152, key, color, 0, 150, 120, 2, color + '26', grid=2, grid_color=color + '1a', auto=True)]
    return S, W


# ─────────────────────────────────────────────────────────────── 8. Overview
@template('overview', 'Overview', 'Everything at once: CPU, GPU, memory, network, four drives, power, FPS and the time.',
          ['CPU', 'GPU', 'RAM', 'VRAM', 'Drives', 'Disk', 'Network', 'Power', 'FPS', 'Clock'], '#101214')
def overview():
    S, W = [], []
    C, F, panel, label, txt, track = '#22d3ee', 'Segoe UI', '#1a1d21', '#8b949e', '#e5e7eb', '#2b3036'
    for x, who in ((16, 'cpu'), (276, 'gpu')):
        group(who.upper())
        S += [rect(x, 16, 252, 160, panel, 10),
              text(x + 14, 28, who.upper(), 11, C, F, 700),
              text(x + 14, 46, f'${who}.name', 12, label, F)]
        W += [text(x + 14, 74, f'{{{who}.temp:0}}°', 38, txt, F, 600, value=f'{who}.temp', th=TEMP_TH),
              text(x + 14, 74, f'{{{who}.load:0}}%', 38, txt, F, 600, 'right', 224, value=f'{who}.load', th=LOAD_TH),
              bar(x + 14, 124, 224, 6, f'{who}.load', C, track, radius=3, th=LOAD_TH),
              text(x + 14, 146, f'{{{who}.clock:0.00}} GHz', 13, label, F),
              text(x + 14, 146, f'{{{who}.power:0}} W', 13, label, F, 400, 'right', 224)]
    group('Time')
    S += [rect(536, 16, 248, 160, panel, 10)]
    W += [text(536, 36, '{time:HH:mm}', 52, txt, F, 600, 'center', 248),
          text(536, 104, '{date:dddd d MMM}', 14, label, F, 400, 'center', 248),
          text(536, 140, '{fps:0} FPS  ·  {system.power:0} W', 14, C, F, 600, 'center', 248)]
    group('Memory')
    S += [rect(16, 184, 380, 120, panel, 10)]
    for i, (key, title, fmt) in enumerate([('ram', 'RAM', '{ram.used:0.0} GB used  ·  {ram.free:0.0} GB free'),
                                           ('vram', 'VRAM', '{vram.used:0.0} / {vram.total:0} GB')]):
        y = 198 + i * 52
        S += [text(30, y, title, 11, C, F, 700)]
        W += [text(30, y - 2, fmt, 13, txt, F, 400, 'right', 352),
              bar(30, y + 22, 352, 6, f'{key}.load', C, track, radius=3, th=LOAD_TH)]
    group('Network')
    S += [rect(404, 184, 380, 120, panel, 10),
          text(418, 198, 'NETWORK', 11, C, F, 700)]
    W += [text(418, 222, '↓ {net.down:0.00} MB/s', 15, txt, F),
          text(418, 252, '↑ {net.up:0.00} MB/s', 15, label, F),
          graph(580, 198, 190, 92, 'net.down', C, 0, 10, 60, 1.5, C + '22', auto=True)]
    group('Drives')
    S += [rect(16, 312, 768, 152, panel, 10)]
    for n in range(1, 5):
        x = 30 + (n - 1) * 188
        S += [text(x, 326, f'$drive{n}', 13, C, F, 700)]
        W += [text(x, 322, f'{{drive.${n}.usedPct:0}}%', 18, txt, F, 600, 'right', 168),
              bar(x, 356, 168, 6, f'drive.${n}.usedPct', C, track, radius=3, th=LOAD_TH),
              text(x, 372, f'{{drive.${n}.freeGB:0}} GB free', 12, label, F)]
    S += [line(30, 404, 770, 404, track, 1),
          text(30, 422, 'SYSTEM DISK', 11, C, F, 700)]
    W += [text(130, 420, 'read {disk.read:0.0} MB/s   ·   write {disk.write:0.0} MB/s   ·   {disk.temp:0} °C', 13, txt, F)]
    return S, W


# ─────────────────────────────────────────────────────────────── 9. Storage and Net
@template('storage-net', 'Storage and Net', 'Up to four drives with free space, plus disk and network activity graphs.',
          ['Drives', 'Disk', 'Network', 'Clock'], '#0f0b1a')
def storage_net():
    S, W = [], []
    V, P, F, card, label, txt, track = '#a78bfa', '#f472b6', 'Segoe UI', '#1a1430', '#8f86a8', '#ede9fe', '#2c2448'
    group('Drives')
    S += [rect(16, 16, 454, 448, card, 14),
          text(36, 34, 'DRIVES', 12, V, F, 700)]
    W += [text(36, 30, '{time:HH:mm}', 14, label, F, 400, 'right', 414)]
    for n in range(1, 5):
        y = 70 + (n - 1) * 96
        S += [text(36, y, f'$drive{n}', 22, txt, F, 600)]
        W += [text(36, y, f'{{drive.${n}.usedPct:0}}%', 22, txt, F, 600, 'right', 414, value=f'drive.${n}.usedPct', th=LOAD_TH),
              bar(36, y + 36, 414, 10, f'drive.${n}.usedPct', V, track, radius=5, th=LOAD_TH, gradient=lin(0, V, P)),
              text(36, y + 56, f'{{drive.${n}.freeGB:0}} GB free', 13, label, F),
              text(36, y + 56, f'{{drive.${n}.totalGB:0}} GB', 13, label, F, 400, 'right', 414)]
    group('System disk')
    S += [rect(486, 16, 298, 212, card, 14),
          text(502, 34, 'SYSTEM DISK', 12, V, F, 700)]
    W += [text(502, 28, '{disk.temp:0} °C', 18, txt, F, 600, 'right', 266, value='disk.temp', th=TEMP_TH),
          graph(502, 66, 266, 110, 'disk.read', V, 0, 50, 60, 2, V + '26', auto=True),
          graph(502, 66, 266, 110, 'disk.write', P, 0, 50, 60, 2, auto=True),
          text(502, 196, 'read {disk.read:0.0}', 13, V, F, 600),
          text(502, 196, 'write {disk.write:0.0} MB/s', 13, P, F, 600, 'right', 266)]
    group('Network')
    S += [rect(486, 236, 298, 228, card, 14),
          text(502, 254, 'NETWORK', 12, V, F, 700)]
    W += [graph(502, 286, 266, 126, 'net.down', V, 0, 10, 60, 2, V + '26', auto=True),
          graph(502, 286, 266, 126, 'net.up', P, 0, 10, 60, 2, auto=True),
          text(502, 432, '↓ {net.down:0.00}', 13, V, F, 600),
          text(502, 432, '↑ {net.up:0.00} MB/s', 13, P, F, 600, 'right', 266)]
    return S, W


# ─────────────────────────────────────────────────────────────── 10. Synthwave
@template('synthwave', 'Synthwave', 'A neon sunset with glowing rings for CPU and GPU, eight core bars on the grid, and FPS.',
          ['CPU', 'GPU', 'CPU cores', 'FPS', 'Clock'], '#0d0221')
def synthwave():
    S, W = [], []
    PINK, CYAN, F, ground = '#ff2e97', '#00f0ff', 'Bahnschrift', '#0d0221'
    group('Sky')
    S += [rect(0, 0, 800, 300, gradient=lin(90, '#0d0221', '#3b0b5c', '#8a1d6e')),
          ellipse(310, 150, 180, 180, gradient=lin(90, '#ffd319', '#ff8c3a', PINK), opacity=0.92)]
    # Sun stripes: each cut is the circle's chord at that height, painted in the sky colour there.
    def sky(y):
        a, b, t = ((0x0d, 0x02, 0x21), (0x3b, 0x0b, 0x5c), y / 150) if y < 150 else ((0x3b, 0x0b, 0x5c), (0x8a, 0x1d, 0x6e), (y - 150) / 150)
        return '#' + ''.join(f'{round(p + (q - p) * t):02x}' for p, q in zip(a, b))
    for i in range(4):
        y, h = 236 + i * 16, 3 + i * 2
        half = (90 ** 2 - (y + h / 2 - 240) ** 2) ** 0.5
        S += [rect(400 - half - 1, y, 2 * half + 2, h, sky(y + h / 2))]
    group('Grid')
    S += [rect(0, 300, 800, 180, ground), line(0, 300, 800, 300, PINK, 2)]
    S += [line(0, y, 800, y, PINK, 1, opacity=0.55) for y in (308, 320, 338, 362, 394, 434)]
    for i in range(-6, 7):  # rays from the vanishing point, clipped where they leave the canvas
        x0, x1 = 400 + i * 40, 400 + i * 200
        t = 1 if 0 <= x1 <= 800 else ((800 if x1 > 800 else 0) - x0) / (x1 - x0)
        S += [line(x0, 300, x0 + (x1 - x0) * t, 300 + 180 * t, PINK, 1, opacity=0.45)]
    rings = [(28, 'cpu.temp', 'CPU TEMP', '{cpu.temp:0}°', CYAN, 20, 100), (172, 'cpu.load', 'CPU LOAD', '{cpu.load:0}%', CYAN, 0, 100),
             (500, 'gpu.temp', 'GPU TEMP', '{gpu.temp:0}°', PINK, 20, 100), (644, 'gpu.load', 'GPU LOAD', '{gpu.load:0}%', PINK, 0, 100)]
    for x, key, title, fmt, color, mn, mx in rings:
        d, y = 128, 36
        group(title.title())
        S += [text(x, y + 98, title, 11, '#ffb3e6', F, 600, 'center', d)]
        W += [ring(x, y, d, key, color, '#00000000', mn, mx, 22, th=TEMP_TH if 'temp' in key else None, opacity=0.18),
              ring(x, y, d, key, color, '#ffffff14', mn, mx, 8, th=TEMP_TH if 'temp' in key else None),
              text(x, y + 46, fmt, 32, '#ffffff', F, 600, 'center', d)]
    group('Time')
    W += [text(300, 40, '{time:HH:mm}', 44, '#ffffff', F, 600, 'center', 200),
          text(300, 98, '{fps:0} FPS', 20, '#ffd319', F, 600, 'center', 200)]
    group('Cores')
    for i in range(8):
        x = 226 + i * 46
        W += [bar(x, 330, 26, 104, f'cpu.core{i + 1}', CYAN, '#ffffff12', vertical=True, radius=3,
                  gradient=lin(0, CYAN, PINK))]
        S += [text(x - 7, 446, f'C{i + 1}', 11, '#ffb3e6', F, 600, 'center', 40)]
    group('Names')
    S += [text(24, 398, '$cpu.name', 13, '#ffb3e6', F, 600),
          text(576, 398, '$gpu.name', 13, '#ffb3e6', F, 600, 'right', 200)]
    W += [text(24, 420, '{cpu.clock:0.00} GHz · {cpu.power:0} W', 18, '#ffffff', F, 600),
          text(576, 420, '{gpu.clock:0.00} GHz · {gpu.power:0} W', 18, '#ffffff', F, 600, 'right', 200)]
    return S, W


# ─────────────────────────────────────────────────────────────── 11. Terminal
@template('terminal', 'Terminal', 'Green phosphor on black: a console-style readout of CPU, GPU, memory and disk with a load graph.',
          ['CPU', 'GPU', 'RAM', 'VRAM', 'Drives', 'Network', 'Power', 'FPS', 'Clock'], '#030803')
def terminal():
    S, W = [], []
    G, DIM, DARK, F = '#39ff6a', '#1f9a42', '#0c2612', 'Consolas'
    group('Scanlines')
    S += [line(0, y, 800, y, '#000000', 1, opacity=0.35) for y in range(2, 480, 4)]
    group('Header')
    S += [text(24, 16, 'panel@sensors:~$ watch -n1 status', 16, DIM, F),
          line(24, 46, 776, 46, DIM, 1)]
    W += [text(576, 16, '{time:HH:mm:ss}', 16, G, F, 400, 'right', 200)]
    rows = [('CPU', '{cpu.temp:0}°C {cpu.clock:0.00}GHz {cpu.power:0}W', 'cpu.load', '{cpu.load:0}%'),
            ('GPU', '{gpu.temp:0}°C {gpu.clock:0.00}GHz {gpu.power:0}W', 'gpu.load', '{gpu.load:0}%'),
            ('MEM', '{ram.used:0.0}G used {ram.free:0.0}G free', 'ram.load', '{ram.load:0}%'),
            ('VRAM', '{vram.used:0.0}G of {vram.total:0}G', 'vram.load', '{vram.load:0}%'),
            ('DISK', '$drive1 {drive.$1.freeGB:0}G free', 'drive.$1.usedPct', '{drive.$1.usedPct:0}%')]
    for i, (title, fmt, key, pct) in enumerate(rows):
        y = 64 + i * 38
        group(title.title())
        S += [text(24, y, title, 16, DIM, F, 700)]
        W += [text(88, y, fmt, 16, G, F),
              bar(400, y + 3, 290, 14, key, G, DARK, th=LOAD_TH),
              text(696, y, pct, 16, G, F, 700, 'right', 80, value=key, th=LOAD_TH)]
    group('Load graph')
    S += [line(24, 258, 776, 258, DIM, 1),
          text(24, 272, '$ cpu load, last 2 min', 14, DIM, F),
          rect(24, 298, 400, 136, None, 0, DIM, 1)]
    W += [graph(26, 300, 396, 132, 'cpu.load', G, 0, 100, 120, 1.5, G + '1f', grid=3, grid_color=DARK)]
    group('Stats')
    stats = [('FPS', '{fps:0}  (1% low {fps.low1:0})'), ('POWER', '{system.power:0} W'),
             ('NET', '↓{net.down:0.00} ↑{net.up:0.00} MB/s'), ('DISK I/O', 'r {disk.read:0.0} w {disk.write:0.0} MB/s'),
             ('DATE', '{date:yyyy-MM-dd ddd}')]
    for i, (title, fmt) in enumerate(stats):
        y = 272 + i * 30
        S += [text(452, y, title, 14, DIM, F, 700)]
        W += [text(548, y, fmt, 14, G, F)]
    group('Prompt')
    S += [text(24, 446, 'panel@sensors:~$', 16, DIM, F), rect(186, 448, 10, 18, G)]
    return S, W


# ─────────────────────────────────────────────────────────────── 12. Aurora
@template('aurora', 'Aurora', 'Frosted glass cards over a soft violet-to-teal glow: CPU and GPU rings, memory, network and the time.',
          ['CPU', 'GPU', 'RAM', 'VRAM', 'Network', 'Power', 'FPS', 'Clock'], '#120c2a')
def aurora():
    S, W = [], []
    V, T, F, soft, glass, edge, track = '#a78bfa', '#2dd4bf', 'Segoe UI Variable', '#c4c0e0', '#ffffff14', '#ffffff2e', '#ffffff1a'
    group('Glow')
    S += [rect(0, 0, 800, 480, gradient=lin(135, '#140d33', '#1b2a5c', '#0c4a4f')),
          # Soft glows: circles whose radial gradient fades to transparent at the edge.
          ellipse(0, 0, 440, 440, gradient=glow('#7c3aed', 0x70)),
          ellipse(360, 40, 440, 440, gradient=glow('#14b8a6', 0x60)),
          ellipse(220, 100, 340, 340, gradient=glow('#3b82f6', 0x30))]
    group('Time')
    S += [rect(16, 16, 368, 216, glass, 20, edge, 1)]
    W += [text(16, 34, '{time:HH:mm}', 88, '#ffffff', F, 300, 'center', 368),
          text(16, 146, '{date:dddd d MMMM}', 18, soft, F, 400, 'center', 368),
          text(16, 184, '{system.power:0} W  ·  {fps:0} FPS', 16, T, F, 600, 'center', 368)]
    group('Memory')
    S += [rect(16, 248, 368, 216, glass, 20, edge, 1)]
    for i, (key, title, fmt, color) in enumerate([('ram', 'Memory', '{ram.used:0.0} GB used · {ram.free:0.0} free', V),
                                                  ('vram', 'Video memory', '{vram.used:0.0} GB of {vram.total:0} GB', T)]):
        y = 268 + i * 66
        S += [text(36, y, title, 14, soft, F, 600)]
        W += [text(36, y, fmt, 14, '#ffffff', F, 400, 'right', 328),
              bar(36, y + 28, 328, 8, f'{key}.load', color, track, radius=4, gradient=lin(0, V, T), th=LOAD_TH)]
    S += [text(36, 404, 'Network', 14, soft, F, 600)]
    W += [text(36, 428, '↓ {net.down:0.00} MB/s', 16, '#ffffff', F),
          text(200, 428, '↑ {net.up:0.00} MB/s', 16, soft, F, 400, 'right', 164)]
    for i, who in enumerate(('cpu', 'gpu')):
        x, y = 400, 16 + i * 232
        group(who.upper())
        S += [rect(x, y, 384, 216, glass, 20, edge, 1),
              text(x + 196, y + 22, who.upper(), 13, T if who == 'gpu' else V, F, 700),
              text(x + 196, y + 40, f'${who}.name', 13, soft, F, 400, 'left', 168),
              text(x + 196, y + 122, 'LOAD', 11, soft, F, 600),
              text(x + 196, y + 164, 'CLOCK', 11, soft, F, 600),
              text(x + 290, y + 164, 'POWER', 11, soft, F, 600)]
        W += [ring(x + 24, y + 28, 160, f'{who}.temp', V, track, 20, 100, 12, gradient=lin(0, V, T), th=TEMP_TH),
              text(x + 24, y + 82, f'{{{who}.temp:0}}°', 44, '#ffffff', F, 300, 'center', 160, value=f'{who}.temp', th=TEMP_TH),
              text(x + 24, y + 140, 'TEMP', 11, soft, F, 600, 'center', 160),
              text(x + 196, y + 72, f'{{{who}.load:0}}%', 36, '#ffffff', F, 300),
              bar(x + 196, y + 140, 164, 6, f'{who}.load', V, track, radius=3, gradient=lin(0, V, T), th=LOAD_TH),
              text(x + 196, y + 180, f'{{{who}.clock:0.00}} GHz', 15, '#ffffff', F),
              text(x + 290, y + 180, f'{{{who}.power:0}} W', 15, '#ffffff', F)]
    return S, W


def main():
    os.makedirs(OUT, exist_ok=True)
    manifest = []
    for id_, name, desc, shows, bg, fn in TEMPLATES:
        _group[0] = None
        static, widgets = fn()
        layout = {'version': 1, 'canvas': {'width': 800, 'height': 480, 'background': bg}, 'scale': 'fit',
                  'aliases': {}, 'static': static, 'widgets': widgets}
        with open(os.path.join(OUT, id_ + '.json'), 'w', encoding='utf-8', newline='\n') as f:
            json.dump(layout, f, ensure_ascii=False, indent=2)
        manifest.append({'id': id_, 'name': name, 'description': desc, 'shows': shows})
    with open(os.path.join(OUT, 'manifest.json'), 'w', encoding='utf-8', newline='\n') as f:
        json.dump(manifest, f, ensure_ascii=False, indent=2)
    print('wrote', len(manifest), 'templates')


main()
