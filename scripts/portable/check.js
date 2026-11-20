// Check portable rules against many client builds.
// Usage: node scripts/portable/check.js --cores <dir> [--tools <dir>] [--group <name>[,<name>]] [--versions a,b] [--verbose]
//   <dir>/<version>/core/  (extracted core.asar)  or  <dir>/<version>/module/BrowserEngine/resources/core.asar
// Reports per build: ok / n/a / skip / FAIL per rule, JS syntax of every modified file.
// On 8.9.7.101 additionally: every rule, applied ALONE to the pristine files, must produce exactly the same file
// contents as its legacy 1.x rules (patchset_def.js) applied alone, using the video-download mode.
// Without --group: also checks that every legacy index is covered by a rule or listed in a group's `dropped`.
'use strict';
const fs = require('fs'), path = require('path'), vm = require('vm');
const E = require('./engine');
const args = process.argv.slice(2);
const opt = k => { const i = args.indexOf('--' + k); return i >= 0 ? args[i + 1] : undefined; };
const flag = k => args.includes('--' + k);
const CORES = opt('cores'), TOOLS = opt('tools') || path.join(__dirname, '..', '..', 'tools');
if (!CORES) { console.error('usage: check.js --cores <dir> [--group g] [--versions v1,v2] [--verbose]'); process.exit(2); }
const REF = '8.9.7.101';

const { loadGroups } = require('./rules');
const groups = loadGroups(opt('group') ? opt('group').split(',') : undefined);
const rules = groups.flatMap(g => g.rules.map(r => Object.assign({ group: g.name }, r)));
const ids = new Set(); for (const r of rules) { if (ids.has(r.id)) { console.error('duplicate rule id ' + r.id); process.exit(2); } ids.add(r.id); }

let versions = opt('versions') ? opt('versions').split(',') : fs.readdirSync(CORES).filter(v => /^\d+\.\d+\.\d+\.\d+$/.test(v));
versions.sort((a, b) => b.localeCompare(a, 'en', { numeric: true }));
const coreOf = v => { const d = path.join(CORES, v, 'core'); return fs.existsSync(d) ? d : path.join(CORES, v, 'module', 'BrowserEngine', 'resources', 'core.asar'); };

const OLD = require('../patchset_def.js').PATCHES;
function applyLegacy(i, texts) {
  const p = OLD[i], s = texts.get(p.file);
  if (s === undefined) return false;
  if (p.findRe) { const m = s.match(new RegExp(p.findRe.source)); if (!m) return false; texts.set(p.file, s.slice(0, m.index) + p.replFn(...m) + s.slice(m.index + m[0].length)); return true; }
  const k = s.indexOf(p.find); if (k < 0) return false;
  texts.set(p.file, s.slice(0, k) + p.repl + s.slice(k + p.find.length)); return true;
}

let failures = 0;
for (const v of versions) {
  const pristine = E.loadCore(coreOf(v), TOOLS);
  const { texts, results } = E.resolveAll(rules, pristine);
  const cnt = st => results.filter(r => r.status === st).length;
  const changed = [...texts.keys()].filter(f => texts.get(f) !== pristine.get(f));
  const syntaxBad = [];
  for (const f of changed) { if (!f.endsWith('.js')) continue; try { new vm.Script(texts.get(f), { filename: f }); } catch (e) { syntaxBad.push(f + ': ' + e.message); } }
  console.log(`== ${v}  ok ${cnt('ok')}  n/a ${cnt('n/a')}  skip ${cnt('skip')}  FAIL ${cnt('fail')}  | ${changed.length} files changed, syntax ${syntaxBad.length ? 'ERRORS' : 'ok'}`);
  for (const r of results) {
    if (r.status === 'fail' || r.status === 'skip' || flag('verbose')) console.log(`   ${r.status.padEnd(4)} ${r.rule.id.padEnd(34)} ${r.status === 'ok' ? r.detail : r.detail}  [${r.rule.group}] ${r.rule.name || ''}`);
  }
  for (const s of syntaxBad) console.log('   SYNTAX ' + s);
  failures += cnt('fail') + syntaxBad.length;

  if (v === REF) {   // per-rule isolation regression against the legacy rules
    let same = 0; const diff = [];
    for (const rule of rules) {
      if (!rule.legacy || !rule.legacy.length) continue;
      const oldT = new Map(pristine), newT = new Map(pristine);
      const okOld = rule.legacy.slice().sort((a, b) => a - b).every(i => applyLegacy(i, oldT));
      const res = E.applyRule(rule, newT, { downloadVideos: true });
      const files = new Set([...oldT.keys()].filter(f => oldT.get(f) !== pristine.get(f) || newT.get(f) !== pristine.get(f)));
      const eq = okOld && res.status === 'ok' && [...files].every(f => oldT.get(f) === newT.get(f));
      if (eq) same++;
      else {
        let where = '';
        for (const f of files) {
          const a = oldT.get(f), b = newT.get(f); if (a === b) continue;
          let k = 0; while (k < a.length && a[k] === b[k]) k++;
          where += ` ${f}@${k} old:${JSON.stringify(a.slice(Math.max(0, k - 30), k + 60))} new:${JSON.stringify(b.slice(Math.max(0, k - 30), k + 60))}`;
        }
        diff.push(`${rule.id} (legacy ${rule.legacy.join(',')}) ${okOld ? '' : '[legacy rule did not apply alone] '}${res.status !== 'ok' ? '[' + res.status + ': ' + res.detail + '] ' : ''}${where}`);
      }
    }
    console.log(`   regress vs 1.x on ${REF}: ${same} rule(s) identical, ${diff.length} different`);
    for (const d of diff) console.log('   REGRESS ' + d.slice(0, 600));
    failures += diff.length;
  }
}

if (!opt('group')) {   // coverage of the legacy rule list
  const covered = new Set(rules.flatMap(r => r.legacy || []));
  for (const g of groups) for (const d of (g.dropped || [])) covered.add(d.legacy);
  const missing = OLD.map((_, i) => i).filter(i => !covered.has(i));
  console.log(`coverage: ${OLD.length - missing.length}/${OLD.length} legacy rules covered` + (missing.length ? '; MISSING ' + missing.join(',') : ''));
  failures += missing.length;
}
console.log(failures ? `${failures} problem(s)` : 'ALL OK');
process.exit(failures ? 1 : 0);
