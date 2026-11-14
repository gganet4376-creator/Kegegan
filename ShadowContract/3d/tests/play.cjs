// Интерактивные тесты геймплея (Playwright + Chromium, WebGL через SwiftShader).
// Запуск: node tests/play.cjs [папка_для_скриншотов]
const {chromium,devices}=require('playwright');const path=require('path');
const OUT=process.argv[2]||'/tmp';const URL='file://'+path.resolve(__dirname,'../dist/index.html');
(async()=>{
const b=await chromium.launch({args:['--use-gl=angle','--use-angle=swiftshader','--enable-unsafe-swiftshader']});
const pg=await b.newPage({viewport:{width:960,height:540}});const errs=[];
pg.on('pageerror',e=>errs.push('PE '+e.message));pg.on('console',m=>{if(m.type()==='error')errs.push('E '+m.text())});
await pg.addInitScript(()=>localStorage.setItem('sc3d_set',JSON.stringify({quality:'low',subs:true})));
await pg.goto(URL);await pg.waitForFunction(()=>window.game&&game.mode==='menu',null,{timeout:120000});
const e=f=>pg.evaluate(f);const R={};let pass=0,fail=0;
const ok=(k,v,i)=>{v?pass++:fail++;console.log((v?'PASS ':'FAIL ')+k+(i!==undefined?' — '+i:''))};
const gw=async(sec,cond)=>{const t0=await e(()=>game.time);for(let i=0;i<2000;i++){await pg.waitForTimeout(40);if(cond&&await pg.evaluate(cond))return true;if(await e(()=>game.time)-t0>=sec)return !cond}return false};
const start=async()=>{await e(()=>{localStorage.removeItem('sc3d_save');game.newMission();game.ui.once={}});await gw(.2)};
const tp=(x,y,z,yaw)=>pg.evaluate(([x,y,z,yaw])=>{const p=game.player;p.pos.set(x,y,z);p.vel.set(0,0,0);p.onGround=true;p.peakY=y;p.state='move';if(yaw!==undefined){p.yaw=yaw;game.cam.yaw=yaw}game.cam.snap()},[x,y,z,yaw]);
const calm=()=>e(()=>game.npcs.forEach(n=>{if(n.kind!=='civ'&&n.kind!=='target'&&n.kind!=='worker'){n.dead=true;n.hidden=true;n.ch.root.visible=false;if(n.icon)n.icon.visible=false}}));
const shot=n=>pg.screenshot({path:OUT+'/'+n+'.png'});
const st=()=>e(()=>({x:+game.player.pos.x.toFixed(2),y:+game.player.pos.y.toFixed(2),z:+game.player.pos.z.toFixed(2),s:game.player.state,m:game.mode}));
// 1. ходьба и камера
await start();const p0=await st();await pg.keyboard.down('KeyW');await gw(1.5);await pg.keyboard.up('KeyW');const p1=await st();
ok('01 движение вперёд (W)',p1.z<p0.z-2,JSON.stringify(p1));await shot('t01_walk');
// 2. стены: упираемся в дом без прыжка — не проходим насквозь
await start();await calm();await tp(-13.8,0,-6,-Math.PI/2);await pg.keyboard.down('KeyW');await gw(1.5);await pg.keyboard.up('KeyW');
let s=await st();ok('02 коллизия со стеной дома (не прошёл внутрь)',s.x>-15.1||s.y>1,JSON.stringify(s));
// 3. лазание по фасаду и подтягивание на крышу (H4, крыша 7 м)
await start();await calm();await tp(-14.5,0,-8,-Math.PI/2);await pg.keyboard.down('KeyW');await pg.keyboard.down('Space');
const climbed=await gw(12,()=>game.player.pos.y>6.9&&game.player.state==='move');await pg.keyboard.up('Space');await pg.keyboard.up('KeyW');
s=await st();ok('03 лазание по фасаду + подтягивание на крышу',climbed&&s.y>6.9,JSON.stringify(s));await shot('t03_roof');
// 4. перелезание низкого препятствия (ящики у площади 0.9 м)
await start();await calm();await tp(10.6,0,-5,Math.PI/2);await pg.keyboard.down('KeyW');const mant=await gw(3,()=>game.player.state==='mantle'||game.player.pos.y>.8);await gw(1.2);await pg.keyboard.up('KeyW');
ok('04 перелезание через ящики',mant,JSON.stringify(await st()));
// 5. спуск с крыши с перекатом (7 м, на бегу)
await start();await calm();await tp(-16,7,-4.6,Math.PI/2);await pg.keyboard.down('KeyW');await pg.keyboard.down('ShiftLeft');
const roll=await gw(4,()=>game.player.lastLand==='roll');await pg.keyboard.up('ShiftLeft');await pg.keyboard.up('KeyW');await gw(.8);
s=await st();ok('05 прыжок с крыши на спринте → перекат, без урона',roll&&s.y<.1&&(await e(()=>game.player.hp))>=100,JSON.stringify(s));
// 6. прыжок веры в сено
await start();await calm();await tp(-17.6,9.5,-1);await e(()=>{game.player.onGround=false;game.player.peakY=9.5});await gw(1.5,()=>game.player.state==='hidden');
ok('06 прыжок веры в телегу с сеном',(await st()).s==='hidden');
// 7. подозрение не равно провалу и убывает вне видимости
await start();const g7=await e(()=>{const g=game.npcs.find(n=>n.o.name==='Привратник');g.sus=.4;g.lkp=game.player.pos.clone();g.enter('curious');return g.npcs});
await gw(.3);const m7=await e(()=>game.mode);await gw(8,()=>{const g=game.npcs.find(n=>n.o.name==='Привратник');return g.sus<.05});
ok('07 подозрение без поражения и спадает',m7==='play'&&await e(()=>game.npcs.find(n=>n.o.name==='Привратник').sus<.06),await e(()=>{const g=game.npcs.find(n=>n.o.name==='Привратник');return g.state+' '+g.sus.toFixed(2)}));
// 8. убийство подозревающего охранника лицом к лицу
await start();await e(()=>{const g=game.npcs.find(n=>n.o.name==='Привратник');game.npcs.forEach(n=>{if(n!==g&&n.kind!=='target'){n.dead=true;n.hidden=true;n.ch.root.visible=false}});g.sus=.7;g.lkp=g.pos.clone();g.state='curious';g.timer=5;});
await tp(11.3,0,-25,-Math.PI/2);await e(()=>{const g=game.npcs.find(n=>n.o.name==='Привратник');g.yaw=Math.PI/2});await gw(.15);
const ctx8=await e(()=>game.ctx&&game.ctx.a&&game.ctx.a[0]);await pg.keyboard.press('KeyE');await gw(1.4);
ok('08 «Убить» работает при подозрении (лицом к лицу)',await e(()=>game.npcs.find(n=>n.o.name==='Привратник').dead&&game.mode==='play'),'кнопка: '+ctx8);await shot('t08_td');
// 9. прерывание тревоги: охранник уже кричит (confirm), оглушаем — тревоги нет
await start();await e(()=>{const g=game.npcs.find(n=>n.o.name==='Привратник');game.npcs.forEach(n=>{if(n!==g&&n.kind!=='target'){n.dead=true;n.hidden=true;n.ch.root.visible=false}});g.sus=1;g.enter('confirm');});
await tp(11.4,0,-25,-Math.PI/2);await gw(.1);await pg.keyboard.press('KeyQ');await gw(2.2);
ok('09 оглушение во время крика «Тревога» срывает тревогу',await e(()=>game.mode==='play'&&game.npcs.find(n=>n.o.name==='Привратник').ko),await e(()=>game.mode));
// 10. настоящая тревога = мгновенное поражение
await start();await e(()=>{const g=game.npcs.find(n=>n.o.name==='Привратник');g.sus=1});await gw(3,()=>game.mode==='fail');await pg.waitForTimeout(1100);
ok('10 тревога → экран поражения',await e(()=>game.mode==='fail')&&(await pg.textContent('#screen')).includes('Обнаружен'));await shot('t10_fail');
// 11. рестарт полностью сбрасывает
await e(()=>{game.L.bell.state='down';game.L.doors[0].target=1});await pg.keyboard.press('KeyR');await gw(.3);
ok('11 рестарт (R) сбрасывает охрану/колокол/двери',await e(()=>game.mode==='play'&&game.npcs.every(n=>n.alive&&n.sus<.05)&&game.L.bell.state==='hung'&&game.L.doors.every(d=>d.target===0)&&game.player.pos.z>24),await e(()=>JSON.stringify({m:game.mode,dead:game.npcs.filter(n=>!n.alive).length,sus:Math.max(...game.npcs.map(n=>n.sus)),bell:game.L.bell.state,doors:game.L.doors.map(d=>d.target),z:game.player.pos.z})));
// ---- маршруты к Орлову ----
const T=()=>e(()=>({dead:game.target.dead,m:game.method,mode:game.mode}));
const tgtAt=(x,z,face)=>pg.evaluate(([x,z,face])=>{const t=game.target;t.pos.set(x,12,z);t.yaw=face;t.route=null;t.o.pos=t.pos.clone();t.o.yaw=face;t.state='post';t.path=[]},[x,z,face]);
// 12a. колокол: отпустить верёвку во дворе, пока Орлов под колоколом
await start();await calm();await tgtAt(0,-15.7,Math.PI);await tp(1.6,0,-21.4,Math.PI);await gw(.2);const c12=await e(()=>game.ctx&&game.ctx.a&&game.ctx.a[0]);await pg.keyboard.press('KeyE');await gw(3,()=>game.target.dead);
let t=await T();ok('12a Орлов: колокол (несчастный случай)',t.dead&&t.m==='accident','кнопка: '+c12+' '+JSON.stringify(t));
// 12b. удар сверху с крыши дома купца
await start();await calm();await tgtAt(-3,-16,-Math.PI/2);await tp(-5.8,15,-16,Math.PI/2);await gw(.2);const c13=await e(()=>game.ctx&&game.ctx.a&&game.ctx.a[0]);await pg.keyboard.press('KeyE');await gw(2.5,()=>game.target.dead);
t=await T();ok('12b Орлов: прыжок с крыши купца, удар сверху',t.dead&&t.m==='air','кнопка: '+c13+' '+JSON.stringify(t));await shot('t12b_air');
// 12c. лазание по башне (северная грань) и удар со спины
await start();await calm();await tgtAt(0,-12.9,0);await tp(0,0,-20.8,0);await pg.keyboard.down('KeyW');await pg.keyboard.down('Space');
const up=await gw(20,()=>game.player.pos.y>=11.9&&game.player.state==='move');await pg.keyboard.up('Space');
await gw(4,()=>game.ctx&&game.ctx.victim===game.target);await pg.keyboard.up('KeyW');await pg.keyboard.press('KeyE');await gw(2,()=>game.target.dead);
t=await T();ok('12c Орлов: подъём по фасаду башни → клинок со спины',up&&t.dead&&/stab|front/.test(t.m),'поднялся='+up+' '+JSON.stringify(t)+' '+JSON.stringify(await st()));await shot('t12c_tower');
// 12d. дверь → лестница внутри → люк → со спины
await start();await calm();await tgtAt(0,-12.9,0);await tp(0,0,-11.2,Math.PI);await gw(.2);await pg.keyboard.press('KeyE');await gw(1);
await pg.keyboard.down('KeyW');const lad=await gw(25,()=>game.player.pos.y>=11.9&&game.player.state==='move');await pg.keyboard.up('KeyW');await e(()=>{game.cam.yaw=0});await pg.keyboard.down('KeyW');await gw(3,()=>game.ctx&&game.ctx.victim===game.target);await pg.keyboard.up('KeyW');await pg.keyboard.press('KeyE');await gw(2,()=>game.target.dead);
t=await T();ok('12d Орлов: дверь башни → лестница → люк → клинок',lad&&t.dead,'наверху='+lad+' '+JSON.stringify(t)+' '+JSON.stringify(await st()));
// 12e. метательный нож с крыши купца
await start();await calm();await tgtAt(-2.9,-16,-Math.PI/2);await tp(-6,15,-16,Math.PI/2);await e(()=>{game.player.item='knife';game.cam.pitch=.45});await gw(.1);await pg.keyboard.press('KeyF');await gw(2,()=>game.target.dead);
t=await T();ok('12e Орлов: метательный нож',t.dead&&t.m==='knife',JSON.stringify(t));
// 12f. нож в верёвку колокола
await start();await calm();await tgtAt(0,-15.7,Math.PI);await tp(-8.3,3.2,-24.5,1.17);await e(()=>{game.player.item='knife';game.cam.pitch=.0;game.target.dead=false});
await e(()=>{const t=game.target;t.ch.root.visible=true});await e(()=>{const n=game.target;game.npcs.forEach(()=>{})});
await e(()=>{game.player.knives=3});await e(()=>{game.target.o.pos=game.target.pos.clone()});
await e(()=>{game.target.state='post'});await e(()=>{});
await e(()=>{game.npcs.forEach(n=>{})});
await e(()=>{const t=game.target;t.sight=0});await gw(.1);await pg.keyboard.press('KeyF');await gw(3,()=>game.target.dead);
t=await T();ok('12f Орлов: нож в верёвку → колокол',t.dead&&t.m==='accident',JSON.stringify(t));
// 12g. маскировка: оглушить звонаря, переодеться, пройти двор без подозрений
await start();await e(()=>{game.npcs.forEach(n=>{if(n.kind!=='civ'&&n.kind!=='worker'){n._p=n.perceive;n.perceive=()=>{}}});const w=game.worker;w.pos.set(12,0,-25);w.route=null;w.o.pos=w.pos.clone();w.state='post'});await tp(13,0,-25,-Math.PI/2);await gw(.1);
await pg.keyboard.press('KeyQ');await gw(1.5);await pg.keyboard.press('KeyQ');await gw(1.6);await e(()=>{const w=game.worker;w.hidden=true;w.ch.root.visible=false;game.npcs.forEach(n=>{if(n._p){n.perceive=n._p;delete n._p}})});
const dis=await e(()=>game.player.disguise);await tp(3,0,-25,Math.PI/2);await gw(4);
const sus=await e(()=>Math.max(...game.npcs.filter(n=>n.alive&&n.kind==='guard').map(n=>n.sus)));
ok('12g Маскировка звонаря: двор без подозрений',dis==='worker'&&sus<.25&&await e(()=>game.mode==='play'),'disguise='+dis+' maxSus='+sus.toFixed(2));await shot('t12g_disguise');
// 13. без маскировки во дворе — подозрение растёт
await start();await tp(3,0,-25,Math.PI/2);await gw(1.5);ok('13 без маскировки двор = нарушение (подозрение растёт)',await e(()=>game.npcs.some(n=>n.kind==='guard'&&n.sus>.1)));
// 14. перенос тела и укрытие
await start();await calm();await e(()=>{const w=game.worker;w.pos.set(-1,0,-24);w.route=null;w.o.pos=w.pos.clone();w.state='post';w.down('ko')});await tp(-.2,0,-24,-Math.PI/2);await gw(.3);
await pg.keyboard.press('KeyE');await gw(1);const car=await e(()=>!!game.player.carry);await tp(-1.6,0,-21.3,0);await gw(.2);await pg.keyboard.press('KeyE');await gw(.5);
ok('14 перенос тела и укрытие в сено',car&&await e(()=>game.worker.hidden),'нёс='+car);
// 15. камень отвлекает охранника у двери
await start();await e(()=>{const g=game.npcs[0];game.npcs.forEach(n=>{if(n!==g&&n.kind!=='target'){n.dead=true;n.hidden=true}})});await tp(-6,0,-9,Math.PI/2);await e(()=>{game.cam.pitch=.1});await gw(.3);await pg.keyboard.press('KeyF');await gw(6,()=>game.npcs[0].state!=='post');
ok('15 камень → охранник уходит с поста проверить',await e(()=>{const g=game.npcs[0];return g.state==='investigate'||g.state==='curious'||g.pos.distanceTo(g.o.pos)>1}),await e(()=>game.npcs[0].state));
// 16. тело находят → обыск
await start();await e(()=>{const w=game.worker;w.pos.set(-3,0,-6);w.route=null;w.o.pos=w.pos.clone();w.down('dead');const g=game.npcs[1];g.pos.set(-3,0,1);g.yaw=Math.PI;g.route=null;g.o.pos=g.pos.clone();g.state='post';g.o.yaw=Math.PI});await gw(2);
ok('16 найденное тело → обыск и созыв охраны',await e(()=>game.npcs.some(n=>n.state==='search')&&game.stats.bodies>0));
// 17. сохранение/восстановление
await start();await tp(-12,0,0);await e(()=>{game.npcs[1].down('ko')});await gw(.2);await e(()=>game.save());await e(()=>game.newMission(JSON.parse(localStorage.getItem('sc3d_save'))));await gw(.2);
ok('17 сохранение и восстановление миссии',await e(()=>Math.abs(game.player.pos.x+12)<.5&&game.npcs[1].ko));
// 18. завершение миссии через выход
await start();await e(()=>{game.targetKilled('stab');game.target.down('dead')});await tp(20,0,0);await gw(.2);await pg.keyboard.press('KeyE');await gw(.5);
ok('18 отход через канализацию → экран итогов',await e(()=>game.mode==='done'));await pg.waitForTimeout(500);await shot('t18_done');
console.log('ERRORS',JSON.stringify(errs.slice(0,10)));console.log(`ИТОГО: ${pass} пройдено, ${fail} провалено`);await b.close()})();
