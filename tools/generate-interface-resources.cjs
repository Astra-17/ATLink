// Run from any directory: node tools/generate-interface-resources.cjs
// TSV columns: stable source key, French, optional English override.
// Keys containing newlines use literal \n in the TSV.
const fs = require('node:fs');
const path = require('node:path');
const crypto = require('node:crypto');
const directory = path.join(__dirname, '..', 'Core', 'Resources');
const source = fs.readFileSync(path.join(directory, 'Interface.translations.tsv'), 'utf8');
const english = {}, french = {}, keys = {};
const seen = new Set();
for (const line of source.split(/\r?\n/)) {
  if (!line || line.startsWith('#')) continue;
  const columns = line.split('\t').map(s => s.replaceAll('\\n', '\n').replaceAll('\\t', '\t').replaceAll('\\r', '\r'));
  if (columns.length < 2 || columns.length > 3) throw new Error('Invalid translation: ' + line);
  const [sourceKey, fr, en = sourceKey] = columns;
  if (seen.has(sourceKey)) throw new Error('Duplicate source key: ' + sourceKey);
  seen.add(sourceKey);
  const key = 'T' + crypto.createHash('sha256').update(sourceKey).digest('hex').slice(0, 14);
  if (key in english) throw new Error('Resource key collision: ' + sourceKey);
  english[key] = en; french[key] = fr; keys[key]=sourceKey;
}
for (const [code, catalog] of [['en', english], ['fr', french], ['keys', keys]]) {
  fs.writeFileSync(path.join(directory, 'Interface.' + code + '.json'), JSON.stringify(Object.fromEntries(Object.entries(catalog).sort(([a], [b]) => a.localeCompare(b))), null, 2) + '\n');
}
console.log(seen.size + ' bilingual interface resources generated.');
