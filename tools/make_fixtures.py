"""Generate deterministic STL fixtures and demo samples for PrintShelf.

Usage (from repo root):
    python tools/make_fixtures.py

Fixtures (docs/05-testing.md §1) live in src/tests/PrintShelf.Stl.Tests/fixtures.
Demo samples used by the seeder / manual checks live in tools/samples.
Everything is generated deterministically so the files can be committed.
"""
from __future__ import annotations

import math
import os
import struct

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
FIX = os.path.join(ROOT, "src", "tests", "PrintShelf.Stl.Tests", "fixtures")
SAMPLES = os.path.join(ROOT, "tools", "samples")

CUBE_VERTS = [
    # (a, b, c) outward winding, 10 mm cube at the origin
    ((0, 0, 0), (0, 10, 0), (10, 10, 0)),
    ((0, 0, 0), (10, 10, 0), (10, 0, 0)),
    ((0, 0, 10), (10, 0, 10), (10, 10, 10)),
    ((0, 0, 10), (10, 10, 10), (0, 10, 10)),
    ((0, 0, 0), (10, 0, 0), (10, 0, 10)),
    ((0, 0, 0), (10, 0, 10), (0, 0, 10)),
    ((0, 10, 0), (0, 10, 10), (10, 10, 10)),
    ((0, 10, 0), (10, 10, 10), (10, 10, 0)),
    ((0, 0, 0), (0, 0, 10), (0, 10, 10)),
    ((0, 0, 0), (0, 10, 10), (0, 10, 0)),
    ((10, 0, 0), (10, 10, 0), (10, 10, 10)),
    ((10, 0, 0), (10, 10, 10), (10, 0, 10)),
]


def normal(a, b, c):
    u = (b[0] - a[0], b[1] - a[1], b[2] - a[2])
    v = (c[0] - a[0], c[1] - a[1], c[2] - a[2])
    n = (
        u[1] * v[2] - u[2] * v[1],
        u[2] * v[0] - u[0] * v[2],
        u[0] * v[1] - u[1] * v[0],
    )
    length = math.sqrt(sum(x * x for x in n)) or 1.0
    return tuple(x / length for x in n)


def binary_stl(tris, header=b"PrintShelf fixture"):
    out = bytearray(header.ljust(80, b"\0")[:80])
    out += struct.pack("<I", len(tris))
    for a, b, c in tris:
        n = normal(a, b, c)
        out += struct.pack("<3f", *n)
        for v in (a, b, c):
            out += struct.pack("<3f", *v)
        out += struct.pack("<H", 0)
    return bytes(out)


def ascii_stl(tris, name="fixture"):
    lines = [f"solid {name}"]
    for a, b, c in tris:
        n = normal(a, b, c)
        lines.append(f"  facet normal {n[0]:.6e} {n[1]:.6e} {n[2]:.6e}")
        lines.append("    outer loop")
        for v in (a, b, c):
            lines.append(f"      vertex {v[0]:.6e} {v[1]:.6e} {v[2]:.6e}")
        lines.append("    endloop")
        lines.append("  endfacet")
    lines.append(f"endsolid {name}")
    return ("\n".join(lines) + "\n").encode("ascii")


def write(path, data):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "wb") as fh:
        fh.write(data)
    print(f"{path}  {len(data):,} bytes")


def pyramid(base=30.0, height=40.0):
    h = base / 2
    v = [(0, 0, 0), (base, 0, 0), (base, base, 0), (0, base, 0), (h, h, height)]
    tris = [
        (v[0], v[2], v[1]),
        (v[0], v[3], v[2]),
        (v[0], v[1], v[4]),
        (v[1], v[2], v[4]),
        (v[2], v[3], v[4]),
        (v[3], v[0], v[4]),
    ]
    return tris


def torus(major=20.0, minor=7.0, u_seg=48, v_seg=24):
    tris = []

    def point(i, j):
        u = 2 * math.pi * i / u_seg
        v = 2 * math.pi * j / v_seg
        r = major + minor * math.cos(v)
        return (r * math.cos(u), r * math.sin(u), minor * math.sin(v))

    for i in range(u_seg):
        for j in range(v_seg):
            a = point(i, j)
            b = point(i + 1, j)
            c = point(i + 1, j + 1)
            d = point(i, j + 1)
            tris.append((a, b, c))
            tris.append((a, c, d))
    return tris


def main():
    cube = [(tuple(map(float, a)), tuple(map(float, b)), tuple(map(float, c))) for a, b, c in CUBE_VERTS]
    inverted = [(a, c, b) for a, b, c in cube]

    write(os.path.join(FIX, "cube_binary.stl"), binary_stl(cube))
    write(os.path.join(FIX, "cube_ascii.stl"), ascii_stl(cube, "cube"))
    write(os.path.join(FIX, "cube_inverted.stl"), binary_stl(inverted, b"inverted cube"))
    write(os.path.join(FIX, "solid_binary_header.stl"), binary_stl(cube, b"solid binary file with text header"))
    write(os.path.join(FIX, "zero_triangles.stl"), binary_stl([]))
    write(os.path.join(FIX, "empty.stl"), b"")

    full = binary_stl(cube)
    write(os.path.join(FIX, "truncated_cube.stl"), full[:-25])

    lying = bytearray(binary_stl(cube))
    struct.pack_into("<I", lying, 80, 100)
    write(os.path.join(FIX, "lying_header.stl"), bytes(lying))

    nan = bytearray(binary_stl(cube))
    struct.pack_into("<f", nan, 84 + 12 + 4, float("nan"))
    write(os.path.join(FIX, "nan_vertex.stl"), bytes(nan))

    write(
        os.path.join(FIX, "ascii_malformed.stl"),
        b"solid broken\n facet normal 0 0 1\n  outer loop\n   vertex 0 0 zero\n   vertex 1 0 0\n   vertex 0 1 0\n  endloop\n endfacet\nendsolid\n",
    )
    write(
        os.path.join(FIX, "ascii_no_endsolid.stl"),
        ascii_stl(cube, "noend").replace(b"endsolid noend\n", b""),
    )
    write(os.path.join(FIX, "not_an_stl.bin"), b"\x89PNG\r\n\x1a\n" + bytes(range(256)) * 4)

    write(os.path.join(SAMPLES, "cube.stl"), binary_stl(cube, b"PrintShelf sample cube 10mm"))
    write(os.path.join(SAMPLES, "pyramid.stl"), binary_stl(pyramid(), b"PrintShelf sample pyramid"))
    write(os.path.join(SAMPLES, "torus.stl"), binary_stl(torus(), b"PrintShelf sample torus"))
    write(
        os.path.join(SAMPLES, "bench_100k.stl"),
        binary_stl(torus(30.0, 10.0, 224, 224), b"PrintShelf 100k triangle bench"),
    )


if __name__ == "__main__":
    main()
