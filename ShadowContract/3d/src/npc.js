import * as THREE from 'three';
import { Character, LOOKS } from './character.js';
import { lightAt } from './world.js';

// ИИ по мотивам Hitman (GDC 2012/2016): состояние = функция от подозрения + памяти.
// Восприятие дискретное (10 Гц, дальним — 4 Гц), знание только из увиденного/услышанного:
// LKP (last known position), источники шума, найденные тела, пропавшие напарники.
const tmpHit = {}, tmpQ = [];
const BARK = {
  curious: ['Хм?', 'Что там?', 'Кто здесь?'], investigate: ['Пойду проверю.', 'Эй, ты!', 'Не двигайся…'], lost: ['Показалось.', 'Ладно…', 'Ветер, наверное.'],
  body: ['Тело! Сюда!', 'Убийство!', 'Человек лежит!'], help: ['Иду!', 'Где?!'], noise: ['Что за шум?', 'Слышал?'], confirm: ['ТРЕВОГА!', 'ЧУЖОЙ! К ОРУЖИЮ!'],
  buddy: ['Эй, ты где?', 'Куда он делся?'], door: ['Дверь открыта?'], wake: ['Очнись!'], woke: ['Кто меня…?!'], scream: ['А-а-а!', 'Помогите!'],
};
const pick = a => a[Math.random() * a.length | 0];
export function astar(nav, s, t) {
  if (s === t) return [s];
  const open = [s], came = new Map(), gS = new Map([[s, 0]]), f = new Map([[s, nav[s].p.distanceTo(nav[t].p)]]);
  while (open.length) {
    let bi = 0; for (let i = 1; i < open.length; i++) if (f.get(open[i]) < f.get(open[bi])) bi = i;
    const c = open.splice(bi, 1)[0]; if (c === t) { const p = [c]; let k = c; while (came.has(k)) { k = came.get(k); p.unshift(k); } return p; }
    for (const n of nav[c].e) { const g = gS.get(c) + nav[c].p.distanceTo(nav[n].p); if (g < (gS.has(n) ? gS.get(n) : 1e9)) { came.set(n, c); gS.set(n, g); f.set(n, g + nav[n].p.distanceTo(nav[t].p)); if (!open.includes(n)) open.push(n); } }
  }
  return null;
}
export class NPC {
  constructor(game, o) {
    this.g = game; this.o = o; this.kind = o.kind; this.layer = o.layer || 'g';
    this.ch = new Character(o.look || o.kind, { lantern: o.lantern });
    this.pos = o.pos.clone(); this.yaw = o.yaw || 0; this.speed = 0; this.path = []; this.wp = 0; this.waitT = 0;
    this.state = 'patrol'; this.sus = 0; this.react = o.react || 1; this.sight = o.sight || 17;
    this.dead = false; this.ko = false; this.hidden = false; this.carried = false; this.found = false; this.perceiveT = Math.random() * .1;
    this.lkp = null; this.bark = 0; this.timer = 0; this.alarmT = 0; this.icon = null; this.lightCache = 0;
    if (game.debugGroup) this.makeDebug();
    this.ch.root.position.copy(this.pos); this.ch.root.rotation.y = this.yaw;
    game.scene.add(this.ch.root);
    this.ch.play(o.idle || 'Idle_Loop', { fade: 0 }); this.ch.mixer.update(Math.random() * 2);
  }
  get alive() { return !this.dead && !this.ko; }
  get eye() { return new THREE.Vector3(this.pos.x, this.pos.y + 1.62, this.pos.z); }
  say(k, force) { if (!force && this.bark > 0) return; this.bark = 3; const s = pick(BARK[k]); this.g.ui.subtitle(this.o.name || 'Стражник', s, this.pos); this.g.audio.voice(k, this.pos); }
  nearestNode(p) {
    const nav = this.g.L.nav; let bi = -1, bd = 1e9;
    for (let i = 0; i < nav.length; i++) { const n = nav[i]; if (n.layer !== this.layer) continue; const d = n.p.distanceTo(p); if (d < bd && this.g.L.phys.ray(p.x, p.y + .6, p.z, n.p.x, n.p.y + .6, n.p.z, 'door') > .999) { bd = d; bi = i; } }
    if (bi < 0) for (let i = 0; i < nav.length; i++) if (nav[i].layer === this.layer) { const d = nav[i].p.distanceTo(p); if (d < bd) { bd = d; bi = i; } }
    return bi;
  }
  goTo(p, speed) {
    this.moveSpeed = speed; const P = this.g.L.phys;
    if (P.ray(this.pos.x, this.pos.y + .6, this.pos.z, p.x, p.y + .6, p.z, 'door') > .999 && Math.abs(p.y - this.pos.y) < .6) { this.path = [p.clone()]; this.wp = 0; return true; }
    const a = this.nearestNode(this.pos), b = this.nearestNode(p), nav = this.g.L.nav;
    const r = a >= 0 && b >= 0 ? astar(nav, a, b) : null;
    if (!r) { this.path = []; return false; }
    this.path = r.map(i => nav[i].p.clone()); this.path.push(p.clone()); this.wp = 0; return true;
  }
  moveAlong(dt) {
    if (!this.path.length || this.wp >= this.path.length) { this.speed = Math.max(0, this.speed - dt * 6); return true; }
    const t = this.path[this.wp], dx = t.x - this.pos.x, dz = t.z - this.pos.z, d = Math.hypot(dx, dz);
    if (d < .35) { this.wp++; return this.wp >= this.path.length; }
    const want = Math.atan2(dx, dz); this.turnTo(want, dt, 7);
    this.speed += Math.max(-dt * 5, Math.min(dt * 5, this.moveSpeed - this.speed));
    const sp = this.speed * Math.max(.2, Math.cos(want - this.yaw));
    this.pos.x += Math.sin(this.yaw) * sp * dt; this.pos.z += Math.cos(this.yaw) * sp * dt;
    // двери открываются перед NPC
    for (const dr of this.g.L.doors) if (dr.center.distanceTo(this.pos) < 1.8) { dr.target = 1; dr.autoClose = 2.5; }
    this.g.L.phys.push(this.pos, .28, this.pos.y + .4, this.pos.y + 1.7, tmpHit, tmpQ);
    // разводим NPC между собой (локальное избегание)
    for (const o of this.g.npcs) if (o !== this && o.alive && !o.carried && Math.abs(o.pos.y - this.pos.y) < 1) { const ex = this.pos.x - o.pos.x, ez = this.pos.z - o.pos.z, e = Math.hypot(ex, ez); if (e < .6 && e > 1e-3) { this.pos.x += ex / e * (.6 - e) * .5; this.pos.z += ez / e * (.6 - e) * .5; } }
    // залипание: если почти не двигаемся — пропускаем точку
    this.stuckT = Math.hypot(this.pos.x - (this.lastX ?? 0), this.pos.z - (this.lastZ ?? 0)) < this.moveSpeed * dt * .15 ? (this.stuckT || 0) + dt : 0; this.lastX = this.pos.x; this.lastZ = this.pos.z;
    if (this.stuckT > 1.2) { this.wp++; this.stuckT = 0; }
    return false;
  }
  turnTo(want, dt, rate = 5) { let d = want - this.yaw; d = Math.atan2(Math.sin(d), Math.cos(d)); this.yaw += d * Math.min(1, dt * rate); return Math.abs(d); }
  faceP(p, dt, rate) { return this.turnTo(Math.atan2(p.x - this.pos.x, p.z - this.pos.z), dt, rate); }
  // ---------- восприятие ----------
  canSee(tp, range, cone = 1.05) {
    const e = this.eye, dx = tp.x - e.x, dy = tp.y - e.y, dz = tp.z - e.z, d = Math.hypot(dx, dy, dz);
    if (d > range) return 0;
    const a = Math.abs(Math.atan2(Math.sin(Math.atan2(dx, dz) - this.yaw), Math.cos(Math.atan2(dx, dz) - this.yaw)));
    let f = a < cone ? 1 : a < 1.9 && d < 3.2 ? .45 : 0;
    if (!f) return 0;
    if (Math.abs(dy) > 9 && d > 12) f *= .4;
    if (this.g.L.phys.ray(e.x, e.y, e.z, tp.x, tp.y, tp.z) < .985) return 0;
    return f * Math.max(.18, Math.pow(1 - d / range, .55));
  }
  perceive(dt) {
    const g = this.g, p = g.player; this.vis = 0;
    if (p.state !== 'hidden' && p.state !== 'dead') {
      const tp = new THREE.Vector3(p.pos.x, p.pos.y + (p.crouch ? .85 : 1.35), p.pos.z);
      let R = this.sight * (.32 + .78 * g.playerLight) * (this.kind === 'lookout' ? 1.2 : 1) * (p.blend ? .5 : 1) * (p.crouch && g.playerLight < .3 ? .8 : 1);
      this.vis = this.canSee(tp, R);
    }
    const trespass = g.suspicionOf(this);
    if (this.vis > 0) {
      if (p.state === 'takedown' && this.vis > .2 && p.tdVictim !== this) { this.sus = Math.max(this.sus, 1.01); this.lkp = p.pos.clone(); return; } // свидетель убийства
      if (trespass > 0 || (this.suspectPlayer && this.state !== 'patrol')) {
        this.sus += this.vis * 1.15 * this.react * Math.max(trespass, this.suspectPlayer ? .5 : 0) * dt;
        this.lkp = p.pos.clone(); this.suspectPlayer = true; this.lastSeen = g.time;
      }
    } else this.sus = Math.max(0, this.sus - (this.state === 'search' ? .05 : .11) * dt);
    // тела
    if (this.state !== 'confirm') for (const b of g.npcs) {
      if (b === this || b.alive || b.hidden || b.carried || b.found && this.state === 'search') continue;
      if (!b.found || !this.knows || !this.knows.has(b)) {
        const r = 17 * (.45 + .75 * b.lightCache);
        if (this.canSee(new THREE.Vector3(b.pos.x, b.pos.y + .25, b.pos.z), r, 1.15) > 0) { (this.knows || (this.knows = new Set())).add(b); this.onBody(b); }
      }
    }
  }
  hear(n) {
    if (!this.alive || this.state === 'confirm' || this.state === 'takedown' || this.kind === 'civ' || this.kind === 'worker') return;
    const d = n.pos.distanceTo(this.pos); let r = n.r;
    if (!this.g.L.phys.clear(n.pos.x, n.pos.y + 1, n.pos.z, this.pos.x, this.pos.y + 1.4, this.pos.z)) r *= .45; // стены глушат
    if (d > r) return;
    if (this.kind === 'civ') return;
    const strong = n.type === 'stone' || n.type === 'scream' || n.type === 'land' || n.type === 'crash';
    if (n.type === 'steps' && this.state !== 'patrol' && this.state !== 'post') return;
    this.lkp = n.pos.clone(); this.noiseType = n.type;
    if (n.type === 'crash') { this.startSearch(n.pos, pick(BARK.noise)); return; }
    this.sus = Math.max(this.sus, strong ? .6 : .32);
    if (this.state === 'patrol' || this.state === 'post' || this.state === 'return') { this.suspectPlayer = n.type === 'steps' || n.type === 'land'; this.enter('curious'); if (strong) this.say('noise'); }
  }
  onBody(b) {
    if (!b.found) { b.found = true; this.g.stats.bodies++; }
    this.startSearch(b.pos, pick(BARK.body), b);
    // голосом зовёт тех, кто рядом (информация передаётся только звуком и только в радиусе слышимости)
    for (const o of this.g.npcs) if (o !== this && o.alive && o.kind !== 'civ' && o.kind !== 'worker' && o.pos.distanceTo(this.pos) < 16 && o.state !== 'confirm') { o.knows = o.knows || new Set(); o.knows.add(b); o.startSearch(b.pos.clone().add(new THREE.Vector3((Math.random() - .5) * 3, 0, (Math.random() - .5) * 3)), pick(BARK.help)); }
  }
  startSearch(p, msg, body) {
    if (!this.alive || this.kind === 'civ' || this.kind === 'worker' || this.state === 'confirm') return;
    if (this.layer !== 'g' && Math.abs(p.y - this.pos.y) > 2) { this.lkp = p.clone(); this.enter('curious'); return; }
    this.state = 'search'; this.timer = 22; this.lkp = p.clone(); this.sus = Math.max(this.sus, .55); this.wakeTarget = body && body.ko ? body : this.wakeTarget;
    this.goTo(p, 2.8); this.lookT = 0; if (msg) { this.g.ui.subtitle(this.o.name || 'Стражник', msg, this.pos); this.g.audio.voice('body', this.pos); }
    this.g.stats.searches++;
  }
  enter(s) {
    if (this.state === s) return; this.state = s; this.timer = 0;
    if (s === 'curious') { this.timer = 1.6; this.say('curious'); }
    if (s === 'investigate') { this.goTo(this.lkp, 1.9); this.say('investigate'); this.g.stats.suspicions++; }
    if (s === 'confirm') { this.alarmT = this.o.alarmTime || 1.25; this.say('confirm', true); this.ch.play('Interact', { fade: .1, once: true, speed: .8 }); this.g.audio.sfx('alarmWind', this.pos); }
    if (s === 'return') { const r = this.route ? this.route[this.ri].p : this.o.pos; this.goTo(r, 1.4); this.say('lost'); this.suspectPlayer = false; }
  }
  // ---------- поведение ----------
  update(dt) {
    const g = this.g;
    this.bark -= dt;
    if (this.dead || this.ko || this.state === 'takedown') { this.ch.update(dt * (this.ko && this.carried ? 0 : 1)); if (this.ko && !this.hidden && !this.carried) this.koUpdate(dt); this.syncIcon(); return; }
    // AI LOD: восприятие реже для дальних
    const far = this.pos.distanceTo(g.player.pos) > 30; this.perceiveT -= dt;
    if (this.perceiveT <= 0) { const pd = far ? .25 : .1; this.perceive(pd); this.perceiveT = pd; }
    if (this.sus >= 1 && this.state !== 'confirm') this.enter('confirm');
    switch (this.state) {
      case 'patrol': case 'post': this.routine(dt); if (this.sus > .25) { this.lkp = this.lkp || g.player.pos.clone(); this.enter('curious'); } break;
      case 'curious':
        this.speed = Math.max(0, this.speed - dt * 6); if (this.lkp) this.faceP(this.lkp, dt, 4); this.timer -= dt;
        this.ch.play('Idle_Loop', { fade: .3 });
        if (this.sus > .55) this.enter('investigate'); else if (this.timer <= 0 && this.sus < .2) this.enter('return');
        else if (this.timer <= 0 && this.lkp && this.layer === 'g') this.enter('investigate');
        break;
      case 'investigate': case 'search': {
        const arrived = this.moveAlong(dt);
        if (this.state === 'search') this.timer -= dt;
        if (this.wakeTarget && this.wakeTarget.ko && !this.wakeTarget.hidden && !this.wakeTarget.carried && this.wakeTarget.pos.distanceTo(this.pos) < 1.3) {
          this.speed = 0; this.wakeT = (this.wakeT || 0) + dt; this.ch.play('PickUp_Table', { fade: .2 }); this.faceP(this.wakeTarget.pos, dt, 6);
          if (this.wakeT > 2.4) { this.say('wake', true); this.wakeTarget.wake(); this.wakeTarget = null; this.wakeT = 0; }
          break;
        }
        if (!arrived) { this.ch.locomote(this.speed, { walk: 'Walk_Loop' }); break; }
        this.lookT = (this.lookT || 0) + dt; this.ch.play('Idle_No_Loop', { fade: .3 });
        if (this.lookT > 3.2) {
          this.lookT = 0;
          if (this.state === 'search' && this.timer > 0) { const c = this.lkp || this.pos; const q = c.clone().add(new THREE.Vector3((Math.random() - .5) * 14, 0, (Math.random() - .5) * 14)); this.goTo(q, 2.2); }
          else if (this.sus < .45) this.enter('return'); else { this.sus -= .1; }
        }
        break;
      }
      case 'return': if (this.moveAlong(dt)) { this.state = this.route ? 'patrol' : 'post'; this.waitT = 1; } else this.ch.locomote(this.speed, { walk: 'Walk_Loop' }); if (this.sus > .3 && this.vis) this.enter('curious'); break;
      case 'confirm':
        this.speed = 0; this.faceP(g.player.pos, dt, 8); this.alarmT -= dt;
        if (this.alarmT <= 0) g.alarm(this);
        break;
    }
    // проверка пропавшего напарника
    if (this.buddy && !this.buddy.alive && !this.buddyChecked && g.time - (this.buddy.downAt || 0) > 10 && (this.state === 'patrol' || this.state === 'post')) {
      this.buddyChecked = true; this.lkp = this.buddy.o.pos.clone(); this.say('buddy', true); this.suspectPlayer = false; this.sus = Math.max(this.sus, .56); this.enter('investigate');
    }
    // открытая дверь на посту
    if (this.o.watchDoor && (this.state === 'post') && this.o.watchDoor.open > .6 && !this.g.npcs.some(n => n !== this && n.alive && n.pos.distanceTo(this.o.watchDoor.center) < 2.5)) {
      this.say('door', true); this.o.watchDoor.target = 0; this.sus = Math.max(this.sus, .3); this.lkp = this.o.watchDoor.center.clone(); this.enter('curious');
    }
    this.pos.y = this.o.pos.y; this.ch.root.position.copy(this.pos); this.ch.root.rotation.y = this.yaw; this.ch.update(dt); this.syncIcon();
    if (this.dbg) this.updDebug();
  }
  routine(dt) {
    if (this.route) {
      const w = this.route[this.ri];
      if (this.waitT > 0) { this.waitT -= dt; this.speed = 0; if (w.face !== undefined) this.turnTo(w.face, dt, 3); this.ch.play(w.anim || this.o.idle || 'Idle_Loop', { fade: .35 }); if (this.waitT <= 0) { this.ri = (this.ri + 1) % this.route.length; this.goTo(this.route[this.ri].p, this.o.walkSpeed || 1.35); } return; }
      if (!this.path.length) this.goTo(w.p, this.o.walkSpeed || 1.35);
      if (this.moveAlong(dt)) { this.waitT = w.wait || 0.01; this.path = []; if (w.on) w.on(this); }
      this.ch.locomote(this.speed, { walk: this.o.walk || 'Walk_Formal_Loop', idle: this.o.idle });
    } else {
      const home = this.o.pos; if (this.pos.distanceTo(home) > .5) { if (!this.path.length) this.goTo(home, 1.35); this.moveAlong(dt); this.ch.locomote(this.speed, { walk: 'Walk_Loop' }); }
      else { this.path = []; this.speed = 0; this.turnTo(this.o.yaw || 0, dt, 3); this.ch.play(this.o.idle || 'Idle_Loop', { fade: .35 }); }
    }
  }
  // ---------- устранение ----------
  down(kind, dirYaw) {
    const g = this.g; this.state = kind === 'ko' ? 'ko' : 'dead'; this.ko = kind === 'ko'; this.dead = kind !== 'ko'; this.sus = 0; this.downAt = g.time;
    this.ch.play('Death01', { fade: .12, once: true, speed: 1.1 }); this.lightCache = lightAt(g.L, this.pos);
    if (this.icon) this.icon.visible = false;
    g.audio.sfx('bodyfall', this.pos, .35);
  }
  wake() { this.ko = false; this.state = 'search'; this.timer = 20; this.sus = .6; this.ch.play('LayToIdle', { fade: .1, once: true }); this.say('woke', true); this.lkp = this.pos.clone(); this.found = false; this.g.stats.ko--; this.g.stats.woken++; setTimeout(() => this.alive && this.goTo(this.pos.clone().add(new THREE.Vector3(4, 0, 3)), 2.4), 1600); }
  koUpdate(dt) { }
  syncIcon() {
    if (!this.icon) return; const show = this.alive && this.state !== 'patrol' && this.state !== 'post' || this.alive && this.sus > .04;
    this.icon.visible = show && this.kind !== 'civ';
    if (!show) return;
    this.icon.position.set(this.pos.x, this.pos.y + 2.25, this.pos.z);
    const lv = this.state === 'confirm' ? 2 + Math.max(0, 1 - this.alarmT / 1.25) : Math.min(1, this.sus);
    if (Math.abs(lv - (this.iconLv ?? -1)) > .03 || this.state !== this.iconSt) { this.iconLv = lv; this.iconSt = this.state; this.g.ui.drawIcon(this.iconCtx, lv, this.state); this.icon.material.map.needsUpdate = true; }
  }
  makeIcon() {
    const c = document.createElement('canvas'); c.width = c.height = 64; this.iconCtx = c.getContext('2d');
    const t = new THREE.CanvasTexture(c); t.colorSpace = THREE.SRGBColorSpace;
    this.icon = new THREE.Sprite(new THREE.SpriteMaterial({ map: t, depthTest: false, transparent: true })); this.icon.scale.setScalar(.55); this.icon.renderOrder = 10; this.icon.visible = false;
    this.g.scene.add(this.icon);
  }
  makeDebug() {
    const geo = new THREE.BufferGeometry(); geo.setAttribute('position', new THREE.Float32BufferAttribute(new Array(3 * 24).fill(0), 3));
    this.dbg = new THREE.LineSegments(geo, new THREE.LineBasicMaterial({ color: 0xffff00, depthTest: false, transparent: true, opacity: .7 })); this.dbg.renderOrder = 9; this.g.debugGroup.add(this.dbg);
    const c = document.createElement('canvas'); c.width = 256; c.height = 64; this.dbgCtx = c.getContext('2d'); const t = new THREE.CanvasTexture(c);
    this.dbgTxt = new THREE.Sprite(new THREE.SpriteMaterial({ map: t, depthTest: false, transparent: true })); this.dbgTxt.scale.set(2, .5, 1); this.g.debugGroup.add(this.dbgTxt);
  }
  updDebug() {
    const a = this.dbg.geometry.attributes.position, e = this.eye, R = this.sight * (.32 + .78 * this.g.playerLight), n = 6; let k = 0;
    const put = (x, y, z) => a.setXYZ(k++, x, y, z);
    for (let i = 0; i < n; i++) { const a0 = this.yaw - 1.05 + 2.1 * i / (n - 1); put(e.x, e.y, e.z); put(e.x + Math.sin(a0) * R, e.y - .8, e.z + Math.cos(a0) * R); }
    if (this.path.length) for (let i = Math.max(0, this.wp - 1); i < this.path.length - 1 && k < 22; i++) { put(this.path[i].x, this.path[i].y + .1, this.path[i].z); put(this.path[i + 1].x, this.path[i + 1].y + .1, this.path[i + 1].z); }
    while (k < 24) put(0, -100, 0); a.needsUpdate = true;
    this.dbg.material.color.setHex(this.state === 'confirm' ? 0xff3030 : this.sus > .5 ? 0xff9a3a : this.sus > .2 ? 0xffee55 : 0x66ddff);
    const s = `${this.state} ${(this.sus * 100 | 0)}%` + (this.state === 'confirm' ? ` ⏱${this.alarmT.toFixed(1)}` : '');
    if (s !== this.dbgS) { this.dbgS = s; const x = this.dbgCtx; x.clearRect(0, 0, 256, 64); x.fillStyle = '#000a'; x.fillRect(0, 8, 256, 40); x.fillStyle = '#fff'; x.font = '26px monospace'; x.fillText(s, 8, 38); this.dbgTxt.material.map.needsUpdate = true; }
    this.dbgTxt.position.set(this.pos.x, this.pos.y + 2.8, this.pos.z);
  }
}

// Горожане: бродят по площади, создают толпу для слияния; видя убийство/тело — кричат и бегут.
export class Civilian extends NPC {
  constructor(game, o) { super(game, o); this.state = 'wander'; this.timer = Math.random() * 4; }
  update(dt) {
    const g = this.g; this.bark -= dt;
    if (!this.alive || this.state === 'takedown') { this.ch.update(dt); return; }
    this.perceiveT -= dt;
    if (this.perceiveT <= 0) { this.perceiveT = .25; this.panicCheck(); }
    if (this.state === 'flee') { if (this.moveAlong(dt)) { this.state = 'wander'; this.timer = 6; } this.ch.locomote(this.speed); }
    else if (this.o.chat) { this.speed = 0; this.turnTo(this.o.yaw, dt, 2); this.ch.play('Idle_Talking_Loop', { fade: .4 }); }
    else {
      if (this.timer > 0) { this.timer -= dt; this.speed = 0; this.ch.play('Idle_Loop', { fade: .4 }); }
      else if (!this.path.length || this.moveAlong(dt)) { if (this.path.length) { this.path = []; this.timer = 2 + Math.random() * 5; } else { const nav = g.L.nav, pool = g.L.plazaNodes; const n = nav[pool[Math.random() * pool.length | 0]].p; this.goTo(n.clone().add(new THREE.Vector3((Math.random() - .5) * 3, 0, (Math.random() - .5) * 3)), 1.15); } }
      this.ch.locomote(this.speed);
    }
    this.pos.y = this.o.pos.y; this.ch.root.position.copy(this.pos); this.ch.root.rotation.y = this.yaw; this.ch.update(dt);
  }
  panicCheck() {
    const g = this.g, p = g.player;
    let saw = null;
    if (p.state === 'takedown' && this.canSee(new THREE.Vector3(p.pos.x, p.pos.y + 1.2, p.pos.z), 15, 1.6) > 0) saw = p.pos;
    else for (const b of g.npcs) if (!b.alive && !b.hidden && !b.carried && b !== this && this.canSee(new THREE.Vector3(b.pos.x, b.pos.y + .3, b.pos.z), 10, 1.4) > 0) { saw = b.pos; break; }
    if (p.carry && this.canSee(new THREE.Vector3(p.pos.x, p.pos.y + 1.4, p.pos.z), 12, 1.4) > 0) saw = p.pos;
    if (!saw || this.state === 'flee') return;
    this.state = 'flee'; this.say('scream', true); g.stats.witness++;
    g.noise(saw, 17, 'scream', true);
    const away = this.pos.clone().sub(saw).setY(0).normalize().multiplyScalar(14).add(this.pos); this.goTo(away, 3.8);
  }
}
