// Единый ввод: сенсор (плавающий джойстик + обзор свайпом + контекстные кнопки), клавиатура+мышь
// (Pointer Lock), геймпад (Gamepad API). На выходе — один «снимок намерений» на кадр.
export class Input {
  constructor(canvas, layer) {
    this.mx = 0; this.my = 0; this.lx = 0; this.ly = 0; this.held = {}; this.pressed = {}; this.keys = {}; this.touchMode = false;
    this.joy = null; this.look = null; this.canvas = canvas;
    const base = this.joyBase = document.createElement('div'), knob = this.joyKnob = document.createElement('div');
    base.className = 'joy'; knob.className = 'joyk'; base.appendChild(knob); layer.appendChild(base);
    const opt = { passive: false };
    layer.addEventListener('touchstart', e => this.ts(e), opt); layer.addEventListener('touchmove', e => this.tm(e), opt);
    layer.addEventListener('touchend', e => this.te(e), opt); layer.addEventListener('touchcancel', e => this.te(e), opt);
    addEventListener('keydown', e => { if (e.repeat) return; this.keys[e.code] = true; const b = KEY[e.code]; if (b) { this.press(b); if (e.code === 'Space' || e.code === 'Tab') e.preventDefault(); } });
    addEventListener('keyup', e => { this.keys[e.code] = false; const b = KEY[e.code]; if (b) this.release(b); });
    layer.addEventListener('mousedown', e => { if (this.touchMode) return; if (document.pointerLockElement !== canvas && this.allowLock) canvas.requestPointerLock && canvas.requestPointerLock(); else if (e.button === 0) this.press('action'); else if (e.button === 2) this.press('alt'); });
    layer.addEventListener('mouseup', e => { if (e.button === 0) this.release('action'); if (e.button === 2) this.release('alt'); });
    layer.addEventListener('contextmenu', e => e.preventDefault());
    addEventListener('mousemove', e => { if (document.pointerLockElement === canvas) { this.lx += e.movementX * .0028; this.ly += e.movementY * .0028; } });
    addEventListener('blur', () => { this.keys = {}; this.held = {}; });
  }
  bindButton(el, name) {
    const dn = e => { e.preventDefault(); e.stopPropagation(); this.touchMode = true; el.classList.add('on'); this.press(name); };
    const up = e => { e.preventDefault(); el.classList.remove('on'); this.release(name); };
    el.addEventListener('touchstart', dn, { passive: false }); el.addEventListener('touchend', up, { passive: false }); el.addEventListener('touchcancel', up, { passive: false });
    el.addEventListener('mousedown', e => { e.stopPropagation(); dn(e); }); el.addEventListener('mouseup', up); el.addEventListener('mouseleave', () => { el.classList.remove('on'); this.release(name); });
  }
  press(n) { if (!this.held[n]) this.pressed[n] = true; this.held[n] = true; }
  release(n) { this.held[n] = false; }
  ts(e) {
    e.preventDefault(); this.touchMode = true;
    for (const t of e.changedTouches) {
      if (!this.joy && t.clientX < innerWidth * .45) { this.joy = { id: t.identifier, x: t.clientX, y: t.clientY, dx: 0, dy: 0 }; this.joyBase.style.display = 'block'; this.joyBase.style.left = t.clientX + 'px'; this.joyBase.style.top = t.clientY + 'px'; this.joyKnob.style.transform = 'translate(-50%,-50%)'; }
      else if (!this.look) this.look = { id: t.identifier, x: t.clientX, y: t.clientY };
    }
  }
  tm(e) {
    e.preventDefault();
    for (const t of e.changedTouches) {
      if (this.joy && t.identifier === this.joy.id) {
        let dx = t.clientX - this.joy.x, dy = t.clientY - this.joy.y; const R = 58, d = Math.hypot(dx, dy);
        if (d > R) { this.joy.x += dx - dx / d * R; this.joy.y += dy - dy / d * R; this.joyBase.style.left = this.joy.x + 'px'; this.joyBase.style.top = this.joy.y + 'px'; dx = dx / d * R; dy = dy / d * R; }
        this.joy.dx = dx / R; this.joy.dy = dy / R; this.joyKnob.style.transform = `translate(calc(-50% + ${dx}px),calc(-50% + ${dy}px))`;
      } else if (this.look && t.identifier === this.look.id) { this.lx += (t.clientX - this.look.x) * .0062; this.ly += (t.clientY - this.look.y) * .0062; this.look.x = t.clientX; this.look.y = t.clientY; }
    }
  }
  te(e) {
    for (const t of e.changedTouches) {
      if (this.joy && t.identifier === this.joy.id) { this.joy = null; this.joyBase.style.display = 'none'; }
      if (this.look && t.identifier === this.look.id) this.look = null;
    }
  }
  poll(dt, sens = 1, invert = false) {
    let mx = 0, my = 0;
    if (this.joy) { mx = this.joy.dx; my = this.joy.dy; }
    const k = this.keys; mx += (k.KeyD || k.ArrowRight ? 1 : 0) - (k.KeyA || k.ArrowLeft ? 1 : 0); my += (k.KeyS || k.ArrowDown ? 1 : 0) - (k.KeyW || k.ArrowUp ? 1 : 0);
    if (!this.joy && (mx || my) && !k.ShiftLeft && !k.ShiftRight) { const l = Math.hypot(mx, my); mx = mx / l * .8; my = my / l * .8; } // клавиатура: бег трусцой
    let lx = this.lx, ly = this.ly; this.lx = this.ly = 0;
    const gp = navigator.getGamepads ? [...navigator.getGamepads()].find(g => g && g.connected) : null;
    if (gp) {
      const dz = v => Math.abs(v) < .15 ? 0 : v; mx += dz(gp.axes[0]); my += dz(gp.axes[1]); lx += dz(gp.axes[2] || 0) * dt * 2.6; ly += dz(gp.axes[3] || 0) * dt * 2;
      const map = [[0, 'jump'], [2, 'action'], [1, 'crouch'], [3, 'alt'], [7, 'throw'], [4, 'item'], [9, 'pause'], [10, 'sprint'], [5, 'throw']];
      this.gpPrev = this.gpPrev || {};
      for (const [i, n] of map) { const v = gp.buttons[i] && gp.buttons[i].pressed; if (v && !this.gpPrev[i]) this.press(n); if (!v && this.gpPrev[i]) this.release(n); this.gpPrev[i] = v; }
    }
    const s = { mx, my, lx: lx * sens, ly: ly * sens * (invert ? -1 : 1), sprint: !!(this.held.sprint || k.ShiftLeft || k.ShiftRight),
      jumpHeld: !!this.held.jump, jumpPressed: !!this.pressed.jump, actionPressed: !!this.pressed.action, altPressed: !!this.pressed.alt,
      crouchPressed: !!this.pressed.crouch, throwPressed: !!this.pressed.throw, itemPressed: !!this.pressed.item, pausePressed: !!this.pressed.pause, debugPressed: !!this.pressed.debug, restartPressed: !!this.pressed.restart };
    this.pressed = {}; return s;
  }
}
const KEY = { Space: 'jump', KeyE: 'action', KeyF: 'throw', KeyQ: 'alt', KeyC: 'crouch', ControlLeft: 'crouch', Tab: 'item', Escape: 'pause', KeyP: 'pause', F3: 'debug', KeyR: 'restart', KeyX: 'alt' };
