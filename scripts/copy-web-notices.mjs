import {copyFileSync,mkdirSync} from 'node:fs';
import {resolve} from 'node:path';
const root=resolve(import.meta.dirname,'..'),out=resolve(root,'examples/web/public/licenses');
mkdirSync(out,{recursive:true});
for(const name of ['LICENSE','LICENSE_SCOPE.md','THIRD_PARTY_NOTICES.md','DEPENDENCIES.json']) copyFileSync(resolve(root,name),resolve(out,name));
copyFileSync(resolve(root,'node_modules/three/LICENSE'),resolve(out,'Three-MIT-LICENSE.txt'));
console.log('WEB_LICENSE_NOTICES_COPIED');
