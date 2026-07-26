"""Genera icon.ico (multi-tamaño, bordes suaves) para RG Tracker.

Bordes limpios: se renderiza a 4x y se reduce con Lanczos (antialiasing real),
porque las formas de Pillow no tienen AA y dejan esquinas dentadas/claras.
"""
import io, struct, os
from PIL import Image, ImageDraw, ImageFont

SS = 4  # supersampling

PINK = (236, 19, 91, 255)
TOP  = (34, 34, 42)   # degradado de fondo: arriba
BOT  = (12, 12, 16)   # abajo


def load_font(px):
    for name in ("arialbd.ttf", "segoeuib.ttf", "arial.ttf"):
        try:
            return ImageFont.truetype(name, px)
        except OSError:
            continue
    return ImageFont.load_default()


def make_frame(size):
    s = size * SS
    img = Image.new("RGBA", (s, s), (0, 0, 0, 0))

    # Fondo con degradado vertical.
    grad = Image.new("RGB", (1, s))
    for y in range(s):
        t = y / (s - 1)
        grad.putpixel((0, y), tuple(int(TOP[i] + (BOT[i] - TOP[i]) * t) for i in range(3)))
    grad = grad.resize((s, s))

    # Máscara redondeada (se dibuja a 4x → al reducir queda suave).
    mask = Image.new("L", (s, s), 0)
    ImageDraw.Draw(mask).rounded_rectangle([0, 0, s - 1, s - 1], radius=int(s * 0.22), fill=255)
    img.paste(grad, (0, 0), mask)

    draw = ImageDraw.Draw(img)

    # Acento: una barra rosa sutil abajo.
    bar_h = max(2, int(s * 0.045))
    bar_w = int(s * 0.34)
    bx = (s - bar_w) // 2
    by = int(s * 0.78)
    draw.rounded_rectangle([bx, by, bx + bar_w, by + bar_h],
                           radius=bar_h // 2, fill=PINK)

    # Texto "RG" blanco + "." rosa, centrado.
    font = load_font(int(s * 0.40))
    bb_rg = draw.textbbox((0, 0), "RG", font=font)
    bb_dot = draw.textbbox((0, 0), ".", font=font)
    w_rg = bb_rg[2] - bb_rg[0]
    w_dot = bb_dot[2] - bb_dot[0]
    h_text = bb_rg[3] - bb_rg[1]
    x = (s - w_rg - w_dot) // 2 - bb_rg[0]
    y = int(s * 0.40) - h_text // 2 - bb_rg[1]
    draw.text((x, y), "RG", fill=(255, 255, 255, 255), font=font)
    draw.text((x + w_rg, y), ".", fill=PINK, font=font)

    return img.resize((size, size), Image.LANCZOS)


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


if __name__ == "__main__":
    out = os.path.join(os.path.dirname(__file__), "..", "src-cs", "RGVMonitor.App", "icon.ico")
    out = os.path.abspath(out)
    build_ico(out, [16, 32, 48, 64, 128, 256])
    print("OK:", out, os.path.getsize(out), "bytes")
