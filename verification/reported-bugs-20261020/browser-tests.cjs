const fs=require('fs'),path=require('path'),assert=require('node:assert/strict');
const {chromium}=require(process.env.DIARY_PLAYWRIGHT);
const root=path.resolve('output/reported-bugs-20261020/browser');fs.mkdirSync(root,{recursive:true});
(async()=>{const browser=await chromium.launch({channel:'msedge',headless:true});try{
 const page=await browser.newPage({acceptDownloads:true,viewport:{width:1100,height:850}});const errors=[];page.on('pageerror',e=>errors.push(e.message));
 await page.goto('file:///'+path.resolve('tools/ShareX/app/数据整理小工具_V1.0.html').replace(/\\/g,'/'));
 await page.locator('#delim').selectOption(',');await page.locator('#colIdx').fill('2');await page.locator('#input').fill('"A,B",X\n"C,B",Y');
 await page.getByRole('button',{name:'按列去重',exact:true}).click();assert.equal(await page.locator('#output').inputValue(),'"A,B",X\n"C,B",Y');
 await page.locator('#input').fill('select distinct 1');await page.getByRole('button',{name:'SQL 美化',exact:true}).click();assert.match(await page.locator('#output').inputValue(),/SELECT DISTINCT\s+1/);
 let message='';page.once('dialog',async d=>{message=d.message();await d.dismiss();});await page.locator('#input').fill('b,1\na,2');await page.locator('#colIdx').fill('0');const before=await page.locator('#output').inputValue();await page.getByRole('button',{name:'升序',exact:true}).click();assert.ok(message.includes('正整数'));assert.equal(await page.locator('#output').inputValue(),before);
 await page.locator('#input').fill('');await page.getByRole('button',{name:'添加符号',exact:true}).click();assert.equal(await page.locator('#output').inputValue(),'');
 await page.locator('#input').fill('  A \t00123');await page.locator('#delim').selectOption('tab');await page.locator('#colIdx').fill('1');await page.getByRole('button',{name:'整行去重',exact:true}).click();
 for(const type of ['csv','xlsx']){const pending=page.waitForEvent('download');await page.getByRole('button',{name:'.'+type,exact:true}).click();const download=await pending;await download.saveAs(path.join(root,'export.'+type));}
 assert.equal(fs.readFileSync(path.join(root,'export.csv'),'utf8').replace(/^\uFEFF/,''),'  A ,00123');
 const timing=await page.evaluate(()=>{const out=[];for(const n of [1000,10000,50000]){document.getElementById('input').value=Array.from({length:n},(_,i)=>'"item,'+i+'",'+(n-i)).join('\n');document.getElementById('delim').value='auto';document.getElementById('colIdx').value='2';const start=performance.now();doSort(1);out.push({rows:n,ms:performance.now()-start});}return out;});
 await page.locator('#input').fill('"A,B",X\n"C,B",Y');await page.locator('#colIdx').fill('2');await page.getByRole('button',{name:'按列去重',exact:true}).click();await page.screenshot({path:path.join(root,'normal.png'),fullPage:true});
 await page.setViewportSize({width:620,height:850});await page.screenshot({path:path.join(root,'narrow.png'),fullPage:true});assert.equal(errors.length,0,errors.join());
 fs.writeFileSync(path.join(root,'results.json'),JSON.stringify({Checks:8,RuntimeErrors:errors,SortTiming:timing},null,2));console.log('PASS 8 browser UI/export checks');console.log(JSON.stringify(timing));
}finally{await browser.close()}})().catch(e=>{console.error(e);process.exitCode=1});
