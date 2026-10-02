// Host-only generic rigid transforms. These never calculate a gear law.
export const identity=Object.freeze([1,0,0,0,0,1,0,0,0,0,1,0,0,0,0,1]);
export function multiply(a,b){const m=new Array(16).fill(0);for(let c=0;c<4;c++)for(let r=0;r<4;r++)for(let k=0;k<4;k++)m[c*4+r]+=a[k*4+r]*b[c*4+k];return m;}
export function inverseRigid(m){const r=[m[0],m[4],m[8],0,m[1],m[5],m[9],0,m[2],m[6],m[10],0,0,0,0,1];for(let i=0;i<3;i++)r[12+i]=-(r[i]*m[12]+r[4+i]*m[13]+r[8+i]*m[14]);return r;}
export function assetLocal(bodyWorld,parentWorld=identity,correction=identity){return multiply(inverseRigid(parentWorld),multiply(bodyWorld,correction));}
export function translation(x,y,z=0){const m=[...identity];m[12]=x;m[13]=y;m[14]=z;return m;}
export const rendererParent=Object.freeze([0,1,0,0,-1,0,0,0,0,0,1,0,17,-9,0,1]);
