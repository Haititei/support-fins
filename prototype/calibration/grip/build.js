/**
 * Grip coupon, step 2: the site's Auto build once per ledge at that ledge's Tine grip
 * (opts.tineDensity, or Tines off), the rest at the site's PLA defaults, the walls
 * under that ledge kept.
 *
 *   deno run -A prototype/calibration/grip/build.js
 */
import { loadCoupon, finsWith, keep, supportOf, writeCoupon } from '../coupon.js';

const c = loadCoupon(import.meta.url);
const sup = [];
const inBox = (p, [x0, x1, y0, y1]) => p && p[0] - c.off.x >= x0 && p[0] - c.off.x <= x1 && p[1] - c.off.y >= y0 && p[1] - c.off.y <= y1;
console.log('ledge  setting                               walls  tines a wall');
for (const r of c.rungs) {
  const built = finsWith(c, { tines: r.tines ?? true, tineDensity: r.tineDensity ?? 0 });
  sup.push(...keep(c, supportOf(built), r.box));
  // built.props, not built.fins: a main-pass wall's fin record reads tines 0
  const walls = built.props.filter((p) => inBox(p.line?.[0], r.box));
  const tines = walls.map((p) => p.tines ?? 0);
  console.log(`${String(r.id).padEnd(5)}  ${r.label.padEnd(36)}  ${String(walls.length).padEnd(5)}  ${tines.join(' ')}`);
  if (walls.length < 3) throw new Error(`ledge ${r.id}: ${walls.length} walls, want 3`);
  if (r.tines === false && tines.some(Boolean)) throw new Error(`ledge ${r.id}: tines off but ${tines} built`);
  if (r.tines !== false && new Set(tines).size !== 1) throw new Error(`ledge ${r.id}: walls differ (${tines})`);
}
await writeCoupon(c, 'grip', 'Grip coupon', sup);
