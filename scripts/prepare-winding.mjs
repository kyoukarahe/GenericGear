// Ordinary DLL authoring and fresh-process recording reconstruction.
import {spawnSync} from 'node:child_process';
import {existsSync,mkdirSync} from 'node:fs';
import {resolve} from 'node:path';
const root=resolve(import.meta.dirname,'..'),out=resolve(root,process.argv[2]??'generated/winding');
if(existsSync(out))throw Error('Create-only output exists: '+out);
function run(args){
  const r=spawnSync('dotnet',args,{cwd:root,stdio:'inherit',windowsHide:true});
  if(r.error)throw r.error;if(r.status!==0)throw Error('Consumer failed: '+args[1]);
}
run(['build','examples/winding/dotnet/Winding.Consumer.csproj','-c','Release','-p:RestoreLockedMode=true']);
mkdirSync(out,{recursive:true});
const dll='examples/winding/dotnet/bin/Release/net8.0/Winding.Consumer.dll';
for(const [name,args] of [['drive',[]],['scaled',['2','20','false']],['circular',['1','21','true']]]){
  const directory=resolve(out,name);run([dll,'produce',directory,...args]);run([dll,'restore',directory]);
}
run([dll,'modes-produce',resolve(out,'modes'),resolve(out,'drive/source.json')]);
run([dll,'modes-restore',resolve(out,'modes')]);
run([dll,'modes-resume',resolve(out,'modes')]);
console.log('PUBLIC_WINDING_CONSUMERS_PASS');
