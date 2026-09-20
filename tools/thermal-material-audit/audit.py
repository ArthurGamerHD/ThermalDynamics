"""Read-only MWM material inventory. No game code or render state is executed.

Format transcribed from installed MyModelImporter, MyMeshPartInfo and
MyMaterialDescriptor. Indexed MWM versions 1066002..1157002 only.
"""
import argparse
import collections
import json
import pathlib
import struct


class Reader:
    def __init__(self, stream):
        self.stream = stream
        stream.seek(0, 2)
        self.end = stream.tell()
        stream.seek(0)

    def take(self, length):
        if length < 0 or length > self.end - self.stream.tell():
            raise ValueError('Truncated or out-of-range field')
        value = self.stream.read(length)
        if len(value) != length:
            raise ValueError('Truncated field')
        return value

    def integer(self):
        return struct.unpack('<i', self.take(4))[0]

    def count(self, maximum):
        value = self.integer()
        if not 0 <= value <= maximum:
            raise ValueError('Invalid collection size')
        return value

    def string(self):
        length = 0
        for shift in range(0, 35, 7):
            byte = self.take(1)[0]
            length |= (byte & 127) << shift
            if byte < 128:
                if length > 65536:
                    raise ValueError('Oversize string')
                return self.take(length).decode('utf-8')
        raise ValueError('Invalid string length')

    def skip(self, count):
        if count < 0 or count > self.end - self.stream.tell():
            raise ValueError('Invalid skip')
        self.stream.seek(count, 1)

    def pairs(self):
        result = {}
        for _ in range(self.count(1024)):
            key, value = self.string(), self.string()
            if key in result:
                raise ValueError('Duplicate material field')
            result[key] = value
        return result


def inspect(stream):
    r = Reader(stream)
    if r.string() != 'Debug':
        raise ValueError('Unknown MWM header')
    debug = [r.string() for _ in range(r.count(1024))]
    if not debug or not debug[0].startswith('Version:'):
        raise ValueError('Missing version')
    version = int(debug[0][8:])
    if not 1066002 <= version <= 1157002:
        raise ValueError('Unaudited MWM version: ' + str(version))
    tags = {}
    for _ in range(r.count(1024)):
        key, offset = r.string(), r.integer()
        if key in tags or not 0 <= offset < r.end:
            raise ValueError('Invalid tag index')
        tags[key] = offset
    header_end = stream.tell()
    if any(offset < header_end for offset in tags.values()):
        raise ValueError('Tag overlaps header')
    geometry = None
    if 'GeometryDataAsset' in tags:
        stream.seek(tags['GeometryDataAsset'])
        if r.string() != 'GeometryDataAsset':
            raise ValueError('Geometry tag mismatch')
        geometry = r.string()
    if 'MeshParts' not in tags:
        return {'version': version, 'geometry_asset': geometry, 'parts': []}
    offset = tags['MeshParts']
    r.end = min([v for v in tags.values() if v > offset] + [r.end])
    stream.seek(offset)
    if r.string() != 'MeshParts':
        raise ValueError('MeshParts tag mismatch')
    parts = []
    for _ in range(r.count(65536)):
        r.integer()  # stored material hash, not a stable material identity
        indices = r.count(60000000)
        if indices % 3:
            raise ValueError('Non-triangle mesh part')
        r.skip(indices * 4)  # no vertex or triangle allocation
        present = r.take(1)[0]
        if present not in (0, 1):
            raise ValueError('Invalid material flag')
        part = {'triangles': indices // 3, 'name': None, 'technique': None, 'textures': {}}
        if present:
            part['name'] = r.string() or None
            part['textures'] = r.pairs()
            if version >= 1068001:
                part['user_data'] = r.pairs()
            if version < 1157001:
                r.skip(7 * 4)
            part['technique'] = r.string()
            if part['technique'] == 'GLASS':
                part['glass'] = [r.string(), r.string(), bool(r.take(1)[0])]
        parts.append(part)
    return {'version': version, 'geometry_asset': geometry, 'parts': parts}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('models', type=pathlib.Path)
    parser.add_argument('output', type=pathlib.Path)
    args = parser.parse_args()
    records, errors = {}, {}
    triangles = collections.Counter()
    for path in sorted(args.models.rglob('*.mwm')):
        key = str(path.relative_to(args.models))
        try:
            with path.open('rb') as stream:
                record = inspect(stream)
            records[key] = record
            for part in record['parts']:
                triangles[part['technique'] or 'NO_MATERIAL'] += part['triangles']
        except (ValueError, OSError, UnicodeError) as error:
            errors[key] = str(error)
    if not records and not errors:
        raise SystemExit('No model files found')
    report = {'models_read': len(records), 'models_rejected': len(errors),
              'triangles_by_technique': dict(triangles), 'errors': errors, 'models': records}
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(report, indent=2))
    print(json.dumps({k: v for k, v in report.items() if k != 'models'}, indent=2))
    return 1 if errors else 0


if __name__ == '__main__':
    raise SystemExit(main())
