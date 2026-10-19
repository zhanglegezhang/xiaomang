import {
  closeSearchPanel, findNext, findPrevious, getSearchQuery, replaceAll,
  replaceNext, SearchQuery, selectMatches, setSearchQuery,
} from '@codemirror/search'
import { EditorSelection, EditorState, Prec } from '@codemirror/state'
import { EditorView, keymap, runScopeHandlers, type Panel, type ViewUpdate } from '@codemirror/view'
import { sourceSearchMarks } from './source-search-marks'
import type { RichMatch, SearchResult } from '../../../../../local/rich-search-engine'
const regexNavigation = new WeakMap<EditorView,(previous:boolean)=>boolean>()
export const sourceRegexKeymap = Prec.highest(keymap.of([
  {key:'F3',run:view=>regexNavigation.get(view)?.(false)??false,shift:view=>regexNavigation.get(view)?.(true)??false},
  {key:'Mod-g',run:view=>regexNavigation.get(view)?.(false)??false,shift:view=>regexNavigation.get(view)?.(true)??false},
]))

// Retain CodeMirror's query, matching, undo and keyboard behavior; only replace its panel.
export function compactSearchPanel(view: EditorView): Panel {
  const dom = document.createElement('div')
  dom.className = 'cm-search compact-search'
  dom.setAttribute('role', 'search')
  dom.setAttribute('aria-label', '当前笔记查找替换')
  let query = getSearchQuery(view.state)
  let regexActive = false, destroyed = false, worker: Worker | undefined, timer = 0, revision = 0
  let settle: ((ok:boolean)=>void) | undefined, pending: Promise<boolean> | undefined
  let matches: RichMatch[] = [], limited = false, searchedDoc = view.state.doc, signature = ''
  let activeRegexMatch:RichMatch|undefined
  const notice = document.createElement('div'); notice.setAttribute('role','status')
  function stop() { worker?.terminate(); worker=undefined; window.clearTimeout(timer); settle?.(false); settle=undefined }
  const input = (name: string, label: string) => {
    const field = document.createElement('input')
    field.name = name; field.type = 'text'; field.className = 'cm-textfield'
    field.placeholder = label; field.setAttribute('aria-label', label)
    field.autocomplete = 'off'; field.spellcheck = false
    field.addEventListener('input', commit)
    return field
  }
  const button = (name: string, label: string, text: string, action: () => void) => {
    const control = document.createElement('button')
    control.name = name; control.type = 'button'; control.className = 'cm-button'
    control.textContent = text; control.title = label; control.setAttribute('aria-label', label)
    control.addEventListener('click', action)
    return control
  }
  const searchField = input('search', '查找')
  searchField.setAttribute('main-field', 'true')
  const replaceField = input('replace', '替换为')
  const replacement = document.createElement('div')
  replacement.className = 'compact-replace-row'; replacement.hidden = true
  replacement.append(replaceField,
    button('replace', '替换', '替换', () => { void execute('replace') }),
    button('replaceAll', '全部替换', '全部替换', () => { void execute('all') }))
  const options = document.createElement('div')
  options.className = 'compact-search-options'; options.hidden = true
  const option = (name: string, text: string) => {
    const toggle = button(name, text, text, () => {
      toggle.setAttribute('aria-pressed', String(toggle.getAttribute('aria-pressed') !== 'true'))
      commit()
    })
    toggle.setAttribute('aria-pressed', 'false')
    options.append(toggle)
    return toggle
  }
  const caseField = option('case', '区分大小写')
  const reField = option('re', '正则')
  const wordField = option('word', '整词')
  options.append(button('select', '全选匹配', '全选匹配', () => {
    void execute('select')
  }))
  const toggleReplace = button('toggleReplace', '展开替换', '替换', () => {
    replacement.hidden = !replacement.hidden
    toggleReplace.setAttribute('aria-expanded', String(!replacement.hidden))
    toggleReplace.setAttribute('aria-label', replacement.hidden ? '展开替换' : '收起替换')
    toggleReplace.title = replacement.hidden ? '展开替换' : '收起替换'
    view.requestMeasure()
    if (!replacement.hidden) { replaceField.focus(); replaceField.select() }
  })
  toggleReplace.setAttribute('aria-expanded', 'false')
  const toggleOptions = button('toggleOptions', '查找选项', '选项', () => {
    options.hidden = !options.hidden
    toggleOptions.setAttribute('aria-expanded', String(!options.hidden))
    view.requestMeasure()
  })
  toggleOptions.setAttribute('aria-expanded', 'false')
  const row = document.createElement('div')
  row.className = 'compact-find-row'
  row.append(searchField,
    button('prev', '上一处', '↑', () => { void execute('prev') }),
    button('next', '下一处', '↓', () => { void execute('next') }),
    toggleReplace, toggleOptions,
    button('close', '关闭查找', '×', () => { closeSearchPanel(view) }))
  dom.append(row, replacement, options, notice)

  function commit() {
    if (destroyed) return
    regexActive = reField.getAttribute('aria-pressed') === 'true'
    if (regexActive) {
      const doc = view.state.doc
      const key = [searchField.value,caseField.getAttribute('aria-pressed'),wordField.getAttribute('aria-pressed')].join('\0')
      if (key === signature && searchedDoc === doc && pending) return
      signature = key; searchedDoc = doc; stop(); matches = []; limited = false;activeRegexMatch=undefined
      const own = ++revision
      // CodeMirror must never receive the user's regex: highlighting and commands
      // would otherwise execute it synchronously on the UI thread.
      view.dispatch({effects:[setSearchQuery.of(new SearchQuery({search:''})),sourceSearchMarks.of([])]})
      notice.textContent = searchField.value ? '正在查找…' : ''
      searchField.setAttribute('aria-invalid','false')
      pending = new Promise<boolean>(resolve => { settle = resolve })
      if (!searchField.value) { settle?.(true); return }
      try {
        worker = new Worker(new URL('./source-search.worker.ts', import.meta.url), {type:'module'})
        timer = window.setTimeout(()=>{ if(own!==revision)return; notice.textContent='查找超时，请简化正则表达式'; stop() },1000)
        worker.onerror = () => { notice.textContent='查找失败，请重试'; stop() }
        worker.onmessage = (event:MessageEvent<SearchResult>) => {
          if (own!==revision || searchedDoc!==view.state.doc) return
          matches=event.data.matches; limited=event.data.limited
          searchField.setAttribute('aria-invalid',String(event.data.invalid))
          notice.textContent=event.data.invalid?'正则表达式无效':limited?'匹配过多，请缩小范围':`${matches.length} 处匹配`
          view.dispatch({effects:sourceSearchMarks.of(matches)})
          window.clearTimeout(timer); worker?.terminate(); worker=undefined
          settle?.(!event.data.invalid); settle=undefined
        }
        worker.postMessage({text:doc.toString(),query:{search:searchField.value,regexp:true,
          caseSensitive:caseField.getAttribute('aria-pressed')==='true',wholeWord:wordField.getAttribute('aria-pressed')==='true'}})
      } catch { notice.textContent='查找不可用，请重试'; stop() }
      return
    }
    stop(); revision++; pending=undefined; signature=''; notice.textContent=''
    const next = new SearchQuery({ search: searchField.value, replace: replaceField.value,
      caseSensitive: caseField.getAttribute('aria-pressed') === 'true',
      regexp: false,
      wholeWord: wordField.getAttribute('aria-pressed') === 'true',
      literal: query.literal })
    view.dispatch({ effects: [sourceSearchMarks.of([]), setSearchQuery.of(next)] })
  }
  async function execute(action:'next'|'prev'|'replace'|'all'|'select') {
    commit()
    if (!regexActive) {
      if(action==='next')findNext(view);else if(action==='prev')findPrevious(view)
      else if(action==='replace')replaceNext(view);else if(action==='all')replaceAll(view);else if(selectMatches(view))view.focus()
      return
    }
    const own=revision
    if (!await pending || own!==revision || searchedDoc!==view.state.doc || !matches.length) return
    const selected=view.state.selection.main
    const available=matches.filter(m=>!(m.from===m.to&&m===activeRegexMatch&&m.from===selected.from&&m.to===selected.to))
    const next=action==='prev'?[...available].reverse().find(m=>m.to<=selected.from)||matches[matches.length-1]
      :available.find(m=>m.from>=selected.to)||matches[0]
    const replacementFor=(match:RichMatch)=>{
      const text=replaceField.value.replace(/\\([nrt\\])/g,(_,ch)=>ch==='n'?'\n':ch==='r'?'\r':ch==='t'?'\t':'\\')
      return text.replace(/\$([$&]|\d+)/g,(token,group:string)=>{
        if(group==='$')return '$';if(group==='&')return match.text
        for(let length=group.length;length>0;length--){const n=Number(group.slice(0,length));if(n>0&&n<match.captures.length)return (match.captures[n]||'')+group.slice(length)}
        return token
      })
    }
    if(action==='all'||action==='select') {
      if(limited){notice.textContent='匹配过多，请缩小范围后再操作';return}
      if(action==='select')view.dispatch({selection:EditorSelection.create(matches.map(m=>EditorSelection.range(m.from,m.to)))})
      else if(!view.state.readOnly)view.dispatch({changes:matches.map(m=>({from:m.from,to:m.to,insert:replacementFor(m)})),userEvent:'input.replace.all'})
    } else if(action==='replace') {
      const current=matches.find(m=>m.from===selected.from&&m.to===selected.to)
      if(current&&!view.state.readOnly)view.dispatch({changes:{from:current.from,to:current.to,insert:replacementFor(current)},selection:{anchor:current.from+replacementFor(current).length},userEvent:'input.replace'})
      else view.dispatch({selection:{anchor:next.from,head:next.to},effects:EditorView.scrollIntoView(next.from)})
    } else {activeRegexMatch=next;view.dispatch({selection:{anchor:next.from,head:next.to},effects:EditorView.scrollIntoView(next.from),userEvent:'select.search'})}
  }
  regexNavigation.set(view,previous=>{if(!regexActive)return false;void execute(previous?'prev':'next');return true})
  function sync() {
    if (regexActive) return
    query = getSearchQuery(view.state)
    if (searchField.value !== query.search) searchField.value = query.search
    if (replaceField.value !== query.replace) replaceField.value = query.replace
    caseField.setAttribute('aria-pressed', String(query.caseSensitive))
    reField.setAttribute('aria-pressed', String(query.regexp))
    wordField.setAttribute('aria-pressed', String(query.wholeWord))
    searchField.setAttribute('aria-invalid', String(!!query.search && !query.valid))
    toggleOptions.classList.toggle('has-options', query.caseSensitive || query.regexp || query.wholeWord)
    toggleReplace.hidden = view.state.facet(EditorState.readOnly)
    if (toggleReplace.hidden) { replacement.hidden = true; toggleReplace.setAttribute('aria-expanded', 'false') }
  }
  dom.addEventListener('keydown', event => {
    if(regexActive && !event.isComposing) {
      if(event.key==='F3'||((event.ctrlKey||event.metaKey)&&!event.altKey&&event.key.toLowerCase()==='g')){event.preventDefault();void execute(event.shiftKey?'prev':'next');return}
      if(event.key==='Escape'){event.preventDefault();stop();closeSearchPanel(view);return}
      if(event.key==='Enter'){event.preventDefault();void execute(event.target===replaceField?'replace':event.shiftKey?'prev':'next');return}
    }
    if (runScopeHandlers(view, event, 'search-panel')) { event.preventDefault(); return }
    if (event.key !== 'Enter' || event.isComposing) return
    if (event.target === searchField) {
      event.preventDefault(); commit(); (event.shiftKey ? findPrevious : findNext)(view)
    } else if (event.target === replaceField) { event.preventDefault(); commit(); replaceNext(view) }
  })
  sync()
  return { dom, top: true,
    mount() { searchField.focus(); searchField.select() },
    update(update: ViewUpdate) { if(regexActive && update.docChanged)queueMicrotask(()=>commit());else sync() },
    destroy() { destroyed=true;revision++;stop();regexNavigation.delete(view) },
  }
}
