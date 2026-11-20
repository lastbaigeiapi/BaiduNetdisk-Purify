// File list / transfer list odds and ends: row-hover icons (知识库 / AI总结), 「添加AI下载任务」, 云打印,
// 智能看图 new previewer, non-video double-click = download then open.
'use strict';
const { MARK } = require('./index');
const { ID, IMPORT } = require('../engine');
const { videoFileCondition } = require('../video_preview');

// Start at a bare identifier so long base64 runs are scanned once, without trying each suffix.
const RECEIVER = `(?<![\\w$.])${ID}`;
const IMP = IMPORT(`${ID}\\.${ID}`);   // Object(x.y)  |  (0,x.Y)

module.exports = {
  rules: [
    // ---- 32,34,36,38,40: row-hover "去知识库" button (component copies: list views) -------------------
    { id: 'filelist_misc.hover-knowledge-icon', legacy: [32, 34, 36, 38, 40], name: '行内按钮: 去知识库 (canShowKnowledgeIcon)',
      feature: 'canShowKnowledgeIcon',
      find: /canShowKnowledgeIcon:function\(\)\{(?=return!\()/,
      mode: 'each', min: 1,
      repl: m => m + MARK + 'return!1;' },

    // ---- 33,35,37,39,41: row-hover "AI总结" button. Copies that already read `return!1` are not matched. ----
    { id: 'filelist_misc.hover-ai-summary-icon', legacy: [33, 35, 37, 39, 41], name: '行内按钮: 去AI总结 (canShowAiSummaryIcon)',
      feature: 'canShowAiSummaryIcon',
      find: /canShowAiSummaryIcon:function\(\)\{(?=var [\w$]+=this;return)/,
      mode: 'each', min: 1,
      repl: m => m + MARK + 'return!1;' },

    // ---- 46-53: 云添加 「添加AI下载任务」 entrance. Matches the main copies (followed by `,nextMethod`), not the
    //      offDownload-list copy which is the last method of its options object (`}},` / `}}` follows) - legacy
    //      never patched that one. ------------------------------------------------------------------------
    { id: 'filelist_misc.ai-download-entrance', legacy: [46, 47, 48, 49, 50, 51, 52, 53], name: '云添加: 去「添加AI下载任务」(aiDownloadEntranceVisible)',
      feature: 'aiDownloadEntranceVisible',
      find: new RegExp(`${RECEIVER}\\.aiDownloadEntranceVisible=${IMP}\\(${ID}\\)(?=\\}\\)\\)?\\.catch\\(\\(?function\\(\\)\\{${ID}\\.aiDownloadEntranceVisible=!1\\}\\)\\)?\\},)`),
      mode: 'all', min: 3,
      repl: m => m.slice(0, m.indexOf('=') + 1) + MARK + '!1' },

    // ---- 54,56: 传输列表 「云打印」 hover button (webpack4 old builds: plain div; newer: u-tooltip) ------
    { id: 'filelist_misc.transfer-cloudprint-btn', legacy: [54, 56], name: '传输列表: 去「云打印」按钮',
      feature: 'cloudPrintSupportType',
      find: new RegExp(`${RECEIVER}\\.comMasAble\\|\\|${ID}\\.row\\.cowork_id\\?${ID}\\._e\\(\\):${ID}\\(` +
        `(?:"u-tooltip",\\{attrs:\\{effect:"dark","popper-class":"bubble-tip",content:"云打印"|"div",\\{staticClass:"cloud-print")`),
      mode: 'each', min: 1,
      repl: m => MARK + '!0' + m.slice(m.indexOf('?')) },

    // ---- 55,57: 传输列表 右键 「云打印」 ----------------------------------------------------------------
    { id: 'filelist_misc.transfer-cloudprint-menu', legacy: [55, 57], name: '传输列表: 去右键「云打印」',
      feature: 'name:"云打印",type:"operate"',
      find: new RegExp(`${IMP}\\(${ID}\\.server_path\\)&&(${ID})\\.push\\(\\{name:"云打印"`),
      mode: 'each', min: 1,
      repl: (m, arr) => MARK + `!1&&${arr}.push({name:"云打印"` },

    // ---- 83-86: 智能看图: isSupportNewPreviewer -> false (definition only, not the call sites) ---------
    { id: 'filelist_misc.no-new-previewer', legacy: [83, 84, 85, 86], name: '智能看图: 禁用新版外置预览器',
      feature: 'isSupportNewPreviewer',
      find: /isSupportNewPreviewer\(([\w$]+),([\w$]+)\)\{(?=if\(!\2\|\|!\1\)return!1;)/,
      mode: 'each', min: 1,
      repl: m => m + MARK + 'return!1;' },

    // ---- 93: 双击文件 = 下载后打开（openOrPreview 的 "case N:" 分支，主窗口） ---------------------------
    // Captures (all from THIS build): case number, app module expr (G | t()), file var (s), callback params, fs alias,
    // `this` alias of the component (i) and the regenerator context (n) taken from the following limitFileSize guard.
    { id: 'filelist_misc.dblclick-download-open', legacy: [93], name: '精简: 双击非视频文件 = 下载后打开（主窗口）',
      feature: '正在打开本地已下载文件',
      find: new RegExp(
        `case (\\d+):if\\(navigator\\.onLine\\|\\|(${ID}(?:\\(\\))?)\\.app\\.\\$networkOnline\\|\\|` +
        `(?=\\2\\.app\\.\\$getLocalPathFromDownloadHistory\\((${ID})\\.path,\\(?function\\((${ID}),(${ID})\\)\\{if\\(0===\\4&&\\5&&(${ID})\\.existsSync\\(\\5\\)\\)return (${ID})\\.\\$toast\\("正在打开本地已下载文件"` +
        `[\\s\\S]{0,400}?!\\7\\.limitFileSize\\(\\3\\)\\)\\{(${ID})\\.next=)`),
      mode: 'one',
      repl: function (m, num, app, f, e, t, fsa, self, rg, ctx) {
        const rest = m.slice(`case ${num}:`.length);
        const guard = ctx.options.downloadVideos ? '' : '&&!' + videoFileCondition(f, f + '.path');
        return `case ${num}:` + MARK +
          `if(${f}.path&&"/"===${f}.path.charAt(0)&&!/^\\/_pcs_\\.(knowledge_subscribe|sharedir_receive)/.test(${f}.path)${guard}){(function(){var __d=!1,__go=function(){if(__d)return;__d=!0;${self}.addDownloadTask([{md5:${f}.md5||"",size:${f}.size,is_dir:0,server_path:${f}.path,from_manual:1,downloadAndEditSwitch:!0}])};try{${app}.app.$getLocalPathFromDownloadHistory(${f}.path,function(${e},${t}){if(__d)return;if(0===${e}&&${t}&&${fsa}.existsSync(${t})){__d=!0;${app}.app.$openImageViewer('"'+${t}+'"');return}__go()})}catch(__e){}setTimeout(__go,1500)})();return ${rg}.abrupt("return")}` +
          rest;
      } },
  ],
  dropped: [],
};
