import * as THREE from 'three';
import { clone as skClone } from 'three/examples/jsm/utils/SkeletonUtils.js';
import { A } from './assets.js';

// Внешность: один стилизованный манекен Quaternius (CC0) + материалы и аксессуары на костях.
// Единый стиль важнее разнородных «реалистичных» моделей.
export const LOOKS = {
  hero: { main: 0x1e222c, joint: 0x7d1a1a, rough: .85, acc: 'hood' },
  guard: { main: 0x6d5a40, joint: 0x2c3138, rough: .8, acc: 'helmet' },
  lookout: { main: 0x55613f, joint: 0x2c3138, rough: .8, acc: 'helmet' },
  body: { main: 0x4a3f63, joint: 0xb08d2a, rough: .55, acc: 'helmet' },
  target: { main: 0x5a1736, joint: 0xc9a227, rough: .45, acc: 'hat' },
  worker: { main: 0x6b4a2b, joint: 0x3b3833, rough: .9, acc: 'cap' },
  disguise: { main: 0x6b4a2b, joint: 0x3b3833, rough: .9, acc: 'cap' },
  undies: { main: 0xb9b2a5, joint: 0x8a837a, rough: .9, acc: null },
  civ: [[0x7a3b2e, 0x3a2d22], [0x2f4a5c, 0x2a2622], [0x6e6a3a, 0x3a3328], [0x5b3a5e, 0x2b2430], [0x8a6b4a, 0x3d3127], [0x3e5a44, 0x2a2b26]],
};
const SCALE = { v: 0 };
export const CLOTH = { time: { value: 0 } };
let blobTex = null;
function blob() {
  if (!blobTex) { const c = document.createElement('canvas'); c.width = c.height = 64; const x = c.getContext('2d'), g = x.createRadialGradient(32, 32, 0, 32, 32, 32); g.addColorStop(0, 'rgba(0,0,0,.55)'); g.addColorStop(1, 'rgba(0,0,0,0)'); x.fillStyle = g; x.fillRect(0, 0, 64, 64); blobTex = new THREE.CanvasTexture(c); }
  const m = new THREE.Mesh(new THREE.PlaneGeometry(1.1, 1.1), new THREE.MeshBasicMaterial({ map: blobTex, transparent: true, depthWrite: false })); m.rotation.x = -Math.PI / 2; m.position.y = .02; m.renderOrder = 1; return m;
}
// плащ: сетка 6x10, раскачивание в вершинном шейдере (низ сильнее верха), сила зависит от скорости героя
function cape(color) {
  const g = new THREE.PlaneGeometry(.42, .8, 6, 10); g.translate(0, -.4, 0);
  const m = new THREE.MeshStandardMaterial({ color, roughness: .92, side: THREE.DoubleSide });
  const u = { sp: { value: 0 } };
  m.onBeforeCompile = s => { s.uniforms.uT = CLOTH.time; s.uniforms.uSp = u.sp;
    s.vertexShader = 'uniform float uT;uniform float uSp;\n' + s.vertexShader.replace('#include <begin_vertex>', `#include <begin_vertex>
      float k = clamp(-position.y / .8, 0., 1.); float w = sin(uT*6.0 + position.y*7.0 + position.x*4.0)*.04*(.35+uSp);
      transformed.z -= k*k*(.08 + uSp*.42) + w*k; transformed.y += k*k*uSp*.12; transformed.x += sin(uT*4.3+position.y*5.)*.015*k;`); };
  const mesh = new THREE.Mesh(g, m); mesh.userData.u = u; mesh.castShadow = true; return mesh;
}
const accMat = {
  metal: new THREE.MeshStandardMaterial({ color: 0x8c949e, metalness: .85, roughness: .35 }),
  cloth: new THREE.MeshStandardMaterial({ color: 0x14171e, roughness: .95 }),
  gold: new THREE.MeshStandardMaterial({ color: 0xc9a227, metalness: .9, roughness: .3 }),
  felt: new THREE.MeshStandardMaterial({ color: 0x3a2a1a, roughness: .95 }),
  glass: new THREE.MeshStandardMaterial({ color: 0x221a10, emissive: 0xffb15c, emissiveIntensity: 2.2, roughness: .4 }),
  blade: new THREE.MeshStandardMaterial({ color: 0xd8dde3, metalness: 1, roughness: .18 }),
};
function accessory(kind) {
  const g = new THREE.Group();
  if (kind === 'hood') {
    const h = new THREE.Mesh(new THREE.SphereGeometry(.135, 16, 12, 0, Math.PI * 2, 0, Math.PI * .62), accMat.cloth);
    h.scale.set(1, 1.15, 1.12); h.position.set(0, .07, -.01); g.add(h);
    const tip = new THREE.Mesh(new THREE.ConeGeometry(.07, .16, 10), accMat.cloth); tip.position.set(0, .12, -.1); tip.rotation.x = -1.1; g.add(tip);
  } else if (kind === 'helmet') {
    const h = new THREE.Mesh(new THREE.SphereGeometry(.13, 16, 10, 0, Math.PI * 2, 0, Math.PI * .5), accMat.metal); h.position.y = .08; g.add(h);
    const brim = new THREE.Mesh(new THREE.TorusGeometry(.14, .018, 6, 20), accMat.metal); brim.rotation.x = Math.PI / 2; brim.position.y = .08; g.add(brim);
    const crest = new THREE.Mesh(new THREE.BoxGeometry(.02, .05, .2), accMat.metal); crest.position.y = .2; g.add(crest);
  } else if (kind === 'hat') {
    const b = new THREE.Mesh(new THREE.CylinderGeometry(.12, .13, .14, 18), accMat.felt); b.position.y = .16; g.add(b);
    const r = new THREE.Mesh(new THREE.CylinderGeometry(.21, .21, .015, 20), accMat.felt); r.position.y = .1; g.add(r);
    const band = new THREE.Mesh(new THREE.CylinderGeometry(.124, .124, .03, 18), accMat.gold); band.position.y = .12; g.add(band);
  } else if (kind === 'cap') {
    const c = new THREE.Mesh(new THREE.SphereGeometry(.125, 14, 8, 0, Math.PI * 2, 0, Math.PI * .45), accMat.felt); c.position.y = .1; c.scale.set(1.05, .8, 1.05); g.add(c);
  }
  return g;
}
export class Character {
  constructor(look, opts = {}) {
    const L = typeof look === 'string' ? LOOKS[look] : look;
    this.root = new THREE.Group();
    const m = skClone(A.char.scene);
    this.mats = [];
    m.traverse(o => {
      if (o.isMesh || o.isSkinnedMesh) {
        o.castShadow = true; o.receiveShadow = true;
        o.material = o.material.clone(); this.mats.push(o.material);
        if (o.geometry.boundingSphere === null) o.geometry.computeBoundingSphere();
      }
    });
    this.model = m; this.root.add(m);
    this.bone = {}; m.traverse(o => { if (o.isBone) this.bone[o.name] = o; });
    if (!SCALE.v) { // нормируем рост манекена к 1.8 м по первой позе
      m.updateMatrixWorld(true); const b = new THREE.Box3().setFromObject(m); SCALE.v = 1.8 / Math.max(.5, b.max.y - b.min.y);
    }
    m.scale.setScalar(SCALE.v);
    this.mixer = new THREE.AnimationMixer(m); this.actions = {}; this.cur = null; this.curName = '';
    this.attach = {};
    this.setLook(L);
    this.blob = blob(); this.root.add(this.blob);
    if (opts.cape) { const c = cape(0x14161d); c.scale.setScalar(1 / SCALE.v); c.position.set(0, .06 / SCALE.v, -.13 / SCALE.v); (this.bone.spine_03 || this.bone.spine_02).add(c); this.attach.cape = c; }
    if (opts.lantern) this.addLantern();
    if (opts.blade) this.addBlade();
  }
  setLook(L) {
    this.look = L;
    for (const mt of this.mats) {
      const j = /Joint/i.test(mt.name);
      mt.color.setHex(j ? L.joint : L.main); mt.roughness = L.rough; mt.metalness = j && L.joint === 0xc9a227 ? .8 : 0;
    }
    if (this.attach.head) { this.attach.head.removeFromParent(); this.attach.head = null; }
    if (L.acc && this.bone.Head) { const a = accessory(L.acc); a.scale.setScalar(1 / SCALE.v); this.bone.Head.add(a); this.attach.head = a; a.traverse(o => { if (o.isMesh) o.castShadow = true; }); }
  }
  addLantern() {
    const g = new THREE.Group(), s = 1 / SCALE.v;
    const glass = new THREE.Mesh(new THREE.CylinderGeometry(.06, .06, .14, 8), accMat.glass); glass.position.y = -.2; g.add(glass);
    const top = new THREE.Mesh(new THREE.ConeGeometry(.08, .07, 8), accMat.metal); top.position.y = -.1; g.add(top);
    g.scale.setScalar(s); (this.bone.hand_l || this.bone.hand_r).add(g); this.attach.lantern = g;
  }
  addBlade() {
    const b = new THREE.Mesh(new THREE.BoxGeometry(.012, .2, .03), accMat.blade); b.position.set(0, .14, .02);
    b.scale.setScalar(1 / SCALE.v); this.bone.hand_r && this.bone.hand_r.add(b); this.attach.blade = b; b.visible = false;
  }
  play(name, o = {}) {
    const clip = A.clips[name]; if (!clip) return null;
    let a = this.actions[name]; if (!a) a = this.actions[name] = this.mixer.clipAction(clip);
    const speed = o.speed === undefined ? 1 : o.speed;
    if (this.cur === a && !o.restart) { a.timeScale = speed; return a; }
    const fade = o.fade === undefined ? .22 : o.fade;
    if (this.cur && this.cur !== a) this.cur.fadeOut(fade);
    a.reset(); a.setLoop(o.once ? THREE.LoopOnce : THREE.LoopRepeat, Infinity); a.clampWhenFinished = !!o.once;
    a.timeScale = speed; a.setEffectiveWeight(1); a.fadeIn(fade); a.play();
    this.cur = a; this.curName = name; this.curStart = performance.now();
    return a;
  }
  // 1D blend «скорость → клип» с подстройкой темпа, чтобы шаг совпадал со скоростью (меньше скольжения ног)
  locomote(speed, o = {}) {
    if (o.carry) return this.play('Walk_Carry_Loop', { speed: Math.max(.05, speed / 1.25) });
    if (o.crouch) return speed < .15 ? this.play('Crouch_Idle_Loop') : this.play('Crouch_Fwd_Loop', { speed: Math.min(1.6, speed / 1.35) });
    if (speed < .15) return this.play(o.idle || 'Idle_Loop', { fade: .3 });
    if (speed < 2.3) return this.play(o.walk || 'Walk_Loop', { speed: Math.max(.6, speed / 1.45) });
    if (speed < 4.8) return this.play('Jog_Fwd_Loop', { speed: speed / 3.5 });
    return this.play('Sprint_Loop', { speed: speed / 6.3 });
  }
  update(dt) { this.mixer.update(dt); }
  dispose() { this.root.removeFromParent(); this.mixer.stopAllAction(); }
}
