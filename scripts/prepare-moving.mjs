// Ordinary source-built DLL consumers. No stored answer replaces authoring/rebuild.
import assert from 'node:assert/strict';
import {spawnSync} from 'node:child_process';
import {existsSync,mkdirSync,readFileSync,readdirSync,writeFileSync} from 'node:fs';
import {resolve} from 'node:path';
const root=resolve(import.meta.dirname,'..'),out=resolve(root,'generated/moving');
if(existsSync(out))throw Error('Create-only output exists: generated/moving');
const cases={
  carrier:[['standalone','100','10'],['prefix','42','18','prefix']],
  differential:[['standalone','100','10','sun-carrier'],['alternate','100','10','sun-planet'],
    ['third-basis','100','10','carrier-planet'],['prefix','42','18','prefix'],
    ['held','100','10','hold'],['custom','37','23','signed','custom-'],
    ['display-unavailable','100','10','display-unavailable']]
};
function run(args,capture=false){
  const r=spawnSync('dotnet',args,{cwd:root,encoding:'utf8',maxBuffer:8*1024*1024,stdio:capture?'pipe':'inherit'});
  if(r.error)throw r.error;
  if(r.status!==0)throw Error(`dotnet ${args[0]} failed (${r.status}): ${r.stderr??''}`);
  return capture?JSON.parse(r.stdout):null;
}
mkdirSync(out,{recursive:true});
for(const [profile,entries] of Object.entries(cases)){
  const name=profile==='carrier'?'Carrier':'Differential';
  run(['build',`examples/${profile}/dotnet/${name}.Consumer.csproj`,'-c','Release','-p:RestoreLockedMode=true','--nologo']);
  const dll=`examples/${profile}/dotnet/bin/Release/net8.0/${name}.Consumer.dll`;
  for(const [sample,...args] of entries){
    const dir=resolve(out,profile,sample);
    const created=run([dll,'create',dir,...args],true),reopened=run([dll,'reopen',dir],true);
    assert.equal(created.status,'PASS');assert.equal(reopened.status,'PASS');
    const {processId:pid1,...a}=created,{processId:pid2,...b}=reopened;
    assert.deepEqual(a,b,'Fresh process must reconstruct and evaluate the same source');
    // Shipped examples are checked against new ordinary generation, never used as its input.
    for(const file of readdirSync(dir))assert.deepEqual(readFileSync(resolve(dir,file)),
      readFileSync(resolve(root,'examples',profile,'web/samples',sample,file)),`${profile}/${sample}/${file}`);
    writeFileSync(resolve(dir,'reopen.json'),JSON.stringify(reopened,null,2)+'\n',{flag:'wx'});
    console.log(`${profile}/${sample}: create ${pid1}, reopen ${pid2}, bodies ${reopened.bodyCount}, PASS`);
  }
}
console.log('PUBLIC_MOVING_CONSUMERS_PASS');
