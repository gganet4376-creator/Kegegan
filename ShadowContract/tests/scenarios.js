const {chromium,devices}=require('playwright');
const SP=process.argv[2];
(async()=>{const b=await chromium.launch();const pg=await b.newPage({viewport:{width:1100,height:620}});
const errs=[];pg.on('pageerror',e=>errs.push(e.message));pg.on('console',m=>{if(m.type()==='error')errs.push(m.text())});
await pg.goto('file://'+SP+'/dbg.html');const e=c=>pg.evaluate(c=>__g(c),c);const w=ms=>pg.waitForTimeout(ms);
const R={};await e("P.q=0;setQ(0);0");
const gw=async(sec,cond)=>{const t0=await e('now');for(let i=0;i<600;i++){await w(30);if(cond&&await e(cond))return true;if(await e('now')-t0>=sec)return!cond}return false};const ok=(k,v,info)=>{R[k]=v;console.log((v?'PASS ':'FAIL ')+k+(info?' — '+info:''))};
const start=async lv=>{await e(`P.lv=${lv};startMission();T.s=T.f=1;0`);await w(150)};
// isolate guard helper: move all other guards far away
const iso="function iso(g){G.forEach(function(o){if(o!==g&&!o.dead){o.dead=1;o.hid=1}})}";
await e(iso.replace('function iso','window.iso=function'));
// 1. KO from behind
await start(0);await e("var g=G[0];iso(g);g.x=800;g.st='patrol';g.stay=1;g.f=1;g.fT=99;g.det=0;H.x=g.x-60;H.h=0;0");
await pg.keyboard.down('KeyS');await w(150);await pg.keyboard.press('KeyJ');await gw(.8);await pg.keyboard.up('KeyS');
ok('1 оглушение сзади',await e("G[0].dead&&G[0].ko&&!M.det"),await e("'dead='+G[0].dead+' ko='+G[0].ko+' det='+M.det+' mode='+mode"));
// 2. suspicious guard facing hero -> takedown works
await start(0);await e("var g=G[0];iso(g);g.x=800;g.st='sus';g.det=.6;g.sT=9;g.look=5;g.stay=1;H.x=g.x-55;H.h=0;g.lx=H.x;g.f=-1;0");
await pg.keyboard.press('KeyJ');await gw(.8);
ok('2 устранение при подозрении (лицом к лицу)',await e("G[0].dead===1&&!G[0].ko&&!M.det&&mode==='play'"),await e("'st='+G[0].st+' dead='+G[0].dead+' mode='+mode"));
// 3. suspicion decays when out of sight
await start(0);await e("var g=G[0];iso(g);g.x=800;g.st='patrol';g.stay=1;g.fT=99;g.f=-1;H.x=g.x-200;H.h=0;0");
await gw(.7);const d1=await e("G[0].det");await e("H.x=G[0].x+400;0");await gw(8);
ok('3 подозрение спадает вне поля зрения',d1>0.2&&await e("G[0].det<0.05&&G[0].st==='patrol'&&!M.det"),'det '+d1.toFixed(2)+' -> '+(await e("G[0].det")).toFixed(2)+', st='+await e("G[0].st"));
// 4. real alarm -> immediate failure screen
await start(0);await e("var g=G[0];iso(g);g.x=800;g.stay=1;g.fT=99;g.f=-1;H.x=g.x-120;H.h=0;0");await gw(5,"mode==='dead'");
ok('4 тревога = поражение',await e("mode==='dead'")&&(await pg.textContent('#ov')).includes('Обнаружен'),await e("mode+' det='+M.det"));
await pg.screenshot({path:SP+'/shots/fail.png'});
// 7. restart resets everything (after kills, alarm, open door, trap cut)
await e("TR[0].st=2;DR[0].open=5;G[1].dead=1");await pg.keyboard.press('KeyR');await w(60);
ok('7 рестарт сбрасывает уровень',await e("mode==='play'&&!M.det&&G.every(function(g){return!g.dead&&g.st==='patrol'})&&TR.every(function(r){return r.st===0})&&DR.every(function(d){return!d.open})&&H.x===440&&M.t<1"),await e("G.filter(function(g){return g.dead}).length+' dead, mode '+mode"));
// 5. Baron (port) by 4 routes
// 5a zip from warehouse roof -> attack from cable
await start(0);await e("G.forEach(function(g){if(g.ty!=='t'){g.dead=1;g.hid=1}});var t=G.filter(function(g){return g.ty==='t'})[0];t.stay=1;t.x=3700;t.fT=99;t.f=1;var z=ZP[1];H.x=z.x1-12;H.h=z.p.top;H.gr=true;0");
await pg.keyboard.press('Space');await gw(3,"H.st==='zip'&&H.x>3420");await pg.keyboard.press('KeyJ');await gw(2,"M.how");
const ra=await e("M.how");ok('5a Барон: крыша склада → трос → удар сверху',ra==='zip',ra);
// 5b knife cuts crane rope -> accident
await start(0);await e("G.forEach(function(g){if(g.ty!=='t'){g.dead=1;g.hid=1}});var t=G.filter(function(g){return g.ty==='t'})[0];t.stay=1;t.x=3900;t.fT=99;H.x=3150;H.h=320;H.f=1;0");
await pg.keyboard.press('KeyL');await gw(2.5,"M.how");const rb=await e("M.how");ok('5b Барон: нож в трос крана → несчастный случай',rb==='accident',rb);
// 5c real traversal: ground -> double-jump -> climb hull -> sneak behind -> takedown
await start(0);await e("G.forEach(function(g){if(g.ty!=='t'){g.dead=1;g.hid=1}});var t=G.filter(function(g){return g.ty==='t'})[0];t.a=3830;t.b=3940;H.x=3262;H.h=0;H.f=1;0");
await pg.keyboard.down('KeyD');await w(60);await pg.keyboard.down('Space');await w(330);await pg.keyboard.up('Space');await w(60);await pg.keyboard.down('Space');await w(300);await pg.keyboard.up('Space');
await gw(4,"H.h>=150&&H.gr");await pg.keyboard.up('KeyD');
const onDeck=await e("H.h===150");
await pg.keyboard.down('KeyS');await pg.keyboard.down('KeyD');await gw(3,"H.hide");await pg.keyboard.up('KeyD');
await gw(15,"var t=G.filter(function(g){return g.ty==='t'})[0];t.f===1&&t.mv&&t.x<3860");
await pg.keyboard.up('KeyS');await pg.keyboard.down('KeyD');
await gw(5,"var c=ctx();c.k==='kill'||mode!=='play'");await pg.keyboard.press('KeyJ');await pg.keyboard.up('KeyD');await gw(1.5,"M.how");
const rc=await e("M.how");ok('5c Барон: прыжок с причала, подъём по борту, тихо со спины',onDeck&&/stab|hide|shove/.test(rc),'onDeck='+onDeck+' how='+rc+' mode='+await e("mode"));
// 5d knife to the head from roof
await start(0);await e("G.forEach(function(g){if(g.ty!=='t'){g.dead=1;g.hid=1}});TR[0].st=2;var t=G.filter(function(g){return g.ty==='t'})[0];t.stay=1;t.x=3700;t.fT=99;t.f=-1;H.x=3200;H.h=320;H.f=1;0");
await pg.keyboard.press('KeyL');await gw(2,"M.how");const rd=await e("M.how");ok('5d Барон: метательный нож с крыши',/knife|shatter/.test(rd),rd);
// 6. remaining methods
await start(1);await e("var g=G.filter(function(g){return g.el>0&&g.ty==='g'})[0];iso(g);g.stay=1;g.fT=99;g.f=1;var p=PL.filter(function(q){return q.k==='b'&&q.top===g.el&&g.x>=q.x&&g.x<=q.x+q.w})[0];g.x=p.x+p.w-60;H.x=g.x-60;H.h=g.el;window._g=g;0");
await pg.keyboard.press('KeyJ');await gw(1.5,"M.st.shove");ok('6 сброс с крыши',await e("_g.dead&&M.st.shove===1"),await e("Object.keys(M.st).join()"));
await start(0);await e("var g=G[0];iso(g);g.x=800;g.stay=1;g.fT=99;g.f=1;H.x=g.x-60;0");await pg.keyboard.down('KeyS');await w(100);await pg.keyboard.press('KeyJ');await gw(.8);
await pg.keyboard.press('KeyJ');await gw(.2);const carried=await e("!!H.carry");
await e("var s=HS[0];H.x=s.x;0");await gw(.15);await pg.keyboard.press('KeyJ');await gw(.2);await pg.keyboard.up('KeyS');
ok('6 перенос и укрытие тела',carried&&await e("G[0].hid===1&&!H.carry"),'carried='+carried);
await start(0);await e("H.x=DR[0].x1;H.h=0;0");await pg.keyboard.press('KeyJ');await gw(.7);ok('6 дверь через склад',await e("Math.abs(H.x-DR[0].x2)<2&&DR[0].open>0"),await e("H.x+''"));
await start(3);await e("var d=DR[0];G.forEach(function(g){if(g.ty!=='t'){g.dead=1;g.hid=1}});H.x=d.x1;0");await pg.keyboard.press('KeyJ');await gw(.7);ok('6 тоннель (лёд)',await e("Math.abs(H.x-DR[0].x2)<2&&M.used.tunnel"));
await start(4);await e("var d=DR.filter(function(d){return d.k==='vent'})[0];G.forEach(function(g){g.dead=1;g.hid=1});H.x=d.x1;H.h=d.h1;0");await pg.keyboard.press('KeyJ');await gw(.7);ok('6 вентиляция (крепость)',await e("var d=DR.filter(function(d){return d.k==='vent'})[0];Math.abs(H.x-d.x2)<2&&H.h===0"));
await start(0);await e("var g=G[0];iso(g);g.x=800;g.stay=1;g.fT=99;g.f=1;H.x=g.x-250;H.h=0;H.f=1;0");await pg.keyboard.down('KeyS');await w(100);await pg.keyboard.press('KeyL');await pg.keyboard.up('KeyS');await gw(1.5);
ok('6 камень отвлекает (расследование)',await e("G[0].st==='sus'||G[0].st==='search'")&&!(await e("M.det")),await e("G[0].st+' lx='+(G[0].lx|0)+' x='+(G[0].x|0)"));
await start(2);await e("var t=G.filter(function(g){return g.ty==='t'})[0];G.forEach(function(g){if(g!==t){g.dead=1;g.hid=1}});t.stay=1;t.x=2600;t.fT=99;H.x=2300;H.h=0;H.f=1;0");await pg.keyboard.press('KeyL');await gw(2.5,"M.how");
ok('6 ловушка рынка (бак)',await e("M.how==='accident'"),await e("M.how"));
await start(0);await e("window._b=G[2];_b.bud=G[3];G[3].x=_b.x+300;var g=_b.bud;g.dead=1;g.hid=1;g.dT=now-10;G.forEach(function(x){if(x!==g&&x!==_b){x.dead=1;x.hid=1}});H.x=100;0");await gw(.4);
ok('6 охранник замечает пропажу напарника',await e("_b&&(_b.st==='sus'||_b.st==='search')"),await e("_b?_b.st:'no buddy'"));
await start(0);await e("var g=G[2],v=G[3];G.forEach(function(o){if(o!==g&&o!==v){o.dead=1;o.hid=1}});v.x=v.lo+30;g.dead=1;g.ko=1;g.found=0;g.x=v.x+120;g.el=0;g.lo=v.lo;g.hi=v.hi;v.f=1;v.stay=1;v.fT=99;v.det=0;v.st='patrol';H.x=100;window._v=v;window._k=g;0");await gw(7,"_k.dead===0");
ok('6 найденное тело → обыск, оглушённого будят',await e("M.bf>=1&&_k.dead===0"),await e("'bf='+M.bf+' kdead='+_k.dead+' st='+_v.st"));
// screenshots per level + timing
for(let lv=0;lv<5;lv++){await start(lv);await pg.keyboard.down('KeyD');await w(1200);await pg.keyboard.up('KeyD');await pg.screenshot({path:SP+'/shots/q_'+lv+'.png'})}
console.log('Q',await e("Q+' GL='+!!GL"));
// fps estimate in headless
await start(2);const f=await pg.evaluate(()=>new Promise(r=>{let n=0,t0=performance.now();function s(){n++;if(performance.now()-t0<2000)requestAnimationFrame(s);else r(n/2)}requestAnimationFrame(s)}));console.log('headless fps ~',f,'Q now',await e("Q"));
console.log('errors',errs);
// 8. Android emulation
const ctx=await b.newContext({...devices['Pixel 7']});const mp=await ctx.newPage();const merr=[];mp.on('pageerror',x=>merr.push(x.message));
await mp.goto('file://'+SP+'/dbg.html');await mp.tap('button[data-a=play]');await mp.tap('button[data-a=go]');await mp.waitForTimeout(400);
const m=c=>mp.evaluate(c=>__g(c),c);const x0=await m("H.x");
const br=await mp.$('#br');const bb=await br.boundingBox();await mp.touchscreen.tap(bb.x+bb.width/2,bb.y+bb.height/2);
await mp.dispatchEvent('#br','pointerdown');await mp.waitForTimeout(700);await mp.dispatchEvent('#br','pointerup');
await mp.screenshot({path:SP+'/shots/android.png'});
ok('8 Android: касание кнопок двигает героя',(await m("H.x"))>x0+30,'x '+x0+' -> '+(await m("H.x")|0)+' Q='+await m("Q"));
console.log('mobile errors',merr);
await b.close()})();
