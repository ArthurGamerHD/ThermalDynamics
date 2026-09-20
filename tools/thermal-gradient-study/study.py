"""Offline temperature-estimator study, not an engine/GPU simulation.
Samples represent equal-volume axis-aligned blocks; no proprietary assets are used.
"""
import argparse
import heapq
from html import escape
import json
import math
from pathlib import Path

SIZE = 32

def temperature(case, x, y, z):
    if case == 'gradient':
        return 290 + 8*x
    if case == 'diagonal':
        return 290 + 4*(x+y+z)
    if case == 'radial':
        return round(300 + 500*math.exp(-((x-16)**2+(y-16)**2+(z-16)**2)/40), 3)
    if case == 'hotspot':
        return 800 if (x, y, z) == (17, 16, 16) else 300
    if case == 'cooled_patch':
        return 300 if 14 <= x < 18 and 14 <= y < 18 and 14 <= z < 18 else 650
    if case == 'mixed':
        if 14 <= x < 18 and 14 <= y < 18 and 14 <= z < 18:
            return 300
        if 24 <= x < 28 and 12 <= y < 20 and 12 <= z < 20:
            return 850
        return 350 + 6*x
    # Deliberately hostile: variation at every block, beyond the leaf budget.
    return 300 if (x+y+z) % 2 else 800

class Cell:
    def __init__(self, lo, width, points):
        self.lo, self.width, self.points = lo, width, points
        values = [p[3] for p in points]
        self.peak = max(values)
        self.error = sum((self.peak-v)**2 for v in values)

    def split(self):
        half = self.width // 2
        groups = {}
        for point in self.points:
            key = tuple(int(point[i] >= self.lo[i]+half) for i in range(3))
            groups.setdefault(key, []).append(point)
        return [Cell(tuple(self.lo[i]+key[i]*half for i in range(3)), half, values)
                for key, values in sorted(groups.items())]

class BinaryCell(Cell):
    def __init__(self, lo, shape, points):
        super().__init__(lo, max(shape), points)
        self.shape = shape

    def split(self):
        choices = []
        for axis in range(3):
            if self.shape[axis] <= 1:
                continue
            half = self.shape[axis] // 2
            groups = [[], []]
            for point in self.points:
                groups[int(point[axis] >= self.lo[axis]+half)].append(point)
            children = []
            for side, points in enumerate(groups):
                if not points:
                    continue
                lo, shape = list(self.lo), list(self.shape)
                lo[axis] += side*half
                shape[axis] = half
                children.append(BinaryCell(tuple(lo), tuple(shape), points))
            choices.append((sum(c.error for c in children), -self.shape[axis], axis, children))
        return min(choices, key=lambda v: v[:3])[3]

def adaptive(points, budget, binary=False):
    """Spend leaves on maximum-estimator error, retaining a peak in every leaf."""
    root = BinaryCell((0, 0, 0), (SIZE, SIZE, SIZE), points) if binary else Cell((0, 0, 0), SIZE, points)
    leaves, queue, serial = {0: root}, [(-root.error, 0, root)], 1
    while queue:
        _, identity, cell = heapq.heappop(queue)
        if cell.width == 1 or cell.error == 0:
            continue
        children = cell.split()
        if len(leaves)-1+len(children) > budget:
            continue
        del leaves[identity]
        for child in children:
            leaves[serial] = child
            heapq.heappush(queue, (-child.error, serial, child))
            serial += 1
    return list(leaves.values())

def fixed(points, width=4):
    groups = {}
    for point in points:
        lo = tuple((point[i]//width)*width for i in range(3))
        groups.setdefault(lo, []).append(point)
    return [Cell(lo, width, values) for lo, values in groups.items()]

def predict(cells):
    result = {}
    for cell in cells:
        for point in cell.points:
            key = point[:3]
            if key in result:
                raise AssertionError('duplicate ownership')
            result[key] = cell.peak
    return result

def verify_geometry(points, cells):
    # Independently evaluate box ownership; do not use the samples assigned to a leaf.
    volume = {}
    for cell in cells:
        shape = getattr(cell, 'shape', (cell.width,)*3)
        for z in range(cell.lo[2], cell.lo[2]+shape[2]):
            for y in range(cell.lo[1], cell.lo[1]+shape[1]):
                for x in range(cell.lo[0], cell.lo[0]+shape[0]):
                    assert (x,y,z) not in volume, 'overlapping output boxes'
                    volume[x,y,z] = cell.peak
    assert volume == predict(cells), 'geometry does not match sample assignment'
    assert len(volume) == len(points)

def metrics(points, cells):
    predicted = predict(cells)
    assert len(predicted) == len(points)
    errors = [predicted[p[:3]]-p[3] for p in points]
    assert min(errors) >= 0  # Peak conservation: no hottest source is hidden.
    return {'leaves': len(cells), 'mae_K': round(sum(errors)/len(errors), 3),
            'max_error_K': max(errors), 'false_hot_blocks': sum(p[3] < 600 <= predicted[p[:3]] for p in points),
            'exact_blocks': sum(e == 0 for e in errors), 'total_blocks': len(points)}

def run(output):
    output.mkdir(parents=True, exist_ok=True)
    report = {}
    svg = ['<svg xmlns="http://www.w3.org/2000/svg" width="990" height="2090" viewBox="0 0 990 2090">',
           '<rect width="990" height="2090" fill="#111820"/>',
           '<g font-family="sans-serif" fill="white"><text x="20" y="24">Synthetic scalar-field slices — not an in-game rendering prediction</text>']
    for row, case in enumerate(('gradient', 'hotspot', 'cooled_patch', 'mixed', 'diagonal', 'radial', 'checkerboard')):
        points = [(x,y,z,temperature(case,x,y,z)) for z in range(SIZE) for y in range(SIZE) for x in range(SIZE)]
        coarse = fixed(points)
        same = adaptive(points, 512)
        detailed = adaptive(points, 1536)
        directed = adaptive(points, 512, binary=True)
        assert len(directed) <= 512
        verify_geometry(points, directed)
        if case == "mixed":
            assert predict(directed) == predict(adaptive(list(reversed(points)), 512, binary=True))
        assert len(same) <= 512 and len(detailed) <= 1536
        report[case] = {'fixed_512': metrics(points, coarse), 'adaptive_512': metrics(points, same),
                        'adaptive_1536': metrics(points, detailed), 'directed_512': metrics(points, directed)}
        for column, (name, data) in enumerate((('Truth', {p[:3]:p[3] for p in points}),
                                               ('Fixed max / 512 cells', predict(coarse)),
                                               ('Directed max / <=512 cells', predict(directed)))):
            left, top = 20+column*325, 65+row*290
            svg.append(f'<text x="{left}" y="{top-10}" font-size="13">{case}: {escape(name)}</text>')
            for y in range(SIZE):
                for x in range(SIZE):
                    # One shared fixed 290..850 K scale, no per-panel contrast tricks.
                    shade = round(18+225*max(0,min(1,(data[x,y,16]-290)/560)))
                    svg.append(f'<rect x="{left+x*8}" y="{top+y*8}" width="8" height="8" fill="rgb({shade},{shade},{shade})"/>')
    svg.append('</g></svg>')
    (output/'slices.svg').write_text('\n'.join(svg))
    (output/'metrics.json').write_text(json.dumps(report, indent=2)+'\n')
    lines = ['# Synthetic thermal estimator comparison', '',
             'Equal-volume 32³ block fixtures. Same fixed temperature scale in all previews. No native depth, GPU, materials, camera-motion or runtime-budget simulation.', '',
             '| Fixture | Fixed 512 MAE K | Adaptive ≤512 MAE K | Directed ≤512 MAE K | False-hot blocks: fixed → directed512 |',
             '| --- | ---: | ---: | ---: | ---: |']
    for case, values in report.items():
        a,b,c = (values[k] for k in ('fixed_512','adaptive_512','directed_512'))
        lines.append(f"| {case} | {a['mae_K']} | {b['mae_K']} | {c['mae_K']} | {a['false_hot_blocks']} → {c['false_hot_blocks']} |")
    lines += ['', 'The checkerboard is an intentional failure case: sub-budget reconstruction cannot preserve block-scale variation everywhere.',
              'This offline batch algorithm keeps all input points and rebuilds children; it is not ready to run inside the mod.',
              'Next gates: grid-owned bounds, screen-visible weighting, cooling-priority refinement, temporal stability, bounded resumable construction, and native ordering/geometry capacity.']
    (output/'report.md').write_text('\n'.join(lines)+'\n')
    print('\n'.join(lines))

if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('output', type=Path)
    run(parser.parse_args().output)
