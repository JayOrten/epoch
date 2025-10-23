from PIL import Image
import os

# --- Config ---
load_image_path = (
    "/home/jayo/repos/aegis/aegis/Content/images/urizen_onebit_tileset__v2d0.png"
)
output_folder = "sprites"  # folder where each sprite will be saved
sprite_size = 12
border = 1  # 1px border around and between sprites

# --- Create output folder if it doesn't exist ---
os.makedirs(output_folder, exist_ok=True)

# --- Load image ---
with Image.open(load_image_path) as im:
    sheet_width, sheet_height = im.size

    # --- Compute grid size ---
    columns = (sheet_width - border) // (sprite_size + border)
    rows = (sheet_height - border) // (sprite_size + border)

    print(f"Image size: {sheet_width}x{sheet_height}, Grid: {columns}x{rows}")
    # --- Iterate over each box in the grid and save ---
    index = 0
    for y in range(rows):
        for x in range(columns):
            sx = border + x * (sprite_size + border)
            sy = border + y * (sprite_size + border)
            box = (sx, sy, sx + sprite_size, sy + sprite_size)
            print(f"Saving sprite {index} at box {box}")
            sprite = im.crop(box)
            sprite.save(os.path.join(output_folder, f"{index}.png"))
            index += 1

print(f"Saved {index} sprites to folder '{output_folder}'.")
