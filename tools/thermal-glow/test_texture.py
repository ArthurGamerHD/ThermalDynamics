"""Asset regression checks: inspect shipped bytes independently of the profile formula."""
import struct
import unittest
import xml.etree.ElementTree as ET
from build_texture import ROOT, OUTPUT, texture_bytes


class GlowTextureTests(unittest.TestCase):
    def setUp(self):
        self.data = OUTPUT.read_bytes()
        header = struct.unpack('<31I', self.data[4:128])
        self.assertEqual(b'DDS ', self.data[:4])
        self.assertEqual((128, 128), (header[2], header[3]))
        self.assertEqual(8, header[6])
        self.assertEqual((32, 0x41, 0, 32, 0xFF, 0xFF00, 0xFF0000, 0xFF000000), header[18:26])
        self.mips = []
        offset, size = 128, 128
        for _ in range(header[6]):
            count = size * size * 4
            self.mips.append((size, self.data[offset:offset+count]))
            self.assertEqual(count, len(self.mips[-1][1]))
            offset += count
            size //= 2
        self.assertEqual(len(self.data), offset)

    def test_reproducible_asset(self):
        self.assertEqual(self.data, texture_bytes())

    def test_every_mip_has_zero_rgba_borders(self):
        for size, data in self.mips:
            for y in range(size):
                for x in range(size):
                    if x in (0, size-1) or y in (0, size-1):
                        self.assertEqual(bytes(4), data[4*(y*size+x):4*(y*size+x+1)])

    def test_radial_symmetry_and_monotone_falloff(self):
        size, data = self.mips[0]
        def pixel(x, y):
            return data[4*(y*size+x):4*(y*size+x+1)]
        for y in range(size):
            for x in range(size):
                self.assertEqual(pixel(x, y), pixel(y, x))
                self.assertEqual(pixel(x, y), pixel(size-1-x, y))
        centre_row = [pixel(x, size//2)[3] for x in range(size//2, size)]
        self.assertGreaterEqual(centre_row[0], 254)
        self.assertEqual(0, centre_row[-1])
        self.assertTrue(all(a >= b for a, b in zip(centre_row, centre_row[1:])))
        self.assertGreater(len(set(centre_row)), 40)

    def test_rgb_contains_falloff_not_just_alpha(self):
        for _, data in self.mips:
            for i in range(0, len(data), 4):
                r, g, b, a = data[i:i+4]
                self.assertEqual((r, r), (g, b))
                value = r / 255
                linear = value / 12.92 if value <= 0.04045 else ((value + 0.055) / 1.055) ** 2.4
                self.assertLessEqual(abs(linear - a / 255), 0.0065)

    def test_material_is_depth_tested_unlit_and_bound_to_asset(self):
        materials = ET.parse(ROOT / 'Thermodynamics/Content/Data/TransparentMaterials.sbc').findall('.//TransparentMaterial')
        matches = [m for m in materials if m.findtext('Id/SubtypeId') == 'GaugeHeatGlow']
        self.assertEqual(1, len(matches))
        material = matches[0]
        for name in ('IgnoreDepth', 'CanBeAffectedByOtherLights', 'UseAtlas', 'AlphaMistingEnable'):
            self.assertEqual('false', material.findtext(name))
        self.assertEqual('0', material.findtext('AlphaSaturation'))
        texture = ROOT / 'Thermodynamics/Content' / material.findtext('Texture').replace('\\', '/')
        self.assertEqual(OUTPUT, texture)
        renderer = (ROOT / 'Thermodynamics/ThermalGlow.cs').read_text()
        self.assertIn('GetOrCompute("GaugeHeatGlow")', renderer)
        self.assertNotIn('GetOrCompute("Square")', renderer)
        self.assertIn('BlendTypeEnum.AdditiveBottom', renderer)


if __name__ == '__main__':
    unittest.main()
