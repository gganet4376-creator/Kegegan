const {chromium}=require('playwright');const SP=process.argv[2];
(async()=>{const b=await chromium.launch();const pg=await b.newPage({viewport:{width:1100,height:620}});const errs=[];pg.on('pageerror',e=>errs.push(e.message));
await pg.goto('file://'+SP+'/dbg.html');const e=c=>pg.evaluate(c=>__g(c),c);const w=ms=>pg.waitForTimeout(ms);await e("P.q=0;setQ(0);0");
const gw=async(sec,cond)=>{const t0=await e('now');for(let i=0;i<600;i++){await w(30);if(cond&&await e(cond))return true;if(await e('now')-t0>=sec)return!cond}return false};
const ok=(k,v,i)=>console.log((v?'PASS ':'FAIL ')+k+(i?' — '+i:''));
const T="G.filter(function(g){return g.ty==='t'})[0]";
// Orlov: street -> tower door -> top behind him -> takedown
await e("P.lv=1;startMission();T.s=T.f=1;var d=DR[0];G.forEach(function(g){if(g.el===0){g.dead=1;g.hid=1}});H.x=d.x1;H.h=0;0");
await pg.keyboard.press('KeyJ');await gw(.8);const top=await e("H.h");await pg.keyboard.down('KeyA');await gw(2,"ctx().k==='kill'&&ctx().g.ty==='t'");await pg.keyboard.up('KeyA');await pg.keyboard.press('KeyJ');await gw(1,"M.how");
ok('Орлов: дверь в основании → вершина → со спины',top===560&&/stab|shove/.test(await e("M.how")),'h='+top+' how='+await e("M.how")+' mode='+await e("mode"));
// Orlov: knife the bell rope from the neighbouring roof
await e("P.lv=1;startMission();var p=PL.filter(function(q){return q.k==='b'&&q.st!=='tower'}).pop();G.forEach(function(g){if(g.ty!=='t'){g.dead=1;g.hid=1}});"+T+".stay=1;"+T+".fT=99;H.x=p.x+p.w-20;H.h=p.top;H.f=1;0");
await pg.keyboard.press('KeyL');await gw(3,"M.how");ok('Орлов: нож в трос колокола с соседней крыши',await e("M.how")==='accident',await e("M.how"));
// Frost: tunnel exit behind him -> takedown
await e("P.lv=3;startMission();T.s=T.f=1;var d=DR[0];G.forEach(function(g){if(g.ty!=='t'){g.dead=1;g.hid=1}});H.x=d.x1;0");
await pg.keyboard.press('KeyJ');await gw(.8);await pg.keyboard.down('KeyS');await pg.keyboard.down('KeyA');await gw(6,"ctx().k==='kill'||mode!=='play'");await pg.keyboard.up('KeyA');await pg.keyboard.up('KeyS');await pg.keyboard.press('KeyJ');await gw(1,"M.how");
ok('Фрост: тоннель → выход за спиной → тихо',/stab|hide/.test(await e("M.how"))&&await e("M.used.tunnel"),await e("M.how+' mode='+mode"));
// Frost: icicles
await e("P.lv=3;startMission();G.forEach(function(g){if(g.ty!=='t'){g.dead=1;g.hid=1}});var r=TR[0];H.x=r.x-380;H.f=1;0");await pg.keyboard.press('KeyL');await gw(3,"M.how");ok('Фрост: сосульки',await e("M.how")==='accident',await e("M.how"));
// Sultan: caravanserai door -> roof -> dive
await e("P.lv=2;startMission();T.s=T.f=1;var t="+T+";G.forEach(function(g){if(g!==t){g.dead=1;g.hid=1}});t.stay=1;t.fT=99;t.f=-1;t.x=3170;var d=DR[0];H.x=d.x1-6;H.h=0;0");
await pg.keyboard.press('KeyJ');await gw(.8);const rh=await e("H.h");await pg.keyboard.down('KeyA');await gw(1.5,"!H.gr");await pg.keyboard.press('KeyJ');await pg.keyboard.up('KeyA');await gw(2,"M.how");
ok('Султан: дверь караван-сарая → крыша → прыжок сверху',rh===220&&await e("M.how")==='air','roof='+rh+' how='+await e("M.how"));
// Sultan: from the crowd (blend) behind him
await e("P.lv=2;startMission();T.s=T.f=1;var t="+T+";G.forEach(function(g){if(g!==t){g.dead=1;g.hid=1}});t.stay=1;t.fT=99;t.f=1;t.x=2500;CR[1].x=2440;CR[1].sc=0;CR[1].a=2300;CR[1].b=2460;H.x=2440;H.h=0;0");await gw(.3);
const bl=await e("H.blend");await pg.keyboard.down('KeyD');await gw(2,"ctx().k==='kill'");await pg.keyboard.up('KeyD');await pg.keyboard.press('KeyJ');await gw(1,"M.how");
ok('Султан: из толпы со спины',bl&&await e("M.how==='stab'&&M.blend===1"),'blend='+bl+' how='+await e("M.how")+' M.blend='+await e("M.blend"));
// King: gate door + chandelier knife
await e("P.lv=4;startMission();G.forEach(function(g){if(g.ty!=='t'){g.dead=1;g.hid=1}});var d=DR.filter(function(d){return d.x1===3684})[0];H.x=d.x1;0");await pg.keyboard.press('KeyJ');await gw(.8);
const thru=await e("H.x>3840");await e("H.x=4200;H.f=1;0");await pg.keyboard.press('KeyL');await gw(3,"M.how");
ok('Король: ворота внутренней стены → люстра ножом',thru&&await e("M.how")==='accident','through='+thru+' how='+await e("M.how"));
await e("P.lv=4;startMission();G.forEach(function(g){if(g.ty!=='t'){g.dead=1;g.hid=1}});var d=DR.filter(function(d){return d.k==='tunnel'})[0];H.x=d.x1;0");await pg.keyboard.press('KeyJ');await gw(.8);
ok('Король: тайный ход от стога',await e("H.x>4000&&M.used.tunnel"));
console.log('errors',errs);await b.close()})();
