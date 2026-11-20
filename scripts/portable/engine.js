// Portable (cross-version) patch rules -> exact per-version literal edits.
//
// A rule finds its target by CONTENT (not by webpack chunk name) and captures minified identifiers from the
// surrounding code, so one rule set can describe many client builds. At scheme build time the rules are resolved
// against one specific core.asar and emitted as plain literal find/replace pairs (patchset.json format 1), which
// the PowerShell patcher applies unchanged. Users' machines never do any fuzzy matching.
//
// Rule fields
//   id       unique string (stable; used in reports)
//   name     human description
//   legacy   [indices into the 1.x PATCHES list]  -> on 8.9.7 the rule must reproduce exactly those edits
//   where    which files to search (default: every top-level .js and index.html)
//              'main.js' | ['a.js','b.js'] | RegExp (tested on the file name) | { contains: string|RegExp }
//   find     RegExp (do not use the g flag) or a literal string
//   repl     string (inserted verbatim) or (match, ...groups, ctx) => string
//            ctx = { text, file, index } (the whole current file text; use it to look up other minified names that
//            are defined elsewhere in the same file). The same object is also bound as `this` for function repls.
//   mode     'one'  (default) exactly one match in exactly one candidate file
//            'each' every candidate file that matches must match exactly once; at least `min` (default 1) files
//            'all'  every match in every candidate file (each matched text must be unique in its file);
//                   at least `min` (default 1) matches in total, at most `max`
//   feature  string|RegExp: if it occurs in NO file of this build the feature does not exist there and the rule
//            is reported as n/a (skipped). If it occurs anywhere, the rule is required.
//   optional true: a non-match is reported as 'skip' instead of an error (use only with a written reason)
//   seq      ordering key when there is no legacy index (rules are applied in ascending order of
//            min(legacy) or seq; legacy rules keep the 1.x order so 8.9.7 output stays byte-identical)
'use strict';

// ---- helpers for writing version-tolerant regexes -------------------------------------------------------
const esc = s => String(s).replace(/[.*+?^${}()|[\]\\\/]/g, '\\$&');
const ID = '[\\w$]+';                         // any minified identifier
const CAP = '([\\w$]+)';                      // capture an identifier
// webpack 4: Object(x.y)(...)   webpack 5: (0,x.Y)(...)   -> pass the inner pattern, e.g. IMPORT(`${CAP}\\.${CAP}`)
const IMPORT = inner => `(?:Object\\(${inner}\\)|\\(0,${inner}\\))`;
// webpack 5 wraps interop modules in a getter: o.app  vs  o().app
const MOD = name => `${name}(?:\\(\\))?`;
// build a RegExp from a template: literal text is escaped, ${...} values are inserted as raw regex source
function R(strings, ...vals) {
  let src = '';
  strings.raw.forEach((s, i) => { src += esc(s.replace(/\\`/g, '`')); if (i < vals.length) src += vals[i]; });
  return new RegExp(src);
}

// ---- engine ---------------------------------------------------------------------------------------------
const DEFAULT_FILE = n => /^[^/\\]+\.js$/.test(n) || n === 'index.html';
const natural = (a, b) => a.localeCompare(b, 'en', { numeric: true });

function candidates(rule, names, texts) {
  const w = rule.where;
  let list;
  if (w === undefined) list = names.filter(DEFAULT_FILE);
  else if (typeof w === 'string') list = names.includes(w) ? [w] : [];
  else if (Array.isArray(w)) list = names.filter(n => w.includes(n));
  else if (w instanceof RegExp) list = names.filter(n => w.test(n));
  else if (w && w.contains !== undefined) {
    const c = w.contains;
    list = names.filter(DEFAULT_FILE).filter(n => (c instanceof RegExp ? c.test(texts.get(n)) : texts.get(n).includes(c)));
  } else throw new Error(`rule ${rule.id}: bad 'where'`);
  return list.sort(natural);
}

function globalRe(find) {
  if (typeof find === 'string') return new RegExp(esc(find), 'g');
  const flags = find.flags.includes('g') ? find.flags : find.flags + 'g';
  return new RegExp(find.source, flags);
}
function countLiteral(s, lit) { let n = 0, i = -1; while ((i = s.indexOf(lit, i + 1)) >= 0) { n++; if (n > 1) break; } return n; }
function hasFeature(f, texts) {
  for (const t of texts.values()) if (f instanceof RegExp ? f.test(t) : t.includes(f)) return true;
  return false;
}

// Apply one rule to the (mutable) Map name->text. Returns { status, edits, detail }.
//   status: 'ok' | 'n/a' | 'skip' | 'fail'
//   edits:  [{ file, index, find, repl }]   (index = position in the text BEFORE this edit)
function applyRule(rule, texts, options = {}) {
  const names = [...texts.keys()];
  if (rule.feature !== undefined && !hasFeature(rule.feature, texts)) return { status: 'n/a', edits: [], detail: 'feature absent' };
  const cands = candidates(rule, names, texts);
  const re = globalRe(rule.find);
  const hits = [];
  for (const f of cands) {
    const s = texts.get(f); re.lastIndex = 0;
    const ms = [...s.matchAll(re)];
    if (ms.length) hits.push({ f, ms });
  }
  const mode = rule.mode || 'one';
  const bad = d => (rule.optional ? { status: 'skip', edits: [], detail: d } : { status: 'fail', edits: [], detail: d });
  const where = hits.map(h => h.f + 'x' + h.ms.length).join(',');
  if (!hits.length) return bad(`no match in ${cands.length} candidate file(s)`);
  if (mode === 'one' && (hits.length !== 1 || hits[0].ms.length !== 1)) return bad(`expected exactly 1 match, got ${where}`);
  if (mode === 'each') {
    const multi = hits.filter(h => h.ms.length !== 1);
    if (multi.length) return bad(`a file matched more than once: ${where}`);
    if (hits.length < (rule.min || 1)) return bad(`only ${hits.length} file(s) matched, need ${rule.min}`);
    if (rule.max && hits.length > rule.max) return bad(`${hits.length} files matched, max ${rule.max}: ${where}`);
  }
  if (mode === 'all') {
    const total = hits.reduce((a, h) => a + h.ms.length, 0);
    if (total < (rule.min || 1)) return bad(`only ${total} match(es), need ${rule.min}`);
    if (rule.max && total > rule.max) return bad(`${total} matches, max ${rule.max}: ${where}`);
  }
  // plan first (so a failure leaves texts untouched), then apply
  const plan = [];
  for (const { f, ms } of hits) {
    const s = texts.get(f);
    for (const m of (mode === 'all' ? ms : [ms[0]])) {
      const find = m[0];
      if (!find.length) return bad('empty match');
      if (countLiteral(s, find) !== 1) return bad(`matched text is not unique as a literal in ${f}`);
      let repl;
      if (typeof rule.repl === 'function') {
        const ctx = { text: s, file: f, index: m.index, options };
        try { repl = rule.repl.apply(ctx, [...m, ctx]); } catch (e) { return bad(`repl threw in ${f}: ${e.message}`); }
      } else repl = rule.repl;
      if (typeof repl !== 'string') return bad('repl did not return a string');
      plan.push({ file: f, find, repl });
    }
  }
  const edits = [];
  const updated = new Map();
  for (const p of plan) {            // literal re-search keeps positions right when one file gets several edits
    const s = updated.get(p.file) ?? texts.get(p.file), i = s.indexOf(p.find);
    if (i < 0 || countLiteral(s, p.find) !== 1) return bad(`literal no longer unique after earlier edits in ${p.file}`);
    edits.push({ file: p.file, index: i, find: p.find, repl: p.repl });
    updated.set(p.file, s.slice(0, i) + p.repl + s.slice(i + p.find.length));
  }
  for (const [file, text] of updated) texts.set(file, text);
  return { status: 'ok', edits, detail: where };
}

function orderKey(r) { return r.legacy && r.legacy.length ? Math.min(...r.legacy) : (r.seq !== undefined ? r.seq : 1e6); }
function sortRules(rules) { return rules.slice().sort((a, b) => orderKey(a) - orderKey(b) || natural(a.id, b.id)); }

// Resolve all rules in order. Returns { texts, results: [{rule, status, edits, detail}] }
function resolveAll(rules, pristine, options) {
  const texts = new Map(pristine);
  const results = [];
  for (const rule of sortRules(rules)) {
    let r;
    try { r = applyRule(rule, texts, options); } catch (e) { r = { status: 'fail', edits: [], detail: 'exception: ' + e.message }; }
    results.push(Object.assign({ rule }, r));
  }
  return { texts, results };
}

// Load the top-level .js files and index.html of a core.asar (or of an extracted folder) into a Map.
function loadCore(src, toolsDir) {
  const fs = require('fs'), path = require('path');
  const map = new Map();
  if (fs.statSync(src).isDirectory()) {
    for (const n of fs.readdirSync(src)) if (DEFAULT_FILE(n)) map.set(n, fs.readFileSync(path.join(src, n), 'utf8'));
    return map;
  }
  const asar = require(path.join(path.resolve(toolsDir), 'node_modules', '@electron', 'asar'));
  for (const p of asar.listPackage(src)) {
    const n = p.replace(/^[\\/]/, '');
    if (DEFAULT_FILE(n)) { try { map.set(n, asar.extractFile(src, n).toString('utf8')); } catch (e) { /* directory */ } }
  }
  return map;
}

module.exports = { esc, ID, CAP, IMPORT, MOD, R, applyRule, resolveAll, sortRules, loadCore, candidates };
