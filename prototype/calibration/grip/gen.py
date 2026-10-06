#!/usr/bin/env python3
"""Grip coupon: what does the Tine grip slider do on a print? (Tines > Tine grip)

Four deep 30 deg ledges off one bar, each built by the site's Auto at one Tine grip
setting, so a rung is three long walls whose tine combs differ at a glance:

    1 Tines off   2 light (slider left, the default)   3 middle   4 firm (slider right)

Tine spacing runs 5 mm (light) -> 2 mm (firm) along a wall's top, but every wall gets
at least 3 (PROP.minGripTines), so on a short wall the slider does nothing: the tine
coupon's 6 mm walls got 3 at every setting, which is why its ledges all looked alike.
Here a wall runs 23 mm up the ramp: 5 / 7 / 11 tines a wall. Shallower than 40 deg on
purpose: the same rise gives a longer wall (a 40 deg ledge this long is 20 mm tall).

ONE solid piece: a bar on the plate, ledges 1-2 on the near side, 3-4 on the far side.

    python3 prototype/calibration/grip/gen.py && deno run -A prototype/calibration/grip/build.js
"""
import math
import sys
from pathlib import Path

import trimesh

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))
from coupon import bx, dots, write  # noqa: E402

RUNGS = [
    {'label': 'Tines off', 'tines': False},
    {'label': 'Tine grip light (left, the default)', 'tineDensity': 0.0},
    {'label': 'Tine grip middle', 'tineDensity': 0.5},
    {'label': 'Tine grip firm (right)', 'tineDensity': 1.0},
]
NEAR = 2
ANGLE, BAR_W, Z0, RISE, TOP_T, W, STEP = 30.0, 10.0, 5.0, 14.0, 2.0, 32.0, 36.0
D = RISE / math.tan(math.radians(ANGLE))


def ledge(x, side):
    """Off the bar face: underside from (bar, Z0) up to (bar + D, Z0 + RISE) at ANGLE,
    a vertical outer face, a flat top."""
    y_in, y_out, top = side * (BAR_W / 2 - 0.5), side * (BAR_W / 2 + D), Z0 + RISE + TOP_T
    yz = [(y_in, Z0), (side * BAR_W / 2, Z0), (y_out, Z0 + RISE), (y_out, top), (y_in, top)]
    if side < 0:
        yz = yz[::-1]
    m = trimesh.creation.extrude_polygon(trimesh.path.polygons.Polygon(yz), W)
    m.apply_transform([[0, 0, 1, x], [1, 0, 0, 0], [0, 1, 0, 0], [0, 0, 0, 1]])
    return m


parts, rungs = [], []
top = Z0 + RISE + TOP_T
for k, r in enumerate(RUNGS):
    side = 1 if k < NEAR else -1
    x = 3.0 + (k if k < NEAR else k - NEAR) * STEP
    parts.append(ledge(x, side))
    n = k + 1
    parts += dots(n, x + 3.0, side * (BAR_W / 2 + D - 2.0), top, step=2.2, size=1.2)
    y0, y1 = sorted([side * BAR_W / 2, side * (BAR_W / 2 + D)])
    rungs.append({'id': n, **r, 'box': [x - 1.5, x + W + 1.5, y0 - 0.5, y1 + 0.5]})
L = 3.0 + (NEAR - 1) * STEP + W + 3.0
parts.append(bx(0, L, -BAR_W / 2, BAR_W / 2, 0, top + 2))
m = write(__file__, parts, rungs)
print(f'grip coupon {m.extents.round(1)} mm, ledges reach {D:.1f} mm out')
