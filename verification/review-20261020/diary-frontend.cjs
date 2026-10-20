// Runs the shipped static frontend with an isolated in-memory Tauri disk bridge.
// Native clipboard/file persistence is also covered by the Rust diary tests.
const fs = require('fs'), path = require('path'), http = require('http'), assert = require('assert/strict');
const { chromium } = require(process.env.DIARY_PLAYWRIGHT);
const root = path.resolve(process.env.DIARY_FRONTEND || path.join(__dirname,'../../src/Diary/out'));
const report = path.resolve(process.env.DIARY_REPORT || path.join(__dirname,'../../test-results/review-20261020'));
fs.mkdirSync(report,{recursive:true});
const fixture = 'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+a5e8AAAAASUVORK5CYII=';
const large = fixture;
const server = http.createServer((req,res) => {
  const requested = path.resolve(root, '.' + decodeURIComponent(req.url.split('?')[0] === '/' ? '/index.html' : req.url.split('?')[0]));
  if (!requested.startsWith(root + path.sep)) { res.writeHead(403).end(); return; }
  fs.readFile(requested,(error,data) => {
    if(error){res.writeHead(404).end();return;}
    const ext=path.extname(requested);res.setHeader('Content-Type',({'.html':'text/html','.js':'text/javascript','.css':'text/css','.png':'image/png'})[ext]||'text/plain');res.end(data);
  });
});
const passed=[];function pass(name){passed.push(name);console.log('PASS '+name);}
(async()=>{
  await new Promise(resolve=>server.listen(0,'127.0.0.1',resolve));
  const browser=await chromium.launch({channel:'msedge',headless:true});
  try {
    const page=await browser.newPage({viewport:{width:1100,height:760}});
    const errors=[];page.on('pageerror',e=>errors.push(e.message));
    await page.addInitScript(({fixture})=>{
      window.testStore={notes:{'测试.md':'开始\n\n末尾','另篇.md':'另篇','纯文.txt':'# 标题\r\n**原样文本**\r\n中文','子目录/内部.md':'内部','工具测试.md':'格式文本'},writes:[],assets:{},callbacks:{},events:{},destroyed:false,failSave:false};
      const s=window.testStore;
s.notes={'A.md':'alpha\n\nsecond','B.md':'beta','C.txt':'plain','Empty.md':'','HTML.md':'# Example\n\n'+String.fromCharCode(96).repeat(3)+'html\n<div>safe</div>\n'+String.fromCharCode(96).repeat(3),'Image.md':'![preview](diary-asset:preview.png)','子目录/Child.txt':'child'};
for(let i=0;i<12;i++)s.notes['文档'+i+'-长名称用于观察页签是否自动滚动.md']='doc'+i;
s.assets['preview.png']=fixture;
      const tree=()=>{const roots=[{name:'空目录',path:'空目录',folder:true,children:[]},{name:'仅有子目录',path:'仅有子目录',folder:true,children:[{name:'空子目录',path:'仅有子目录/空子目录',folder:true,children:[]}]}];for(const path of Object.keys(s.notes)){let nodes=roots;const parts=path.split('/');parts.forEach((name,index)=>{const folder=index<parts.length-1;const current=parts.slice(0,index+1).join('/');let node=nodes.find(n=>n.path===current);if(!node){node={name,path:current,folder,children:[]};nodes.push(node)}nodes=node.children})}const paint=nodes=>nodes.forEach(n=>{if(n.path==='子目录')n.color='#3976b8';if(n.path==='测试.md')n.color='#b85a39';paint(n.children)});paint(roots);return roots};
      window.__TAURI_EVENT_PLUGIN_INTERNALS__={unregisterListener(){}};
      window.__TAURI_INTERNALS__={metadata:{currentWindow:{label:'main'}},transformCallback(fn){const id=Object.keys(s.callbacks).length+1;s.callbacks[id]=fn;return id},
        async invoke(command,a){
          if(command==='plugin:dialog|open')return a.options.directory ? (s.cancelExport ? null : 'C:/isolated/exports') : 'C:/isolated/legacy.txt';
          if(command==='plugin:dialog|save')return 'C:/isolated/export.txt';
          if(command==='plugin:event|listen'){s.events[a.event]=a.handler;return a.handler;}
          if(command==='plugin:event|unlisten')return;
          if(command==='plugin:window|destroy'){s.destroyed=true;return;}
          if(command!=='diary_command')throw Error('Unexpected IPC '+command);
          const v=a.args;
          if(a.op==='batch_color'){s.batchColor=v;return true;}
          if(a.op==='batch_export'){s.batchExport={...v,notes:{...s.notes}};if(s.exportFailure)throw Error('模拟导出失败');return {destination:'C:/isolated/exports/小蟒日记导出-测试',files:3};}
          if(a.op==='batch_move'){
            s.batchMove=v; if(s.moveConflict)throw Error('目标已有同名项目，未移动任何项目');
            const completed=[],failed=[];
            v.paths.forEach((path,i)=>{if(s.partialMove&&i>0){failed.push({path,error:'模拟后续项目失败'});return;}const next=(v.parent?v.parent+'/':'')+path.split('/').pop();const remapped={};for(const [key,text] of Object.entries(s.notes))remapped[key===path||key.startsWith(path+'/')?next+key.slice(path.length):key]=text;s.notes=remapped;completed.push({path,next});});return {completed,failed};
          }
          if(a.op==='batch_delete'){s.batchDelete=v;v.paths.forEach(path=>Object.keys(s.notes).filter(key=>key===path||key.startsWith(path+'/')).forEach(key=>delete s.notes[key]));return {completed:v.paths.map(path=>({path})),failed:[]};}

          if(a.op==='import_encoding')return {encoding:null};
          if(a.op==='import'){if(v.encoding!=='gbk')throw Error('编码不匹配');s.importArgs=v;s.notes['legacy.txt']='中文导入';return 'legacy.txt';}
          if(a.op==='export'){s.exportArgs=v;return true;}
          if(a.op==='init')return {root:'fixture',tree:tree()};if(a.op==='tree')return tree();
          if(a.op==='trash')return [];
          if(a.op==='search')return {results:Object.entries(s.notes).filter(([path,text])=>path.includes(v.query)||text.includes(v.query)).map(([path,text])=>({path,matches:[{line:1,snippet:text}]})),limited:false,skipped:0};
          if(a.op==='create'){s.lastCreate=v;const path=(v.parent?v.parent+'/':'')+v.name+'.'+(v.format||'md');s.notes[path]='';return path;}
          if(a.op==='delete'){delete s.notes[v.path];return;} if(a.op==='read')return {content:s.notes[v.path] || '',revision:'r'+s.writes.length};
          if(a.op==='save'){if(s.failSave)throw Error('测试保存失败');s.notes[v.path]=v.content;s.writes.push({path:v.path,content:v.content,time:Date.now()});return {revision:'r'+s.writes.length};}
          if(a.op==='asset_bytes'){const id='paste-'+Object.keys(s.assets).length+'.'+v.extension;s.assets[id]=v.data;return id;}
          if(a.op==='asset_data')return 'data:image/png;base64,'+s.assets[v.id];
          if(a.op==='clipboard_image_files'){s.assets['explorer.png']=fixture;return ['explorer.png'];}
          if(a.op==='reorder'){s.reorder=v;return true;}if(a.op==='visit')return true;if(a.op==='take_open_request')return null;throw Error('Unexpected op '+a.op);
        }};
    },{fixture});
    page.setDefaultTimeout(10000);

    const row=p=>page.locator('.tree-row[title="'+p+'"] .tree-open');
    async function fresh(){if(page.url()!=='about:blank')await page.evaluate(()=>localStorage.clear());await page.goto('http://127.0.0.1:'+server.address().port);await row('A.md').waitFor();
      await page.evaluate(()=>{const original=window.__TAURI_INTERNALS__.invoke;window.__TAURI_INTERNALS__.invoke=async(c,a)=>{
        const s=window.testStore;if(c==='plugin:dialog|open'){s.openDialogs=(s.openDialogs||0)+1;if(s.cancelOpen)return null;}
        if(c==='diary_command'){
          if(a.op==='save'&&s.conflict&&a.args.path==='C.txt')throw Error('文件已被其他程序修改。当前内容仍在编辑器中，请导出副本后重新打开，避免覆盖');
          if(a.op==='read'&&s.readFailure&&a.args.path==='C.txt')throw Error('模拟读取失败');
          if(a.op==='export'&&s.exportFailure)throw Error('模拟导出失败');
          if(a.op==='import'){s.imports=(s.imports||0)+1;if(s.importUnreadable){s.notes['legacy.txt']='created';s.unreadable='legacy.txt';return 'legacy.txt';}}
          if(a.op==='read'&&s.unreadable===a.args.path)throw Error('日记超过 10 MB，请拆分文件后打开');
        }return original(c,a);
      }})}
    async function note(p){await row(p).click();await page.waitForFunction(p=>document.querySelector('.tab.active .tab-open')?.title===p,p)}
    async function draft(){await note('C.txt');await page.locator('.cm-content').fill('local draft');await page.evaluate(()=>{window.testStore.conflict=true;window.testStore.notes['C.txt']='external version'});await page.keyboard.press('Control+s');await page.getByRole('button',{name:'导出草稿副本',exact:true}).waitFor()}
    await fresh();await draft();
    const reload=()=>page.getByRole('button',{name:'重新读取磁盘版本',exact:true});
    await page.screenshot({path:path.join(report,'conflict-normal.png')});await page.setViewportSize({width:800,height:600});await page.screenshot({path:path.join(report,'conflict-narrow.png')});await page.setViewportSize({width:1100,height:760});
    assert(await reload().isDisabled());assert.equal(await page.locator('.cm-content').textContent(),'local draft');pass('B01 conflict protects local draft and blocks reload until export');
    await page.evaluate(()=>window.testStore.exportFailure=true);await page.getByRole('button',{name:'导出草稿副本',exact:true}).click();await page.getByRole('alert').filter({hasText:'模拟导出失败'}).waitFor();assert(await reload().isDisabled());pass('B01 export failure never permits discard');
    await page.evaluate(()=>window.testStore.exportFailure=false);await page.getByRole('button',{name:'导出草稿副本',exact:true}).click();await page.waitForFunction(()=>!document.querySelector('.draft-conflict button:last-child').disabled);
    assert.equal(await page.evaluate(()=>window.testStore.exportArgs.content),'local draft');await reload().click();await page.getByRole('dialog').getByRole('button',{name:'取消',exact:true}).click();assert.equal(await page.locator('.cm-content').textContent(),'local draft');pass('B01 cancel keeps draft and exported copy');
    await page.locator('.cm-content').fill('new local draft');assert(await reload().isDisabled());pass('B01 edits after export require another export');
    await page.getByRole('button',{name:'导出草稿副本',exact:true}).click();await page.waitForFunction(()=>!document.querySelector('.draft-conflict button:last-child').disabled);await page.evaluate(()=>window.testStore.readFailure=true);await reload().click();await page.getByRole('dialog').getByRole('button',{name:'确定',exact:true}).click();await page.getByRole('alert').filter({hasText:'模拟读取失败'}).first().waitFor();assert.equal(await page.locator('.cm-content').textContent(),'new local draft');pass('B01 failed reload preserves draft and confirmation');
    await page.evaluate(()=>{window.testStore.readFailure=false;window.testStore.conflict=false});await page.getByRole('dialog').getByRole('button',{name:'确定',exact:true}).click();await page.waitForFunction(()=>!document.querySelector('.draft-conflict'));assert.equal(await page.locator('.cm-content').textContent(),'external version');
    await page.keyboard.press('Control+z');assert.equal(await page.locator('.cm-content').textContent(),'external version');await note('A.md');pass('B01 confirmed reload reads disk, resets old undo and permits switching');
    await fresh();await note('C.txt');await page.locator('.cm-content').fill('unsaved remains');await page.evaluate(()=>window.testStore.notes['external.txt']='new externally');await page.getByRole('button',{name:'刷新目录（保留未保存内容）',exact:true}).click();await row('external.txt').waitFor();assert.equal(await page.locator('.cm-content').textContent(),'unsaved remains');assert.equal(await page.evaluate(()=>window.testStore.writes.length),0);pass('O04 refresh discovers external addition without saving or replacing the draft');
    await page.setViewportSize({width:800,height:600});assert(await page.getByRole('button',{name:'刷新目录（保留未保存内容）',exact:true}).isVisible());pass('O04 refresh remains reachable in narrow window');await page.setViewportSize({width:1100,height:760});
    await fresh();for(const p of ['A.md','B.md','C.txt','Empty.md','HTML.md','Image.md',...Array.from({length:12},(_,i)=>'文档'+i+'-长名称用于观察页签是否自动滚动.md'),'子目录','子目录/Child.txt']){if(p==='子目录/Child.txt')await page.locator('.tree-row[title="子目录"] .tree-toggle').click();await note(p)}
    assert.equal(await page.locator('.tab').count(),20);await page.getByRole('button',{name:'导入日记',exact:true}).click();await page.getByRole('alert').filter({hasText:'先关闭一个页签再导入'}).waitFor();assert.equal(await page.evaluate(()=>window.testStore.openDialogs||0),0);assert.equal(await page.evaluate(()=>window.testStore.imports||0),0);pass('B03 full tabs reject import before dialog and disk operation');
    await fresh();await page.getByRole('button',{name:'导入日记',exact:true}).click();await page.getByRole('dialog').getByRole('button',{name:'确定',exact:true}).click();await page.waitForFunction(()=>document.querySelector('.tab.active .tab-open')?.title==='legacy.txt');assert.equal(await page.locator('.cm-content').textContent(),'中文导入');pass('B03 normal encoding selection and import still opens');
    await fresh();await page.evaluate(()=>window.testStore.importUnreadable=true);await page.getByRole('button',{name:'导入日记',exact:true}).click();await page.getByRole('dialog').getByRole('button',{name:'确定',exact:true}).click();await page.getByRole('alert').filter({hasText:'文件已导入到 legacy.txt，但尚未打开'}).first().waitFor();assert.equal(await page.locator('.tab').count(),0);pass('B03 post-import open failure remains visible with imported path');
    assert.equal(errors.length,0,JSON.stringify(errors));pass('No frontend runtime errors');
    fs.writeFileSync(path.join(report,'checks.json'),JSON.stringify(passed,null,2));
  }finally{await browser.close();server.close()}
})().catch(e=>{console.error(e);server.close();process.exitCode=1});
