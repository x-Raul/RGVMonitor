"""Genera icon.ico (multi-tamaño) y la vista previa del logo de RG Valorant Monitor.

Logo: retícula de "monitor" — anillo partido en cruz, marcas de mira, barrido de
radar y un rombo central. Todo se define en un lienzo de 256 (igual que icon.svg)
y se renderiza a 4x con reducción Lanczos para bordes suaves.

Los tamaños pequeños (<= 32 px) usan una versión simplificada: sin barrido ni
punto de detección y con trazos más gruesos, para que se lea en la barra de tareas.
"""
import io, math, os, struct

import numpy as np
from PIL import Image, ImageDraw

SS = 4          # supersampling
BASE = 256      # lienzo lógico (coincide con el viewBox de icon.svg)

ACCENT_A = np.array([255, 92, 110])   # coral (arriba-izquierda)
ACCENT_B = np.array([232, 24, 72])    # carmesí (abajo-derecha)
BG_TOP = np.array([36, 26, 38])
BG_BOT = np.array([10, 9, 14])
WHITE = (244, 242, 247, 255)


def squircle_mask(s, radius_frac=0.235):
    mask = Image.new("L", (s, s), 0)
    ImageDraw.Draw(mask).rounded_rectangle([0, 0, s - 1, s - 1], radius=int(s * radius_frac), fill=255)
    return mask


def linear_gradient(s, c0, c1, diagonal=False):
    y, x = np.mgrid[0:s, 0:s] / (s - 1)
    t = ((x + y) / 2) if diagonal else y
    rgb = c0[None, None, :] + (c1 - c0)[None, None, :] * t[..., None]
    return rgb


def make_frame(size):
    s = size * SS
    k = s / BASE                      # escala lógica → píxeles
    small = size <= 32
    c = s / 2

    # ── Fondo: degradado vertical + halo suave del acento detrás del centro.
    bg = linear_gradient(s, BG_TOP, BG_BOT)
    y, x = np.mgrid[0:s, 0:s]
    d = np.hypot(x - c, y - c) / s
    glow = np.clip(1 - d / 0.42, 0, 1) ** 2 * 0.22
    bg = bg * (1 - glow[..., None]) + ACCENT_B[None, None, :] * glow[..., None]
    img = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    img.paste(Image.fromarray(bg.astype(np.uint8), "RGB"), (0, 0), squircle_mask(s))

    # Capa del acento (se dibuja como máscara y se rellena con degradado diagonal).
    accent_mask = Image.new("L", (s, s), 0)
    am = ImageDraw.Draw(accent_mask)
    white = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    wd = ImageDraw.Draw(white)

    # ── Barrido de radar (cuadrante superior derecho) con cola que se desvanece.
    if not small:
        r_sweep = 66 * k
        ang = (np.degrees(np.arctan2(y - c, x - c)) + 360) % 360   # 0° = este, sentido horario
        # El borde de ataque está a -45° (315°) y la cola se extiende 80° hacia atrás.
        lead, span = 315.0, 80.0
        behind = (lead - ang) % 360
        inside = (np.hypot(x - c, y - c) <= r_sweep) & (behind <= span)
        alpha = np.where(inside, np.clip(1 - behind / span, 0, 1) ** 1.3 * 0.85, 0)
        sweep = Image.fromarray((alpha * 255).astype(np.uint8), "L")
        accent_mask = Image.fromarray(np.maximum(np.array(accent_mask), np.array(sweep)))
        am = ImageDraw.Draw(accent_mask)
        # Línea del borde de ataque.
        lx = c + r_sweep * math.cos(math.radians(lead))
        ly = c + r_sweep * math.sin(math.radians(lead))
        sx = c + 30 * k * math.cos(math.radians(lead))
        sy = c + 30 * k * math.sin(math.radians(lead))
        am.line([(sx, sy), (lx, ly)], fill=235, width=int(5 * k))

    # ── Anillo partido en cruz.
    ring_r = 76 * k
    ring_w = (16 if small else 11) * k
    gap = 34 if small else 30          # grados de hueco en cada eje
    box = [c - ring_r, c - ring_r, c + ring_r, c + ring_r]
    for start in (0, 90, 180, 270):
        wd.arc(box, start + gap / 2, start + 90 - gap / 2, fill=WHITE, width=int(ring_w))

    # ── Marcas de mira: cuñas afiladas hacia el centro que atraviesan los huecos.
    t_in, t_out = (52, 102) if small else (48, 104)
    w_out = (20 if small else 18) * k / 2
    w_in = (12 if small else 3) * k / 2
    for dx, dy in ((0, -1), (1, 0), (0, 1), (-1, 0)):
        px, py = -dy, dx                      # perpendicular
        xo, yo = c + dx * t_out * k, c + dy * t_out * k
        xi, yi = c + dx * t_in * k, c + dy * t_in * k
        wd.polygon([(xo + px * w_out, yo + py * w_out), (xo - px * w_out, yo - py * w_out),
                    (xi - px * w_in, yi - py * w_in), (xi + px * w_in, yi + py * w_in)], fill=WHITE)

    # ── Rombo central.
    dr = (30 if small else 24) * k
    am.polygon([(c, c - dr), (c + dr, c), (c, c + dr), (c - dr, c)], fill=255)

    # ── Punto de detección dentro del barrido.
    if not small:
        bx = c + 44 * k * math.cos(math.radians(330))
        by = c + 44 * k * math.sin(math.radians(330))
        br = 7.5 * k
        am.ellipse([bx - br, by - br, bx + br, by + br], fill=255)

    accent_rgb = linear_gradient(s, ACCENT_A, ACCENT_B, diagonal=True).astype(np.uint8)
    accent = Image.fromarray(accent_rgb, "RGB").convert("RGBA")
    accent.putalpha(accent_mask)

    img = Image.alpha_composite(img, accent)
    img = Image.alpha_composite(img, white)
    # Recorta todo a la forma del fondo.
    clip = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    clip.paste(img, (0, 0), squircle_mask(s))
    return clip.resize((size, size), Image.LANCZOS)


def build_ico(path, sizes):
    entries = []
    for s in sizes:
        buf = io.BytesIO()
        make_frame(s).save(buf, format="PNG")
        entries.append((s, buf.getvalue()))

    n = len(entries)
    header = struct.pack("<HHH", 0, 1, n)
    offset = 6 + n * 16
    directory = b""
    blob = b""
    for s, data in entries:
        w = 0 if s == 256 else s
        h = 0 if s == 256 else s
        directory += struct.pack("<BBBBHHII", w, h, 0, 0, 1, 32, len(data), offset)
        blob += data
        offset += len(data)

    with open(path, "wb") as f:
        f.write(header + directory + blob)


def build_preview(path):
    """Tira con los tamaños reales sobre fondo claro y oscuro."""
    sizes = [256, 64, 48, 32, 24, 16]
    pad = 24
    w = sum(sizes) + pad * (len(sizes) + 1)
    h = 256 + pad * 2
    sheet = Image.new("RGBA", (w, h * 2), (0, 0, 0, 0))
    ImageDraw.Draw(sheet).rectangle([0, 0, w, h], fill=(243, 243, 243, 255))
    ImageDraw.Draw(sheet).rectangle([0, h, w, h * 2], fill=(32, 32, 32, 255))
    for row in (0, 1):
        x = pad
        for s in sizes:
            frame = make_frame(s)
            sheet.alpha_composite(frame, (x, row * h + (h - s) // 2))
            x += s + pad
    sheet.save(path)


if __name__ == "__main__":
    here = os.path.dirname(os.path.abspath(__file__))
    out = os.path.join(here, "..", "src-cs", "RGVMonitor.App", "icon.ico")
    out = os.path.abspath(out)
    build_ico(out, [16, 20, 24, 32, 40, 48, 64, 128, 256])
    build_preview(os.path.join(here, "icon_preview.png"))
    print("OK:", out, os.path.getsize(out), "bytes")
