#!/usr/bin/env python3
"""Tile Viewer server — static files + save endpoints for tile/entity definitions."""

import json
import os
import sys
import xml.etree.ElementTree as ET
from http.server import HTTPServer, SimpleHTTPRequestHandler

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
TILE_DEFS_PATH = os.path.join(REPO_ROOT, "epoch", "Content", "config", "tile-definitions.xml")
ENTITY_DEFS_PATH = os.path.join(REPO_ROOT, "epoch", "Content", "config", "entity-definitions.xml")

TILE_ATTRS = ["id", "name", "index", "bg1", "bg2", "base", "accent", "border"]


def indent_xml(elem, level=0):
    """Add indentation to XML tree (Python 3.8 compat)."""
    indent = "\n" + "    " * level
    if len(elem):
        if not elem.text or not elem.text.strip():
            elem.text = indent + "    "
        if not elem.tail or not elem.tail.strip():
            elem.tail = indent
        for child in elem:
            indent_xml(child, level + 1)
        if not child.tail or not child.tail.strip():
            child.tail = indent
    else:
        if not elem.tail or not elem.tail.strip():
            elem.tail = indent


def write_xml(tree, path):
    """Write XML tree to file with declaration and trailing newline."""
    indent_xml(tree.getroot())
    tree.write(path, encoding="utf-8", xml_declaration=True)
    # Ensure trailing newline
    with open(path, "a") as f:
        f.write("\n")


class TileViewerHandler(SimpleHTTPRequestHandler):
    def __init__(self, *args, **kwargs):
        super().__init__(*args, directory=REPO_ROOT, **kwargs)

    def do_POST(self):
        if self.path == "/api/save-tile":
            self._handle_save_tile()
        elif self.path == "/api/save-entity":
            self._handle_save_entity()
        else:
            self._json_response(404, {"error": "Not found"})

    def _read_body(self):
        length = int(self.headers.get("Content-Length", 0))
        return json.loads(self.rfile.read(length))

    def _json_response(self, code, data):
        body = json.dumps(data).encode()
        self.send_response(code)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def _handle_save_tile(self):
        try:
            data = self._read_body()
            tree = ET.parse(TILE_DEFS_PATH)
            root = tree.getroot()

            tile_id = data.get("id")

            if tile_id is None:
                # Auto-assign next ID
                existing_ids = [int(t.get("id", 0)) for t in root.findall("tile")]
                tile_id = max(existing_ids, default=-1) + 1

            tile_id = int(tile_id)

            # Find existing or create new
            existing = None
            for t in root.findall("tile"):
                if int(t.get("id", -1)) == tile_id:
                    existing = t
                    break

            if existing is None:
                existing = ET.SubElement(root, "tile")

            # Set attributes in canonical order
            for attr in TILE_ATTRS:
                if attr in data:
                    existing.set(attr, str(data[attr]))
                elif attr == "id":
                    existing.set("id", str(tile_id))

            write_xml(tree, TILE_DEFS_PATH)
            self._json_response(200, {"ok": True, "id": tile_id})

        except Exception as e:
            self._json_response(500, {"error": str(e)})

    def _handle_save_entity(self):
        try:
            data = self._read_body()
            tree = ET.parse(ENTITY_DEFS_PATH)
            root = tree.getroot()

            entity_id = data.get("id")
            entity_name = data.get("name", "unnamed")
            tiles = data.get("tiles", [])

            if entity_id is None:
                existing_ids = [int(e.get("id", 0)) for e in root.findall("entity")]
                entity_id = max(existing_ids, default=-1) + 1

            entity_id = int(entity_id)

            # Find existing or create new
            existing = None
            for e in root.findall("entity"):
                if int(e.get("id", -1)) == entity_id:
                    existing = e
                    break

            if existing is None:
                existing = ET.SubElement(root, "entity")
                existing.set("name", entity_name)
                existing.set("id", str(entity_id))
                # New entities get Position
                ET.SubElement(existing, "Position")
            else:
                existing.set("name", entity_name)
                existing.set("id", str(entity_id))

            # Remove old GraphicalTileList (preserve other components)
            old_gtl = existing.find("GraphicalTileList")
            if old_gtl is not None:
                existing.remove(old_gtl)
            # Also remove old single GraphicalTile if present
            old_gt = existing.find("GraphicalTile")
            if old_gt is not None:
                existing.remove(old_gt)

            # Build new GraphicalTileList
            gtl = ET.Element("GraphicalTileList")
            for t in tiles:
                tile_el = ET.SubElement(gtl, "tile")
                tile_el.set("TileId", str(t["tileId"]))
                if t.get("offset") is not None:
                    tile_el.set("Offset", str(t["offset"]))
                # Color overrides
                color_overrides = [
                    ("bg1Override", "Background1Color"),
                    ("bg2Override", "Background2Color"),
                    ("baseOverride", "BaseColor"),
                    ("accentOverride", "AccentColor"),
                    ("borderOverride", "BorderColor"),
                ]
                for json_key, xml_attr in color_overrides:
                    if t.get(json_key):
                        tile_el.set(xml_attr, t[json_key])
                if t.get("forceDraw"):
                    tile_el.set("ForceDraw", "true")
                if t.get("autoTile"):
                    tile_el.set("AutoTile", "true")
                if t.get("borderType"):
                    tile_el.set("BorderType", t["borderType"])
                if t.get("interpolateMovement") is not None:
                    tile_el.set("InterpolateMovement", str(t["interpolateMovement"]).lower())

            # Insert GraphicalTileList as first child (before Position, etc.)
            existing.insert(0, gtl)

            write_xml(tree, ENTITY_DEFS_PATH)
            self._json_response(200, {"ok": True, "id": entity_id})

        except Exception as e:
            self._json_response(500, {"error": str(e)})


def main():
    port = int(sys.argv[1]) if len(sys.argv) > 1 else 8080
    server = HTTPServer(("", port), TileViewerHandler)
    print(f"Tile Viewer server on http://localhost:{port}/tools/tile-viewer/")
    print("Press Ctrl+C to stop.")
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        print("\nStopped.")
        server.server_close()


if __name__ == "__main__":
    main()
