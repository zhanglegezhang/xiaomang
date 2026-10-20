const fs=require('fs'),path=require('path');const {chromium}=require(process.env.DIARY_PLAYWRIGHT);
const root=path.resolve(process.argv[2]||'output/reported-bugs-20261020/scroll');fs.mkdirSync(root,{recursive:true});
(async()=>{const browser=await chromium.launch({channel:'msedge',headless:true});try{
 const results=[];
 for(const kind of ['normal','short-final-scroll','blank-tail','fixed-footer','edge-only-tail']){
  const dir=path.join(root,kind);fs.mkdirSync(dir,{recursive:true});
  const height=kind==='short-final-scroll'?2030:2400,blank=kind==='edge-only-tail'?1200:kind==='blank-tail'?420:0;
  const html=`<!doctype html><meta charset="utf-8"><title>小蟒长截图隔离验证 ${kind}</title><style>*{box-sizing:border-box}body{margin:0;font:20px Arial}section{height:120px;border-bottom:1px solid #ccc;padding:24px;background:linear-gradient(90deg,#f4f6fa,white)}.end{position:absolute;top:${height-60}px;height:60px;width:100%;background:#f414a0;color:white;padding:16px}.fixed{position:fixed;bottom:0;height:24px;background:#222;color:white;width:100%;font-size:14px}</style><main style="height:${height}px">${Array.from({length:Math.floor((height-60-blank)/120)},(_,i)=>`<section>ROW ${i+1} — ${kind}<span style="margin-left:160px">Content ${i*i+17}</span></section>`).join('')}<div class="end">END-OF-PAGE-20261020 — LAST LINE</div></main>${kind==='fixed-footer'?'<div class="fixed">fixed browser toolbar</div>':''}`;
  const pageHtml=kind==='edge-only-tail'?html.replace('<div class="end">END-OF-PAGE-20261020 — LAST LINE</div>','<div class="end" style="background:white;padding:0"><span style="display:block;width:40px;height:60px;background:#f414a0;font-size:10px">END</span></div>'):html;
  fs.writeFileSync(path.join(dir,'page.html'),pageHtml);
  const page=await browser.newPage({viewport:{width:900,height:600},deviceScaleFactor:1});await page.goto('file:///'+path.join(dir,'page.html').replace(/\\/g,'/'));
  const positions=[];for(let y=0;y<height-600;y+=180)positions.push(y);positions.push(height-600);
  const files=[];for(let i=0;i<positions.length;i++){await page.evaluate(y=>window.scrollTo(0,y),positions[i]);await page.screenshot({path:path.join(dir,i+'.png')});files.push(i+'.png');}
  await page.screenshot({path:path.join(dir,'expected-full.png'),fullPage:true});
  results.push({kind,height,positions,files,directory:dir});await page.close();
 }
 fs.writeFileSync(path.join(root,'cases.json'),JSON.stringify(results,null,2));console.log(JSON.stringify(results.map(x=>({kind:x.kind,frames:x.files.length,height:x.height}))));
}finally{await browser.close()}})().catch(e=>{console.error(e);process.exitCode=1});
