"""Standalone, verified extraction for the FETM variants exercised by ATLink."""
import argparse
import ctypes
import hashlib
import io
import json
import os
import re
import shutil
import struct
import sys
import tempfile
import uuid
from collections import Counter
from pathlib import Path

MAX_SOURCE = 512 * 1024 * 1024
MAX_RESOURCE = 512 * 1024 * 1024
MAX_TOTAL = 8 * 1024 * 1024 * 1024


class Unsupported(ValueError):
    pass


class Reader:
    def __init__(self, data, position=0):
        self.data, self.position = data, position

    def take(self, count):
        if count < 0 or self.position + count > len(self.data):
            raise ValueError("Fichier tronque.")
        start = self.position
        self.position += count
        return self.data[start:self.position]

    def integer(self):
        value = 0
        for shift in range(0, 35, 7):
            byte = self.take(1)[0]
            value |= (byte & 127) << shift
            if byte < 128:
                if value > 0x7fffffff:
                    raise ValueError("Entier hors limites.")
                return value
        raise ValueError("Entier invalide.")

    def string(self):
        length = self.integer()
        if length > 65536:
            raise ValueError("Texte trop long.")
        return self.take(length).decode("utf-8")


def safe_path(name):
    parts = name.replace("\\", "/").split("/")
    for part in parts:
        device = part.split(".")[0].upper()
        if (not part or len(part) > 255 or part.endswith((".", " "))
                or any(ord(char) < 32 or char in '<>:"|?*' for char in part)
                or device in ("CON", "AUX", "PRN", "NUL")
                or re.fullmatch(r"(?:COM|LPT)[0-9¹²³]", device)):
            raise ValueError("Chemin de ressource invalide: " + name)
    return "/".join(parts)


def header(data):
    reader = Reader(data)
    if reader.take(4) != b"FETM":
        raise Unsupported("Signature inconnue. Seuls les conteneurs FETM sont pris en charge.")
    version = reader.take(1)[0]
    if version not in (1, 3):
        raise Unsupported("Version FETM non prise en charge: " + str(version))
    game = reader.string()
    build = reader.take(3 if version == 1 else 7).hex()
    title, author = reader.string(), reader.string()
    reader.take(2)
    reader.string()
    reader.string()
    mod_version, description = reader.string(), reader.string()
    links = [reader.string() for _ in range(8)]
    images = []
    image_size = reader.integer()
    images.append(reader.take(image_size))
    screenshot_count = reader.integer()
    if screenshot_count > 32:
        raise Unsupported("Section d'images non prise en charge.")
    for _ in range(screenshot_count):
        images.append(reader.take(reader.integer()))
    if any(reader.take(4 if version == 1 else 5)):
        raise Unsupported("Sections de scripts/configuration non prises en charge dans ce mod.")
    offset = struct.unpack("<I", reader.take(4))[0]
    if version == 3:
        size = struct.unpack("<I", reader.take(4))[0]
        if size != len(data) or any(reader.take(16)):
            raise Unsupported("Sections FETM v3 non prises en charge.")
    if not reader.position <= offset <= min(len(data), 32 * 1024 * 1024):
        raise ValueError("Repertoire de ressources invalide.")
    return reader, offset, images, dict(game=game, containerVersion=version, gameBuild=build,
        title=title, author=author, modVersion=mod_version, description=description, links=links)


def blocks(data, offset):
    bounds, sizes = [offset], [0]
    while bounds[-1] < len(data):
        position = bounds[-1]
        if position + 8 > len(data):
            raise ValueError("Bloc tronque.")
        raw, method = struct.unpack_from(">II", data, position)
        raw, stored = raw & 0xffffff, method & 0xfffff
        if not 0 < raw <= 1024 * 1024 or not stored or position + 8 + stored > len(data):
            raise ValueError("Dimensions de bloc invalides.")
        if method >> 24 not in (0, 25):
            raise Unsupported("Compression non prise en charge: " + str(method >> 24))
        if sizes[-1] + raw > MAX_TOTAL:
            raise ValueError("Les donnees depassent 8 Gio.")
        bounds.append(position + 8 + stored)
        sizes.append(sizes[-1] + raw)
    return bounds, sizes


def legacy_index(data, start, offset, bounds, sizes):
    """Recover v1 records only when their bounds, original size AND SHA-1 agree."""
    positions = {position - offset: index for index, position in enumerate(bounds)}
    names = {}
    for position in range(start, offset):
        reader = Reader(data, position)
        try:
            length = reader.integer()
            if 4 < length < 4096 and reader.position + length + 21 <= offset:
                name = reader.take(length).decode("ascii")
                if "/" in name and re.fullmatch(r"[A-Za-z0-9_./ -]+", name):
                    safe_path(name)
                    names[reader.position + 1] = name
        except (ValueError, UnicodeDecodeError):
            continue
    cache, records = {}, []
    for sha_position in range(start, offset - 24):
        for skip in (20, 22):
            reader = Reader(data, sha_position + skip)
            try:
                relative, stored, original = reader.integer(), reader.integer(), reader.integer()
            except ValueError:
                continue
            if not stored or relative not in positions or relative + stored not in positions:
                continue
            first, last = positions[relative], positions[relative + stored]
            if last <= first or original != sizes[last] - sizes[first] or original > MAX_RESOURCE:
                continue
            if skip == 20 and sha_position not in names:
                continue
            if skip == 22 and (data[sha_position + 20] not in (0xb0, 0xb4)
                               or data[sha_position + 21] not in (0, 1)):
                continue
            key = (relative, stored)
            if key not in cache:
                cache[key] = hashlib.sha1(memoryview(data)[offset + relative:offset + relative + stored]).digest()
            if cache[key] != data[sha_position:sha_position + 20]:
                continue
            name = names[sha_position] if skip == 20 else None
            guid = str(uuid.UUID(bytes_le=data[sha_position - 16:sha_position])) if skip == 22 else None
            records.append(dict(first=first, last=last, gamePath=name, id=guid,
                storedSha1=cache[key].hex(), originalSize=original))
    return records


def v3_index(reader, offset, bounds, sizes):
    positions = {position - offset: index for index, position in enumerate(bounds)}
    count = reader.integer()
    if count > 100000 or any(reader.take(2)):
        raise Unsupported("Ce lecteur v3 prend en charge les ressources legacy Compdata/LOC.")
    records = []
    for _ in range(count):
        guid = str(uuid.UUID(bytes_le=reader.take(16)))
        sha = reader.take(20).hex()
        if reader.take(2) not in (b"\x16\x00", b"\x56\x00"):
            raise Unsupported("Indicateurs de ressource v3 non pris en charge.")
        relative, stored, original = reader.integer(), reader.integer(), reader.integer()
        reader.take(8)
        name = safe_path(reader.string())
        reader.take(4)
        if relative not in positions or relative + stored not in positions or original > MAX_RESOURCE:
            raise ValueError("Ressource v3 hors limites.")
        first, last = positions[relative], positions[relative + stored]
        if original != sizes[last] - sizes[first]:
            raise ValueError("Taille originale incoherente.")
        records.append(dict(first=first, last=last, gamePath=name, id=guid,
            storedSha1=sha, originalSize=original, type="legacy"))
    return records


class Decoder:
    def __init__(self, library):
        self.library, self.decode = library, None

    def block(self, payload, raw, method):
        if method == 0:
            if len(payload) != raw:
                raise ValueError("Taille de bloc non compresse incorrecte.")
            return payload
        if self.decode is None:
            if self.library is None:
                raise Unsupported("Oodle requis. Indiquez --oodle suivi du chemin oo2core_9_win64.dll de votre jeu.")
            self.dll = ctypes.CDLL(str(self.library))
            self.decode = self.dll.OodleLZ_Decompress
            pointer, size, integer = ctypes.c_void_p, ctypes.c_ssize_t, ctypes.c_int
            self.decode.argtypes = [pointer, size, pointer, size, integer, integer, integer,
                pointer, size, pointer, pointer, pointer, size, integer]
            self.decode.restype = size
        output = ctypes.create_string_buffer(raw)
        result = self.decode(payload, len(payload), output, raw, 1, 0, 0, None, 0, None, None, None, 0, 3)
        if result != raw:
            raise ValueError("Echec de decompression Oodle.")
        return output.raw


def write_new(root, relative, content):
    relative = safe_path(relative)
    path = root / relative
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("xb") as stream:
        stream.write(content)
    if hashlib.sha256(path.read_bytes()).digest() != hashlib.sha256(content).digest():
        raise ValueError("Verification d'ecriture echouee: " + relative)
    return relative


def textures(root, inventory):
    if not any(item["type"] == "res" for item in inventory):
        return [], []
    chunks = {item["id"]: item for item in inventory if item["type"] == "chunk"}
    converted, skipped = [], []
    try:
        from PIL import Image, PngImagePlugin
    except ImportError:
        return [], [dict(reason="PNG indisponibles: installer Pillow avec python -m pip install Pillow.")]
    for item in inventory:
        if item["type"] != "res":
            continue
        resource = (root / item["file"]).read_bytes()
        if len(resource) < 132 or struct.unpack_from("<I", resource, 8)[0] != 0:
            skipped.append(dict(file=item["file"], reason="Structure de texture non prise en charge."))
            continue
        fmt = struct.unpack_from("<I", resource, 12)[0]
        if fmt not in (54, 55, 56, 57):
            skipped.append(dict(file=item["file"], reason="Format de texture non pris en charge: " + str(fmt)))
            continue
        guid = str(uuid.UUID(bytes_le=resource[40:56]))
        width, height, depth, slices = struct.unpack_from("<4H", resource, 22)
        mip_count, first_mip = resource[30:32]
        if guid not in chunks or depth != 1 or slices != 1 or first_mip or not 0 < mip_count <= 15:
            skipped.append(dict(file=item["file"], reason="Chunk ou niveaux de detail indisponibles."))
            continue
        pixels = (root / chunks[guid]["file"]).read_bytes()
        mip_sizes = struct.unpack_from("<15I", resource, 56)[:mip_count]
        expected = [max(1, ((width >> level) + 3) // 4) * max(1, ((height >> level) + 3) // 4) * 8
                    for level in range(mip_count)]
        if not width or not height or list(mip_sizes) != expected or sum(mip_sizes) != len(pixels):
            skipped.append(dict(file=item["file"], reason="Dimensions ou mipmaps non prises en charge."))
            continue
        values = [124, 0xA1007, height, width, mip_sizes[0], 0, mip_count] + [0] * 11
        values += [32, 4, int.from_bytes(b"DXT1", "little"), 0, 0, 0, 0, 0]
        values += [0x401008, 0, 0, 0, 0]
        content = b"DDS " + struct.pack("<31I", *values) + pixels
        name = item["gamePath"]
        dds = write_new(root, "textures/dds/" + name + ".dds", content)
        with Image.open(io.BytesIO(content)) as image:
            rgba = image.convert("RGBA")
            output = io.BytesIO()
            info = PngImagePlugin.PngInfo()
            if fmt in (55, 57):
                info.add(b"sRGB", b"\x00")
            rgba.save(output, format="PNG", pnginfo=info)
            png = write_new(root, "textures/png/" + name + ".png", output.getvalue())
            with Image.open(root / png) as check:
                if check.convert("RGBA").tobytes() != rgba.tobytes():
                    raise ValueError("Verification PNG echouee.")
        converted.append(dict(resource=item["file"], dds=dds, png=png,
            width=width, height=height, mipCount=mip_count, frostbiteFormat=fmt))
    return converted, skipped


def extract(source, destination, decoder, convert_textures):
    if not 0 < source.stat().st_size <= MAX_SOURCE:
        raise ValueError("Le fichier doit faire au plus 512 Mio.")
    data = source.read_bytes()
    reader, offset, images, details = header(data)
    bounds, sizes = blocks(data, offset)
    records = (legacy_index(data, reader.position, offset, bounds, sizes)
               if details["containerVersion"] == 1 else v3_index(reader, offset, bounds, sizes))
    covered = set()
    for record in records:
        covered.update(range(record["first"], record["last"]))
    if not records or covered != set(range(len(bounds) - 1)):
        raise Unsupported("Le repertoire ne permet pas de verifier toutes les donnees. Extraction abandonnee.")
    if sum(record["originalSize"] for record in records) > MAX_TOTAL:
        raise ValueError("Les ressources depassent 8 Gio.")
    inventory, seen = [], set()
    for record in records:
        first, last = record["first"], record["last"]
        packed = memoryview(data)[bounds[first]:bounds[last]]
        if hashlib.sha1(packed).hexdigest() != record["storedSha1"]:
            raise ValueError("Empreinte SHA-1 invalide.")
        decoded = bytearray()
        for index in range(first, last):
            position = bounds[index]
            raw, code = struct.unpack_from(">II", data, position)
            decoded.extend(decoder.block(data[position + 8:bounds[index + 1]], raw & 0xffffff, code >> 24))
        if len(decoded) != record["originalSize"]:
            raise ValueError("Taille decodee invalide.")
        name = record["gamePath"]
        kind = record.get("type") or ("chunk" if name is None else
            "ebx" if decoded[:4] == b"RIFF" and decoded[8:11] == b"EBX" else "res")
        relative = ("legacy/" + name if kind == "legacy" else
                    kind + "/" + (name or record["id"]) + "." + kind)
        relative = safe_path(relative)
        if relative.casefold() in seen:
            raise ValueError("Ressource dupliquee: " + relative)
        seen.add(relative.casefold())
        write_new(destination, relative, decoded)
        inventory.append(dict(type=kind, file=relative, gamePath=name, id=record["id"],
            decodedBytes=len(decoded), storedOffset=bounds[first], storedBytes=len(packed),
            storedSha1=record["storedSha1"], decodedSha256=hashlib.sha256(decoded).hexdigest()))
    for index, image in enumerate(images):
        if image:
            ext = ".png" if image.startswith(b"\x89PNG\r\n\x1a\n") else ".bin"
            write_new(destination, "images/" + str(index) + ext, image)
    converted, skipped = textures(destination, inventory) if convert_textures else ([], [])
    report = dict(source=str(source), sourceSha256=hashlib.sha256(data).hexdigest(), **details,
                  resources=inventory, textures=converted, texturesSkipped=skipped)
    write_new(destination, "inventory.json", json.dumps(report, ensure_ascii=False, indent=2).encode("utf-8"))
    return Counter(item["type"] for item in inventory), len(converted), len(skipped)


def find_oodle(explicit):
    if explicit:
        path = Path(explicit).resolve(strict=True)
        if not path.is_file():
            raise ValueError("Bibliotheque Oodle invalide.")
        return path
    roots = [Path(os.environ.get("ProgramFiles", "C:/Program Files")) / "EA Games"]
    roots += [Path(letter + ":/GAMES") for letter in "CDEFGHIJKLMNOPQRSTUVWXYZ"]
    for root in roots:
        if root.is_dir():
            for path in sorted(root.glob("EA SPORTS FC */oo2core_9_win64.dll"), reverse=True):
                return path.resolve()
    return None


def main():
    parser = argparse.ArgumentParser(description="Extraction verifiee des FIFAMOD FETM v1/v3 connus.")
    parser.add_argument("mods", nargs="*", help="Un ou plusieurs fichiers .fifamod")
    parser.add_argument("--output", type=Path, help="Dossier parent des extractions (par defaut: a cote de chaque mod)")
    parser.add_argument("--oodle", help="Chemin de oo2core_9_win64.dll depuis votre installation du jeu")
    parser.add_argument("--no-textures", action="store_true", help="Extraire seulement les ressources natives")
    args = parser.parse_args()
    if not args.mods:
        import tkinter as tk
        from tkinter import filedialog
        root = tk.Tk()
        root.withdraw()
        args.mods = list(filedialog.askopenfilenames(title="Extraire des FIFAMOD", filetypes=[("FIFA mods", "*.fifamod")]))
        root.destroy()
    if not args.mods:
        return 0
    try:
        decoder = Decoder(find_oodle(args.oodle))
    except (OSError, ValueError) as error:
        parser.error(str(error))
    failed = 0
    for name in args.mods:
        temporary = None
        try:
            source = Path(name).resolve(strict=True)
            parent = args.output.resolve() if args.output else source.parent
            parent.mkdir(parents=True, exist_ok=True)
            destination = parent / (source.stem + "-extracted")
            suffix = 2
            while destination.exists():
                destination = parent / (source.stem + "-extracted-" + str(suffix))
                suffix += 1
            temporary = Path(tempfile.mkdtemp(prefix=".fifamod-", dir=parent))
            print("Extraction: " + source.name, flush=True)
            counts, converted, skipped = extract(source, temporary, decoder, not args.no_textures)
            # Never replace an existing extraction, including a destination created concurrently.
            if destination.exists():
                raise FileExistsError(destination)
            temporary.rename(destination)
            temporary = None
            print(f"OK {dict(counts)} | {converted} textures converties | {skipped} ignorees\n{destination}", flush=True)
        except (OSError, ValueError, struct.error) as error:
            failed += 1
            print("ECHEC " + name + ": " + str(error), file=sys.stderr, flush=True)
        finally:
            if temporary is not None:
                shutil.rmtree(temporary)
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
