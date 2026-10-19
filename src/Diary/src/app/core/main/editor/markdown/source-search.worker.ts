import type { RichQuery, SearchResult } from '../../../../../local/rich-search-engine'

self.onmessage = (event: MessageEvent<{ text: string; query: RichQuery }>) => {
  const {text,query}=event.data
  const result:SearchResult={matches:[],invalid:false,limited:false}
  let expression:RegExp
  try { expression=new RegExp(query.search,query.caseSensitive?'gmu':'gmiu') }
  catch { result.invalid=true;self.postMessage(result);return }
  const word=(value:string)=>/[\p{L}\p{N}_]/u.test(value)
  let match:RegExpExecArray|null,characters=0
  while((match=expression.exec(text))) {
    const from=match.index,to=from+match[0].length
    if(!match[0].length)expression.lastIndex=from+(text.codePointAt(from)!>0xFFFF?2:1)
    if(query.wholeWord&&match[0].length) {
      const before=[...text.slice(Math.max(0,from-2),from)].pop()||''
      const first=String.fromCodePoint(text.codePointAt(from)!),after=text.codePointAt(to)
      const last=[...text.slice(Math.max(from,to-2),to)].pop()||''
      if((word(before)&&word(first))||(word(last)&&word(after===undefined?'':String.fromCodePoint(after))))continue
    }
    characters+=match.reduce((n,c)=>n+(c?.length||0),0)
    if(characters>2*1024*1024||result.matches.length>=10000){result.limited=true;break}
    match.input=''
    result.matches.push({from,to,text:match[0],captures:match,block:''})
  }
  self.postMessage(result)
}
