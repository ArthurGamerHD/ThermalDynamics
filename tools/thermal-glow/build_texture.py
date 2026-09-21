"""Generate a compact premultiplied radial glow DDS, with a full mip chain; no artwork input."""
from pathlib import Path
import math
import struct

ROOT = Path(__file__).resolve().parents[2]
OUTPUT = ROOT / 'Textures/Particles/GaugeHeatGlow.dds'
SIZE = 128


def profile(x, y):
    r2 = x * x + y * y
    if r2 >= 1:
        return 0.0
    return (0.72 * math.exp(-5 * r2) + 0.28 * math.exp(-1.5 * r2)) * (1 - r2) ** 2


def srgb(linear):
    return 12.92 * linear if linear <= 0.0031308 else 1.055 * linear ** (1 / 2.4) - 0.055


def levels():
    size = SIZE
    values = [[profile(2 * x / (size - 1) - 1, 2 * y / (size - 1) - 1)
               for x in range(size)] for y in range(size)]
    while True:
        # Transparent borders remain transparent at every mip, including the final texel.
        for i in range(size):
            values[0][i] = values[-1][i] = values[i][0] = values[i][-1] = 0.0
        pixels = bytearray()
        for row in values:
            for value in row:
                rgb = round(255 * srgb(value))
                pixels.extend((rgb, rgb, rgb, round(255 * value)))
        yield size, bytes(pixels)
        if size == 1:
            return
        size //= 2
        values = [[sum(values[2*y+dy][2*x+dx] for dy in range(2) for dx in range(2)) / 4
                   for x in range(size)] for y in range(size)]


def texture_bytes():
    mips = list(levels())
    header = [124, 0x2100F, SIZE, SIZE, SIZE * 4, 0, len(mips)] + [0] * 11
    header += [32, 0x41, 0, 32, 0xFF, 0xFF00, 0xFF0000, 0xFF000000]
    header += [0x401008, 0, 0, 0, 0]
    return b'DDS ' + struct.pack('<31I', *header) + b''.join(data for _, data in mips)


if __name__ == '__main__':
    OUTPUT.write_bytes(texture_bytes())
    print(f'{OUTPUT}: {OUTPUT.stat().st_size} bytes')
