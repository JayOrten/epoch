import xml.etree.ElementTree as ET
from xml.dom import minidom
from PIL import Image

# --- Config ---
image_path = "images/urizen_onebit_tileset__v2d0.png"
load_image_path = (
    "/home/jayo/repos/aegis/aegis/Content/images/urizen_onebit_tileset__v2d0.png"
)
output_path = "atlas.xml"
sprite_size = 12
border = 1  # 1px border around and between sprites

# --- Detect image size automatically ---
with Image.open(load_image_path) as im:
    sheet_width, sheet_height = im.size

# --- Compute grid size ---
columns = (sheet_width - border) // (sprite_size + border)
rows = (sheet_height - border) // (sprite_size + border)

# --- Build XML structure ---
root = ET.Element("TextureAtlas")
comment = ET.Comment(
    f"This tileset is {sprite_size}x{sprite_size} with {border} pixel border and {border} pixel between tiles."
)
root.append(comment)

# Add <Texture>
texture_elem = ET.SubElement(root, "Texture")
texture_elem.text = image_path

# Add <Regions>
regions_elem = ET.SubElement(root, "Regions")

for y in range(rows):
    for x in range(columns):
        sx = border + x * (sprite_size + border)
        sy = border + y * (sprite_size + border)
        ET.SubElement(
            regions_elem,
            "Region",
            {
                "name": f"sprite_{x}_{y}",
                "x": str(sx),
                "y": str(sy),
                "width": str(sprite_size),
                "height": str(sprite_size),
            },
        )

# --- Pretty-print the XML ---
xml_str = ET.tostring(root, encoding="utf-8")
parsed = minidom.parseString(xml_str)
pretty_xml = parsed.toprettyxml(indent="    ", encoding="utf-8")

# --- Write to file ---
with open(output_path, "wb") as f:
    f.write(pretty_xml)

print(f"Generated {output_path} with {rows * columns} regions.")
