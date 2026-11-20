// Renderer (renderer.js): ad vuex mutations, search bar AI entries, image viewer flag, preview entry,
// safebox gating, recycle bin / safebox routes.
'use strict';
const { MARK, } = require('./index');
const { esc, ID, R } = require('../engine');
const { videoFileCondition } = require('../video_preview');

// ---- 7-17: ad related vuex mutations become no-ops ----------------------------------------------------------
const AD = [
  [7, 'setMemberChannel', 'memberChannel'], [8, 'setHeaderAd', 'headerAd'], [9, 'setAsideAd', 'asideAd'],
  [10, 'setTextAd', 'textAd'], [11, 'setActivityConfig', 'activityConfig'], [12, 'setSideActivityVisible', 'sideActivityVisible'],
  [13, 'setTransferAd', 'transferAd'], [14, 'setTransferAdNative', 'transferAdNative'], [15, 'setPalletAd', 'palletAd'],
  [16, 'setLinkShareAdNative', 'linkShareAdNative'], [17, 'setLinkShareDetailAdNative', 'linkShareDetailAdNative'],
];
const adRules = AD.map(([legacy, fn, prop]) => ({
  id: 'renderer.ad-' + fn.replace(/^set/, '').replace(/([a-z])([A-Z])/g, '$1-$2').toLowerCase(),
  legacy: [legacy], where: 'renderer.js', name: '广告状态置空 ' + fn,
  // vuex mutation  setX(state,value){state.x=value}  ->  setX(state,value){}
  find: new RegExp(esc(fn) + '\\(([\\w$]+),([\\w$]+)\\)\\{\\1\\.' + esc(prop) + '=\\2\\}'),
  feature: prop === 'sideActivityVisible' ? 'sideActivityVisible' : undefined, // added after 8.8.6 (no state/getter/mutation in 8.5.9-8.8.6)
  repl: (m, s, v) => `${fn}(${s},${v}){${MARK}}`,
}));

// ---- 94 helpers ---------------------------------------------------------------------------------------------
// start index + webpack "require" parameter name of the module that contains position `pos`
function moduleOf(text, pos) {
  let p = pos;
  for (;;) {
    p = text.lastIndexOf('"use strict"', p - 1);
    if (p < 0) throw new Error('module start not found');
    const m = /(?:\},function\(e,t,([\w$]+)\)\{|\b\d+\(e,t,([\w$]+)\)\{)$/.exec(text.slice(Math.max(0, p - 60), p));
    if (m) return { start: p, req: m[1] || m[2] };
  }
}
// the vuex store: ['<module-local var>', '<export accessor>'] found by looking at another module's dispatch call
function storeRef(text, modStart, modEnd, req) {
  const dm = /(?<![\w$])([\w$]+)(\.default|\.a|\.A)\.dispatch\("globalComponents\/changeComponentState"/.exec(text);
  if (!dm) throw new Error('store dispatch site not found');
  const acc = dm[2];
  const om = moduleOf(text, dm.index);
  const head = text.slice(om.start, dm.index);
  const im = new RegExp('[,;]' + esc(dm[1]) + '=' + esc(om.req) + '\\((\\d+)\\)').exec(head);
  if (!im) throw new Error('store import not found');
  const id = im[1];
  const mine = new RegExp('[,;\\s]([\\w$]+)=' + esc(req) + '\\(' + id + '\\)[,;]').exec(text.slice(modStart, modEnd));
  return mine ? { ref: mine[1] + acc, param: '' } : { ref: '__s', param: `,__s=${req}(${id})${acc}` };
}

module.exports = {
  rules: [
    ...adRules,

    { id: 'renderer.search-ai-tools', legacy: [77], where: 'renderer.js', name: '搜索栏: 去「AI工具」分类',
      feature: '"ai-tools"',
      find: /(?<![\w$])([\w$]+)\.isNewVersion\?([\w$]+)\("ai-tools",/,
      repl: (m, e, t) => `${MARK}!1?${t}("ai-tools",` },

    { id: 'renderer.search-ai-range', legacy: [78], where: 'renderer.js', name: '搜索栏: 去「AI全网搜」范围',
      feature: 'key:"AISearch"',
      find: 'this.rangeList.unshift({type:"range",name:"AI全网搜",key:"AISearch"},{type:"line"}),',
      repl: MARK },

    { id: 'renderer.image-viewer-exist', legacy: [82], where: 'renderer.js', name: '智能看图: imageViewerExist=false',
      feature: 'APP_PARAM_ID_IMAGE_VIEWER_EXIST',
      find: /imageViewerExist:"1"===[\w$]+(?:\(\))?\.\$getSystemParamById\([\w$]+(?:\(\))?\.[\w$]+\.APP_PARAM_ID_IMAGE_VIEWER_EXIST\)/,
      repl: 'imageViewerExist:' + MARK + '!1' },

    { id: 'renderer.preview-download', legacy: [94], where: 'renderer.js', name: '精简: 非视频预览 = 下载后打开（其它窗口/列表）',
      feature: 'previewStartTime',
      // keep the 2nd arg name (`t`) and the "unsupported type" branch constant (t.from!==X.Y) from the same method
      find: /preview\(e,t\)\{e\.previewStartTime=Date\.now\(\);(?=[^]{0,1200}?t\.from!==[\w$]+\.[\w$]+\?[\w$.(),]+\(\{type:"info",title:"百度网盘提示",message:"暂不支持此类型文件预览)/,
      repl: (m, ctx) => {
        const text = ctx.text, i = ctx.index;
        const mod = moduleOf(text, i);
        const modEnd = i + 1500;
        const fromConst = /t\.from!==([\w$]+\.[\w$]+)\?/.exec(text.slice(i, i + 1500))[1];
        // `app` of `const{app:X,dialog:Y}=...` declared before the method in the same module
        let app = null;
        for (const x of text.slice(mod.start, i).matchAll(/\{app:([\w$]+),dialog:[\w$]+\}=/g)) app = x[1];
        if (!app) throw new Error('app binding not found');
        const store = storeRef(text, mod.start, modEnd, mod.req);
        const guard = ctx.options.downloadVideos ? '' : '!' + videoFileCondition('e', 'e.path||__p') + '&&';
        // No module-local binding: the method body declares `let i=...` (shadowing the webpack require param `i`),
        // so resolve the store in a default parameter (evaluated outside the body scope).
        return 'preview(e,t' + store.param + '){' + MARK + 'try{var __p=e&&(e.server_path||e.path),__f=t&&t.from;' +
          'if(__p&&"/"===String(__p).charAt(0)&&!/^\\/_pcs_\\.(knowledge_subscribe|sharedir_receive)/.test(String(__p))&&!e.from_uk&&!e.fromUk&&!e.shareInfo&&!e.share_info&&!e.sender&&!e.fileUrl&&!e.isChat&&' +
          `${guard}(!__f||(__f!==${fromConst}&&"conversation"!==__f))){var __d=!1,__go=function(){if(__d)return;__d=!0;` +
          `${store.ref}.dispatch("downloading/addDownloadTask",[{md5:e.md5||"",size:e.size,is_dir:0,server_path:String(__p),from_manual:1,downloadAndEditSwitch:!0}])};` +
          `try{${app}.$getLocalPathFromDownloadHistory(String(__p),function(c,l){if(__d)return;` +
          `if(0===c&&l&&window.require&&window.require("fs").existsSync(l)){__d=!0;${app}.$openImageViewer('"'+l+'"');return}__go()})}catch(__e){}` +
          'setTimeout(__go,1500);return Promise.resolve({fs_id:e.fs_id})}}catch(__e){}e.previewStartTime=Date.now();';
      } },

    { id: 'renderer.safebox-info', legacy: [97], where: 'renderer.js',
      name: '精简: safebox gating (右键/header-more/移动复制对话框 隐藏空间, operateShow)',
      feature: 'safeBoxInfo',
      find: /(\[[\w$]+\]\([\w$]+,[\w$]+\)\{)[\w$]+\.safeBoxInfo=[\w$]+\}/,
      repl: (m, head) => head + MARK + '}' },

    { id: 'renderer.route-recycle', legacy: [98], where: 'renderer.js', name: '精简: recycle bin route',
      feature: 'path:"/RecycleBin"',
      find: /\{path:"\/RecycleBin",name:"RecycleBin",component:\(\)=>[\w$]+\.e\(\d+\)\.then\((?:[\w$]+\.bind\(null,\d+(?:e\d+)?\)|\(\)=>[\w$]+\(\d+\))\)\},/,
      repl: '{path:"/RecycleBin",name:"RecycleBin",redirect:{name:"allFile"}},' + MARK },

    { id: 'renderer.route-safebox', legacy: [99], where: 'renderer.js', name: '精简: safebox route',
      feature: 'path:"/safebox"',
      find: /\{path:"\/safebox",name:"safebox",component:\(\)=>Promise\.all\(\[(?:[\w$]+\.e\(\d+(?:e\d+)?\),?)+\]\)\.then\((?:[\w$]+\.bind\(null,\d+(?:e\d+)?\)|\(\)=>[\w$]+\(\d+\))\)\},/,
      repl: '{path:"/safebox",name:"safebox",redirect:{name:"allFile"}},' + MARK },
  ],
  dropped: [],
};
