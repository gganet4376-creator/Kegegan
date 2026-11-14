// Мобильный тест: эмуляция Pixel 7 (сенсор, DPR 2.6), меню касаниями, джойстик и кнопки через TouchEvent.
const {chromium,devices}=require('playwright');const path=require('path');
const OUT=process.argv[2]||'/tmp';const URL='file://'+path.resolve(__dirname,'../dist/index.html');
(async()=>{const b=await chromium.launch({args:['--use-gl=angle','--use-angle=swiftshader','--enable-unsafe-swiftshader']});
const ctx=await b.newContext({...devices['Pixel 7']});const pg=await ctx.newPage();const errs=[];pg.on('pageerror',e=>errs.push(e.message));
await pg.addInitScript(()=>localStorage.setItem('sc3d_set',JSON.stringify({quality:'low'})));
await pg.goto(URL);await pg.waitForFunction(()=>window.game&&game.mode==='menu',null,{timeout:150000});
let pass=0,fail=0;const ok=(k,v,i)=>{v?pass++:fail++;console.log((v?'PASS ':'FAIL ')+k+(i!==undefined?' — '+i:''))};const e=f=>pg.evaluate(f);
ok('M1 загрузка на телефоне, сенсорный режим',await e(()=>game.input.touchMode===true));
await pg.tap('[data-a=brief]');await pg.waitForTimeout(300);await pg.tap('[data-a=start]');await pg.waitForTimeout(1500);
ok('M2 миссия запущена касаниями',await e(()=>game.mode==='play'));
ok('M3 сенсорные кнопки видны сразу',await e(()=>getComputedStyle(document.querySelector('#tc')).display==='block'));
await pg.screenshot({path:OUT+'/m_hud.png'});
// джойстик: TouchEvent на левой половине
const touch=(type,id,x,y)=>pg.evaluate(([type,id,x,y])=>{const el=document.querySelector('#touch');const t=new Touch({identifier:id,target:el,clientX:x,clientY:y});el.dispatchEvent(new TouchEvent(type,{touches:type==='touchend'?[]:[t],changedTouches:[t],bubbles:true,cancelable:true}))},[type,id,x,y]);
const W=await e(()=>innerWidth),H=await e(()=>innerHeight);const z0=await e(()=>game.player.pos.z);
await touch('touchstart',1,W*.2,H*.7);for(let i=1;i<=6;i++){await touch('touchmove',1,W*.2,H*.7-i*9);await pg.waitForTimeout(30)}
const t0=await e(()=>game.time);while(await e(()=>game.time)-t0<1.5)await pg.waitForTimeout(60);
await touch('touchend',1,W*.2,H*.7-54);const z1=await e(()=>game.player.pos.z);
ok('M4 джойстик двигает героя',z1<z0-1.5,(z0-z1).toFixed(2)+' м');
// свайп камеры правой половиной
const y0=await e(()=>game.cam.yaw);await touch('touchstart',2,W*.75,H*.4);for(let i=1;i<=5;i++){await touch('touchmove',2,W*.75+i*20,H*.4);await pg.waitForTimeout(20)}await touch('touchend',2,W*.75+100,H*.4);
await pg.waitForTimeout(200);ok('M5 свайп поворачивает камеру',Math.abs(await e(()=>game.cam.yaw)-y0)>.2);
// кнопка действия: оглушить подозревающего охранника касанием
await e(()=>{const g=game.npcs.find(n=>n.o.name==='Привратник');game.npcs.forEach(n=>{if(n!==g&&n.kind!=='target'){n.dead=true;n.hidden=true;n.ch.root.visible=false}});g.sus=.6;g.state='curious';g.timer=9;g.lkp=g.pos.clone();const p=game.player;p.pos.set(11.3,0,-25);p.yaw=-Math.PI/2;game.cam.yaw=-Math.PI/2;game.cam.snap()});
await pg.waitForTimeout(400);const lab=await pg.textContent('#b-alt span');
const bb=await pg.$('#b-alt');const box=await bb.boundingBox();await pg.evaluate(([x,y])=>{const el=document.querySelector('#b-alt');const t=new Touch({identifier:5,target:el,clientX:x,clientY:y});el.dispatchEvent(new TouchEvent('touchstart',{touches:[t],changedTouches:[t],bubbles:true,cancelable:true}));setTimeout(()=>el.dispatchEvent(new TouchEvent('touchend',{touches:[],changedTouches:[t],bubbles:true,cancelable:true})),80)},[box.x+box.width/2,box.y+box.height/2]);
const t1=await e(()=>game.time);while(await e(()=>game.time)-t1<2)await pg.waitForTimeout(60);
ok('M6 кнопка «'+lab+'» на экране оглушает подозревающего',await e(()=>game.npcs.find(n=>n.o.name==='Привратник').ko&&game.mode==='play'));
await pg.screenshot({path:OUT+'/m_ko.png'});
ok('M7 нет ошибок JS',errs.length===0,JSON.stringify(errs.slice(0,3)));
console.log(`ИТОГО (мобильный): ${pass} пройдено, ${fail} провалено`);await b.close()})();
