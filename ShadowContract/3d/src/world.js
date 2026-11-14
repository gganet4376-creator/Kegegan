import * as THREE from 'three';
import { mergeGeometries } from 'three/examples/jsm/utils/BufferGeometryUtils.js';
import { World } from './physics.js';
import { A } from './assets.js';

// Уровень «Колокольня Орлова»: функциональная геометрия (каждая видимая опора — коллайдер,
// каждый фасад, по которому можно лезть, помечен climb). Статика сливается по материалам
// в несколько мешей → единицы draw calls даже на телефоне.
const V = (x, y, z) => new THREE.Vector3(x, y, z);

function worldUV(g, ts) {
  const p = g.attributes.position, n = g.attributes.normal, uv = g.attributes.uv;
  for (let i = 0; i < p.count; i++) {
    const nx = Math.abs(n.getX(i)), ny = Math.abs(n.getY(i));
    let u, v;
    if (nx > .5) { u = p.getZ(i); v = p.getY(i); } else if (ny > .5) { u = p.getX(i); v = p.getZ(i); } else { u = p.getX(i); v = p.getY(i); }
    uv.setXY(i, u / ts, v / ts);
  }
  return g;
}
function boxG(x0, y0, z0, x1, y1, z1, ts = 2.5) {
  const g = new THREE.BoxGeometry(x1 - x0, y1 - y0, z1 - z0);
  g.translate((x0 + x1) / 2, (y0 + y1) / 2, (z0 + z1) / 2); return worldUV(g, ts);
}
function radialTex(inner, outer) {
  const c = document.createElement('canvas'); c.width = c.height = 128; const x = c.getContext('2d');
  const g = x.createRadialGradient(64, 64, 0, 64, 64, 64); g.addColorStop(0, inner); g.addColorStop(1, outer);
  x.fillStyle = g; x.fillRect(0, 0, 128, 128); const t = new THREE.CanvasTexture(c); t.colorSpace = THREE.SRGBColorSpace; return t;
}

export function buildLevel(scene) {
  const phys = new World(), T = A.tex;
  const std = (c, n, o) => new THREE.MeshStandardMaterial(Object.assign({ map: T[c + '_c'], normalMap: T[n + '_n'], roughness: .9 }, o));
  const M = {
    ground: std('ground', 'ground', { color: 0x9a948a, roughness: .82, normalScale: new THREE.Vector2(1.2, 1.2) }),
    plasterA: std('plaster', 'plaster', { color: 0xd9b48a }), plasterB: std('plaster', 'plaster', { color: 0xc48f6f }), plasterC: std('plaster', 'plaster', { color: 0xb3ad9f }),
    stone: std('stone', 'stone', { roughness: .85 }), roof: std('roof', 'roof', { roughness: .7 }), wood: std('wood', 'wood', { roughness: .78 }),
    darkwood: std('wood', 'wood', { color: 0x6b5a4a, roughness: .8 }),
    winDark: new THREE.MeshStandardMaterial({ color: 0x0a0e15, roughness: .12, metalness: .3 }),
    winLit: new THREE.MeshStandardMaterial({ color: 0x24160a, emissive: 0xffa04a, emissiveIntensity: 1.35, roughness: .5 }),
    iron: new THREE.MeshStandardMaterial({ color: 0x24262b, metalness: .75, roughness: .45 }),
    lampGlass: new THREE.MeshStandardMaterial({ color: 0x302010, emissive: 0xffb866, emissiveIntensity: 2, roughness: .3 }),
    clothR: new THREE.MeshStandardMaterial({ color: 0x8a2f25, roughness: 1, side: THREE.DoubleSide }),
    clothG: new THREE.MeshStandardMaterial({ color: 0x3d5a3a, roughness: 1, side: THREE.DoubleSide }),
    clothB: new THREE.MeshStandardMaterial({ color: 0x2d4560, roughness: 1, side: THREE.DoubleSide }),
    hay: new THREE.MeshStandardMaterial({ color: 0xb8933f, roughness: 1, map: T.wood_c }),
    water: new THREE.MeshStandardMaterial({ color: 0x0a1822, roughness: .05, metalness: .2, normalMap: T.stone_n, normalScale: new THREE.Vector2(.15, .15) }),
    bronze: new THREE.MeshStandardMaterial({ color: 0x8a6a2c, metalness: .9, roughness: .35 }),
    rope: new THREE.MeshStandardMaterial({ color: 0xb9a17a, roughness: 1 }),
  };
  const B = {}; const add = (k, g) => (B[k] || (B[k] = [])).push(g);
  const solid = (k, x0, y0, z0, x1, y1, z1, o = {}) => { add(k, boxG(x0, y0, z0, x1, y1, z1, o.ts)); if (o.col !== false) return phys.add([x0, y0, z0], [x1, y1, z1], o); };
  const L = { phys, M, lamps: [], hides: [], doors: [], inter: [], exits: [], nav: [], zones: [], decals: [], dyn: [] };

  // --- земля ---
  const gg = new THREE.PlaneGeometry(140, 140, 1, 1); gg.rotateX(-Math.PI / 2); worldUV(gg, 3.2); add('ground', gg);

  // --- дома ---
  const winsDark = [], winsLit = [];
  function windowAt(x, y, z, nx, nz, rnd) {
    const w = .95, h = 1.35, along = nx !== 0 ? [0, 0, 1] : [1, 0, 0], ox = x + nx * .06, oz = z + nz * .06;
    const fx0 = ox - along[0] * w / 2 - Math.abs(nx) * .07, fx1 = ox + along[0] * w / 2 + Math.abs(nx) * .07, fz0 = oz - along[2] * w / 2 - Math.abs(nz) * .07, fz1 = oz + along[2] * w / 2 + Math.abs(nz) * .07;
    add('darkwood', boxG(fx0, y - h / 2, fz0, fx1, y + h / 2, fz1, 1));
    const gx0 = x + nx * .1 - along[0] * (w / 2 - .1), gx1 = x + nx * .12 + along[0] * (w / 2 - .1), gz0 = z + nz * .1 - along[2] * (w / 2 - .1), gz1 = z + nz * .12 + along[2] * (w / 2 - .1);
    (rnd < .32 ? winsLit : winsDark).push(boxG(Math.min(gx0, gx1), y - h / 2 + .1, Math.min(gz0, gz1), Math.max(gx0, gx1), y + h / 2 - .1, Math.max(gz0, gz1), 1));
    add('stone', boxG(fx0 - along[0] * .05 + nx * .1, y - h / 2 - .12, fz0 - along[2] * .05 + nz * .1, fx1 + along[0] * .05 + nx * .1, y - h / 2, fz1 + along[2] * .05 + nz * .1, 1));
    if (rnd > .6) { // ставни
      for (const s of [-1, 1]) { const sx = ox + along[0] * s * (w / 2 + .28) + nx * .03, sz = oz + along[2] * s * (w / 2 + .28) + nz * .03;
        add('wood', boxG(sx - along[0] * .25 - Math.abs(nx) * .03, y - h / 2, sz - along[2] * .25 - Math.abs(nz) * .03, sx + along[0] * .25 + Math.abs(nx) * .03, y + h / 2, sz + along[2] * .25 + Math.abs(nz) * .03, 1)); }
    }
  }
  let seed = 7; const rnd = () => (seed = (seed * 16807) % 2147483647) / 2147483647;
  function house(x0, z0, x1, z1, h, o = {}) {
    const mat = o.mat || 'plasterA';
    phys.add([x0, 0, z0], [x1, h, z1], { climb: true, surf: 'tile', tag: o.tag });
    add('stone', boxG(x0 - .07, 0, z0 - .07, x1 + .07, .85, z1 + .07, 2));
    add(mat, boxG(x0, .85, z0, x1, h - .3, z1, 3));
    add('stone', boxG(x0 - .16, h - .3, z0 - .16, x1 + .16, h, z1 + .16, 2));
    add('roof', boxG(x0 + .05, h, z0 + .05, x1 - .05, h + .03, z1 - .05, 2.2));
    // карниз из черепицы по периметру (визуальный)
    for (const [ax, az, bx, bz, nx, nz] of [[x0, z0, x1, z0, 0, -1], [x0, z1, x1, z1, 0, 1], [x0, z0, x0, z1, -1, 0], [x1, z0, x1, z1, 1, 0]]) {
      const len = Math.hypot(bx - ax, bz - az), g = new THREE.BoxGeometry(nx ? .6 : len + .6, .08, nx ? len + .6 : .6);
      g.rotateX(nz * .35); g.rotateZ(-nx * .35); g.translate((ax + bx) / 2 + nx * .35, h - .42, (az + bz) / 2 + nz * .35); add('roof', worldUV(g, 1.5));
    }
    const floors = Math.max(1, Math.floor((h - 1.2) / 3));
    for (const [nx, nz, a0, a1, fixed] of [[0, 1, x0, x1, z1], [0, -1, x0, x1, z0], [1, 0, z0, z1, x1], [-1, 0, z0, z1, x0]]) {
      if (o.skip && o.skip.includes(nx + ',' + nz)) continue;
      const len = a1 - a0, n = Math.max(1, Math.floor((len - 1.2) / 2.6)), step = len / n;
      for (let f = 0; f < floors; f++) for (let i = 0; i < n; i++) {
        const a = a0 + step * (i + .5), y = 2.6 + f * 3;
        if (f === 0 && o.door && o.door[0] === nx && o.door[1] === nz && Math.abs(a - (o.door[2] ?? (a0 + a1) / 2)) < step / 2) continue;
        windowAt(nx ? fixed : a, y, nx ? a : fixed, nx, nz, rnd());
      }
      // водосточная труба на углу — визуальная подсказка, что фасад проходим
      const pc = new THREE.CylinderGeometry(.06, .06, h - .4, 6); pc.translate(nx ? fixed + nx * .12 : a0 + .25, (h - .4) / 2, nx ? a0 + .25 : fixed + nz * .12); add('iron', pc);
    }
    if (o.door) { const [nx, nz, at] = o.door, c = at ?? (nx ? (z0 + z1) / 2 : (x0 + x1) / 2), fx = nx > 0 ? x1 : nx < 0 ? x0 : c, fz = nz > 0 ? z1 : nz < 0 ? z0 : c;
      add('darkwood', boxG(fx - (nx ? .05 : .65), 0, fz - (nz ? .05 : .65), fx + (nx ? .05 : .65), 2.3, fz + (nz ? .05 : .65), 1));
      add('stone', boxG(fx - (nx ? .1 : .8), 2.3, fz - (nz ? .1 : .8), fx + (nx ? .1 : .8), 2.55, fz + (nz ? .1 : .8), 1));
      L.lamps.push(lantern(fx + nx * .35, 2.9, fz + nz * .35, nx, nz)); }
    if (o.balcony) for (const [nx, nz, at, y] of o.balcony) balcony(nx, nz, at, y, x0, z0, x1, z1);
    if (o.chimney) { const [cx, cz] = o.chimney; solid('stone', cx - .4, h, cz - .4, cx + .4, h + 1.5, cz + .4, { surf: 'stone' }); }
  }
  function balcony(nx, nz, at, y, x0, z0, x1, z1) {
    const fx = nx > 0 ? x1 : nx < 0 ? x0 : at, fz = nz > 0 ? z1 : nz < 0 ? z0 : at, w = 1.4, d = .95;
    const bx0 = nx ? Math.min(fx, fx + nx * d) : fx - w, bx1 = nx ? Math.max(fx, fx + nx * d) : fx + w, bz0 = nz ? Math.min(fz, fz + nz * d) : fz - w, bz1 = nz ? Math.max(fz, fz + nz * d) : fz + w;
    solid('wood', bx0, y - .15, bz0, bx1, y, bz1, { surf: 'wood', kind: 'prop', ts: 1.2 });
    // перила: низкий коллайдер, через который персонаж перелезает
    const rx0 = nx ? (nx > 0 ? bx1 - .06 : bx0) : bx0, rx1 = nx ? (nx > 0 ? bx1 : bx0 + .06) : bx1, rz0 = nz ? (nz > 0 ? bz1 - .06 : bz0) : bz0, rz1 = nz ? (nz > 0 ? bz1 : bz0 + .06) : bz1;
    phys.add([rx0, y, rz0], [rx1, y + .9, rz1], { kind: 'railing', surf: 'wood' });
    add('iron', boxG(rx0, y + .85, rz0, rx1, y + .92, rz1, 1));
    const n = 7; for (let i = 0; i <= n; i++) { const t = i / n, px = rx0 + (rx1 - rx0) * (nx ? .5 : t), pz = rz0 + (rz1 - rz0) * (nz ? .5 : t); add('iron', boxG(px - .02, y, pz - .02, px + .02, y + .88, pz + .02, 1)); }
    windowAt(nx ? fx : at, y + 1.15, nz ? fz : at, nx, nz, .1);
  }
  function lantern(x, y, z, nx = 0, nz = 0, post = false) {
    if (post) { const p = new THREE.CylinderGeometry(.08, .12, y, 8); p.translate(x, y / 2, z); add('iron', p); phys.add([x - .13, 0, z - .13], [x + .13, y - .3, z + .13], { kind: 'prop', climb: false }); }
    else add('iron', boxG(x - nx * .35 - .04, y + .2, z - nz * .35 - .04, x + .04, y + .26, z + .04, 1));
    add('lampGlass', boxG(x - .13, y - .2, z - .13, x + .13, y + .2, z + .13, 1));
    add('iron', boxG(x - .17, y + .2, z - .17, x + .17, y + .27, z + .17, 1));
    return { pos: V(x, y, z), r: 8.5, i: 1 };
  }
  function crate(x, z, s = .9, y = 0) { solid('wood', x - s / 2, y, z - s / 2, x + s / 2, y + s, z + s / 2, { surf: 'wood', kind: 'prop', ts: 1 }); }
  function barrel(x, z, y = 0) { const g = new THREE.CylinderGeometry(.38, .38, 1, 12); g.translate(x, y + .5, z); add('darkwood', worldUV(g, 1)); phys.add([x - .36, y, z - .36], [x + .36, y + 1, z + .36], { kind: 'prop', surf: 'wood' }); }
  function hayCart(x, z, rot = 0) {
    const w = rot ? 1.4 : 2.6, d = rot ? 2.6 : 1.4;
    solid('wood', x - w / 2, .45, z - d / 2, x + w / 2, .85, z + d / 2, { surf: 'wood', kind: 'prop', ts: 1 });
    for (const sx of [-1, 1]) { const wh = new THREE.CylinderGeometry(.45, .45, .1, 14); wh.rotateZ(rot ? 0 : Math.PI / 2); if (rot) wh.rotateX(Math.PI / 2); wh.rotateY(rot ? Math.PI / 2 : 0); wh.translate(x + (rot ? sx * .75 : 0), .45, z + (rot ? 0 : sx * .75)); add('darkwood', worldUV(wh, 1)); }
    const hay = new THREE.SphereGeometry(1, 14, 8, 0, Math.PI * 2, 0, Math.PI / 2); hay.scale(w * .48, .75, d * .48); hay.translate(x, .85, z); add('hay', worldUV(hay, 1));
    L.hides.push({ pos: V(x, 0, z), r: 1.5, kind: 'hay', bodies: 0, cap: 2, top: 1.6 });
  }
  function stall(x, z, cloth) {
    solid('wood', x - 1.3, 0, z - .45, x + 1.3, 1, z + .45, { surf: 'wood', kind: 'prop', ts: 1 });
    for (const sx of [-1, 1]) for (const sz of [-1, 1]) add('darkwood', boxG(x + sx * 1.25 - .05, 0, z + sz * .7 - .05, x + sx * 1.25 + .05, 2.4 + (sz < 0 ? .25 : 0), z + sz * .7 + .05, 1));
    const c = new THREE.PlaneGeometry(2.9, 1.8, 6, 1); c.rotateX(-Math.PI / 2 + .28); c.translate(x, 2.5, z); const pa = c.attributes.position;
    for (let i = 0; i < pa.count; i++) pa.setY(i, pa.getY(i) + Math.sin(pa.getX(i) * 3) * .04); c.computeVertexNormals(); add(cloth, c);
    for (let i = 0; i < 4; i++) add(i % 2 ? 'clothR' : 'hay', boxG(x - 1 + i * .55, 1, z - .2, x - .7 + i * .55, 1.25, z + .2, 1));
  }

  // Площадь x[-14,14] z[-12,12]; север (−z) — колокольня, за ней двор.
  house(-24, -12, -15, -3, 7, { mat: 'plasterB', door: [1, 0, -7.5], balcony: [[1, 0, -5, 3.6]], chimney: [-20, -6] });
  house(-24, 1, -15, 10, 10, { mat: 'plasterA', door: [1, 0, 7], balcony: [[0, -1, -19.5, 6.6]], tag: 'H5' });
  house(-12, -20, -5.5, -13, 15, { mat: 'plasterC', door: [0, 1, -8.8], balcony: [[0, 1, -10.5, 3.6], [1, 0, -16.5, 9.6]], chimney: [-10, -18], tag: 'merchant' });
  house(5.5, -20, 12, -13, 8, { mat: 'plasterB', door: [0, 1, 9], chimney: [10.5, -18], tag: 'H3' });
  house(15, -12, 24, -2, 9, { mat: 'plasterA', door: [-1, 0, -7], balcony: [[-1, 0, -9.5, 3.6]] });
  house(15, 2, 24, 12, 6.5, { mat: 'plasterC', door: [-1, 0, 7], tag: 'workerHouse' });
  house(-14, 14, -3, 22, 8, { mat: 'plasterA', door: [0, -1, -8], balcony: [[0, -1, -11.5, 3.6]], chimney: [-6, 20] });
  house(3, 14, 14, 22, 11, { mat: 'plasterB', door: [0, -1, 8], balcony: [[-1, 0, 18, 6.6], [0, -1, 5.5, 3.6]] });
  house(-26, 14, -16, 24, 9, { mat: 'plasterC' }); house(16, 14, 26, 24, 7.5, { mat: 'plasterA' });
  house(-26, -30, -15, -13, 8.5, { mat: 'plasterA', skip: ['-1,0'] }); house(15, -30, 26, -13, 7, { mat: 'plasterC', skip: ['1,0'] });

  // Колокольня x[-4,4] z[-20,-12], галерея на 12 м, дверь на юг, лестница внутри у северной стены.
  const tw = (x0, z0, x1, z1) => { phys.add([x0, 0, z0], [x1, 11.6, z1], { climb: true, surf: 'stone' }); add('stone', boxG(x0, 0, z0, x1, 11.6, z1, 3)); };
  tw(-4, -12.8, -.8, -12); tw(.8, -12.8, 4, -12); phys.add([-.8, 2.6, -12.8], [.8, 11.6, -12], { surf: 'stone' }); add('stone', boxG(-.8, 2.6, -12.8, .8, 11.6, -12, 3));
  tw(-4, -20, 4, -19.2); tw(-4, -19.2, -3.2, -12.8); tw(3.2, -19.2, 4, -12.8);
  for (const y of [3.2, 6.2, 9.2]) { // каменные пояса-уступы: видимые зацепы для лазания
    add('stone', boxG(-4.18, y, -20.18, 4.18, y + .28, -11.82, 1.5)); }
  const fl = (x0, z0, x1, z1) => { phys.add([x0, 11.6, z0], [x1, 12, z1], { surf: 'wood', kind: 'floor' }); add('wood', boxG(x0, 11.6, z0, x1, 12, z1, 2)); };
  fl(-4.2, -17.8, 4.2, -11.8); fl(-4.2, -20.2, -.6, -17.8); fl(.6, -20.2, 4.2, -17.8); fl(-.6, -20.2, .6, -19.2);
  // крышка люка: по ней ходят; лестница проходит сквозь неё сценой подъёма
  phys.add([-.6, 11.6, -19.2], [.6, 12, -17.8], { surf: 'wood', kind: 'floor' }); add('darkwood', boxG(-.6, 11.62, -19.2, .6, 12.02, -17.8, 1)); add('iron', boxG(-.5, 12.02, -18.6, .5, 12.06, -18.4, 1));
  // перила галереи (с проёмом на севере к лестнице-люку не нужно — люк в полу)
  const rail = (x0, z0, x1, z1) => { phys.add([x0, 12, z0], [x1, 13, z1], { kind: 'railing', surf: 'stone' }); add('stone', boxG(x0, 12, z0, x1, 12.15, z1, 1)); add('stone', boxG(x0, 12.85, z0, x1, 13, z1, 1));
    const n = Math.max(2, Math.round(Math.max(x1 - x0, z1 - z0) / .45)); for (let i = 0; i <= n; i++) { const t = i / n, px = x0 + (x1 - x0) * t, pz = z0 + (z1 - z0) * t; const b = new THREE.CylinderGeometry(.07, .09, .7, 6); b.translate(Math.min(Math.max(px, x0 + .09), x1 - .09), 12.5, Math.min(Math.max(pz, z0 + .09), z1 - .09)); add('stone', worldUV(b, 1)); } };
  rail(-4.2, -12.05, 4.2, -11.8); rail(-4.2, -20.2, 4.2, -19.95); rail(-4.2, -19.95, -3.95, -12.05); rail(3.95, -19.95, 4.2, -12.05);
  for (const [px, pz] of [[-3.9, -12.1], [3.9, -12.1], [-3.9, -19.9], [3.9, -19.9]]) solid('stone', px - .3, 12, pz - .3, px + .3, 15.9, pz + .3, { ts: 1.5 });
  solid('stone', -4.5, 15.9, -20.5, 4.5, 16.3, -11.5, { ts: 2 });
  const pyr = new THREE.ConeGeometry(6.6, 4.2, 4, 1); pyr.rotateY(Math.PI / 4); pyr.translate(0, 18.4, -16); add('roof', worldUV(pyr, 1.6));
  add('darkwood', boxG(-4, 15.3, -16.15, 4, 15.6, -15.85, 1)); // балка колокола
  // лестница внутри (вертикальный коллайдер-«ladder»)
  phys.add([-.45, 0, -19.2], [.45, 11.55, -19.05], { kind: 'ladder', climb: true, surf: 'wood' });
  for (let y = .3; y < 11.6; y += .35) add('darkwood', boxG(-.4, y, -19.18, .4, y + .05, -19.06, 1));
  add('darkwood', boxG(-.48, 0, -19.18, -.4, 11.6, -19.06, 1)); add('darkwood', boxG(.4, 0, -19.18, .48, 11.6, -19.06, 1));
  // пол внутри и факел
  L.lamps.push({ pos: V(2.6, 2.4, -14), r: 5, i: .7, inner: true });
  add('lampGlass', boxG(2.9, 2.2, -14.15, 3.15, 2.6, -13.85, 1));
  // дверь колокольни (функциональная)
  const tDoor = door(-.8, -12.65, .8, -12.15, 2.6, 'z', 'towerDoor');
  L.lamps.push(lantern(-1.4, 3, -11.6, 0, 1)); L.lamps.push(lantern(1.4, 3, -11.6, 0, 1));
  L.lamps.push({ pos: V(-3.9, 13.7, -12.1), r: 7, i: .9 }); add('lampGlass', boxG(-3.75, 13.4, -12.25, -3.55, 13.8, -11.95, 1));

  // Колокол (ловушка): висит над центром галереи
  const bellG = new THREE.LatheGeometry([new THREE.Vector2(0, 1.2), new THREE.Vector2(.3, 1.15), new THREE.Vector2(.45, .9), new THREE.Vector2(.55, .35), new THREE.Vector2(.78, .05), new THREE.Vector2(.8, 0), new THREE.Vector2(.62, .02)], 20);
  const bell = new THREE.Mesh(bellG, M.bronze); bell.position.set(0, 13.9, -16); bell.castShadow = true; scene.add(bell);
  const ropeGeo = new THREE.CylinderGeometry(.025, .025, 1, 5); ropeGeo.translate(0, .5, 0);
  const bellRope = new THREE.Mesh(ropeGeo, M.rope); bellRope.position.set(0, 15.1, -16); bellRope.scale.y = .25; scene.add(bellRope);
  // трос от балки по северному фасаду вниз во двор к кнехту
  const pull = new THREE.Mesh(ropeGeo, M.rope); pull.position.set(1.6, 1.1, -20.35); pull.scale.y = 14.3; scene.add(pull);
  const pull2 = new THREE.Mesh(ropeGeo, M.rope); pull2.position.set(0, 15.45, -16); pull2.rotation.z = -Math.atan2(1.6, .2) ; pull2.rotation.x = Math.atan2(4.35, 1); pull2.scale.y = 4.7; scene.add(pull2);
  add('darkwood', boxG(1.35, 0, -20.75, 1.85, 1.2, -20.4, .8)); phys.add([1.35, 0, -20.75], [1.85, 1.2, -20.4], { kind: 'prop', surf: 'wood' });
  L.bell = { mesh: bell, rope: bellRope, pull, pull2, y: 13.9, state: 'hung', vy: 0, x: 0, z: -16, floor: 12 };
  L.inter.push({ id: 'cleat', pos: V(1.6, 0, -20.2), r: 1.4, label: 'Отпустить колокол', act: 'dropBell' });

  // Двор x[-8.6,8.6] z[-30.6,-19.8], стены 3.2 м, ворота на востоке z[-26,-24].
  const wall = (x0, z0, x1, z1) => solid('stone', x0, 0, z0, x1, 3.2, z1, { ts: 2.2 });
  wall(-8.6, -30.6, 8.6, -30); wall(-8.6, -30, -8, -20.4); wall(8, -30, 8.6, -26); wall(8, -24, 8.6, -20.4);
  wall(-8.6, -20.4, -4, -19.8); wall(4, -20.4, 8.6, -19.8);
  for (const [a, b] of [[-8.6, -30.6], [8.6, -30.6]]) add('stone', boxG(a - .15, 3.2, b - .15, a + .15, 3.5, b + .15, 1));
  add('stone', boxG(7.9, 3.2, -26.2, 8.7, 3.6, -23.8, 1));
  door(8.05, -26, 8.55, -24, 2.9, 'x', 'gate');
  L.lamps.push(lantern(9, 3, -26.6, 1, 0)); L.lamps.push(lantern(5.5, 2.6, -21, 0, 1, true));
  solid('darkwood', -5.2, 0, -27, -2.8, .9, -25.8, { surf: 'wood', kind: 'prop', ts: 1 });  // верстак звонаря
  hayCart(-1.6, -22.8); crate(6.4, -29.2); crate(7.2, -29.2); crate(6.8, -29.2, .9, .9); barrel(-7.2, -29.3); barrel(-6.4, -29.4);
  L.hides.push({ pos: V(-6.8, 0, -28.4), r: 1.3, kind: 'chest', bodies: 0, cap: 2, top: 1 });
  solid('darkwood', -7.6, 0, -28.9, -6, .8, -27.9, { surf: 'wood', kind: 'prop', ts: 1 });

  // Площадь: фонтан, лавки, фонари
  const basin = new THREE.LatheGeometry([new THREE.Vector2(0, .3), new THREE.Vector2(2.2, .3), new THREE.Vector2(2.2, .75), new THREE.Vector2(2.5, .78), new THREE.Vector2(2.6, 0)], 28); basin.translate(0, 0, 2); add('stone', worldUV(basin, 1.5));
  const col = new THREE.CylinderGeometry(.35, .5, 2.4, 12); col.translate(0, 1.2, 2); add('stone', worldUV(col, 1));
  const top = new THREE.CylinderGeometry(.9, .3, .3, 16); top.translate(0, 2.3, 2); add('stone', worldUV(top, 1));
  const wat = new THREE.CircleGeometry(2.2, 28); wat.rotateX(-Math.PI / 2); wat.translate(0, .62, 2); add('water', worldUV(wat, 2));
  phys.add([-2.3, 0, -.3], [2.3, .78, 4.3], { kind: 'prop', surf: 'stone' }); phys.add([-.5, .78, 1.5], [.5, 2.45, 2.5], { kind: 'prop' });
  L.fountain = V(0, .8, 2);
  stall(-9, 6, 'clothR'); stall(9, 7, 'clothG'); stall(-8.5, -7.5, 'clothB');
  for (const [x, z] of [[-11, -9.5], [11, -9.5], [-11, 10], [11, 10]]) L.lamps.push(lantern(x, 3.6, z, 0, 0, true));
  for (const [x, z] of [[-12.6, 3], [-12.6, 4], [12.8, -3]]) barrel(x, z);
  crate(12.6, -4.5); crate(12.6, -5.5); crate(12.6, -5, .9, .9);

  // Улицы: запад (телега с сеном под крышей дома), восток (люк канализации), юг (ворота города)
  hayCart(-17.6, -1, 0); barrel(-14.6, -2.6); crate(-21, 11.4);
  L.lamps.push(lantern(-20, 3.2, 1.1, 0, 1)); L.lamps.push(lantern(19, 3.2, -1.9, 0, 1));
  const hatch = new THREE.CylinderGeometry(.6, .6, .05, 16); hatch.translate(20, .02, 0); add('iron', hatch);
  L.exits.push({ id: 'sewer', pos: V(20, 0, 0), r: 1.2, label: 'Уйти через канализацию' });
  add('stone', boxG(-4.5, 0, 27.5, -2.5, 9, 29, 2)); add('stone', boxG(2.5, 0, 27.5, 4.5, 9, 29, 2)); add('stone', boxG(-4.5, 6, 27.5, 4.5, 9, 29, 2));
  phys.add([-4.5, 0, 27.5], [-2.5, 9, 29], { climb: false }); phys.add([2.5, 0, 27.5], [4.5, 9, 29], { climb: false }); phys.add([-4.5, 6, 27.5], [4.5, 9, 29], { climb: false });
  L.exits.push({ id: 'gate', pos: V(0, 0, 30.5), r: 2.4, label: 'Покинуть город' });
  L.lamps.push(lantern(-2.2, 4, 26.9, 0, -1)); L.lamps.push(lantern(2.2, 4, 26.9, 0, -1));
  crate(-1.2, -28.8, .8); // мелочь

  // Граница города: высокая стена (не лазается) + дальний силуэт
  const bound = (x0, z0, x1, z1) => { phys.add([x0, 0, z0], [x1, 30, z1], { climb: false, kind: 'bound' }); };
  bound(-34, -40, -26, 40); bound(26, -40, 34, 40); bound(-34, -40, 34, -30.6); bound(-34, 29, -4.5, 40); bound(4.5, 29, 34, 40); bound(-4.5, 32, 4.5, 40);
  add('stone', boxG(-30, 0, -36, 30, 8, -34, 3)); add('stone', boxG(-30, 0, 30, -4.5, 8, 32, 3)); add('stone', boxG(4.5, 0, 30, 30, 8, 32, 3));
  add('stone', boxG(-30, 0, -36, -28, 8, 32, 3)); add('stone', boxG(28, 0, -36, 30, 8, 32, 3));
  // невидимые края: между домами и границей не проваливаемся
  const silG = []; for (let i = 0; i < 46; i++) { const a = i / 46 * Math.PI * 2, r = 52 + (i * 37 % 11), w = 6 + i % 4 * 2, h = 10 + (i * 13 % 9) * 2;
    const g = new THREE.BoxGeometry(w, h, 6); g.translate(0, h / 2, 0); g.rotateY(-a); g.translate(Math.cos(a) * r, 0, Math.sin(a) * r); silG.push(g); }
  const sil = new THREE.Mesh(mergeGeometries(silG), new THREE.MeshStandardMaterial({ color: 0x0c1019, roughness: 1 })); scene.add(sil);

  // двери: коллайдер активен, пока закрыта
  function door(x0, z0, x1, z1, h, axis, id) {
    const box = phys.add([x0, 0, z0], [x1, h, z1], { kind: 'door', climb: false, surf: 'wood' });
    const piv = new THREE.Group(), w = axis === 'z' ? x1 - x0 : z1 - z0, leaf = new THREE.Mesh(new THREE.BoxGeometry(axis === 'z' ? w : .12, h, axis === 'z' ? .12 : w), M.darkwood);
    leaf.position.set(axis === 'z' ? w / 2 : 0, h / 2, axis === 'z' ? 0 : w / 2); leaf.castShadow = true; piv.add(leaf);
    piv.position.set(x0, 0, axis === 'z' ? (z0 + z1) / 2 : z0); if (axis === 'x') piv.position.x = (x0 + x1) / 2; scene.add(piv);
    const d = { id, box, piv, open: 0, target: 0, axis, center: V((x0 + x1) / 2, 0, (z0 + z1) / 2), autoClose: 0 };
    L.doors.push(d); return d;
  }

  // ---- зоны: где присутствие героя само по себе подозрительно ----
  L.zones.push({ id: 'yard', min: [-8.6, -1, -30.6], max: [8.6, 6, -19.8], allow: ['worker'] });
  L.zones.push({ id: 'tower', min: [-4, -1, -20], max: [4, 11.5, -12], allow: ['worker'] });
  L.zones.push({ id: 'gallery', min: [-4.3, 11.5, -20.3], max: [4.3, 20, -11.7], allow: ['worker'], strict: true });

  // ---- навигационный граф (узлы по слоям; ребра — если прямая видимость на уровне пояса) ----
  const N = (x, z, y = 0, layer = 'g') => { L.nav.push({ p: V(x, y, z), layer, e: [] }); return L.nav.length - 1; };
  const ring = []; for (let i = 0; i < 10; i++) { const a = i / 10 * Math.PI * 2; ring.push(N(Math.cos(a) * 7.2, 2 + Math.sin(a) * 7.2)); }
  [[-12, -10], [12, -10], [-12, 11], [12, 11], [0, -10.4], [0, 12.5], [-13.5, -15], [-13.5, -25], [-10.5, -27.5], [13.5, -15], [12, -25], [9.8, -25], [6.6, -25], [3, -25.6], [-3.4, -25], [-6.4, -27], [4.6, -28.2], [1.8, -21.6], [-6.2, -22.2],
    [-17, -1], [-22, -1], [17, 0], [22, 0], [14.2, 7], [0, 17], [0, 23], [0, 29], [0, -14.2], [0, -18.4], [-13.5, 12.5], [13.5, -1]].forEach(([x, z]) => N(x, z));
  for (const [x, z] of [[-2.8, -13.2], [2.8, -13.2], [2.8, -18.6], [-2.8, -18.6], [0, -15.2], [-2.9, -16], [0, -18.6]]) N(x, z, 12, 'gal');
  N(-22, -11, 7, 'r1'); N(-22, -4, 7, 'r1'); N(-17, -4, 7, 'r1'); N(-17, -11, 7, 'r1');
  N(6.5, -14, 8, 'r2'); N(11, -14, 8, 'r2'); N(11, -19, 8, 'r2'); N(6.5, -19, 8, 'r2');
  for (let i = 0; i < L.nav.length; i++) for (let j = i + 1; j < L.nav.length; j++) {
    const a = L.nav[i], b = L.nav[j]; if (a.layer !== b.layer) continue; const d = a.p.distanceTo(b.p); if (d > 12.5) continue;
    let ok = true; for (const hy of [.5, 1.4]) if (phys.ray(a.p.x, a.p.y + hy, a.p.z, b.p.x, b.p.y + hy, b.p.z, 'door') < .999) ok = false;
    // проверяем ширину прохода: два параллельных луча со сдвигом
    if (ok) { const dx = b.p.x - a.p.x, dz = b.p.z - a.p.z, l = Math.hypot(dx, dz), ox = -dz / l * .35, oz = dx / l * .35;
      for (const s of [-1, 1]) if (phys.ray(a.p.x + ox * s, a.p.y + .6, a.p.z + oz * s, b.p.x + ox * s, b.p.y + .6, b.p.z + oz * s, 'door') < .999) ok = false; }
    if (ok) { a.e.push(j); b.e.push(i); }
  }

  // ---- лужи света под фонарями (дешёвая «объёмность» без лишних источников) ----
  const pool = radialTex('rgba(255,160,80,0.32)', 'rgba(255,140,60,0)'), halo = radialTex('rgba(255,200,140,0.55)', 'rgba(255,160,80,0)');
  const pm = new THREE.MeshBasicMaterial({ map: pool, transparent: true, depthWrite: false, blending: THREE.AdditiveBlending });
  const hm = new THREE.SpriteMaterial({ map: halo, transparent: true, depthWrite: false, blending: THREE.AdditiveBlending });
  const coneMat = new THREE.ShaderMaterial({ transparent: true, depthWrite: false, blending: THREE.AdditiveBlending, side: THREE.DoubleSide, uniforms: {},
    vertexShader: 'varying float vY;varying vec3 vN;varying vec3 vV;void main(){vY=uv.y;vec4 wp=modelViewMatrix*vec4(position,1.);vN=normalize(normalMatrix*normal);vV=normalize(-wp.xyz);gl_Position=projectionMatrix*wp;}',
    fragmentShader: 'varying float vY;varying vec3 vN;varying vec3 vV;void main(){float f=pow(abs(dot(vN,vV)),1.5);gl_FragColor=vec4(vec3(1.,.66,.36)*.07*f*vY*vY,1.);}' });
  for (const l of L.lamps) {
    if (l.inner) continue;
    const gy = phys.groundAt(l.pos.x, l.pos.z, .1, l.pos.y).y;
    const d = new THREE.Mesh(new THREE.PlaneGeometry(l.r * 1.2, l.r * 1.2), pm); d.rotation.x = -Math.PI / 2; d.position.set(l.pos.x, gy + .03, l.pos.z); d.renderOrder = 2; scene.add(d);
    const s = new THREE.Sprite(hm); s.position.copy(l.pos); s.scale.setScalar(.9); scene.add(s);
    const h = l.pos.y - gy, cone = new THREE.Mesh(new THREE.CylinderGeometry(.12, l.r * .32, h, 16, 1, true), coneMat); cone.position.set(l.pos.x, gy + h / 2, l.pos.z); cone.renderOrder = 3; scene.add(cone);
  }

  // ---- слияние статики по материалам ----
  B.winDark = winsDark; B.winLit = winsLit;
  for (const k in B) {
    if (!B[k].length) continue;
    const g = mergeGeometries(B[k], false); if (!g) { console.warn('merge', k); continue; }
    const m = new THREE.Mesh(g, M[k]); m.receiveShadow = true; m.castShadow = !/ground|winDark|winLit|lampGlass|water/.test(k);
    m.matrixAutoUpdate = false; m.updateMatrix(); scene.add(m);
  }
  L.spawn = V(0, 0, 27);
  return L;
}

// Освещённость точки (0..1): луна, если не в тени зданий, + фонари в радиусе. Используется стелсом.
export const MOON_DIR = new THREE.Vector3(-.45, .75, .48).normalize();
export function lightAt(L, p, extra) {
  let l = L.phys.clear(p.x, p.y + .9, p.z, p.x + MOON_DIR.x * 30, p.y + .9 + MOON_DIR.y * 30, p.z + MOON_DIR.z * 30) ? .26 : .06;
  for (const s of L.lamps) {
    const d = s.pos.distanceTo(p); if (d > s.r) continue;
    if (!L.phys.clear(s.pos.x, s.pos.y, s.pos.z, p.x, p.y + 1, p.z)) continue;
    l += Math.pow(1 - d / s.r, 1.6) * (s.i || 1);
  }
  if (extra) for (const s of extra) { const d = s.distanceTo(p); if (d < 5) l += (1 - d / 5) * .6; }
  return Math.min(1, l);
}
