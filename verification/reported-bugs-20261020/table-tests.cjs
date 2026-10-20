// Offline, isolated browser-function regression; no personal data or network access.
const fs=require('fs'),path=require('path'),vm=require('vm'),assert=require('node:assert/strict');
const input=process.argv[2]||'resources/Tools/数据整理小工具_V1.0.html';
const report=process.argv[3]||'output/reported-bugs-20261020';fs.mkdirSync(report,{recursive:true});
const html=fs.readFileSync(input,'utf8'),fields={},alerts=[],downloads=[];
const el=id=>fields[id]??={value:'',checked:false,textContent:'',style:{},scrollTop:0,clientHeight:400,addEventListener(){}};
const sandbox={document:{getElementById:el},window:{addEventListener(){}},alert:t=>alerts.push(t),TextEncoder,Uint8Array,Uint32Array,DataView,Blob,console};
vm.createContext(sandbox);vm.runInContext(html.match(/<script>([\s\S]*?)<\/script>/i)[1],sandbox);
sandbox.downloads=downloads;vm.runInContext('triggerDownload=(blob,name)=>downloads.push({blob,name})',sandbox);
let count=0;function check(name,test){test();count++;console.log('PASS '+name);}
function run(text,operation,options={}){
 el('input').value=text;el('output').value='previous result';el('delim').value=options.delim??',';el('colIdx').value=options.column??'2';el('skipEmpty').checked=options.skip??false;el('suffix').value=',';alerts.length=0;
 vm.runInContext(operation,sandbox);return el('output').value;
}
const sqlPairs=[];
const sqlCases=[
 'select distinct 1','select all 1','select brand from t where brand=1',
 "select 'a  b', 'it''s distinct', \"brand\" from t",
 'select distinct brand, count(*) as total from t group by brand order by brand',
 'select brand from t -- where brand=0\nwhere brand=1',
 'select /* keep  two spaces */ brand from t where brand in (select brand from t)',
 'select case when brand=1 then 2 else 3 end as x from t',
 'select 1 union all select 2','select brand from t where brand between 1 and 2',
 'select distinct 1 + 2','select brand as [br]]and] from t',
 "select $$a  'b from where$$",'select `br``and` from `fromage`',
 'select brand from t where brand is not null; -- tail',
 "select 'a\n  b' as x from t",'select brand from t where brand=1 and brand<>2',
 'select brand from t order by brand limit 2',
];
for(const sql of sqlCases)check('BUG-01 preserves SQL: '+sql.replace(/\n/g,' '),()=>{
 const output=run(sql,'doSqlFormat()');assert.equal(alerts.length,0,alerts.join());assert.notEqual(output,'previous result');
 sqlPairs.push({sql,output});if(/^select distinct /i.test(sql))assert.match(output,/DISTINCT/);if(sql.includes('brand'))assert.match(output,/brand/);
});
for(const sql of ["select 'unclosed",'select (1','select /* unclosed'])check('BUG-01 invalid SQL keeps result',()=>{assert.equal(run(sql,'doSqlFormat()'),'previous result');assert.equal(alerts.length,1);});
check('BUG-02 distinct quoted fields survive dedup',()=>assert.equal(run('"A,B",X\n"C,B",Y','doDedup(true)'),'"A,B",X\n"C,B",Y'));
check('BUG-02 equal decoded keys deduplicate',()=>assert.equal(run('"A,B",X\n"C,B",X','doDedup(true)'),'"A,B",X'));
check('BUG-02 escaped quotes and embedded newline survive',()=>assert.equal(run('"A\nB","x""y"\nC,z','doDedup(true)'),'"A\nB","x""y"\nC,z'));
check('BUG-02 sort quoted CSV numerically',()=>assert.equal(run('"A,B",10\n"C,B",2','doSort(1)'),'"C,B",2\n"A,B",10'));
check('BUG-04 auto comma uses requested column',()=>assert.equal(run('x,10\ny,2','doSort(1)',{delim:'auto'}),'y,2\nx,10'));
check('BUG-04 auto TSV uses requested column',()=>assert.equal(run('x\t10\ny\t2','doSort(1)',{delim:'auto'}),'y\t2\nx\t10'));
check('BUG-04 auto semicolon ignores quoted commas',()=>assert.equal(run('"x,a";10\n"y,a";2','doSort(1)',{delim:'auto'}),'"y,a";2\n"x,a";10'));
for(const column of ['0','-1','1.5','abc','','99999999999999999999','3'])check('BUG-05 reject invalid or missing column '+column,()=>{assert.equal(run('x,1\ny,2','doSort(1)',{column}),'previous result');assert.equal(alerts.length,1);});
check('BUG-05 dedup rejects column zero',()=>{assert.equal(run('a,b','doDedup(true)',{column:'0'}),'previous result');assert.equal(alerts.length,1);});
check('malformed CSV never overwrites results',()=>{assert.equal(run('"open,x','doDedup(true)'),'previous result');assert.equal(alerts.length,1);});
check('BUG-06 MySQL identifier escapes backtick',()=>assert.equal(run('a`b',"doSqlColToRow('backtick')"),'`a``b`'));
check('SQL Server identifier escaping unchanged',()=>assert.equal(run('a]b',"doSqlColToRow('bracket')"),'[a]]b]'));
check('ANSI identifier escaping unchanged',()=>assert.equal(run('a"b',"doSqlColToRow('dquote')"),'"a""b"'));
for(const skip of [true,false])check('BUG-07 empty suffix never creates data skip='+skip,()=>assert.equal(run('','doSuffix()',{skip}),''));
check('explicit per-cell trimming preserves CSV structure',()=>assert.equal(run('" a,b ", c ','doTrim(true)'),'"a,b",c'));
check('plain text line operations do not demand valid CSV',()=>assert.equal(run('"unfinished\n"unfinished','doDedup(false)'),'"unfinished'));
check('spaces remain part of deduplication keys',()=>assert.equal(run('x, a\ny,a','doDedup(true)'),'x, a\ny,a'));
check('numeric cell with trailing newline must remain text',()=>assert.equal(vm.runInContext("isNumericCell('0\\n')",sandbox),false));
function zipEntry(bytes,name){const b=Buffer.from(bytes);let i=0;while(b.readUInt32LE(i)===0x04034b50){const size=b.readUInt32LE(i+18),n=b.readUInt16LE(i+26),extra=b.readUInt16LE(i+28),start=i+30+n+extra;if(b.toString('utf8',i+30,i+30+n)===name)return b.toString('utf8',start,start+size);i=start+size;}throw Error('Missing ZIP entry '+name);}
(async()=>{
 el('delim').value=',';el('output').value='"  A,B  ", 00123 ,"x""y"\nC,"multi\nline",-2';downloads.length=0;vm.runInContext("downloadOut('csv');downloadOut('xlsx')",sandbox);
 const csv=await downloads[0].blob.text(),sheet=zipEntry(await downloads[1].blob.arrayBuffer(),'xl/worksheets/sheet1.xml');
 check('BUG-03 CSV preserves spaces and quoted records',()=>assert.equal(csv.replace(/^\uFEFF/,''),'"  A,B  ", 00123 ,"x""y"\r\nC,"multi\nline",-2'));
 check('BUG-03 XLSX preserves field spaces',()=>{assert.ok(sheet.includes('>  A,B  </t>'));assert.ok(sheet.includes('> 00123 </t>'));});
 check('XLSX keeps multiline, escaped quote and numeric values',()=>{assert.ok(sheet.includes('>multi\nline</t>'));assert.ok(sheet.includes('x&quot;y'));assert.ok(sheet.includes('<v>-2</v>'));});
 const timings=[];
 for(const n of [1000,10000,50000]){const text=Array.from({length:n},(_,i)=>'"item,'+i+'",'+(n-i)).join('\n');const start=performance.now();run(text,'doSort(1)',{delim:'auto'});timings.push({operation:'CSV sort',rows:n,ms:performance.now()-start});assert.equal(alerts.length,0);}
 const sql=Array.from({length:1000},()=>"select distinct brand from t where brand=1 and brand<>2;").join('\n'),start=performance.now();run(sql,'doSqlFormat()');timings.push({operation:'SQL format',characters:sql.length,ms:performance.now()-start});assert.equal(alerts.length,0);
 fs.writeFileSync(path.join(report,'sql-pairs.json'),JSON.stringify(sqlPairs,null,2));fs.writeFileSync(path.join(report,'table-results.json'),JSON.stringify({checks:count,timings},null,2));
 console.log('RESULT '+count+' checks passed');console.log(JSON.stringify(timings));
})().catch(e=>{console.error(e);process.exitCode=1});
