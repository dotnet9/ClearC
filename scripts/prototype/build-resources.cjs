// 宿主和原型各自维护 JSON；原型共用宿主语义键并补充仅用于样例的文案。
const fs = require('node:fs');
const path = require('node:path');
const root = __dirname;
const resources = {};
function flatten(value, prefix = '', output = {}) {
  for (const [key, text] of Object.entries(value)) {
    const name = prefix ? prefix + '.' + key : key;
    if (text && typeof text === 'object') flatten(text, name, output);
    else if (typeof text === 'string') output[name] = text;
  }
  return output;
}
for (const culture of ['zh-CN', 'en-US']) {
  resources[culture] = {
    ...flatten(JSON.parse(fs.readFileSync(path.join(root, '../../src/ClearC.Desktop/I18n', culture + '.json'), 'utf8').replace(/^\uFEFF/, ''))),
    ...JSON.parse(fs.readFileSync(path.join(root, culture + '.json'), 'utf8'))
  };
}
const keys = Object.keys(resources['zh-CN']).filter(key => key.startsWith('ClearC.') || key.startsWith('Prototype.'));
const englishKeys = Object.keys(resources['en-US']).filter(key => key.startsWith('ClearC.') || key.startsWith('Prototype.'));
if (keys.some(key => !englishKeys.includes(key)) || englishKeys.some(key => !keys.includes(key))) throw Error('中英文资源键不一致。');
const output = path.join(root, 'assets/resources.js');
const script = '// 根据宿主和原型 JSON 生成，请运行 scripts/build_standalone_prototype.ps1。\nwindow.CLEARC_RESOURCES=' + JSON.stringify(resources) + ';\n';
if (process.argv.includes('--check')) {
  if (!fs.existsSync(output) || fs.readFileSync(output, 'utf8').replace(/\r\n/g, '\n') !== script) throw Error('原型资源未同步。');
} else fs.writeFileSync(output, script);
