import * as THREE from 'three';
import { EffectComposer } from 'three/examples/jsm/postprocessing/EffectComposer.js';
import { RenderPass } from 'three/examples/jsm/postprocessing/RenderPass.js';
import { UnrealBloomPass } from 'three/examples/jsm/postprocessing/UnrealBloomPass.js';
import { OutputPass } from 'three/examples/jsm/postprocessing/OutputPass.js';
import { A, loadAll } from './assets.js';
import { buildLevel, lightAt, MOON_DIR } from './world.js';
import { Player } from './player.js';
import { NPC, Civilian } from './npc.js';
import { LOOKS, CLOTH } from './character.js';
import { Input } from './input.js';
import { CamRig } from './camera.js';
import { Audio } from './audio.js';
import { UI } from './ui.js';

const V = (x, y, z) => new THREE.Vector3(x, y, z), PI = Math.PI;
const DEF = { quality: 'auto', sens: 1, invert: false, ui: 1, reduceMotion: false, subs: true, debug: false, vol: .85, music: .6, sfx: .9, autoCam: true, marker: true };
const QP = { low: { dpr: .85, shadow: 0, lights: 1, bloom: false, aa: false }, medium: { dpr: 1.3, shadow: 1024, lights: 3, bloom: false, aa: false }, high: { dpr: 2, shadow: 2048, lights: 4, bloom: true, aa: true } };
const QN = { auto: 'Авто', low: 'Низкое', medium: 'Среднее', high: 'Высокое' };
const MOBILE = matchMedia('(pointer:coarse)').matches;

class Game {
  constructor() {
    this.set = Object.assign({}, DEF, JSON.parse(localStorage.getItem('sc3d_set') || '{}'));
    this.mode = 'boot'; this.time = 0; this.npcs = []; this.proj = []; this.noises = [];
    this.audio = new Audio(this.set); this.ui = new UI(this);
    document.documentElement.style.setProperty('--ui', this.set.ui);
    this.boot().catch(e => { console.error(e); document.querySelector('#loadmsg').textContent = 'Ошибка загрузки: ' + e.message; });
  }
  // ---------- рендер ----------
  preset() { return QP[this.set.quality === 'auto' ? (MOBILE ? 'medium' : 'high') : this.set.quality]; }
  initRenderer() {
    const q = this.preset(), cv = document.querySelector('#gl');
    this.renderer = new THREE.WebGLRenderer({ canvas: cv, antialias: q.aa, powerPreference: 'high-performance', stencil: false });
    const r = this.renderer; r.outputColorSpace = THREE.SRGBColorSpace; r.toneMapping = THREE.ACESFilmicToneMapping; r.toneMappingExposure = 1.05;
    r.shadowMap.enabled = q.shadow > 0; r.shadowMap.type = q.shadow >= 2048 ? THREE.PCFSoftShadowMap : THREE.PCFShadowMap;
    this.scene = new THREE.Scene(); this.scene.fog = new THREE.FogExp2(0x0c1322, .021); this.scene.background = new THREE.Color(0x0c1322);
    this.camera = new THREE.PerspectiveCamera(62, innerWidth / innerHeight, .1, 260);
    this.rs = 1; this.resize(); addEventListener('resize', () => this.resize());
  }
  resize() {
    const q = this.preset(); this.renderer.setPixelRatio(Math.min(devicePixelRatio || 1, q.dpr) * this.rs); this.renderer.setSize(innerWidth, innerHeight);
    this.camera.aspect = innerWidth / innerHeight; this.camera.updateProjectionMatrix(); if (this.composer) { this.composer.setPixelRatio(this.renderer.getPixelRatio()); this.composer.setSize(innerWidth, innerHeight); }
  }
  initLights() {
    const s = this.scene, q = this.preset();
    this.hemi = new THREE.HemisphereLight(0x5c72a8, 0x1c150f, .9); s.add(this.hemi);
    const m = this.moon = new THREE.DirectionalLight(0xb3c6ff, 1.7); m.castShadow = q.shadow > 0;
    if (q.shadow) { m.shadow.mapSize.set(q.shadow, q.shadow); const c = m.shadow.camera; c.left = c.bottom = -24; c.right = c.top = 24; c.near = 1; c.far = 90; m.shadow.bias = -.0006; m.shadow.normalBias = .04; }
    s.add(m); s.add(m.target);
    this.pool = []; for (let i = 0; i < q.lights; i++) { const l = new THREE.PointLight(0xff9d4d, 0, 11, 1.6); s.add(l); this.pool.push(l); }
    // небо: градиент + звёзды + луна
    const sky = new THREE.Mesh(new THREE.SphereGeometry(200, 24, 12), new THREE.ShaderMaterial({ side: THREE.BackSide, depthWrite: false, fog: false,
      vertexShader: 'varying vec3 v;void main(){v=normalize(position);gl_Position=projectionMatrix*modelViewMatrix*vec4(position,1.);}',
      fragmentShader: 'varying vec3 v;void main(){float h=max(v.y,0.);vec3 c=mix(vec3(.05,.07,.13),vec3(.012,.018,.045),pow(h,.6));c+=vec3(.08,.06,.05)*pow(1.-h,8.);gl_FragColor=vec4(c,1.);}' }));
    s.add(sky);
    const sg = new THREE.BufferGeometry(), sp = []; for (let i = 0; i < 700; i++) { const u = Math.random() * 2 * PI, v = Math.random() * .9 + .08; sp.push(Math.cos(u) * Math.cos(v) * 190, Math.sin(v) * 190, Math.sin(u) * Math.cos(v) * 190); }
    sg.setAttribute('position', new THREE.Float32BufferAttribute(sp, 3)); s.add(new THREE.Points(sg, new THREE.PointsMaterial({ color: 0xcfd8ff, size: 1.1, sizeAttenuation: false, fog: false, transparent: true, opacity: .8 })));
    const moon = new THREE.Mesh(new THREE.CircleGeometry(7, 32), new THREE.MeshBasicMaterial({ color: 0xe8eeff, fog: false })); moon.position.copy(MOON_DIR).multiplyScalar(185); moon.lookAt(0, 0, 0); s.add(moon);
    const glow = new THREE.Sprite(new THREE.SpriteMaterial({ map: (() => { const c = document.createElement('canvas'); c.width = c.height = 64; const x = c.getContext('2d'), g = x.createRadialGradient(32, 32, 0, 32, 32, 32); g.addColorStop(0, 'rgba(200,215,255,.5)'); g.addColorStop(1, 'rgba(200,215,255,0)'); x.fillStyle = g; x.fillRect(0, 0, 64, 64); return new THREE.CanvasTexture(c); })(), fog: false, depthWrite: false, transparent: true }));
    glow.position.copy(moon.position); glow.scale.setScalar(60); s.add(glow);
    // IBL: окружение из процедурной ночной сцены → отражения на металле/стекле/воде
    const env = new THREE.Scene(); env.add(new THREE.Mesh(new THREE.SphereGeometry(10, 16, 8), new THREE.MeshBasicMaterial({ color: 0x18203a, side: THREE.BackSide })));
    for (const [x, y, z] of [[4, 1, 3], [-5, 1.5, -2], [2, 1, -5]]) { const b = new THREE.Mesh(new THREE.SphereGeometry(.6, 8, 6), new THREE.MeshBasicMaterial({ color: 0xffa050 })); b.position.set(x, y, z); env.add(b); }
    const mb = new THREE.Mesh(new THREE.SphereGeometry(1, 8, 6), new THREE.MeshBasicMaterial({ color: 0xc0d0ff })); mb.position.copy(MOON_DIR).multiplyScalar(8); env.add(mb);
    const pm = new THREE.PMREMGenerator(this.renderer); s.environment = pm.fromScene(env, .04).texture; s.environmentIntensity = .42; pm.dispose();
    if (q.bloom) {
      this.composer = new EffectComposer(this.renderer, new THREE.WebGLRenderTarget(1, 1, { type: THREE.HalfFloatType, samples: 4 }));
      this.composer.addPass(new RenderPass(s, this.camera));
      this.bloom = new UnrealBloomPass(new THREE.Vector2(innerWidth / 2, innerHeight / 2), .38, .55, .92); this.composer.addPass(this.bloom);
      this.composer.addPass(new OutputPass()); this.resize();
    }
  }
  async boot() {
    this.initRenderer();
    this.ui.progress(.05, 'Загрузка текстур и анимаций…');
    await loadAll(v => this.ui.progress(.05 + v * .7), this.renderer.capabilities.getMaxAnisotropy());
    this.ui.progress(.8, 'Сборка города…'); await new Promise(r => setTimeout(r, 20));
    this.initLights();
    this.L = buildLevel(this.scene); this.L.ladderTop = V(0, 12, -17.2);
    this.L.plazaNodes = this.L.nav.map((n, i) => i).filter(i => this.L.nav[i].layer === 'g' && Math.abs(this.L.nav[i].p.x) < 13 && this.L.nav[i].p.z > -11 && this.L.nav[i].p.z < 12);
    this.debugGroup = new THREE.Group(); this.debugGroup.visible = this.set.debug; this.scene.add(this.debugGroup);
    this.input = new Input(document.querySelector('#gl'), document.querySelector('#touch')); this.input.touchMode = MOBILE; document.body.classList.toggle('touch', MOBILE);
    for (const [id, n] of [['b-action', 'action'], ['b-alt', 'alt'], ['b-jump', 'jump'], ['b-crouch', 'crouch'], ['b-throw', 'throw'], ['b-item', 'item'], ['b-pause', 'pause']]) this.input.bindButton(document.getElementById(id), n);
    this.cam = new CamRig(this.camera, this);
    this.player = new Player(this); this.scene.add(this.player.ch.root);
    this.ui.progress(.9, 'Расстановка охраны…'); this.spawn();
    // прогрев шейдеров, чтобы не было рывков при первом кадре
    this.cam.snap(); this.cam.update(0, { lx: 0, ly: 0 }, this.set); this.renderer.compile(this.scene, this.camera);
    this.ui.progress(1, 'Готово'); this.ui.hideLoad();
    this.mode = 'menu'; this.menu();
    this.last = performance.now(); this.acc = 0; this.ft = 16; requestAnimationFrame(t => this.loop(t));
  }
  // ---------- NPC ----------
  spawn() {
    for (const n of this.npcs) { n.ch.dispose(); if (n.icon) n.icon.removeFromParent(); }
    if (this.debugGroup) this.debugGroup.clear();
    this.npcs = []; const R = (pts, y = 0) => pts.map(([x, z, wait, face, anim]) => ({ p: V(x, y, z), wait, face, anim }));
    const D = [
      { kind: 'guard', name: 'Стражник у двери', pos: V(0, 0, -10.5), yaw: 0, idle: 'Idle_FoldArms_Loop', door: 'towerDoor', react: 1.1 },
      { kind: 'guard', name: 'Патрульный', route: R([[-10, -8, 2], [10, -8, 2.5], [10, 9.5, 2], [-10, 9.5, 2.5]]) },
      { kind: 'guard', name: 'Патрульный', route: R([[-13.5, -2, 3], [-13.5, -24, 2], [-10.5, -27.5, 4, PI / 2], [-13.5, -14, 1]]) },
      { kind: 'guard', name: 'Привратник', pos: V(9.9, 0, -25), yaw: PI / 2, idle: 'Idle_FoldArms_Loop', react: 1.05 },
      { kind: 'guard', name: 'Стражник двора', route: R([[3, -25.6, 3], [-6.2, -22.2, 2], [-6.4, -27.4, 3, -PI / 2], [4.6, -28.2, 2]]) },
      { kind: 'lookout', name: 'Дозорный', layer: 'r1', y: 7, lantern: true, route: R([[-22, -11, 4, PI / 4], [-17, -4, 4, 0]], 7), idle: 'Idle_Lantern_Loop', walk: 'Walk_Loop', sight: 21 },
      { kind: 'lookout', name: 'Дозорный', layer: 'r2', y: 8, lantern: true, route: R([[6.5, -14, 4, 0], [11, -19, 4, PI * .8]], 8), idle: 'Idle_Lantern_Loop', walk: 'Walk_Loop', sight: 21 },
      { kind: 'guard', name: 'Стражник', route: R([[13.5, -1, 3], [22, 0, 4, PI / 2]]) },
      { kind: 'body', look: 'body', name: 'Телохранитель', layer: 'gal', y: 12, route: R([[-2.8, -13.2, 2, 0], [2.8, -13.2, 2], [2.8, -18.6, 2.5, PI], [-2.8, -18.6, 2]], 12), react: 1.2 },
      { kind: 'target', look: 'target', name: 'Мастер Орлов', layer: 'gal', y: 12, route: R([[0, -12.9, 9, 0], [-2.9, -16, 7, -PI / 2], [0, -15.6, 8, PI, 'Idle_Talking_Loop'], [2.6, -18.2, 5, PI / 2]], 12), walkSpeed: 1.05, idle: 'Idle_Loop', react: 1.1 },
    ];
    for (const d of D) {
      d.pos = d.pos || d.route[0].p.clone(); d.layer = d.layer || 'g'; if (d.door) d.watchDoor = this.L.doors.find(x => x.id === d.door);
      const n = new NPC(this, d); n.route = d.route || null; n.ri = 0; n.state = d.route ? 'patrol' : 'post'; n.makeIcon(); this.npcs.push(n);
      if (d.kind === 'target') this.target = n;
    }
    // напарники: соседние по маршруту
    const gs = this.npcs.filter(n => n.kind === 'guard'); for (const n of gs) { let b = null, bd = 30; for (const o of gs) if (o !== n && o.pos.distanceTo(n.pos) < bd) { bd = o.pos.distanceTo(n.pos); b = o; } n.buddy = b; }
    // звонарь (маскировка) и горожане
    const w = new Worker(this, { kind: 'worker', look: 'worker', name: 'Звонарь', pos: V(14.2, 0, 7), route: R([[14.2, 7, 6, -PI / 2], [13.5, -1, 0], [13.5, -15, 0], [12, -25, 0], [6.6, -25, 0], [-4, -25.5, 12, PI, 'Interact'], [1.6, -21.4, 5, PI, 'PickUp_Table'], [6.6, -25, 0], [12, -25, 0], [13.5, -15, 0], [13.5, -1, 0]]), layer: 'g' });
    w.route = w.o.route; w.ri = 0; this.npcs.push(w); this.worker = w;
    const civSpots = [[-4, 6], [5, 8], [-8, -2], [8, -3], [3, -6], [-10, 8], [10, 2]];
    civSpots.forEach(([x, z], i) => this.npcs.push(new Civilian(this, { kind: 'civ', look: { ...LOOKS.hero, main: LOOKS.civ[i % 6][0], joint: LOOKS.civ[i % 6][1], acc: i % 3 ? null : 'cap' }, name: 'Горожанин', pos: V(x, 0, z), layer: 'g' })));
    for (const [x, z, y] of [[-6, -4, -.9], [-5.1, -3.4, 2.2], [7, 4, .8], [7.9, 4.6, -2.4]]) this.npcs.push(new Civilian(this, { kind: 'civ', chat: true, look: { ...LOOKS.hero, main: LOOKS.civ[(x * 7 & 7) % 6][0], joint: 0x2a2622, acc: null }, name: 'Горожанин', pos: V(x, 0, z), yaw: y, layer: 'g' }));
  }
  // ---------- миссия ----------
  newMission(restore) {
    const L = this.L;
    for (const d of L.doors) { d.open = d.target = 0; d.box.active = true; d.piv.rotation.y = 0; }
    const b = L.bell; b.state = 'hung'; b.y = 13.9; b.vy = 0; b.mesh.position.y = b.y; b.mesh.rotation.set(0, 0, 0); b.rope.visible = b.pull.visible = b.pull2.visible = true;
    for (const h of L.hides) h.bodies = 0;
    for (const p of this.proj) p.mesh.removeFromParent(); this.proj = [];
    this.spawn(); this.player.reset(L.spawn); this.player.ch.root.visible = true;
    this.time = 0; this.targetDown = false; this.method = ''; this.alarmBy = null; this.done = false;
    this.stats = { kills: 0, ko: 0, bodies: 0, suspicions: 0, searches: 0, witness: 0, woken: 0, disguise: false, exit: '' };
    this.cam.snap(); this.cam.yaw = PI; this.player.yaw = PI;
    if (restore) this.restore(restore);
    this.mode = 'play'; this.ui.hide(); this.ui.setHUD(true); this.audio.setMood('calm'); this.saveT = 10;
    this.ui.toast('Цель: Мастер Орлов на галерее колокольни');
  }
  suspicionOf(n) { // насколько подозрительно для этого NPC само присутствие/действие героя
    const p = this.player; if (p.state === 'hidden') return 0;
    let f = 0; const z = this.zoneAt(p.pos);
    if (z) { const ok = p.disguise && z.allow.includes(p.disguise); if (!ok) f = 1; else if (z.strict && (n.kind === 'target' || n.kind === 'body') && n.pos.distanceTo(p.pos) < 3.2) f = .35; }
    else if (p.pos.y > 2.3) f = n.kind === 'lookout' ? 1 : .7;
    if (p.state === 'climb' || p.state === 'ladder' || p.state === 'mantle') f = Math.max(f, 1);
    if (p.carry) f = Math.max(f, 2.2);
    if (p.sprinting && n.pos.distanceTo(p.pos) < 9) f = Math.max(f, .55);
    if (p.crouch && p.speed > .3 && !z) f = Math.max(f, .45);
    return f;
  }
  zoneAt(p) { for (const z of this.L.zones) if (p.x > z.min[0] && p.x < z.max[0] && p.y > z.min[1] && p.y < z.max[1] && p.z > z.min[2] && p.z < z.max[2]) return z; return null; }
  noise(pos, r, type, civ) { for (const n of this.npcs) if (n.alive && n.state !== 'takedown') n.hear({ pos, r, type }); if (this.set.debug) this.debugNoise(pos, r); }
  alarm(n) { if (this.mode !== 'play') return; this.alarmBy = n; this.audio.setMood('alarm'); this.fail(`${n.o.name || 'Стражник'} поднял тревогу.`); }
  fail(why) {
    if (this.mode !== 'play') return; this.mode = 'fail'; this.audio.sfx('fail'); this.ui.setHUD(false); localStorage.removeItem('sc3d_save');
    const tips = ['Подозрение (?) — не провал: уйди из поля зрения, спрячься или устрани его, пока он не крикнул.', 'Пока над охранником горит «!», его ещё можно устранить — тревога сорвётся.', 'Камень отвлекает охрану от поста: брось его за спину стражника.', 'Переодевшись звонарём, ты свободно ходишь по двору и башне.', 'Тела находят быстро: неси их в сено или в сундук во дворе.'];
    setTimeout(() => this.ui.show(`<div class="kicker">Контракт провален</div><h1>Обнаружен</h1><p>${why}</p><p class="tip">${tips[Math.random() * tips.length | 0]}</p><button data-a="restart" class="pri">↻ Заново <small>R</small></button><button data-a="menu">В меню</button>`, 'fail'), 900);
  }
  complete(exit) {
    if (this.mode !== 'play') return; this.mode = 'done'; this.stats.exit = exit; this.audio.setMood('done'); this.audio.sfx('done'); this.ui.setHUD(false); localStorage.removeItem('sc3d_save');
    const s = this.stats, CH = [
      ['accident', 'Несчастный случай: колокол', this.method === 'accident'], ['air', 'Удар сверху', this.method === 'air'], ['disguise', 'Под маскировкой звонаря', s.disguise],
      ['only', 'Только цель (оглушать можно)', s.kills === 0], ['clean', 'Ни одного найденного тела', s.bodies === 0], ['sewer', 'Уйти через канализацию', exit === 'sewer'], ['ghost', 'Ни одного подозрения', s.suspicions === 0 && s.searches === 0]];
    const got = JSON.parse(localStorage.getItem('sc3d_ch') || '{}'); CH.forEach(c => { if (c[2]) got[c[0]] = 1; }); localStorage.setItem('sc3d_ch', JSON.stringify(got));
    const score = Math.max(0, 5 - s.bodies - s.witness - (s.kills > 0 ? 1 : 0) - (s.suspicions > 2 ? 1 : 0) - (s.searches ? 1 : 0)), rank = ['Мясник', 'Убийца', 'Наёмник', 'Профессионал', 'Тень', 'Бесшумный убийца'][score];
    const best = JSON.parse(localStorage.getItem('sc3d_best') || 'null'); if (!best || this.time < best.t) localStorage.setItem('sc3d_best', JSON.stringify({ t: this.time, rank }));
    const M = { accident: 'Несчастный случай (колокол)', air: 'Удар сверху', stab: 'Скрытый клинок', front: 'Клинок в лицо', knife: 'Метательный нож' }[this.method] || this.method;
    this.ui.show(`<div class="kicker">Контракт выполнен</div><h1>${rank}</h1><div class="stars">${'★'.repeat(score)}${'☆'.repeat(5 - score)}</div>
      <div class="stats"><span>Время</span><b>${fmt(this.time)}</b><span>Способ</span><b>${M}</b><span>Отход</span><b>${exit === 'sewer' ? 'Канализация' : 'Городские ворота'}</b><span>Убито / оглушено</span><b>${s.kills} / ${s.ko}</b><span>Найдено тел</span><b>${s.bodies}</b><span>Свидетели</span><b>${s.witness}</b><span>Подозрения / обыски</span><b>${s.suspicions} / ${s.searches}</b></div>
      <div class="ch">${CH.map(c => `<div class="${c[2] ? 'ok' : got[c[0]] ? 'was' : ''}">${c[2] ? '✔' : got[c[0]] ? '★' : '○'} ${c[1]}</div>`).join('')}</div>
      <button data-a="restart" class="pri">Сыграть снова</button><button data-a="menu">В меню</button>`, 'done');
  }
  targetKilled(method) { this.targetDown = true; this.method = method; this.ui.toast('Цель устранена. Уходи: ворота на юге или люк в восточном переулке.'); this.audio.setMood(this.npcs.some(n => n.state === 'search') ? 'search' : 'tense'); }
  // ---------- действия героя ----------
  context() {
    const p = this.player, near = (q, r) => Math.hypot(q.x - p.pos.x, q.z - p.pos.z) < r && Math.abs(q.y - p.pos.y) < 1.2;
    if (p.state === 'hidden') return { a: ['Выйти', () => p.leaveHide()] };
    if (p.state !== 'move' && p.state !== 'roll') return {};
    if (p.carry) {
      const h = this.L.hides.find(h => near(h.pos, h.r + .4) && h.bodies < h.cap);
      return h ? { a: ['Спрятать тело', () => this.stash(h)], b: ['Положить', () => this.drop()] } : { a: ['Положить тело', () => this.drop()] };
    }
    // убийство сверху: цель ниже на 1.2–6 м, в 4.5 м по горизонтали, есть прямая видимость
    let air = null, ad = 4.6;
    for (const n of this.npcs) if (n.alive && n.state !== 'takedown') { const dy = p.pos.y - n.pos.y, d = Math.hypot(n.pos.x - p.pos.x, n.pos.z - p.pos.z); if (dy > 1.2 && dy < 6.5 && d < ad && this.L.phys.clear(p.pos.x, p.pos.y + 1, p.pos.z, n.pos.x, n.pos.y + 1.2, n.pos.z)) { ad = d; air = n; } }
    if (air) return { a: ['Убить сверху', () => this.airKill(air)] };
    let v = null, vd = 1.75;
    for (const n of this.npcs) if (n.alive && n.state !== 'takedown') { const d = Math.hypot(n.pos.x - p.pos.x, n.pos.z - p.pos.z); if (d < vd && Math.abs(n.pos.y - p.pos.y) < .8) { vd = d; v = n; } }
    if (v) { const behind = this.isBehind(v); const nm = v.kind === 'target' ? 'Устранить цель' : behind ? 'Убить' : 'Убить (лицом)';
      return { a: [nm, () => this.takedown(v, false)], b: v.kind === 'target' ? null : ['Оглушить', () => this.takedown(v, true)], victim: v }; }
    const body = this.npcs.find(n => !n.alive && !n.hidden && !n.carried && near(n.pos, 1.5));
    if (body) return { a: ['Поднять тело', () => this.pickUp(body)], b: body.kind === 'worker' && !body.stripped && !p.disguise ? ['Переодеться', () => this.disguise(body)] : null };
    if (this.targetDown) { const e = this.L.exits.find(e => near(e.pos, e.r)); if (e) return { a: [e.label, () => this.complete(e.id)] }; }
    const it = this.L.inter.find(i => near(i.pos, i.r) && (i.act !== 'dropBell' || this.L.bell.state === 'hung')); if (it) return { a: [it.label, () => this.interact(it)] };
    if (near(V(0, 12, -18.5), 1) && p.pos.y > 11) return { a: ['Спуститься по лестнице', () => this.ladderDown()] };
    const d = this.L.doors.find(d => near(d.center, 1.7)); if (d) return { a: [d.target ? 'Закрыть' : 'Открыть', () => { d.target = d.target ? 0 : 1; d.autoClose = 0; this.audio.sfx('door', d.center); }] };
    const h = this.L.hides.find(h => h.kind === 'hay' && near(h.pos, h.r + .3)); if (h) return { a: ['Спрятаться в сене', () => p.enterHide(h)] };
    return {};
  }
  isBehind(n) { const fx = Math.sin(n.yaw), fz = Math.cos(n.yaw), dx = this.player.pos.x - n.pos.x, dz = this.player.pos.z - n.pos.z, d = Math.hypot(dx, dz) || 1; return (fx * dx + fz * dz) / d < -.25; }
  takedown(n, ko) {
    const p = this.player, behind = this.isBehind(n), front = !behind, fx = Math.sin(n.yaw), fz = Math.cos(n.yaw);
    const goal = behind ? V(n.pos.x - fx * .7, n.pos.y, n.pos.z - fz * .7) : V(n.pos.x + fx * .78, n.pos.y, n.pos.z + fz * .78);
    // motion warping: проверяем, что точка выравнивания свободна, иначе остаёмся на месте
    const test = goal.clone(), h = {}; if (this.L.phys.push(test, .28, goal.y + .4, goal.y + 1.6, h) && test.distanceTo(goal) > .2) goal.copy(p.pos);
    const from = p.pos.clone(), y0 = p.yaw, y1 = Math.atan2(n.pos.x - goal.x, n.pos.z - goal.z);
    n.state = 'takedown'; n.path = []; n.speed = 0; n.prevState = null; p.tdVictim = n; p.tdSide = Math.random() < .5 ? -1 : 1; p.crouch = false;
    if (front) { n.yawGoal = Math.atan2(goal.x - n.pos.x, goal.z - n.pos.z); this.noise(n.pos, 6, 'struggle'); }
    const dur = front ? 1.15 : .95, hit = front ? .55 : .38; let t = 0, hitDone = false;
    p.ch.play(ko ? 'Melee_Hook' : 'Sword_Attack', { fade: .1, once: true, speed: front ? .8 : 1.05 }); if (!ko) { p.ch.attach.blade.visible = true; this.audio.sfx('blade', p.pos); } else this.audio.sfx('grab', p.pos);
    p.runScript('takedown', dur, dt => {
      t += dt; const k = Math.min(1, t / .18), e = k * k * (3 - 2 * k);
      p.pos.lerpVectors(from, goal, e); let dy = y1 - y0; dy = Math.atan2(Math.sin(dy), Math.cos(dy)); p.yaw = y0 + dy * e;
      if (front && n.yawGoal !== undefined) n.turnTo(n.yawGoal, dt, 10), n.ch.root.rotation.y = n.yaw;
      if (!hitDone && t >= hit) {
        hitDone = true; this.audio.sfx(ko ? 'punch' : 'stab', n.pos); n.ch.play(ko ? 'Hit_Head' : 'Hit_Chest', { fade: .05, once: true, speed: 1.2 });
        if (!this.set.reduceMotion) this.cam.shake = .12;
        setTimeout(() => { if (n.state === 'takedown') { n.down(ko ? 'ko' : 'dead'); } }, 220);
        if (ko) this.stats.ko++; else if (n.kind !== 'target') this.stats.kills++;
        if (n.kind === 'target') this.targetKilled(front ? 'front' : 'stab');
      }
    }, () => { p.ch.attach.blade.visible = false; p.tdVictim = null; if (n.state === 'takedown') n.down(ko ? 'ko' : 'dead'); });
  }
  airKill(n) {
    const p = this.player, from = p.pos.clone(), fx = Math.sin(n.yaw), fz = Math.cos(n.yaw), to = V(n.pos.x - fx * .45, n.pos.y, n.pos.z - fz * .45);
    const dur = Math.max(.5, Math.min(.85, from.distanceTo(to) * .1 + .35)); let t = 0, landed = false;
    n.state = 'takedown'; n.path = []; n.speed = 0; p.tdVictim = n; p.tdSide = 1;
    p.ch.play('Jump_Loop', { fade: .05 }); p.ch.attach.blade.visible = true; this.audio.sfx('jump', p.pos); p.yaw = Math.atan2(to.x - from.x, to.z - from.z);
    p.runScript('takedown', dur + .55, dt => {
      t += dt; const k = Math.min(1, t / dur);
      p.pos.lerpVectors(from, to, k); p.pos.y = from.y + (to.y - from.y) * k + Math.sin(k * PI) * .9;
      if (k >= 1 && !landed) { landed = true; p.ch.play('Sword_Attack', { fade: .05, once: true, speed: 1.3 }); this.audio.sfx('stab', n.pos); this.audio.sfx('landHard', n.pos); n.down('dead'); if (!this.set.reduceMotion) this.cam.shake = .2;
        if (n.kind === 'target') this.targetKilled('air'); else this.stats.kills++; this.noise(n.pos, 5, 'land'); }
    }, () => { p.ch.attach.blade.visible = false; p.tdVictim = null; p.peakY = p.pos.y; p.onGround = true; });
  }
  pickUp(n) {
    const p = this.player; p.ch.play('PickUp_Table', { fade: .1, once: true, speed: 1.4 }); this.audio.sfx('pickup', n.pos);
    p.yaw = Math.atan2(n.pos.x - p.pos.x, n.pos.z - p.pos.z);
    p.runScript('pickup', .6, null, () => {
      p.carry = n; n.carried = true; p.crouch = false;
      // кладём на плечо: центрируем позу «лёжа» над плечами героя
      const r = n.ch.root, m = n.ch.model; r.position.set(0, 0, 0); r.rotation.set(0, 0, 0); m.position.set(0, 0, 0); m.rotation.set(0, 0, 0); r.updateMatrixWorld(true);
      const bb = new THREE.Box3().setFromObject(m), c = bb.getCenter(new THREE.Vector3());
      m.rotation.y = PI / 2; m.position.set(c.z, 1.32 - bb.min.y * .2, -c.x + .05); n.carryOff = true;
      this.ui.hintOnce('carry', 'Неси тело в сено или в сундук во дворе. С телом ты заметен даже в толпе.');
    });
  }
  drop(silent) {
    const p = this.player, n = p.carry; if (!n) return; p.carry = null; n.carried = false;
    const m = n.ch.model; m.position.set(0, 0, 0); m.rotation.set(0, 0, 0);
    n.pos.set(p.pos.x + Math.sin(p.yaw) * .8, p.pos.y, p.pos.z + Math.cos(p.yaw) * .8); this.L.phys.push(n.pos, .3, n.pos.y + .2, n.pos.y + .8, {});
    n.pos.y = this.L.phys.groundAt(n.pos.x, n.pos.z, .2, p.pos.y + .5).y; n.ch.root.position.copy(n.pos); n.ch.root.rotation.y = p.yaw;
    n.lightCache = lightAt(this.L, n.pos); if (!silent) this.audio.sfx('bodyfall', n.pos, .6);
  }
  stash(h) { const n = this.player.carry; this.drop(true); n.hidden = true; n.ch.root.visible = false; h.bodies++; this.audio.sfx(h.kind === 'hay' ? 'hay' : 'door', h.pos); this.ui.toast('Тело спрятано'); }
  disguise(b) {
    const p = this.player; p.runScript('busy', 1.2, null, () => { p.disguise = 'worker'; p.ch.setLook(LOOKS.disguise); b.ch.setLook(LOOKS.undies); b.stripped = true; this.stats.disguise = true;
      this.ui.toast('Маскировка: звонарь. Двор и колокольня теперь твои — но не лезь, не беги и не подходи близко к Орлову.'); });
    p.ch.play('Interact', { fade: .15, once: true }); this.audio.sfx('disguise', p.pos);
  }
  interact(it) { if (it.act === 'dropBell') { this.player.ch.play('Interact', { fade: .1, once: true }); this.player.runScript('busy', .7, null, () => this.dropBell()); } }
  ladderDown() { const p = this.player, lb = this.L.phys.boxes.find(b => b.kind === 'ladder'); p.pos.set(0, 10.3, lb.max[2] + .3); p.startClimb({ box: lb, nx: 0, nz: 1 }, { box: lb, top: 11.55 }); }
  dropBell() { const b = this.L.bell; if (b.state !== 'hung') return; b.state = 'fall'; b.vy = 0; b.rope.visible = b.pull.visible = b.pull2.visible = false; this.audio.sfx('snap', V(0, 15, -16)); }
  throwItem() {
    const p = this.player; if (p.state !== 'move' || p.carry) return;
    if (p.item === 'knife' && p.knives <= 0) { this.ui.toast('Ножей нет'); return; }
    const cam = this.camera, f = this.cam.forward.clone(), from = V(p.pos.x, p.pos.y + 1.55, p.pos.z);
    let aim = null, best = .16;
    if (p.item === 'knife') {
      const cands = this.npcs.filter(n => n.alive && n.state !== 'takedown').map(n => ({ n, pt: V(n.pos.x, n.pos.y + 1.6, n.pos.z) }));
      if (this.L.bell.state === 'hung') { cands.push({ rope: true, pt: V(0, 15.2, -16) }); for (let y = 2; y <= 14; y += 1.5) cands.push({ rope: true, pt: V(1.6, y, -20.38) }); } // верёвка колокола и тяга по северному фасаду
      for (const c of cands) { const d = c.pt.clone().sub(from), l = d.length(); if (l > 24) continue; const a = 1 - d.normalize().dot(f); if (a < best && this.L.phys.ray(from.x, from.y, from.z, c.pt.x, c.pt.y, c.pt.z) > .97) { best = a; aim = c; } }
    }
    p.yaw = Math.atan2(f.x, f.z); p.ch.play('OverhandThrow', { fade: .08, once: true, speed: 1.6 }); this.audio.sfx('throw', p.pos);
    p.runScript('throw', .45, null, null);
    setTimeout(() => {
      const geo = p.item === 'knife' ? new THREE.BoxGeometry(.03, .03, .3) : new THREE.DodecahedronGeometry(.07);
      const mesh = new THREE.Mesh(geo, p.item === 'knife' ? new THREE.MeshStandardMaterial({ color: 0xd8dde3, metalness: 1, roughness: .2 }) : this.L.M.stone); mesh.castShadow = true; this.scene.add(mesh);
      const vel = aim ? aim.pt.clone().sub(from).normalize().multiplyScalar(30) : f.clone().multiplyScalar(p.item === 'knife' ? 28 : 15).add(V(0, p.item === 'knife' ? 1 : 4.5, 0));
      this.proj.push({ kind: p.item, pos: from.clone(), vel, mesh, aim, life: 3 }); if (p.item === 'knife') { p.knives--; this.ui.items(p); }
    }, 180);
  }
  stepProj(dt) {
    const P = this.L.phys;
    for (const q of this.proj) {
      q.life -= dt; if (q.kind === 'stone') q.vel.y -= 18 * dt; else q.vel.y -= 2 * dt;
      const nx = q.pos.x + q.vel.x * dt, ny = q.pos.y + q.vel.y * dt, nz = q.pos.z + q.vel.z * dt;
      if (q.kind === 'knife' && q.aim) { const d = q.aim.pt.distanceTo(q.pos); if (d < q.vel.length() * dt + .3) { q.life = 0; this.knifeHit(q); continue; } }
      const t = P.ray(q.pos.x, q.pos.y, q.pos.z, nx, ny, nz); const hitGround = ny <= .02;
      if (t < 1 || hitGround) { q.pos.set(q.pos.x + (nx - q.pos.x) * Math.min(t, 1), Math.max(.03, q.pos.y + (ny - q.pos.y) * Math.min(t, 1)), q.pos.z + (nz - q.pos.z) * Math.min(t, 1)); q.life = 0;
        if (q.kind === 'stone') { this.audio.sfx('stone', q.pos); this.noise(q.pos.clone(), 10, 'stone'); this.ui.hintOnce('stone', 'Охрана услышала стук и пойдёт проверить.'); } else this.audio.sfx('knifeHit', q.pos);
        continue; }
      q.pos.set(nx, ny, nz); q.mesh.position.copy(q.pos); q.mesh.lookAt(q.pos.clone().add(q.vel)); if (q.kind === 'stone') q.mesh.rotation.x += dt * 12;
    }
    this.proj = this.proj.filter(q => { if (q.life <= 0) { setTimeout(() => q.mesh.removeFromParent(), 4000); return false; } return true; });
  }
  knifeHit(q) {
    q.mesh.position.copy(q.aim.pt);
    if (q.aim.rope) { this.dropBell(); this.audio.sfx('knifeHit', q.aim.pt); return; }
    const n = q.aim.n; if (!n.alive) return; this.audio.sfx('knifeHit', n.pos); n.down('dead');
    if (n.kind === 'target') this.targetKilled('knife'); else this.stats.kills++; this.noise(n.pos, 4, 'land');
  }
  stepWorld(dt) {
    const L = this.L;
    for (const d of L.doors) {
      if (d.autoClose > 0) { d.autoClose -= dt; if (d.autoClose <= 0 && !this.npcs.some(n => n.alive && n.pos.distanceTo(d.center) < 1.6) && this.player.pos.distanceTo(d.center) > 1.4) d.target = 0; }
      d.open += Math.sign(d.target - d.open) * Math.min(Math.abs(d.target - d.open), dt * 2.2);
      d.piv.rotation.y = (d.axis === 'z' ? -1 : 1) * d.open * 1.75; d.box.active = d.open < .35;
    }
    const b = L.bell;
    if (b.state === 'fall') {
      b.vy += 22 * dt; b.y -= b.vy * dt; b.mesh.position.y = b.y;
      if (b.y <= b.floor) { b.y = b.floor; b.state = 'down'; b.mesh.position.y = b.y; b.mesh.rotation.z = .25; this.audio.sfx('crash', V(0, 12, -16)); if (!this.set.reduceMotion) this.cam.shake = .3;
        for (const n of this.npcs) if (n.alive && Math.abs(n.pos.y - 12) < .8 && Math.hypot(n.pos.x - b.x, n.pos.z - b.z) < 1.25) { n.down('dead'); if (n.kind === 'target') this.targetKilled('accident'); else this.stats.kills++; }
        if (this.player.pos.y > 11 && Math.hypot(this.player.pos.x - b.x, this.player.pos.z - b.z) < 1.1) this.player.die('Раздавлен колоколом.');
        this.noise(V(0, 12, -16), 30, 'crash'); }
    }
  }
  // ---------- сохранение ----------
  save() {
    const p = this.player; if (this.mode !== 'play' || p.state !== 'move' || this.npcs.some(n => n.state === 'confirm')) return;
    localStorage.setItem('sc3d_save', JSON.stringify({ t: this.time, p: [p.pos.x, p.pos.y, p.pos.z, p.yaw, p.disguise, p.knives], td: this.targetDown, m: this.method, bell: L_bell(this.L), st: this.stats,
      n: this.npcs.map(n => [n.dead ? 1 : 0, n.ko ? 1 : 0, n.hidden ? 1 : 0, n.stripped ? 1 : 0, +n.pos.x.toFixed(2), +n.pos.z.toFixed(2), n.found ? 1 : 0]) }));
  }
  restore(s) {
    const p = this.player; p.pos.set(s.p[0], s.p[1], s.p[2]); p.yaw = s.p[3]; p.knives = s.p[5]; if (s.p[4]) { p.disguise = s.p[4]; p.ch.setLook(LOOKS.disguise); }
    this.time = s.t; this.targetDown = s.td; this.method = s.m; Object.assign(this.stats, s.st);
    if (s.bell !== 'hung') { const b = this.L.bell; b.state = 'down'; b.y = b.floor; b.mesh.position.y = b.y; b.mesh.rotation.z = .25; b.rope.visible = b.pull.visible = b.pull2.visible = false; }
    s.n.forEach((r, i) => { const n = this.npcs[i]; if (!n) return; n.pos.x = r[4]; n.pos.z = r[5]; n.ch.root.position.copy(n.pos); n.found = !!r[6];
      if (r[0] || r[1]) { n.down(r[1] ? 'ko' : 'dead'); const a = n.ch.actions.Death01; if (a) { a.time = a.getClip().duration; n.ch.mixer.update(0); } }
      if (r[2]) { n.hidden = true; n.ch.root.visible = false; } if (r[3]) { n.stripped = true; n.ch.setLook(LOOKS.undies); } });
    this.cam.snap(); this.ui.toast('Миссия восстановлена');
  }
  // ---------- меню ----------
  menu() {
    this.mode = 'menu'; this.ui.setHUD(false); const has = !!localStorage.getItem('sc3d_save'), best = JSON.parse(localStorage.getItem('sc3d_best') || 'null');
    this.ui.show(`<div class="logo"><div class="kicker">Shadow Contract</div><h1>Колокольня Орлова</h1><p class="sub">Стелс-контракт в ночном городе</p></div>
      ${has ? '<button data-a="continue" class="pri">Продолжить миссию</button>' : ''}<button data-a="brief" class="${has ? '' : 'pri'}">Новая миссия</button>
      <button data-a="settings">Настройки</button><button data-a="help">Управление</button>${best ? `<p class="tip">Лучший результат: ${fmt(best.t)} · ${best.rank}</p>` : ''}`, 'main');
  }
  brief() {
    this.ui.show(`<div class="kicker">Контракт</div><h1>Мастер Орлов</h1><p>Глава городской стражи проводит ночь на галерее колокольни. Ни свидетелей, ни тревоги: <b>если тебя обнаружат — контракт провален</b>. Подозрение — ещё нет.</p>
      <div class="opps"><b>Возможности</b>
      <div>🔔 Колокол держит верёвка, закреплённая во дворе. Орлов подолгу стоит под колоколом.</div>
      <div>🧹 Звонарь ходит во двор к верстаку. Его одежда откроет двор и башню.</div>
      <div>🏚 Крыша дома купца выше галереи — удобно для удара сверху.</div>
      <div>🧱 Каменные пояса башни годятся для подъёма. Внутри — лестница к люку галереи.</div>
      <div>🪨 Камень отвлечёт стражника у двери. 🗡 Нож — в голову или в верёвку колокола.</div>
      <div>🌾 Сено во дворе под башней — для прыжка веры и для тел.</div></div>
      <button data-a="start" class="pri">Начать</button><button data-a="menu">Назад</button>`);
  }
  settingsUI(back) {
    const s = this.set, row = (k, label, opts) => `<label class="row"><span>${label}</span><select data-k="${k}">${opts.map(([v, t]) => `<option value="${v}" ${String(s[k]) === String(v) ? 'selected' : ''}>${t}</option>`).join('')}</select></label>`;
    const rng = (k, label, a, b, st) => `<label class="row"><span>${label}</span><input type="range" data-k="${k}" min="${a}" max="${b}" step="${st}" value="${s[k]}"></label>`;
    const yes = [[true, 'Вкл'], [false, 'Выкл']];
    this.ui.show(`<h1>Настройки</h1><div class="form">${row('quality', 'Графика', Object.entries(QN))}${rng('sens', 'Чувствительность камеры', .3, 2.5, .1)}${row('invert', 'Инверсия по вертикали', yes)}${row('autoCam', 'Камера доводится за спину', yes)}
      ${rng('ui', 'Размер интерфейса', .8, 1.4, .05)}${row('reduceMotion', 'Меньше движения камеры', yes)}${row('subs', 'Субтитры', yes)}${row('marker', 'Маркер цели', yes)}${row('debug', 'Отладка ИИ (F3)', yes)}
      ${rng('vol', 'Громкость', 0, 1, .05)}${rng('music', 'Музыка', 0, 1, .05)}${rng('sfx', 'Эффекты', 0, 1, .05)}</div><p class="tip">Качество «Авто»: ${MOBILE ? 'Среднее' : 'Высокое'}; при просадке FPS разрешение снижается автоматически. Смена пресета теней — после перезапуска.</p>
      <button data-a="${back}" class="pri">Готово</button>`);
    this.ui.scr.querySelectorAll('[data-k]').forEach(el => el.addEventListener('change', () => {
      const k = el.dataset.k; let v = el.value; if (v === 'true' || v === 'false') v = v === 'true'; else if (el.type === 'range') v = +v;
      s[k] = v; localStorage.setItem('sc3d_set', JSON.stringify(s)); document.documentElement.style.setProperty('--ui', s.ui); this.audio.applyVolumes(); this.debugGroup.visible = s.debug; if (k === 'quality') this.resize();
    }));
  }
  help(back) {
    this.ui.show(`<h1>Управление</h1><div class="help"><div><b>Телефон</b><br>Левая половина — джойстик (дальше от центра — бег, до упора — спринт).<br>Правая — поворот камеры.<br>⚔ — главное действие (подпись меняется по ситуации), второе действие — над ним.<br>⤴ — прыжок / держать у стены — лезть. ▼ — присесть / отпустить стену. 🪨 — бросок, ⇄ — сменить предмет.</div>
      <div><b>Клавиатура и мышь</b><br>WASD — движение, Shift — спринт, мышь — камера (клик — захват курсора).<br>E / ЛКМ — действие, Q / ПКМ — второе действие.<br>Пробел — прыжок / лезть, C — присесть, F — бросок, Tab — предмет.<br>Esc — пауза, R — заново, F3 — отладка ИИ.</div>
      <div><b>Геймпад</b><br>Левый стик — движение, правый — камера. A — прыжок, X — действие, Y — второе, B — присесть, RT — бросок, LB — предмет, Start — пауза.</div>
      <div><b>Паркур</b><br>Беги на низкое препятствие — перелезешь сам. У стены удерживай ⤴ или упирайся — полезешь. На краю крыши на спринте — прыжок. С высоты при движении — перекат, в сено — без вреда.</div></div>
      <button data-a="${back}" class="pri">Назад</button>`);
  }
  pause() { if (this.mode !== 'play') return; this.mode = 'pause'; this.save(); this.ui.setHUD(false); if (document.pointerLockElement) document.exitPointerLock();
    this.ui.show(`<h1>Пауза</h1><p class="tip">${fmt(this.time)} · ${this.targetDown ? 'цель устранена — уходи' : 'цель жива'}</p><button data-a="resume" class="pri">Продолжить</button><button data-a="restart">Заново</button><button data-a="settings_p">Настройки</button><button data-a="help_p">Управление</button><button data-a="menu">Выйти в меню</button>`); }
  onUI(a) {
    this.audio.start(); this.input.allowLock = !MOBILE;
    if (a === 'brief') this.brief(); else if (a === 'menu') this.menu(); else if (a === 'start' || a === 'restart') { localStorage.removeItem('sc3d_save'); this.newMission(); }
    else if (a === 'continue') { const s = JSON.parse(localStorage.getItem('sc3d_save') || 'null'); this.newMission(s); }
    else if (a === 'settings') this.settingsUI('menu'); else if (a === 'settings_p') this.settingsUI('pauseback'); else if (a === 'help') this.help('menu'); else if (a === 'help_p') this.help('pauseback');
    else if (a === 'pauseback') { this.mode = 'play'; this.pause(); } else if (a === 'resume') { this.mode = 'play'; this.ui.hide(); this.ui.setHUD(true); }
  }
  // ---------- цикл ----------
  loop(now) {
    requestAnimationFrame(t => this.loop(t));
    const dt = Math.min(.1, (now - this.last) / 1000); this.last = now; this.ft = this.ft * .92 + dt * 1000 * .08;
    const inp = this.input.poll(dt, this.set.sens, this.set.invert);
    if (inp.restartPressed && (this.mode === 'fail' || this.mode === 'pause')) { localStorage.removeItem('sc3d_save'); this.newMission(); }
    if (inp.debugPressed) { this.set.debug = !this.set.debug; this.debugGroup.visible = this.set.debug; }
    if (this.mode === 'play') {
      if (inp.pausePressed) { this.pause(); }
      else {
        // фиксированный шаг симуляции 60 Гц (стабильная физика и ИИ независимо от FPS)
        this.acc += dt; const h = 1 / 60; let first = true, steps = 0;
        while (this.acc >= h && steps < 5) { this.sim(h, first ? inp : { ...inp, jumpPressed: false, actionPressed: false, altPressed: false, crouchPressed: false, throwPressed: false, itemPressed: false }); first = false; this.acc -= h; steps++; }
        if (steps === 5) this.acc = 0;
      }
    } else if (this.mode === 'fail' || this.mode === 'done') { for (const n of this.npcs) n.ch.update(dt); this.player.ch.update(dt); }
    if (this.mode !== 'boot' && this.mode !== 'menu') this.cam.update(dt, this.mode === 'play' ? inp : { lx: 0, ly: 0 }, this.set);
    else if (this.L) { const t = now / 1000; this.camera.position.set(Math.sin(t * .05) * 22, 9 + Math.sin(t * .1), 24 + Math.cos(t * .05) * 6); this.camera.lookAt(0, 7, -14); }
    if (this.cam.shake > 0) { this.cam.shake -= dt; this.camera.position.x += (Math.random() - .5) * this.cam.shake * .3; this.camera.position.y += (Math.random() - .5) * this.cam.shake * .3; }
    this.render(dt);
  }
  sim(dt, inp) {
    this.time += dt; const p = this.player; CLOTH.time.value = this.time;
    // контекстные действия (буфер ввода: нажатие во время короткой анимации не теряется)
    if (inp.actionPressed || inp.altPressed) this.buffer = { k: inp.actionPressed ? 'a' : 'b', t: .25 };
    const c = this.context(); this.ctx = c;
    if (this.buffer) { const f = c[this.buffer.k]; if (f) { f[1](); this.buffer = null; } else if ((this.buffer.t -= dt) <= 0) this.buffer = null; }
    if (inp.throwPressed) this.throwItem();
    if (inp.itemPressed) { p.item = p.item === 'stone' ? 'knife' : 'stone'; this.ui.items(p); }
    p.update(dt, inp); p.ch.update(dt);
    // смешивание с толпой
    p.blend = p.state === 'move' && !p.crouch && !p.carry && p.speed < 2.6 && this.npcs.some(n => n.kind === 'civ' && n.alive && n.state !== 'flee' && n.pos.distanceTo(p.pos) < 1.7);
    this.lightT = (this.lightT || 0) - dt; if (this.lightT <= 0) { this.lightT = .1; this.playerLight = lightAt(this.L, p.pos, this.npcs.filter(n => n.alive && n.o.lantern).map(n => n.pos)); }
    for (const n of this.npcs) n.update(dt);
    this.stepProj(dt); this.stepWorld(dt);
    this.audio.indoor = this.zoneAt(p.pos) && this.zoneAt(p.pos).id === 'tower' ? 1 : 0;
    // музыка по состоянию
    const conf = this.npcs.some(n => n.state === 'confirm'), srch = this.npcs.some(n => n.alive && n.state === 'search'), tense = this.npcs.some(n => n.alive && n.kind !== 'civ' && (n.state === 'investigate' || n.state === 'curious' || n.sus > .3));
    this.audio.setMood(conf ? 'alarm' : srch ? 'search' : tense ? 'tense' : 'calm');
    if (this.audio.crowd) this.audio.crowd.gain.value = .05;
    if ((this.saveT -= dt) <= 0) { this.saveT = 15; this.save(); }
  }
  render(dt) {
    if (!this.L) { return; }
    const p = this.player;
    if (this.mode === 'play' || this.mode === 'pause') {
      // HUD
      const c = this.ctx || {}; this.ui.prompts(c.a && c.a[0], c.b && c.b[0]);
      const z = this.zoneAt(p.pos), anyConf = this.npcs.some(n => n.state === 'confirm'), srch = this.npcs.some(n => n.alive && n.state === 'search');
      const st = anyConf ? ['! ТЕБЯ ЗАМЕТИЛИ — ДЕЙСТВУЙ', 'al'] : p.state === 'hidden' ? ['Укрыт', 'ok'] : srch ? ['Идёт обыск', 'sus'] : z && !(p.disguise && z.allow.includes(p.disguise)) ? ['Запретная зона', 'warn'] : p.pos.y > 2.3 && !z ? ['На крышах — дозорные начеку', 'warn'] : p.blend ? ['В толпе', 'ok'] : p.disguise ? ['Маскировка: звонарь', 'ok'] : this.playerLight > .55 ? ['На свету', ''] : ['В тени', 'ok'];
      this.ui.status(st[0], st[1]);
      this.ui.objective(this.targetDown ? '◆ Уходи: <b>ворота</b> на юге или <b>люк</b> на востоке' : `◆ Устранить: <b>Мастер Орлов</b>${this.target ? ` <span class="dim">${Math.round(this.target.pos.distanceTo(p.pos))} м</span>` : ''}`);
      this.ui.items(p); this.ui.indicators(this.camera);
      const tcv = this.mode === 'play' && this.input.touchMode; if (tcv !== this.tcv) { this.tcv = tcv; document.body.classList.toggle('touch', this.input.touchMode); document.querySelector('#tc').style.display = tcv ? 'block' : 'none'; }
    }
    // тень и «пул» фонарей следуют за игроком/камерой
    const m = this.moon; m.target.position.set(p.pos.x, 0, p.pos.z); m.position.set(p.pos.x + MOON_DIR.x * 40, MOON_DIR.y * 40, p.pos.z + MOON_DIR.z * 40);
    if ((this.poolT = (this.poolT || 0) - dt) <= 0) { this.poolT = .3; const cp = this.camera.position, ls = this.L.lamps.slice().sort((a, b) => a.pos.distanceToSquared(cp) - b.pos.distanceToSquared(cp));
      this.pool.forEach((l, i) => { const s = ls[i]; if (s) { l.position.copy(s.pos); l.position.y -= .2; l.intensity = 14 * (s.i || 1); l.distance = s.r * 1.3; } }); }
    // дальние NPC растворяются в тумане — не рисуем их (экономия скиннинга и draw calls)
    const cp = this.camera.position; for (const n of this.npcs) if (!n.hidden) n.ch.root.visible = n.carried || n.pos.distanceToSquared(cp) < 3000;
    this.audio.listen(this.camera);
    if (this.set.debug) this.dbgText();
    // адаптивное разрешение: держим ~60 FPS, на слабых устройствах снижаем внутреннее разрешение
    this.adT = (this.adT || 0) + dt;
    if (this.adT > 1.5 && this.mode === 'play') { this.adT = 0; if (this.ft > 22 && this.rs > .55) { this.rs -= .1; this.resize(); } else if (this.ft > 30 && this.set.quality === 'auto' && !this.degraded) { this.degrade(); } else if (this.ft < 14.5 && this.rs < 1) { this.rs = Math.min(1, this.rs + .05); this.resize(); } }
    if (this.composer) this.composer.render(dt); else this.renderer.render(this.scene, this.camera);
  }
  degrade() { // последняя ступень авто-качества: без теней и с одним источником света
    this.degraded = true; this.renderer.shadowMap.enabled = false; this.moon.castShadow = false;
    this.scene.traverse(o => { if (o.material) (Array.isArray(o.material) ? o.material : [o.material]).forEach(m => m.needsUpdate = true); });
    this.pool.slice(1).forEach(l => { l.intensity = 0; l.visible = false; }); this.pool.length = Math.min(1, this.pool.length);
    if (this.composer) { this.composer = null; } this.ui.toast('Графика упрощена для плавности');
  }
  dbgText() { const i = this.renderer.info, e = document.querySelector('#dbg'); e.style.display = 'block'; e.textContent = `${(1000 / this.ft).toFixed(0)} FPS · ${this.ft.toFixed(1)} мс · draw ${i.render.calls} · tris ${(i.render.triangles / 1000).toFixed(0)}k · res ×${this.rs.toFixed(2)} · свет ${this.playerLight.toFixed(2)} · NPC ${this.npcs.length}`; }
  debugNoise(pos, r) { const m = new THREE.Mesh(new THREE.RingGeometry(r - .1, r, 32), new THREE.MeshBasicMaterial({ color: 0x66ccff, transparent: true, opacity: .6, depthTest: false })); m.rotation.x = -PI / 2; m.position.set(pos.x, pos.y + .1, pos.z); this.debugGroup.add(m); setTimeout(() => m.removeFromParent(), 700); }
}
const L_bell = L => L.bell.state === 'hung' ? 'hung' : 'down';
const fmt = t => (t / 60 | 0) + ':' + String(t % 60 | 0).padStart(2, '0');

// Звонарь: живёт по расписанию, реагирует как горожанин (кричит, если видит убийство).
class Worker extends Civilian {
  constructor(g, o) { super(g, o); this.state = 'patrol'; }
  update(dt) {
    const g = this.g; this.bark -= dt;
    if (!this.alive || this.state === 'takedown') { this.ch.update(dt); return; }
    this.perceiveT -= dt; if (this.perceiveT <= 0) { this.perceiveT = .25; this.panicCheck(); }
    if (this.state === 'flee') { if (this.moveAlong(dt)) { this.state = 'patrol'; this.path = []; } this.ch.locomote(this.speed); }
    else this.routine(dt);
    this.pos.y = this.o.pos.y; this.ch.root.position.copy(this.pos); this.ch.root.rotation.y = this.yaw; this.ch.update(dt);
  }
}

window.game = new Game();
