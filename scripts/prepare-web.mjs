import {copyFileSync,mkdirSync,existsSync} from 'node:fs';
import {resolve} from 'node:path';
import {writeArrowGlb} from './arrow-asset.mjs';
const root=resolve(import.meta.dirname,'..');
const data=resolve(root,'examples/web/public/data'),assets=resolve(root,'examples/web/public/assets');
mkdirSync(data,{recursive:true});mkdirSync(assets,{recursive:true});
for(const name of ['coaxial','variant','renamed','exact-phases']) {
  const target=resolve(data,name+'.replay.json');
  if(existsSync(target))throw Error('Create-only example output exists: '+target);
  copyFileSync(resolve(root,'generated',name==='exact-phases'?'exact':'mechanisms',name+'.replay.json'),target);
}
for(const [name,alternate] of [['arrow',false],['alternate',true]]) {
  const target=resolve(assets,name+'.glb');if(existsSync(target))throw Error('Create-only asset already exists');writeArrowGlb(target,alternate);
}
console.log('PUBLIC_WEB_PREPARED');
