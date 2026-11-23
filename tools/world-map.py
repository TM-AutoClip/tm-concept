"""Builds the app's compact world map (src/TMConcept.Core/Maps/Files/world.json) from Natural Earth's public-domain 1:110m countries.

Usage: python -I tools/world-map.py <ne_110m_admin_0_countries.geojson> <output world.json>
The source is github.com/nvkelso/natural-earth-vector, geojson/ne_110m_admin_0_countries.geojson (public domain, "Made with Natural
Earth"). Each country keeps its English name, its ISO 3166-1 alpha-3 code (or the Natural Earth ADM0_A3 code when it has none), the
centre of its label (LABEL_X/LABEL_Y) and its outline rings, longitude and latitude rounded to 0.1 degree and stored as tenths, each ring
delta-encoded (x0, y0, dx1, dy1, ...) so the file stays small. Rings with fewer than four points after rounding are dropped.
"""
import json
import sys


def ring(points):
    out = []
    last = None
    for lon, lat in points:
        p = (round(lon * 10), round(lat * 10))
        if p != last:
            out.append(p)
            last = p
    if len(out) < 4:
        return None
    flat = [out[0][0], out[0][1]]
    for (ax, ay), (bx, by) in zip(out, out[1:]):
        flat += [bx - ax, by - ay]
    return flat


def main(source, target):
    data = json.load(open(source, encoding='utf-8'))
    countries = []
    for feature in data['features']:
        props = feature['properties']
        geometry = feature['geometry']
        polygons = geometry['coordinates'] if geometry['type'] == 'MultiPolygon' else [geometry['coordinates']]
        rings = [r for polygon in polygons for r in (ring(polygon[0]),) if r]
        if not rings:
            continue
        code = props.get('ISO_A3') if props.get('ISO_A3') not in (None, '-99') else props.get('ADM0_A3')
        countries.append({
            'n': props.get('NAME_EN') or props.get('NAME'),
            'c': code,
            'x': round(props.get('LABEL_X', 0), 1),
            'y': round(props.get('LABEL_Y', 0), 1),
            'r': rings,
        })
    countries.sort(key=lambda c: c['c'] or '')
    with open(target, 'w', encoding='utf-8', newline='\n') as f:
        json.dump({'source': 'Natural Earth 1:110m admin-0 countries (public domain)', 'scale': 10, 'countries': countries}, f,
                  ensure_ascii=False, separators=(',', ':'))


if __name__ == '__main__':
    main(sys.argv[1], sys.argv[2])
