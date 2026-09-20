// Exercise the actual gallery scheduler without a browser or rendering engine.
const fs=require('fs'),vm=require('vm'),assert=require('assert');
const html=fs.readFileSync(process.argv[2],'utf8');
const script=html.match(/<script>([\s\S]*?)<\/script>/)[1];
const elements={};
const document={querySelector(id){return elements[id]??=( {value:id==='#nearest'?'0':id==='#distance'?'0.2':id==='#palette'?'colour':'',checked:false,style:{},textContent:'',innerHTML:''});}};
const context=vm.createContext({document,requestAnimationFrame(){},console});vm.runInContext(script,context);
let clock=0;
// Travel both directions, rapidly steer while a transition is already active,
// then allow every target to settle. The scheduler asserts its 6000 limit itself.
for(const d of [0,1,.1,.9,.2,.8,0,1]){
 document.querySelector('#distance').value=String(d);
 for(let frame=0;frame<160;frame++){clock+=16;vm.runInContext(`update(${clock})`,context);}
 assert.equal(vm.runInContext('JSON.stringify(current)',context),vm.runInContext('JSON.stringify(wanted)',context));
}
assert(vm.runInContext('total()',context)<1000,'Distant fleet should not retain thousands of triangles');
console.log('Gallery scheduler: transitions stay within 6,000, settle after steering, and distant fleet stays below 1,000 triangles.');

// Reordering at an unchanged slider value must invalidate the old allocation.
document.querySelector('#distance').value='0';
for(const nearest of [0,1,2,1]){
 document.querySelector('#nearest').value=String(nearest);
 for(let frame=0;frame<180;frame++){clock+=16;vm.runInContext(`update(${clock})`,context);}
 assert.equal(vm.runInContext('JSON.stringify(current)',context),vm.runInContext('JSON.stringify(wanted)',context));
 if(nearest===0)assert.equal(vm.runInContext('current[1]',context),3,'Raider should demonstrate the original budget plateau');
 if(nearest===1)assert.equal(vm.runInContext('current[1]',context),0,'Nearest Raider must reach full source detail');
}
console.log('Raider regression: budget plateau explained; selecting Raider as nearest reaches full detail without exceeding the limit.');
