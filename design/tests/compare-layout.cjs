// 只量测明确列出的区域，不把坐标吻合当成整页像素相似率。
const fs=require('node:fs'),path=require('node:path');
const [nativeDirectory,prototypeDirectory]=process.argv.slice(2);
if(!nativeDirectory||!prototypeDirectory)throw Error('Usage: compare-layout.cjs <native> <prototype>');
const read=file=>new Map(fs.readFileSync(file,'utf8').trim().split(/\r?\n/).slice(1).map(line=>{const [key,...values]=line.split(',');return[key,values.map(Number)];}));
const names=[];
for(const culture of ['zh-CN','en-US']){
  for(const palette of ['DotNet','Aurora','Ember','Default','Aquatic','Desert','Dusk','NightSky'])for(const mode of ['Light','Dark'])names.push(`main-${culture}-${palette}-${mode}`);
  for(const phase of ['scanning','results','confirming','cleaning','done'])names.push(`workflow-${culture}-${phase}`);
}
let count=0;const failures=[];
for(const name of names){const actual=read(path.join(nativeDirectory,name+'.geometry.csv')),expected=read(path.join(prototypeDirectory,name+'.geometry.csv'));
  for(const key of ['window','workspace','donut','list','console','statusbar']){
    const a=actual.get(key),b=expected.get(key);if(!a||!b)throw Error('Missing geometry: '+name+' '+key);
    for(let index=0;index<4;index++){count++;if(Math.abs(a[index]-b[index])>2)failures.push(`${name} ${key}[${index}]: ${a[index]} vs ${b[index]}`);}
  }
}
if(failures.length)throw Error(failures.join('\n'));
console.log(`PASS ${count} measured coordinates across ${names.length} views, within 2 DIP. Whole-page visual similarity is not measured.`);
