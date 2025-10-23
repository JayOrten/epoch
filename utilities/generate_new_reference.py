from PIL import Image, ImageDraw, ImageFont

# --- Config ---
load_image_path = (
    "/home/jayo/repos/aegis/aegis/Content/images/urizen_onebit_tileset__v2d0.png"
)
output_path = "spritesheet_reference.png"
sprite_size = 12
border = 1
num_space = 30
scale = 2
sheet_row_width = 20  # number of sprites per row in the new reference sheet


# Helper: check if all pixels are black
def is_fully_black(sprite):
    for px in sprite.getdata():
        if len(px) == 4:  # RGBA
            if px[:3] != (0, 0, 0):
                return False
        else:  # RGB
            if px != (0, 0, 0):
                return False
    return True


# Load original image
with Image.open(load_image_path) as im:
    sheet_width, sheet_height = im.size

    columns_orig = (sheet_width + border) // (sprite_size + border)
    rows_orig = (sheet_height + border) // (sprite_size + border)
    total_sprites = columns_orig * rows_orig

    # Collect all non-black sprites first
    non_black_sprites = []
    for y_orig in range(rows_orig):
        for x_orig in range(columns_orig):
            sx = border + x_orig * (sprite_size + border)
            sy = border + y_orig * (sprite_size + border)
            sx2 = min(sx + sprite_size, sheet_width)
            sy2 = min(sy + sprite_size, sheet_height)
            sprite = im.crop((sx, sy, sx2, sy2))
            if not is_fully_black(sprite):
                non_black_sprites.append(sprite)

    # Determine size of reference sheet
    columns_new = min(sheet_row_width, len(non_black_sprites))
    rows_new = (len(non_black_sprites) + columns_new - 1) // columns_new

    cell_width = sprite_size * scale + num_space
    cell_height = sprite_size * scale + num_space

    new_width = columns_new * cell_width
    new_height = rows_new * cell_height
    reference_img = Image.new("RGBA", (new_width, new_height), (0, 0, 0, 255))
    draw = ImageDraw.Draw(reference_img)
    font = ImageFont.load_default()

    index = 0  # numbering for original grid
    placement_index = 0  # placement for non-black sprites
    for y_orig in range(rows_orig):
        for x_orig in range(columns_orig):
            if index >= total_sprites:
                break

            sx = border + x_orig * (sprite_size + border)
            sy = border + y_orig * (sprite_size + border)
            sx2 = min(sx + sprite_size, sheet_width)
            sy2 = min(sy + sprite_size, sheet_height)
            sprite = im.crop((sx, sy, sx2, sy2))

            if not is_fully_black(sprite):
                sprite = sprite.resize(
                    (sprite_size * scale, sprite_size * scale), Image.NEAREST
                )
                x_new = (placement_index % columns_new) * cell_width
                y_new = (placement_index // columns_new) * cell_height
                reference_img.paste(sprite, (x_new, y_new))
                draw.text(
                    (x_new + sprite_size * scale, y_new),
                    str(index),
                    fill=(255, 255, 255),
                    font=font,
                )
                placement_index += 1  # only increment placement for non-black sprites

            index += 1  # always increment numbering

    reference_img.save(output_path)
    print(
        f"Saved reference sheet with {placement_index} visible sprites to '{output_path}'"
    )
