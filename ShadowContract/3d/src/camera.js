import * as THREE from 'three';

// Камера от третьего лица: сглаженный «поводок», плечевой сдвиг, обход препятствий лучом,
// контекстная дистанция (лазание/перенос/устранение), FOV-кик на спринте (отключается в «меньше движения»).
export class CamRig {
  constructor(cam, game) { this.cam = cam; this.g = game; this.yaw = Math.PI; this.pitch = .28; this.dist = 3.6; this.pivot = new THREE.Vector3(); this.idleLook = 0; this.fov = 62; this.side = 0; }
  snap() { const p = this.g.player; this.pivot.set(p.pos.x, p.pos.y + 1.5, p.pos.z); this.yaw = p.yaw; }
  update(dt, inp, set) {
    const p = this.g.player, reduce = set.reduceMotion;
    this.yaw -= inp.lx; this.pitch += inp.ly; this.pitch = Math.max(-.75, Math.min(1.1, this.pitch));
    if (Math.abs(inp.lx) + Math.abs(inp.ly) > 1e-4) this.idleLook = 0; else this.idleLook += dt;
    // авто-доводка за спину на телефоне, если игрок не крутит камеру
    if (set.autoCam && this.idleLook > 1.4 && p.speed > 1.2 && p.state === 'move') { let d = p.yaw - this.yaw; d = Math.atan2(Math.sin(d), Math.cos(d)); this.yaw += d * Math.min(1, dt * .9); }
    const climbing = p.state === 'climb' || p.state === 'ladder' || p.state === 'mantle';
    const want = new THREE.Vector3(p.pos.x, p.pos.y + (p.crouch ? 1.15 : p.state === 'hidden' ? 1.4 : 1.55), p.pos.z);
    const k = 1 - Math.exp(-dt * (climbing ? 7 : 11)); this.pivot.x += (want.x - this.pivot.x) * k; this.pivot.z += (want.z - this.pivot.z) * k; this.pivot.y += (want.y - this.pivot.y) * (1 - Math.exp(-dt * (climbing ? 5 : 9)));
    let dist = climbing ? 4.4 : p.carry ? 4 : p.crouch ? 3.1 : p.state === 'takedown' ? 2.9 : 3.6;
    if (p.state === 'takedown' && !reduce) this.side += ((p.tdSide || 1) * .9 - this.side) * Math.min(1, dt * 4); else this.side += (0 - this.side) * Math.min(1, dt * 3);
    this.dist += (dist - this.dist) * Math.min(1, dt * 4);
    const cy = Math.cos(this.pitch), dir = new THREE.Vector3(Math.sin(this.yaw) * cy, -Math.sin(this.pitch), Math.cos(this.yaw) * cy);
    const right = new THREE.Vector3(-Math.cos(this.yaw), 0, Math.sin(this.yaw));
    const piv = this.pivot.clone().addScaledVector(right, .42 + this.side * .6);
    // луч от героя к плечу, затем от плеча к камере
    const P = this.g.L.phys;
    const t0 = P.ray(this.pivot.x, this.pivot.y, this.pivot.z, piv.x, piv.y, piv.z, 'low'); piv.lerpVectors(this.pivot, piv, Math.max(0, t0 - .05));
    const desired = piv.clone().addScaledVector(dir, -this.dist);
    const t = P.ray(piv.x, piv.y, piv.z, desired.x, desired.y, desired.z, 'low');
    const d = Math.max(.6, this.dist * t - .25); const pos = piv.clone().addScaledVector(dir, -d);
    if (pos.y < .25) pos.y = .25;
    this.cam.position.copy(pos); this.cam.lookAt(piv.clone().addScaledVector(dir, 2));
    const f = 62 + (p.sprinting && !reduce ? 6 : 0) + (climbing ? 3 : 0); this.fov += (f - this.fov) * Math.min(1, dt * 3);
    if (Math.abs(this.cam.fov - this.fov) > .05) { this.cam.fov = this.fov; this.cam.updateProjectionMatrix(); }
    this.forward = dir; this.right = right;
  }
}
