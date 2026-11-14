const fs = require('node:fs');
const path = require('node:path');
const crypto = require('node:crypto');
const root = path.join(__dirname, '..');
const read = file => fs.readFileSync(path.join(root, file), 'utf8');
const en = JSON.parse(read('Core/Resources/Interface.en.json'));
const fr = JSON.parse(read('Core/Resources/Interface.fr.json'));
const keyFor = text => 'T' + crypto.createHash('sha256').update(text).digest('hex').slice(0, 14);
const issues = [];
if (JSON.stringify(Object.keys(en).sort()) !== JSON.stringify(Object.keys(fr).sort())) issues.push('Catalog key sets differ');
const parameters = text => [...text.matchAll(/(?<!\{)\{(\d+)(?:[,}:])/g)].map(m => m[1]).sort().join(',');
for (const key of Object.keys(en)) {
  if (!en[key] || !fr[key]) issues.push('Empty translation: ' + key);
  if (parameters(en[key]) !== parameters(fr[key] || '')) issues.push('Parameter mismatch: ' + key);
}
const files = ['MainWindow.xaml', 'MainWindow.xaml.cs'];
for (const folder of ['Views', 'ViewModels', 'Core', 'Services', 'Themes'])
  for (const file of fs.readdirSync(path.join(root, folder)))
    if (/\.(cs|xaml)$/.test(file) && file !== 'UiText.cs') files.push(folder + '/' + file);
let references = 0;
for (const file of files) {
  const text = read(file);
  if (file.endsWith('.xaml')) {
    for (const match of text.matchAll(/\{loc:Text (T[0-9a-f]+)\}/g)) {
      references++;
      if (!(match[1] in en)) issues.push(file + ': missing ' + match[1]);
    }
    for (const match of text.matchAll(/\b(?:Text|Header|Content|Title|ToolTip|AutomationProperties.Name)="([^"{]*[A-Za-zÀ-ÿ][^"]*)"/g))
      if (!match[1].startsWith('http')) issues.push(file + ': untranslated XAML text ' + match[1]);
  } else {
    for (const match of text.matchAll(/\bUiText\.(?:Get|Format)\(("(?:[^"\\]|\\.)*")/g)) {
      let source;
      try { source = JSON.parse(match[1]); } catch { continue; }
      references++;
      if (!(keyFor(source) in en) && !(source in en)) issues.push(file + ': missing resource for ' + source);
    }
  }
}
if (issues.length) { console.error(issues.join('\n')); process.exitCode = 1; }
else console.log('PASS ' + Object.keys(en).length + ' bilingual keys, parameters and ' + references + ' source references; no hard-coded XAML labels.');
