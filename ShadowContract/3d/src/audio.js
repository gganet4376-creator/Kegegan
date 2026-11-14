// Звук: Web Audio, всё синтезируется на лету (0 байт ассетов — важно для загрузки на телефоне).
// Позиционные источники через PannerNode (HRTF на ПК, equalpower на телефоне), шины музыки/эффектов/голоса,
// реверберация в помещениях, адаптивная музыка по состоянию миссии.
export class Audio {
  constructor(settings) { this.s = settings; this.ac = null; this.mood = 'calm'; this.indoor = 0; }
  start() {
    if (this.ac) { if (this.ac.state === 'suspended') this.ac.resume(); return; }
    try { this.ac = new (window.AudioContext || window.webkitAudioContext)(); } catch (e) { return; }
    const ac = this.ac, mk = (g, to) => { const n = ac.createGain(); n.gain.value = g; n.connect(to); return n; };
    this.comp = ac.createDynamicsCompressor(); this.comp.threshold.value = -14; this.comp.connect(ac.destination);
    this.master = mk(1, this.comp); this.music = mk(.5, this.master); this.sfxB = mk(1, this.master); this.voiceB = mk(1, this.master); this.amb = mk(.6, this.master);
    // реверберация: сгенерированный импульс
    const len = ac.sampleRate * 1.6 | 0, ir = ac.createBuffer(2, len, ac.sampleRate);
    for (let c = 0; c < 2; c++) { const d = ir.getChannelData(c); for (let i = 0; i < len; i++) d[i] = (Math.random() * 2 - 1) * Math.pow(1 - i / len, 3); }
    this.verb = ac.createConvolver(); this.verb.buffer = ir; this.verbSend = mk(.12, this.verb); this.verb.connect(this.master);
    this.noiseBuf = ac.createBuffer(1, ac.sampleRate * 2, ac.sampleRate); const nd = this.noiseBuf.getChannelData(0); for (let i = 0; i < nd.length; i++) nd[i] = Math.random() * 2 - 1;
    this.mobile = matchMedia('(pointer:coarse)').matches;
    this.applyVolumes(); this.startAmbience(); this.mt = ac.currentTime + .1; this.step16 = 0; setInterval(() => this.sched(), 100);
  }
  applyVolumes() { if (!this.ac) return; const s = this.s; this.master.gain.value = s.vol; this.music.gain.value = s.music * .55; this.sfxB.gain.value = s.sfx; this.voiceB.gain.value = s.sfx; }
  listen(cam) {
    if (!this.ac) return; const L = this.ac.listener, p = cam.position, f = cam.getWorldDirection(this._f || (this._f = cam.position.clone()));
    if (L.positionX) { const t = this.ac.currentTime; L.positionX.setTargetAtTime(p.x, t, .02); L.positionY.setTargetAtTime(p.y, t, .02); L.positionZ.setTargetAtTime(p.z, t, .02); L.forwardX.setTargetAtTime(f.x, t, .02); L.forwardY.setTargetAtTime(f.y, t, .02); L.forwardZ.setTargetAtTime(f.z, t, .02); }
    else { L.setPosition(p.x, p.y, p.z); L.setOrientation(f.x, f.y, f.z, 0, 1, 0); }
    this.verbSend.gain.setTargetAtTime(.1 + this.indoor * .5, this.ac.currentTime, .3);
  }
  out(pos, bus) {
    const ac = this.ac, b = bus || this.sfxB; if (!pos) return b;
    const p = ac.createPanner(); p.panningModel = this.mobile ? 'equalpower' : 'HRTF'; p.distanceModel = 'inverse'; p.refDistance = 2; p.rolloffFactor = 1.3; p.maxDistance = 60;
    if (p.positionX) { p.positionX.value = pos.x; p.positionY.value = pos.y + 1; p.positionZ.value = pos.z; } else p.setPosition(pos.x, pos.y + 1, pos.z);
    p.connect(b); p.connect(this.verbSend); return p;
  }
  nz(dur, vol, freq, type, dst, t, q = 1) { const ac = this.ac, s = ac.createBufferSource(), f = ac.createBiquadFilter(), g = ac.createGain(); s.buffer = this.noiseBuf; f.type = type || 'bandpass'; f.frequency.value = freq; f.Q.value = q; g.gain.setValueAtTime(vol, t); g.gain.exponentialRampToValueAtTime(.0008, t + dur); s.connect(f); f.connect(g); g.connect(dst); s.start(t, Math.random() * 1.5); s.stop(t + dur + .05); }
  tone(type, f0, f1, dur, vol, dst, t, att = .005) { const ac = this.ac, o = ac.createOscillator(), g = ac.createGain(); o.type = type; o.frequency.setValueAtTime(f0, t); if (f1) o.frequency.exponentialRampToValueAtTime(f1, t + dur); g.gain.setValueAtTime(.0001, t); g.gain.linearRampToValueAtTime(vol, t + att); g.gain.exponentialRampToValueAtTime(.0008, t + dur); o.connect(g); g.connect(dst); o.start(t); o.stop(t + dur + .05); }
  step(surf, pos, v) {
    if (!this.ac || this.s.vol <= 0) return; const t = this.ac.currentTime, o = this.out(pos), r = .9 + Math.random() * .2;
    if (surf === 'wood') { this.nz(.07, .5 * v, 650 * r, 'bandpass', o, t, 2); this.tone('triangle', 140 * r, 90, .06, .18 * v, o, t); }
    else if (surf === 'tile') { this.nz(.05, .45 * v, 2300 * r, 'bandpass', o, t, 3); this.tone('square', 900 * r, 0, .02, .05 * v, o, t); }
    else if (surf === 'hay') this.nz(.15, .4 * v, 1500, 'highpass', o, t);
    else { this.nz(.045, .55 * v, 1500 * r, 'bandpass', o, t, 1.5); this.nz(.08, .25 * v, 300, 'lowpass', o, t); }
    if (v > .6) this.nz(.12, .08 * v, 3500, 'highpass', o, t + .02); // шорох одежды
  }
  sfx(n, pos, v = 1) {
    if (!this.ac) return; const t = this.ac.currentTime, o = this.out(pos);
    const S = {
      jump: () => this.nz(.12, .25, 1200, 'bandpass', o, t), land: () => this.nz(.1, .5, 400, 'lowpass', o, t), landHard: () => { this.nz(.25, .9, 260, 'lowpass', o, t); this.tone('sine', 90, 40, .25, .5, o, t); },
      roll: () => this.nz(.4, .45, 700, 'bandpass', o, t), mantle: () => { this.nz(.18, .35, 900, 'bandpass', o, t); this.nz(.1, .3, 2400, 'highpass', o, t + .15); }, grab: () => this.nz(.1, .4, 1800, 'bandpass', o, t, 2), climb: () => this.nz(.08, .25, 1300 + Math.random() * 500, 'bandpass', o, t, 2),
      blade: () => { this.tone('sawtooth', 2400, 5200, .12, .12, o, t); this.nz(.06, .3, 6000, 'highpass', o, t); }, stab: () => { this.nz(.14, .7, 900, 'bandpass', o, t, 1.2); this.tone('sine', 160, 70, .18, .45, o, t); },
      punch: () => { this.nz(.08, .9, 600, 'lowpass', o, t); this.tone('sine', 120, 50, .12, .7, o, t); }, bodyfall: () => { this.nz(.3, .9 * v, 220, 'lowpass', o, t); this.nz(.15, .4 * v, 900, 'bandpass', o, t + .05); },
      throw: () => this.nz(.15, .35, 2600, 'bandpass', o, t), stone: () => { this.tone('square', 1300, 700, .05, .2, o, t); this.nz(.06, .6, 2600, 'bandpass', o, t, 3); this.tone('square', 1100, 650, .04, .1, o, t + .09); },
      knifeHit: () => { this.nz(.08, .6, 1500, 'bandpass', o, t); this.tone('sine', 300, 120, .1, .3, o, t); }, door: () => { this.tone('sawtooth', 70, 55, .5, .1, o, t, .08); this.nz(.08, .3, 600, 'bandpass', o, t + .4); },
      hay: () => this.nz(.45, .55, 2200, 'highpass', o, t), bell: () => { for (const [f, d] of [[220, 6], [440, 4.5], [523, 3.5], [660, 3], [880, 2]]) this.tone('sine', f, 0, d, .25, o, t, .002); },
      crash: () => { this.nz(1.2, 1, 300, 'lowpass', o, t); for (const f of [233, 466, 699, 931]) this.tone('sine', f, f * .97, 4, .22, o, t, .001); this.nz(.4, .9, 1800, 'bandpass', o, t); },
      alarmWind: () => { this.tone('sawtooth', 300, 520, .6, .16, o, t, .1); }, pickup: () => this.nz(.3, .35, 700, 'bandpass', o, t), disguise: () => { this.nz(.5, .3, 1800, 'bandpass', o, t); this.nz(.4, .2, 900, 'bandpass', o, t + .3); },
      ui: () => this.tone('sine', 880, 1200, .07, .08, this.sfxB, t), fail: () => { this.tone('sawtooth', 220, 110, 1.4, .25, this.sfxB, t); this.tone('sawtooth', 233, 116, 1.4, .2, this.sfxB, t); },
      done: () => { for (const [i, f] of [392, 494, 587, 784].entries()) this.tone('triangle', f, 0, 1.4, .14, this.sfxB, t + i * .12); },
    };
    (S[n] || (() => { }))();
  }
  // «голос»: формантная огибающая без слов, текст — в субтитрах
  voice(kind, pos) {
    if (!this.ac) return; const t = this.ac.currentTime, o = this.out(pos, this.voiceB), base = kind === 'scream' ? 520 : kind === 'confirm' ? 210 : 140 + Math.random() * 30;
    const n = kind === 'confirm' ? 3 : kind === 'scream' ? 2 : 2 + (Math.random() * 2 | 0);
    for (let i = 0; i < n; i++) {
      const s = t + i * .16, oc = this.ac.createOscillator(), f = this.ac.createBiquadFilter(), g = this.ac.createGain();
      oc.type = 'sawtooth'; oc.frequency.setValueAtTime(base * (1 + Math.random() * .2), s); oc.frequency.linearRampToValueAtTime(base * (kind === 'curious' && i === n - 1 ? 1.35 : .9), s + .14);
      f.type = 'bandpass'; f.frequency.value = 700 + Math.random() * 900; f.Q.value = 4; g.gain.setValueAtTime(.0001, s); g.gain.linearRampToValueAtTime(kind === 'confirm' || kind === 'scream' ? .5 : .28, s + .02); g.gain.exponentialRampToValueAtTime(.001, s + .15);
      oc.connect(f); f.connect(g); g.connect(o); oc.start(s); oc.stop(s + .2);
    }
  }
  startAmbience() {
    const ac = this.ac, mk = (freq, type, vol, pos) => { const s = ac.createBufferSource(), f = ac.createBiquadFilter(), g = ac.createGain(); s.buffer = this.noiseBuf; s.loop = true; f.type = type; f.frequency.value = freq; g.gain.value = vol; s.connect(f); f.connect(g); g.connect(pos ? this.out(pos, this.amb) : this.amb); s.start(); return g; };
    this.wind = mk(380, 'lowpass', .18); this.fountainG = mk(2400, 'bandpass', .5, { x: 0, y: 0, z: 2 }); this.crowd = mk(500, 'bandpass', .0, { x: 0, y: 0, z: 0 });
  }
  setMood(m) { this.mood = m; }
  sched() {
    const ac = this.ac; if (!ac) return; const t0 = ac.currentTime; if (this.mt < t0) this.mt = t0 + .05;
    this.wind.gain.setTargetAtTime(.12 + .08 * Math.sin(t0 * .3), t0, 1);
    const m = this.mood, bpm = m === 'alarm' ? 140 : m === 'search' ? 112 : m === 'tense' ? 96 : 72, root = 45;
    while (this.mt < t0 + .35) {
      const i = this.step16 % 16, t = this.mt, bar = this.step16 >> 4, mus = this.music;
      if (m !== 'done') {
        if (i === 0) this.tone('triangle', mf(root + [0, -2, -4, -5][bar % 4]), 0, 60 / bpm * 4, .16, mus, t, .4);
        if (i === 0 && bar % 2 === 0) this.tone('sine', mf(root + 12 + [7, 5, 3, 2][bar % 4]), 0, 60 / bpm * 3.5, .05, mus, t, .8);
        if (m !== 'calm') { if (i % 4 === 0) this.tone('sine', 70, 38, .25, m === 'tense' ? .25 : .45, mus, t); if (i % 2 === 1) this.nz(.03, .05 + (m === 'search' ? .05 : 0), 7000, 'highpass', mus, t); }
        if (m === 'search' || m === 'alarm') { if (i % 4 === 2) this.tone('sawtooth', mf(root + 12 + (i === 6 ? 1 : 0)), 0, .14, .06, mus, t); }
        if (m === 'calm' && i === 8 && Math.random() < .5) this.tone('sine', mf(root + 24 + [0, 3, 7, 10, 12][Math.random() * 5 | 0]), 0, 1.6, .025, mus, t, .3);
      }
      this.mt += 60 / bpm / 4; this.step16++;
    }
  }
}
const mf = m => 440 * Math.pow(2, (m - 69) / 12);
