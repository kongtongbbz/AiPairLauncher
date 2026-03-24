from __future__ import annotations

from pathlib import Path

from PIL import Image, ImageDraw, ImageFilter


def hex_color(value: str) -> tuple[int, int, int, int]:
    value = value.lstrip("#")
    if len(value) == 6:
        value += "FF"
    return tuple(int(value[index:index + 2], 16) for index in range(0, 8, 2))


def create_vertical_gradient(size: int, top_color: tuple[int, int, int, int], bottom_color: tuple[int, int, int, int]) -> Image.Image:
    image = Image.new("RGBA", (size, size))
    pixels = image.load()
    for y in range(size):
        ratio = y / max(size - 1, 1)
        row = tuple(int(top_color[channel] * (1 - ratio) + bottom_color[channel] * ratio) for channel in range(4))
        for x in range(size):
            pixels[x, y] = row
    return image


def create_icon(size: int) -> Image.Image:
    canvas = Image.new("RGBA", (size, size), (0, 0, 0, 0))

    background = create_vertical_gradient(size, hex_color("#0F2747"), hex_color("#1D456E"))
    mask = Image.new("L", (size, size), 0)
    mask_draw = ImageDraw.Draw(mask)

    outer_margin = int(size * 0.09)
    radius = int(size * 0.24)
    mask_draw.rounded_rectangle(
        [outer_margin, outer_margin, size - outer_margin, size - outer_margin],
        radius=radius,
        fill=255,
    )
    canvas.paste(background, (0, 0), mask)

    shadow = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    shadow_draw = ImageDraw.Draw(shadow)
    shadow_draw.rounded_rectangle(
        [outer_margin + int(size * 0.015), outer_margin + int(size * 0.025), size - outer_margin + int(size * 0.015), size - outer_margin + int(size * 0.025)],
        radius=radius,
        fill=(8, 14, 28, 90),
    )
    shadow = shadow.filter(ImageFilter.GaussianBlur(radius=max(2, int(size * 0.03))))
    canvas.alpha_composite(shadow)
    canvas.paste(background, (0, 0), mask)

    draw = ImageDraw.Draw(canvas)

    inner_margin = int(size * 0.18)
    gap = int(size * 0.04)
    panel_width = int((size - inner_margin * 2 - gap) / 2)
    panel_height = int(size * 0.46)
    panel_top = int(size * 0.22)
    panel_bottom = panel_top + panel_height
    left_panel = [inner_margin, panel_top, inner_margin + panel_width, panel_bottom]
    right_panel = [inner_margin + panel_width + gap, panel_top, inner_margin + panel_width * 2 + gap, panel_bottom]
    panel_radius = int(size * 0.08)

    draw.rounded_rectangle(left_panel, radius=panel_radius, fill=hex_color("#FFD36B"))
    draw.rounded_rectangle(right_panel, radius=panel_radius, fill=hex_color("#77E3D1"))

    divider_top = panel_top + int(size * 0.04)
    divider_bottom = panel_bottom - int(size * 0.04)
    divider_x = size // 2
    draw.rounded_rectangle(
        [divider_x - int(size * 0.01), divider_top, divider_x + int(size * 0.01), divider_bottom],
        radius=int(size * 0.01),
        fill=hex_color("#F3F7FC"),
    )

    arrow_y = panel_bottom + int(size * 0.09)
    stroke = max(4, int(size * 0.036))
    arrow_color = hex_color("#F3F7FC")
    left_arrow_start = (inner_margin + int(panel_width * 0.18), arrow_y)
    left_arrow_end = (size // 2 - int(size * 0.035), arrow_y)
    right_arrow_start = (size // 2 + int(size * 0.035), arrow_y)
    right_arrow_end = (inner_margin + panel_width * 2 + gap - int(panel_width * 0.18), arrow_y)

    draw.line([left_arrow_start, left_arrow_end], fill=arrow_color, width=stroke)
    draw.line([right_arrow_start, right_arrow_end], fill=arrow_color, width=stroke)

    head = int(size * 0.035)
    draw.line([left_arrow_end, (left_arrow_end[0] - head, left_arrow_end[1] - head)], fill=arrow_color, width=stroke)
    draw.line([left_arrow_end, (left_arrow_end[0] - head, left_arrow_end[1] + head)], fill=arrow_color, width=stroke)
    draw.line([right_arrow_start, (right_arrow_start[0] + head, right_arrow_start[1] - head)], fill=arrow_color, width=stroke)
    draw.line([right_arrow_start, (right_arrow_start[0] + head, right_arrow_start[1] + head)], fill=arrow_color, width=stroke)

    accent_radius = int(size * 0.045)
    accent_y = int(size * 0.17)
    accent_spacing = int(size * 0.065)
    center_x = size // 2
    for offset, color in [(-accent_spacing, "#FFD36B"), (0, "#F3F7FC"), (accent_spacing, "#77E3D1")]:
        draw.ellipse(
            [
                center_x + offset - accent_radius,
                accent_y - accent_radius,
                center_x + offset + accent_radius,
                accent_y + accent_radius,
            ],
            fill=hex_color(color),
        )

    return canvas


def main() -> None:
    repo_root = Path(__file__).resolve().parent.parent
    assets_dir = repo_root / "assets"
    assets_dir.mkdir(parents=True, exist_ok=True)

    icon_path = assets_dir / "AiPairLauncher.ico"
    preview_path = assets_dir / "AiPairLauncher-preview.png"

    base = create_icon(256)
    base.save(preview_path)

    sizes = [(256, 256), (128, 128), (64, 64), (48, 48), (32, 32), (24, 24), (16, 16)]
    base.save(icon_path, format="ICO", sizes=sizes)

    print(f"Generated icon: {icon_path}")
    print(f"Generated preview: {preview_path}")


if __name__ == "__main__":
    main()
