'use strict';

const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');

const patchsetPath = process.argv[2];
const downloadVideos = process.argv[3] === '--download-videos';
if (!patchsetPath) {
  console.error('usage: node scripts/test_preview_routing.js <patchset.json> [--download-videos]');
  process.exit(2);
}

const { patches } = JSON.parse(fs.readFileSync(patchsetPath, 'utf8').replace(/^\uFEFF/, ''));
const main = patches.find(p => p.name.startsWith('filelist_misc.dblclick-download-open '));
const renderer = patches.find(p => p.name.startsWith('renderer.preview-download '));
assert.ok(main && renderer, 'expected both file preview rules');

const mainGuard = /\/\*nd_purify\*\/if\((([\w$]+)\.path&&.*?)\)\{\(function\(\)/.exec(main.repl);
const rendererGuard = /try\{(var __p=.*?;)if\((.*?)\)\{var __d/.exec(renderer.repl);
assert.ok(mainGuard && rendererGuard, 'expected both injected download guards');

const mainDownloads = new vm.Script('Boolean(' + mainGuard[1] + ')');
const rendererDownloads = new vm.Script(rendererGuard[1] + 'Boolean(' + rendererGuard[2] + ')');
const cases = [
  [{ path: '/film.mp4', category: 1 }, downloadVideos],
  [{ path: '/film.MKV' }, downloadVideos],
  [{ path: '/recording.m2ts', category: null }, downloadVideos],
  [{ path: '/recording.m2ts', category: 6 }, true],
  [{ path: '/app.ts', category: 6 }, true],
  [{ path: '/x.dat', category: 6 }, true],
  [{ path: '/extensionless', category: '1' }, downloadVideos],
  [{ path: '/_pcs_.sharedir_receive/clip.mp4', category: 1 }, false],
  [{ path: '/report.pdf', category: 4 }, true],
  [{ path: '/clip.mp4.zip', category: 6 }, true],
  [{ path: '/_pcs_.sharedir_receive/report.pdf', category: 4 }, false],
];

for (const [file, downloads] of cases) {
  assert.equal(mainDownloads.runInNewContext({ [mainGuard[2]]: file }), downloads, 'file list: ' + file.path);
  assert.equal(rendererDownloads.runInNewContext({ e: file, t: {} }), downloads, 'preview manager: ' + file.path);
}
assert.equal(rendererDownloads.runInNewContext({ e: { server_path: '/movie.webm' }, t: {} }), downloadVideos, 'preview manager with server_path only');
assert.equal(rendererDownloads.runInNewContext({ e: { path: '/report.pdf', from_uk: 100 }, t: {} }), false, 'shared document keeps native routing');

console.log('PASS: video previews and non-video download routing');
