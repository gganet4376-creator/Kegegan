// Готовит ассеты для мобильной сборки: качает CC0-текстуры Poly Haven, ужимает до 512px,
// вырезает нужные клипы из Quaternius Universal Animation Library (CC0) и убирает каналы пальцев.
import fs from 'node:fs';import path from 'node:path';import sharp from 'sharp';
import {NodeIO} from '@gltf-transform/core';import {prune,dedup,resample,meshopt} from '@gltf-transform/functions';import {MeshoptEncoder} from 'meshoptimizer';import {EXTMeshoptCompression,KHRMeshQuantization} from '@gltf-transform/extensions';
const OUT=path.resolve('assets'),CACHE=process.env.SC_CACHE||path.resolve('.cache');fs.mkdirSync(OUT,{recursive:true});fs.mkdirSync(CACHE,{recursive:true});
async function get(url,file){const f=path.join(CACHE,file);if(fs.existsSync(f)&&fs.statSync(f).size>1000)return f;const r=await fetch(url);if(!r.ok)throw new Error(url+' '+r.status);fs.writeFileSync(f,Buffer.from(await r.arrayBuffer()));return f}
const TEX={ground:'cobblestone_floor_08',plaster:'white_plaster_rough_02',stone:'castle_wall_slates',roof:'clay_roof_tiles_02',wood:'brown_planks_05'};
for(const [k,id] of Object.entries(TEX)){for(const [m,suf] of [['c','diff'],['n','nor_gl']]){
 const f=await get(`https://dl.polyhaven.org/file/ph-assets/Textures/jpg/1k/${id}/${id}_${suf}_1k.jpg`,`${id}_${suf}.jpg`);
 await sharp(f).resize(512,512).jpeg({quality:m==='c'?80:85,mozjpeg:true}).toFile(path.join(OUT,`${k}_${m}.jpg`));}}
const UAL='https://media.githubusercontent.com/media/series-ai/jam-ready-assets/main/';
const KEEP1=['Idle_Loop','Walk_Loop','Jog_Fwd_Loop','Sprint_Loop','Crouch_Idle_Loop','Crouch_Fwd_Loop','Jump_Start','Jump_Loop','Jump_Land','Roll','Death01','Hit_Chest','Hit_Head','Interact','Idle_Torch_Loop','Idle_Talking_Loop','Walk_Formal_Loop','Sword_Attack','Punch_Cross','PickUp_Table'];
const KEEP2=['ClimbUp_1m','Walk_Carry_Loop','OverhandThrow','LayToIdle','Melee_Hook','Idle_Lantern_Loop','Idle_FoldArms_Loop','Hit_Knockback','NinjaJump_Land','Idle_No_Loop'];
const io=new NodeIO().registerExtensions([EXTMeshoptCompression,KHRMeshQuantization]).registerDependencies({'meshopt.encoder':MeshoptEncoder});
async function cut(src,keep,keepMesh,out){const doc=await io.read(src);const root=doc.getRoot();
 for(const a of root.listAnimations()){if(!keep.includes(a.getName())){a.listChannels().forEach(c=>c.dispose());a.listSamplers().forEach(s=>s.dispose());a.dispose();continue}
  for(const ch of a.listChannels()){const n=ch.getTargetNode()&&ch.getTargetNode().getName()||'',tp=ch.getTargetPath();if(/index|middle|pinky|ring|thumb|leaf/.test(n)||tp==='scale'||(tp==='translation'&&!/^(root|pelvis)$/.test(n))){const s=ch.getSampler();ch.dispose();s.dispose()}}}
 if(!keepMesh)for(const n of root.listNodes()){if(n.getMesh()){n.getMesh().dispose();n.setMesh(null)}if(n.getSkin())n.setSkin(null)}
 await doc.transform(resample({tolerance:5e-4}),dedup(),prune({keepLeaves:true,keepAttributes:false}));await MeshoptEncoder.ready;await doc.transform(meshopt({encoder:MeshoptEncoder,level:'medium'}));
 await io.write(out,doc);console.log(path.basename(out),(fs.statSync(out).size/1024|0)+' KB',root.listAnimations().map(a=>a.getName()).join(','))}
await cut(await get(UAL+'quaternius-universal-animation-library/3D/characters/UAL1_Standard.glb','UAL1.glb'),KEEP1,true,path.join(OUT,'char.glb'));
await cut(await get(UAL+'quaternius-universal-animation-library-2/3D/characters/UAL2_Standard.glb','UAL2.glb'),KEEP2,false,path.join(OUT,'anim2.glb'));
for(const f of fs.readdirSync(OUT))console.log(f,(fs.statSync(path.join(OUT,f)).size/1024|0)+'KB');
