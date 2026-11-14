// Статические и динамические коллайдеры уровня: AABB в пространственной сетке.
// Почему не Rapier: персонаж кинематический, мир статичный — WASM-физика добавила бы ~1.5 МБ
// к загрузке на телефоне без выигрыша в поведении. Сетка 4 м даёт O(1) выборку соседей.
const CELL = 4;
export class World {
  constructor() { this.boxes = []; this.grid = new Map(); this.stamp = 0; }
  key(ix, iz) { return ix * 73856093 ^ iz * 19349663; }
  add(min, max, o = {}) {
    const b = { min, max, climb: o.climb !== false, kind: o.kind || 'wall', active: true, surf: o.surf || 'stone', id: this.boxes.length, tag: o.tag || null, _s: 0 };
    this.boxes.push(b);
    for (let ix = Math.floor(min[0] / CELL); ix <= Math.floor(max[0] / CELL); ix++)
      for (let iz = Math.floor(min[2] / CELL); iz <= Math.floor(max[2] / CELL); iz++) {
        const k = this.key(ix, iz); let l = this.grid.get(k); if (!l) this.grid.set(k, l = []); l.push(b);
      }
    return b;
  }
  // все боксы в XZ-прямоугольнике (без дублей)
  query(x0, z0, x1, z1, out = []) {
    out.length = 0; const s = ++this.stamp;
    for (let ix = Math.floor(Math.min(x0, x1) / CELL); ix <= Math.floor(Math.max(x0, x1) / CELL); ix++)
      for (let iz = Math.floor(Math.min(z0, z1) / CELL); iz <= Math.floor(Math.max(z0, z1) / CELL); iz++) {
        const l = this.grid.get(this.key(ix, iz)); if (!l) continue;
        for (const b of l) if (b._s !== s && b.active) { b._s = s; out.push(b); }
      }
    return out;
  }
  // высота опоры под кругом (x,z,r), не выше maxY
  groundAt(x, z, r, maxY, tmp = []) {
    let g = 0, gb = null;
    for (const b of this.query(x - r, z - r, x + r, z + r, tmp)) {
      if (b.kind === 'trigger') continue;
      if (x + r * .55 > b.min[0] && x - r * .55 < b.max[0] && z + r * .55 > b.min[2] && z - r * .55 < b.max[2] && b.max[1] <= maxY && b.max[1] > g) { g = b.max[1]; gb = b; }
    }
    return { y: g, box: gb };
  }
  // выталкивание вертикального цилиндра (x,z,r,[y0,y1]) из боксов; возвращает нормаль самой сильной стены
  push(p, r, y0, y1, hit, tmp = []) {
    hit.box = null; hit.nx = 0; hit.nz = 0; let best = 0;
    for (let it = 0; it < 3; it++) {
      let moved = false;
      for (const b of this.query(p.x - r, p.z - r, p.x + r, p.z + r, tmp)) {
        if (b.kind === 'trigger' || b.max[1] <= y0 + .001 || b.min[1] >= y1) continue;
        const cx = Math.max(b.min[0], Math.min(p.x, b.max[0])), cz = Math.max(b.min[2], Math.min(p.z, b.max[2]));
        let dx = p.x - cx, dz = p.z - cz, d2 = dx * dx + dz * dz;
        if (d2 >= r * r) continue;
        let d = Math.sqrt(d2), nx, nz, pen;
        if (d < 1e-5) { // центр внутри бокса: выталкиваем по кратчайшей оси
          const ex = [p.x - b.min[0], b.max[0] - p.x, p.z - b.min[2], b.max[2] - p.z], m = Math.min(...ex), i = ex.indexOf(m);
          nx = i === 0 ? -1 : i === 1 ? 1 : 0; nz = i === 2 ? -1 : i === 3 ? 1 : 0; pen = m + r;
        } else { nx = dx / d; nz = dz / d; pen = r - d; }
        p.x += nx * pen; p.z += nz * pen; moved = true;
        if (pen > best) { best = pen; hit.box = b; hit.nx = nx; hit.nz = nz; }
      }
      if (!moved) break;
    }
    return hit.box;
  }
  // отрезок a->b против боксов; возвращает t в [0,1] первого пересечения или 1
  ray(ax, ay, az, bx, by, bz, ignore, tmp = []) {
    const dx = bx - ax, dy = by - ay, dz = bz - az; let tmin = 1, hitBox = null;
    for (const b of this.query(Math.min(ax, bx), Math.min(az, bz), Math.max(ax, bx), Math.max(az, bz), tmp)) {
      if (b.kind === 'trigger' || b.kind === 'ladder' || (ignore === 'door' && b.kind === 'door') || (ignore === 'low' && b.kind === 'railing') || b === ignore) continue;
      let t0 = 0, t1 = tmin, ok = true;
      const o = [ax, ay, az], d = [dx, dy, dz];
      for (let k = 0; k < 3 && ok; k++) {
        if (Math.abs(d[k]) < 1e-9) { if (o[k] < b.min[k] || o[k] > b.max[k]) ok = false; continue; }
        let u0 = (b.min[k] - o[k]) / d[k], u1 = (b.max[k] - o[k]) / d[k]; if (u0 > u1) { const t = u0; u0 = u1; u1 = t; }
        if (u0 > t0) t0 = u0; if (u1 < t1) t1 = u1; if (t0 > t1) ok = false;
      }
      if (ok && t0 < tmin) { tmin = t0; hitBox = b; }
    }
    this.lastHit = hitBox; return tmin;
  }
  clear(ax, ay, az, bx, by, bz) { return this.ray(ax, ay, az, bx, by, bz) >= .999; }
}
