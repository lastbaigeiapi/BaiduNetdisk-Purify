// Resolve the portable rules against ONE build and write its exact patchset.json (format 1, literal edits only),
// which the PowerShell patcher applies unchanged. Any failing rule aborts (no partial scheme).
// Usage: node scripts/portable/build_patchset.js <core.asar | extracted dir> <tools> <out.json> <originalSha256 | -> [expectedOutSha256 | -] [label] [--download-videos]
'use strict';
const fs = require('fs'), path = require('path'), vm = require('vm');
const E = require('./engine');
const { loadGroups, NEW_FILES, MARK } = require('./rules');
const [SRC, TOOLS, OUT, ORIG, EXPECTED, LABEL, VIDEO_MODE] = process.argv.slice(2);
if (!SRC || !TOOLS || !OUT || !ORIG) { console.error('usage: build_patchset.js <core> <tools> <out.json> <origSha|-> [expectedSha|-] [label]'); process.exit(2); }
const val = x => (x && x !== '-' ? x.toLowerCase() : '');

const pristine = E.loadCore(SRC, TOOLS);
if ((pristine.get('main.js') || '').includes(MARK)) { console.error('this core.asar is already patched; use the original'); process.exit(3); }
const rules = loadGroups().flatMap(g => g.rules);
const { texts, results } = E.resolveAll(rules, pristine, { downloadVideos: VIDEO_MODE === '--download-videos' });

const failed = results.filter(r => r.status === 'fail');
for (const r of failed) console.error(`FAIL ${r.rule.id}: ${r.detail}`);
for (const f of [...texts.keys()].filter(f => f.endsWith('.js') && texts.get(f) !== pristine.get(f))) {
  try { new vm.Script(texts.get(f), { filename: f }); } catch (e) { console.error(`SYNTAX ${f}: ${e.message}`); failed.push(f); }
}
if (failed.length) { console.error(`${failed.length} problem(s); no patchset written`); process.exit(1); }

const patches = results.flatMap(r => r.edits.map(e => ({ file: e.file, name: r.rule.id + (r.rule.name ? ' ' + r.rule.name : ''), kind: 'literal', find: e.find, repl: e.repl })));
const doc = {
  format: 1,
  product: 'BaiduNetdisk de-marketing patch',
  target: { appVersion: LABEL || '', originalCoreSha256: val(ORIG) },
  expectedOutputSha256: val(EXPECTED),
  mark: MARK,
  newFiles: NEW_FILES,
  notApplicable: results.filter(r => r.status === 'n/a').map(r => r.rule.id),
  skipped: results.filter(r => r.status === 'skip').map(r => r.rule.id),
  patches,
};
fs.writeFileSync(OUT, JSON.stringify(doc, null, 1), 'utf8');
const n = st => results.filter(r => r.status === st).length;
console.log(`patchset: ${patches.length} literal edits from ${n('ok')} rules (n/a ${n('n/a')}, skip ${n('skip')}) -> ${OUT}`);
