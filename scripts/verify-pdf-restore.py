"""Development QA: validate actual vector geometry, not just PDF page dimensions."""
from pathlib import Path
from math import hypot
import json
from pypdf import PdfReader
from pypdf.generic import ContentStream

root = Path(__file__).resolve().parents[1]
identity = (1, 0, 0, 1, 0, 0)

def product(a, b):
    return (a[0]*b[0]+a[2]*b[1], a[1]*b[0]+a[3]*b[1],
            a[0]*b[2]+a[2]*b[3], a[1]*b[2]+a[3]*b[3],
            a[0]*b[4]+a[2]*b[5]+a[4], a[1]*b[4]+a[3]*b[5]+a[5])

def point(m, p):
    x, y = map(float, p)
    return m[0]*x+m[2]*y+m[4], m[1]*x+m[3]*y+m[5]

def paths(stream, reader, resources, matrix=identity, color=None):
    stack, segments, found, images = [], [], [], 0
    last = None
    for values, op in ContentStream(stream, reader).operations:
        if op == b'q': stack.append((matrix, color))
        elif op == b'Q': matrix, color = stack.pop()
        elif op == b'cm': matrix = product(matrix, tuple(map(float, values)))
        elif op == b'RG': color = tuple(map(float, values))
        elif op == b'm': last = point(matrix, values)
        elif op == b'l':
            end = point(matrix, values)
            if last is not None: segments.append((last, end))
            last = end
        elif op == b'S':
            if color == (1, 0, 0): found.extend(segments)
            segments, last = [], None
        elif op == b'Do':
            form = resources['/XObject'][values[0]].get_object()
            if form['/Subtype'] == '/Image': images += 1
            elif form['/Subtype'] == '/Form':
                nested, raster = paths(form, reader, form.get('/Resources', resources),
                                       product(matrix, tuple(map(float, form.get('/Matrix', identity)))), color)
                found.extend(nested); images += raster
    return found, images

result = []
for angle in (0, 90, 180, 270):
    path = root / 'artifacts/verification/features' / f'restored-{angle}.pdf'
    reader = PdfReader(path); page = reader.pages[0]
    lines, images = paths(page.get_contents(), reader, page['/Resources'])
    assert images == 0, (angle, images)
    lengths = [hypot(b[0]-a[0], b[1]-a[1])*25.4/72 for a, b in lines]
    assert len(lengths) == 1 and abs(lengths[0]-100) < .01, (angle, lengths)
    a, b = lines[0]
    assert abs((a[0]-b[0]) if angle in (90,270) else (a[1]-b[1])) < .01
    result.append({'rotation': angle, 'controlMm': lengths[0], 'rasterImages': images})
output = root / 'artifacts/verification/vector-restore.json'
output.write_text(json.dumps(result, indent=2), encoding='utf-8')
print(json.dumps(result, indent=2))
