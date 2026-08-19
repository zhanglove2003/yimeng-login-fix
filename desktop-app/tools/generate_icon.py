from pathlib import Path
from PIL import Image, ImageDraw


ROOT = Path(__file__).resolve().parents[1]
ASSETS = ROOT / "src" / "YiMengFix.UI" / "Assets"
SIZE = 1024


def bezier(p0, p1, p2, steps=160):
    points = []
    for index in range(steps + 1):
        t = index / steps
        mt = 1 - t
        x = mt * mt * p0[0] + 2 * mt * t * p1[0] + t * t * p2[0]
        y = mt * mt * p0[1] + 2 * mt * t * p1[1] + t * t * p2[1]
        points.append((round(x), round(y)))
    return points


canvas = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
draw = ImageDraw.Draw(canvas)

draw.rounded_rectangle((32, 32, 992, 992), radius=216, fill="#171717")
draw.rounded_rectangle((52, 52, 972, 972), radius=196, outline="#34383D", width=22)

arch = bezier((220, 680), (512, 190), (804, 680))
draw.line(arch, fill="#F7F8FA", width=82, joint="curve")
draw.line((280, 696, 744, 696), fill="#F7F8FA", width=74)
draw.ellipse((646, 144, 918, 416), fill="#A16207")
draw.line((714, 280, 758, 324, 846, 220), fill="#FFFFFF", width=44, joint="curve")

png_path = ASSETS / "app-icon.png"
ico_path = ASSETS / "app-icon.ico"
canvas.save(png_path, format="PNG", optimize=True)
canvas.save(
    ico_path,
    format="ICO",
    sizes=[(16, 16), (20, 20), (24, 24), (32, 32), (40, 40), (48, 48), (64, 64), (128, 128), (256, 256)],
)

print(png_path)
print(ico_path)
