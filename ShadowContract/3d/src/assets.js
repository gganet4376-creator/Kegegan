import * as THREE from 'three';
import { GLTFLoader } from 'three/examples/jsm/loaders/GLTFLoader.js';
import { MeshoptDecoder } from 'three/examples/jsm/libs/meshopt_decoder.module.js';

// Ассеты встроены в HTML (base64) — игра открывается файлом прямо на телефоне, без сервера.
export const A = { tex: {}, clips: {} };
function bytes(name) {
  const s = window.__ASSETS && window.__ASSETS[name];
  if (!s) throw new Error('Нет ассета ' + name);
  const bin = atob(s), u = new Uint8Array(bin.length);
  for (let i = 0; i < bin.length; i++) u[i] = bin.charCodeAt(i);
  return u;
}
function loadImage(u) {
  return new Promise((res, rej) => {
    const url = URL.createObjectURL(new Blob([u], { type: 'image/jpeg' })), img = new Image();
    img.onload = () => { URL.revokeObjectURL(url); res(img); }; img.onerror = rej; img.src = url;
  });
}
export async function loadAll(progress, maxAniso) {
  const sets = ['ground', 'plaster', 'stone', 'roof', 'wood'];
  const total = sets.length * 2 + 2; let done = 0; const tick = () => progress(++done / total);
  for (const n of sets) for (const m of ['c', 'n']) {
    const img = await loadImage(bytes(`${n}_${m}.jpg`));
    const t = new THREE.Texture(img); t.wrapS = t.wrapT = THREE.RepeatWrapping; t.anisotropy = Math.min(4, maxAniso || 1);
    t.colorSpace = m === 'c' ? THREE.SRGBColorSpace : THREE.NoColorSpace; t.needsUpdate = true;
    A.tex[n + '_' + m] = t; tick();
  }
  const gl = new GLTFLoader(); gl.setMeshoptDecoder(MeshoptDecoder);
  const parse = u => new Promise((res, rej) => gl.parse(u.buffer.slice(u.byteOffset, u.byteOffset + u.byteLength), '', res, rej));
  A.char = await parse(bytes('char.glb')); tick();
  A.anim2 = await parse(bytes('anim2.glb')); tick();
  for (const c of [...A.char.animations, ...A.anim2.animations]) {
    // корневые смещения оставляем только по Y (клипы in-place), чтобы ноги не «уезжали»
    for (const tr of c.tracks) if (/^(root|pelvis)\.position$/.test(tr.name) && /^root/.test(tr.name)) {
      const v = tr.values; for (let i = 0; i < v.length; i += 3) { v[i] = 0; v[i + 2] = 0; }
    }
    A.clips[c.name] = c;
  }
}
