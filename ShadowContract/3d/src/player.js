import * as THREE from 'three';
import { Character, LOOKS } from './character.js';

// Контроллер героя: кинематический цилиндр + контекстный паркур (как в AC: одна кнопка «вверх»,
// остальное определяется геометрией): перелезание низких препятствий, лазание по фасадам,
// подтягивание на уступ, хват в прыжке, автопрыжок с края на спринте, перекат при приземлении.
const R = .3, H = 1.75, HC = 1.15, G = 21, STEP = .42;
const tmpHit = {}, tmpQ = [];
export class Player {
  constructor(game) {
    this.g = game; this.ch = new Character('hero', { blade: true, cape: true });
    this.pos = new THREE.Vector3(); this.vel = new THREE.Vector3(); this.yaw = Math.PI; this.state = 'move';
    this.reset(game.L.spawn);
  }
  reset(p) {
    this.pos.copy(p); this.vel.set(0, 0, 0); this.yaw = Math.PI; this.state = 'move'; this.onGround = true; this.crouch = false; this.peakY = p.y;
    this.carry = null; this.disguise = null; this.hidden = null; this.knives = 3; this.item = 'stone'; this.t = 0; this.pushT = 0; this.wall = null;
    this.ch.setLook(LOOKS.hero); this.ch.play('Idle_Loop', { fade: 0 }); this.ch.root.visible = true; this.speed = 0; this.sprinting = false; this.hp = 100;
    this.stun = 0; this.climbing = false;
  }
  get height() { return this.crouch ? HC : H; }
  get suspiciousAct() { // действия, которые настораживают даже в «разрешённой» зоне
    return this.state === 'climb' || this.state === 'ladder' || this.state === 'mantle' || this.carry || this.sprinting || this.state === 'takedown' || (this.crouch && this.speed > .2);
  }
  // ---------- основной шаг ----------
  update(dt, inp) {
    const g = this.g, P = g.L.phys; this.t += dt;
    if (this.stun > 0) this.stun -= dt;
    const camYaw = g.cam.yaw;
    // желаемое направление относительно камеры
    const ix = inp.mx, iz = inp.my, mag = Math.min(1, Math.hypot(ix, iz));
    const wx = Math.sin(camYaw) * -iz - Math.cos(camYaw) * ix, wz = Math.cos(camYaw) * -iz + Math.sin(camYaw) * ix;
    const wl = Math.hypot(wx, wz) || 1, dx = wx / wl, dz = wz / wl;
    if (inp.crouchPressed && this.state === 'move' && !this.carry) this.crouch = !this.crouch;

    switch (this.state) {
      case 'hidden': this.ch.root.visible = false; if (inp.actionPressed || inp.jumpPressed || mag > .6) this.leaveHide(); return;
      case 'mantle': return this.stepMantle(dt);
      case 'climb': case 'ladder': return this.stepClimb(dt, inp, dx, dz, mag);
      case 'takedown': case 'pickup': case 'throw': case 'busy': return this.stepScript(dt);
      case 'roll': this.rollT -= dt; if (this.rollT <= 0) this.state = 'move'; break;
      case 'dead': return;
    }
    // ---------- скорость ----------
    let target = 0;
    if (mag > .08 && this.stun <= 0) {
      this.sprinting = (inp.sprint || mag > .93) && !this.crouch && !this.carry;
      target = this.carry ? 1.35 : this.crouch ? 1.45 : this.sprinting ? 6.1 : mag < .55 ? 1.6 : 3.5;
      if (this.state === 'roll') target = Math.max(target, 4.2);
      const want = Math.atan2(dx, dz); let d = want - this.yaw; d = Math.atan2(Math.sin(d), Math.cos(d));
      this.yaw += d * Math.min(1, dt * (this.onGround ? 12 : 4));
    } else this.sprinting = false;
    const acc = this.onGround ? (target > this.speed ? 14 : 18) : 3;
    this.speed += Math.max(-acc * dt, Math.min(acc * dt, target - this.speed));
    const fx = Math.sin(this.yaw), fz = Math.cos(this.yaw);
    if (this.onGround) { this.vel.x = fx * this.speed; this.vel.z = fz * this.speed; }
    else { this.vel.x += (fx * target - this.vel.x) * Math.min(1, dt * 1.5); this.vel.z += (fz * target - this.vel.z) * Math.min(1, dt * 1.5); }
    // ---------- прыжок / автопрыжок с края ----------
    if (inp.jumpPressed && this.onGround && !this.carry && this.stun <= 0) this.jump(6.2);
    else if (this.onGround && this.sprinting && mag > .5) {
      const ax = this.pos.x + fx * .55, az = this.pos.z + fz * .55, gy = P.groundAt(ax, az, R * .5, this.pos.y + STEP, tmpQ).y;
      if (this.pos.y - gy > 1.2) { this.jump(5.4); g.audio.sfx('jump', this.pos); }
    }
    // ---------- интеграция с коллизиями ----------
    const prevY = this.pos.y, wasGround = this.onGround;
    this.pos.x += this.vel.x * dt; this.pos.z += this.vel.z * dt;
    const wall = P.push(this.pos, R, this.pos.y + STEP, this.pos.y + this.height, tmpHit, tmpQ) ? { box: tmpHit.box, nx: tmpHit.nx, nz: tmpHit.nz } : null;
    this.vel.y -= G * dt; this.pos.y += this.vel.y * dt;
    const gr = P.groundAt(this.pos.x, this.pos.z, R, Math.max(prevY, this.pos.y) + STEP, tmpQ);
    this.onGround = false; this.groundBox = gr.box;
    if (this.pos.y <= gr.y + .001 && this.vel.y <= 0) {
      if (!wasGround) this.land(gr);
      this.pos.y = gr.y; this.vel.y = 0; this.onGround = true;
    } else if (this.vel.y > 0) { // потолок
      for (const b of P.query(this.pos.x - R * .5, this.pos.z - R * .5, this.pos.x + R * .5, this.pos.z + R * .5, tmpQ))
        if (b.kind !== 'trigger' && b.min[1] > prevY + this.height - .05 && b.min[1] < this.pos.y + this.height && this.pos.x > b.min[0] && this.pos.x < b.max[0] && this.pos.z > b.min[2] && this.pos.z < b.max[2]) { this.pos.y = b.min[1] - this.height; this.vel.y = 0; }
    }
    if (!this.onGround) this.peakY = Math.max(this.peakY, this.pos.y); else this.peakY = this.pos.y;
    // ---------- паркур от стен ----------
    if (wall && mag > .3) {
      const into = -(wall.nx * dx + wall.nz * dz);
      this.pushT = into > .55 ? this.pushT + dt : 0;
      if (into > .55 && !this.carry) this.tryParkour(wall, inp, dx, dz);
    } else this.pushT = 0;
    // ---------- анимация ----------
    if (this.state === 'move') {
      if (!this.onGround) { if (this.ch.curName !== 'Jump_Start' || this.vel.y < 0) this.ch.play(this.vel.y > 1 ? 'Jump_Start' : 'Jump_Loop', { fade: .15, once: this.vel.y > 1 }); }
      else if (this.stun > 0) { }
      else this.ch.locomote(this.speed, { crouch: this.crouch, carry: !!this.carry });
    }
    this.applyTransform();
    // шаги
    if (this.onGround && this.speed > .5) { this.stepT = (this.stepT || 0) - dt * this.speed / 1.6; if (this.stepT <= 0) { this.stepT = .55; this.footstep(); } }
  }
  jump(vy) { this.vel.y = vy; this.onGround = false; this.peakY = this.pos.y; this.crouch = false; if (this.state === 'move') this.ch.play('Jump_Start', { fade: .08, once: true }); this.g.audio.sfx('jump', this.pos); }
  land(gr) {
    const fall = this.peakY - this.pos.y, g = this.g;
    const hay = g.L.hides.find(h => h.kind === 'hay' && Math.hypot(h.pos.x - this.pos.x, h.pos.z - this.pos.z) < h.r);
    if (hay && fall > 2.5 && !this.carry) { this.enterHide(hay, true); return; }
    if (fall > 14) { this.hp = 0; this.die('Разбился при падении.'); return; }
    if (fall > 2.2) this.lastLand = 'hard';
    if (fall > 3.6 && this.speed > 1.2 && this.state === 'move') { this.lastLand = 'roll'; this.state = 'roll'; this.rollT = .62; this.ch.play('Roll', { fade: .08, once: true, speed: 1.15 }); g.audio.sfx('roll', this.pos); g.noise(this.pos, 4, 'land'); }
    else if (fall > 2.2) { this.stun = fall > 3.6 ? .55 : .2; this.ch.play('Jump_Land', { fade: .06, once: true }); g.audio.sfx('landHard', this.pos); g.noise(this.pos, fall > 3.6 ? 10 : 5, 'land'); if (fall > 9) { this.hp -= (fall - 9) * 18; g.ui.toast('Тяжёлое приземление'); if (this.hp <= 0) this.die('Разбился при падении.'); } }
    else if (fall > .4) g.audio.sfx('land', this.pos);
  }
  footstep() {
    const g = this.g, s = this.groundBox ? this.groundBox.surf : 'stone';
    g.audio.step(s, this.pos, this.crouch ? .25 : this.sprinting ? 1 : this.speed > 2.5 ? .7 : .45);
    const r = this.crouch ? 0 : this.sprinting ? 8 : this.speed > 2.5 ? 3.5 : 1.2;
    if (r) g.noise(this.pos, r, 'steps');
  }
  // высота «колонны» над точкой касания: стена + плиты/перила сверху
  columnTop(px, pz, start) {
    let top = start.max[1], box = start, changed = true;
    while (changed) { changed = false;
      for (const b of this.g.L.phys.query(px - .02, pz - .02, px + .02, pz + .02, tmpQ))
        if (b.kind !== 'trigger' && b.kind !== 'door' && px >= b.min[0] && px <= b.max[0] && pz >= b.min[2] && pz <= b.max[2] && b.min[1] <= top + .06 && b.max[1] > top + .01) { top = b.max[1]; box = b; changed = true; }
    }
    return { top, box };
  }
  tryParkour(wall, inp, dx, dz) {
    const b = wall.box; if (!b || b.kind === 'bound' || b.kind === 'door') return;
    const cx = Math.max(b.min[0], Math.min(this.pos.x, b.max[0])), cz = Math.max(b.min[2], Math.min(this.pos.z, b.max[2]));
    let best = null;
    for (const s of [-.12, .08]) { const c = this.columnTop(cx - wall.nx * s, cz - wall.nz * s, b); if (!best || c.top > best.top) best = { ...c, px: cx - wall.nx * s, pz: cz - wall.nz * s }; }
    const rise = best.top - this.pos.y;
    if (rise <= STEP) return;
    const fast = this.speed > 2.4 || inp.jumpHeld;
    if (rise <= 1.4) {
      // перила/край над обрывом: только осознанно (удерживая прыжок), чтобы не падать случайно
      const P = this.g.L.phys, bx = best.px - wall.nx * .9, bz = best.pz - wall.nz * .9, beyond = P.groundAt(bx, bz, .1, best.top + .1, tmpQ).y;
      const risky = b.kind === 'railing' || best.top - beyond > 2.6;
      if (risky && !inp.jumpHeld) { this.g.ui.hintOnce('rail', 'За перилами обрыв. Удерживай ⤴, чтобы перелезть.'); return; }
      if (fast || this.pushT > .12) return this.startMantle(best, wall);
      return;
    }
    if (!b.climb) return;
    if (inp.jumpHeld || inp.jumpPressed || this.pushT > .35 || !this.onGround) {
      if (!this.onGround && rise > 2.4) return; // в прыжке хватаемся только если уступ близко
      if (!this.onGround && rise <= 1.9) return this.startMantle(best, wall);
      this.startClimb(wall, best);
    }
  }
  startMantle(c, wall) {
    const b = c.box, depth = Math.min(b.max[0] - b.min[0], b.max[2] - b.min[2]), inward = Math.min(.42, depth / 2);
    const tx = c.px - wall.nx * inward, tz = c.pz - wall.nz * inward;
    this.state = 'mantle'; this.mT = 0; this.crouch = false;
    this.m = { from: this.pos.clone(), to: new THREE.Vector3(tx, c.top, tz), dur: Math.max(.38, Math.min(.85, (c.top - this.pos.y) * .45 + .25)) };
    this.yaw = Math.atan2(-wall.nx, -wall.nz); this.vel.set(0, 0, 0);
    this.ch.play('ClimbUp_1m', { fade: .08, once: true, speed: .67 / this.m.dur * 1.05 });
    this.g.audio.sfx('mantle', this.pos); this.climbStarted = true;
  }
  stepMantle(dt) {
    this.mT += dt; const k = Math.min(1, this.mT / this.m.dur), up = Math.min(1, k / .62), fw = Math.max(0, (k - .45) / .55);
    const e = t => t * t * (3 - 2 * t);
    this.pos.y = this.m.from.y + (this.m.to.y + .02 - this.m.from.y) * e(up);
    this.pos.x = this.m.from.x + (this.m.to.x - this.m.from.x) * e(fw); this.pos.z = this.m.from.z + (this.m.to.z - this.m.from.z) * e(fw);
    this.applyTransform();
    if (k >= 1) { this.pos.copy(this.m.to); this.state = 'move'; this.onGround = true; this.speed = 0; this.peakY = this.pos.y; }
  }
  startClimb(wall, c) {
    this.state = c.box.kind === 'ladder' ? 'ladder' : 'climb'; this.wall = { box: wall.box, nx: wall.nx, nz: wall.nz }; this.crouch = false;
    this.yaw = Math.atan2(-wall.nx, -wall.nz); this.vel.set(0, 0, 0); this.climbTop = c;
    this.ch.play('ClimbUp_1m', { fade: .12, speed: 0 }); this.g.audio.sfx('grab', this.pos); this.g.ui.hintOnce('climb', 'Лазание: двигайся вверх/вниз и в стороны. ▼ — отпустить, ⤴ — оттолкнуться.');
  }
  stepClimb(dt, inp, dx, dz, mag) {
    const w = this.wall, b = w.box, P = this.g.L.phys;
    if (inp.crouchPressed) { this.state = 'move'; this.pos.x += w.nx * .15; this.pos.z += w.nz * .15; this.vel.set(0, 0, 0); this.peakY = this.pos.y; return; }
    if (inp.jumpPressed && this.state === 'climb') { this.state = 'move'; this.vel.set(w.nx * 3.5, 5, w.nz * 3.5); this.yaw = Math.atan2(w.nx, w.nz); this.onGround = false; this.peakY = this.pos.y; this.ch.play('Jump_Loop', { fade: .1 }); return; }
    // вверх = «в стену» или вверх по стику относительно камеры
    const into = -(w.nx * dx + w.nz * dz) * mag, side = (w.nz * dx - w.nx * dz) * mag;
    const up = Math.abs(inp.my) > .2 && this.state === 'ladder' ? -inp.my : into;
    const vy = (up > .25 ? 1 : up < -.25 ? -1 : 0) * (this.state === 'ladder' ? 2.2 : 1.6);
    this.pos.y += vy * dt;
    if (this.state === 'climb' && Math.abs(side) > .3) {
      const s = Math.sign(side) * 1.3 * dt; this.pos.x += w.nz * s; this.pos.z -= w.nx * s;
      // держимся в пределах грани бокса
      if (w.nx) this.pos.z = Math.max(b.min[2] + .1, Math.min(b.max[2] - .1, this.pos.z)); else this.pos.x = Math.max(b.min[0] + .1, Math.min(b.max[0] - .1, this.pos.x));
    }
    // прилипание к грани
    if (w.nx > 0) this.pos.x = b.max[0] + R; else if (w.nx < 0) this.pos.x = b.min[0] - R;
    if (w.nz > 0) this.pos.z = b.max[2] + R; else if (w.nz < 0) this.pos.z = b.min[2] - R;
    const a = this.ch.play('ClimbUp_1m', { fade: .1, speed: 0 });
    if (a) { a.paused = false; a.timeScale = 0; a.time = ((this.t * (vy ? 1.1 : .0)) % .45) + .05; }
    if (vy && (this.climbSnd = (this.climbSnd || 0) - dt) <= 0) { this.climbSnd = .45; this.g.audio.sfx('climb', this.pos); }
    const ground = P.groundAt(this.pos.x, this.pos.z, R * .6, this.pos.y + .05, tmpQ).y;
    if (vy < 0 && this.pos.y <= ground + .02) { this.pos.y = ground; this.state = 'move'; this.onGround = true; return; }
    // верх: подтягиваемся
    const c = this.columnTop(Math.max(b.min[0], Math.min(this.pos.x - w.nx * R, b.max[0])) - w.nx * .1, Math.max(b.min[2], Math.min(this.pos.z - w.nz * R, b.max[2])) - w.nz * .1, b);
    if (this.state === 'ladder') {
      if (this.pos.y + 1.2 >= c.top) { const ex = this.g.L.ladderTop; this.state = 'mantle'; this.mT = 0; this.m = { from: this.pos.clone(), to: ex.clone(), dur: .7 }; this.ch.play('ClimbUp_1m', { fade: .1, once: true, speed: .9 }); }
    } else if (this.pos.y + 1.45 >= c.top) {
      const cx = Math.max(b.min[0], Math.min(this.pos.x, b.max[0])), cz = Math.max(b.min[2], Math.min(this.pos.z, b.max[2]));
      let best = null; for (const s of [-.12, .08]) { const cc = this.columnTop(cx - w.nx * s, cz - w.nz * s, b); if (!best || cc.top > best.top) best = { ...cc, px: cx - w.nx * s, pz: cz - w.nz * s }; }
      this.startMantle(best, w);
    }
    this.applyTransform();
  }
  stepScript(dt) { this.scriptT -= dt; if (this.script && this.script(dt)) { } if (this.scriptT <= 0) { this.state = this.carry ? 'move' : 'move'; if (this.onDone) { const f = this.onDone; this.onDone = null; f(); } } this.applyTransform(); }
  runScript(state, dur, fn, done) { this.state = state; this.scriptT = dur; this.script = fn; this.onDone = done; this.vel.set(0, 0, 0); this.speed = 0; }
  enterHide(h, leap) {
    this.state = 'hidden'; this.hidden = h; this.ch.root.visible = false; this.pos.set(h.pos.x, 0, h.pos.z);
    this.g.audio.sfx('hay', this.pos); this.g.ui.toast(leap ? 'Прыжок веры! Ты укрыт в сене.' : 'Ты укрыт');
  }
  leaveHide() {
    const h = this.hidden; this.hidden = null; this.state = 'move'; this.ch.root.visible = true;
    const a = this.g.cam.yaw; this.pos.set(h.pos.x - Math.sin(a) * 1.9, 0, h.pos.z - Math.cos(a) * 1.9);
    this.g.L.phys.push(this.pos, R, .3, 1.7, tmpHit, tmpQ); this.g.audio.sfx('hay', this.pos);
  }
  die(why) { if (this.state === 'dead') return; this.state = 'dead'; this.ch.play('Death01', { once: true, fade: .1 }); this.g.fail(why); }
  applyTransform() {
    if (this.ch.attach.cape) this.ch.attach.cape.userData.u.sp.value += ((this.state === 'move' ? Math.min(1, this.speed / 6) : this.state === 'climb' ? .1 : .6) - this.ch.attach.cape.userData.u.sp.value) * .1;
    this.ch.root.position.copy(this.pos); this.ch.root.rotation.y = this.yaw;
    if (this.carry) { const b = this.carry; b.ch.root.position.set(this.pos.x, this.pos.y, this.pos.z); b.ch.root.rotation.y = this.yaw; }
  }
}
