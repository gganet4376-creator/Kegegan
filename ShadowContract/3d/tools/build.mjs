// Сборка в один HTML: esbuild (Three.js + модули игры, tree-shaking, minify) + ассеты base64.
// Один файл — чтобы игра открывалась на телефоне прямо из загрузок/мессенджера, без сервера.
import * as esbuild from 'esbuild';import fs from 'node:fs';
const r=await esbuild.build({entryPoints:['src/main.js'],bundle:true,minify:true,format:'iife',target:['es2020','chrome90','safari15'],write:false,legalComments:'none'});
const js=r.outputFiles[0].text;const assets={};for(const f of fs.readdirSync('assets'))assets[f]=fs.readFileSync('assets/'+f).toString('base64');
let html=fs.readFileSync('src/index.html','utf8');
html=html.replace('/*ASSETS*/',()=>JSON.stringify(assets)).replace('/*GAME*/',()=>js.replace(/<\/script/g,'<\\/script'));
fs.mkdirSync('dist',{recursive:true});fs.writeFileSync('dist/index.html',html);
console.log('dist/index.html',(html.length/1024/1024).toFixed(2)+' MB; js',(js.length/1024|0)+' KB');
