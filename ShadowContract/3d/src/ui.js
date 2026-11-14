import * as THREE from 'three';
// Интерфейс: DOM-оверлей + 2D-холст для индикаторов обнаружения (дуги у краёв экрана, как в Hitman).
const $ = s => document.querySelector(s);
export class UI {
  constructor(game) {
    this.g = game; this.subs = []; this.once = {};
    this.hud = $('#hud'); this.scr = $('#screen'); this.cv = $('#ind'); this.cx = this.cv.getContext('2d');
    this.scr.addEventListener('click', e => { const b = e.target.closest('[data-a]'); if (!b) return; this.g.audio.start(); this.g.audio.sfx('ui'); this.g.onUI(b.dataset.a, b); });
    this.resize(); addEventListener('resize', () => this.resize());
  }
  resize() { const d = Math.min(2, devicePixelRatio || 1); this.cv.width = innerWidth * d; this.cv.height = innerHeight * d; this.cv.style.width = innerWidth + 'px'; this.cv.style.height = innerHeight + 'px'; this.dpr = d; }
  progress(v, msg) { $('#loadbar i').style.width = (v * 100 | 0) + '%'; if (msg) $('#loadmsg').textContent = msg; }
  hideLoad() { const l = $('#load'); l.classList.add('out'); setTimeout(() => l.remove(), 600); }
  show(html, cls = '') { this.scr.className = 'on ' + cls; this.scr.innerHTML = '<div class="panel">' + html + '</div>'; document.body.classList.add('menu'); }
  hide() { this.scr.className = ''; this.scr.innerHTML = ''; document.body.classList.remove('menu'); }
  setHUD(v) { this.hud.style.display = v ? 'block' : 'none'; $('#tc').style.display = v && this.g.input.touchMode ? 'block' : 'none'; }
  status(text, cls) { const e = $('#status'); if (e.textContent !== text) e.textContent = text; e.className = cls || ''; }
  objective(t) { const e = $('#obj'); if (e.innerHTML !== t) e.innerHTML = t; }
  items(p) { $('#item').innerHTML = (p.item === 'stone' ? '🪨 Камень <b>∞</b>' : '🗡 Нож <b>' + p.knives + '</b>'); }
  prompts(a, b) {
    const pa = $('#pa'), pb = $('#pb'), ba = $('#b-action'), bb = $('#b-alt');
    const kb = !this.g.input.touchMode;
    pa.textContent = a ? (kb ? 'E / ЛКМ — ' : '') + a : ''; pb.textContent = b ? (kb ? 'Q / ПКМ — ' : '') + b : '';
    ba.classList.toggle('dim', !a); ba.querySelector('span').textContent = a || '⚔'; bb.style.display = b ? 'flex' : 'none'; bb.querySelector('span').textContent = b || '';
  }
  subtitle(who, text, pos) {
    if (!this.g.set.subs) return; const d = pos ? pos.distanceTo(this.g.player.pos) : 0; if (d > 28) return;
    const e = $('#subs'); const line = document.createElement('div'); line.innerHTML = `<b>${who}:</b> ${text}`; e.appendChild(line);
    while (e.children.length > 3) e.firstChild.remove(); setTimeout(() => line.remove(), 3200);
  }
  toast(t) { const e = $('#toast'); e.textContent = t; e.classList.remove('on'); void e.offsetWidth; e.classList.add('on'); }
  hintOnce(k, t) { if (this.once[k]) return; this.once[k] = 1; this.toast(t); }
  drawIcon(x, lv, st) {
    x.clearRect(0, 0, 64, 64); const r = 22, c = 32;
    const col = st === 'confirm' ? '#ff3b3b' : st === 'search' ? '#ff7a2f' : lv > .55 ? '#ffa23a' : '#ffe27a';
    x.lineWidth = 6; x.strokeStyle = 'rgba(0,0,0,.55)'; x.beginPath(); x.arc(c, c, r, 0, 7); x.stroke();
    const f = st === 'confirm' ? lv - 2 : lv;
    x.strokeStyle = col; x.beginPath(); x.arc(c, c, r, -Math.PI / 2, -Math.PI / 2 + Math.PI * 2 * Math.min(1, f)); x.stroke();
    x.fillStyle = col; x.font = '900 30px system-ui'; x.textAlign = 'center'; x.textBaseline = 'middle'; x.fillText(st === 'confirm' ? '!' : st === 'search' ? '!?' : '?', c, c + 2);
  }
  // дуги направлений к подозревающим NPC
  indicators(cam) {
    const x = this.cx, W = this.cv.width, H = this.cv.height, d = this.dpr; x.clearRect(0, 0, W, H);
    const g = this.g, p = g.player, v = new THREE.Vector3();
    for (const n of g.npcs) {
      if (!n.alive || n.kind === 'civ' || n.sus < .04 && n.state !== 'search') continue;
      const dx = n.pos.x - p.pos.x, dz = n.pos.z - p.pos.z, a = Math.atan2(dx, dz) - g.cam.yaw; // 0 = впереди камеры
      const lv = n.state === 'confirm' ? 1 : Math.min(1, n.sus), col = n.state === 'confirm' ? '255,59,59' : lv > .55 ? '255,150,50' : '255,226,122';
      const R = Math.min(W, H) * .28, ang = -a - Math.PI / 2, w = .22;
      x.lineWidth = 7 * d; x.strokeStyle = 'rgba(0,0,0,.35)'; x.beginPath(); x.arc(W / 2, H / 2, R, ang - w, ang + w); x.stroke();
      x.strokeStyle = `rgba(${col},${.35 + lv * .65})`; x.beginPath(); x.arc(W / 2, H / 2, R, ang - w * lv, ang + w * lv); x.stroke();
    }
    // маркер цели
    const t = g.target; if (t && t.alive && g.set.marker) {
      v.set(t.pos.x, t.pos.y + 2.6, t.pos.z).project(cam);
      if (v.z < 1 && Math.abs(v.x) < 1.1 && Math.abs(v.y) < 1.1) { const sx = (v.x + 1) / 2 * W, sy = (1 - v.y) / 2 * H; x.fillStyle = '#d4af37'; x.font = `${20 * d}px system-ui`; x.textAlign = 'center'; x.fillText('◆', sx, sy); x.font = `600 ${11 * d}px system-ui`; x.fillText(Math.round(t.pos.distanceTo(p.pos)) + ' м', sx, sy + 14 * d); }
    }
    for (const e of g.L.exits) if (g.targetDown) { v.copy(e.pos).setY(e.pos.y + 1.5).project(cam); if (v.z < 1) { const sx = (v.x + 1) / 2 * W, sy = (1 - v.y) / 2 * H; x.fillStyle = '#7CFFB2'; x.font = `${18 * d}px system-ui`; x.textAlign = 'center'; x.fillText('⬢', Math.max(20, Math.min(W - 20, sx)), Math.max(30, Math.min(H - 30, sy))); } }
  }
}
