'use client'
import { useEffect, useRef, useState } from 'react'
import type { Editor } from '@tiptap/core'
import { TextSelection } from '@tiptap/pm/state'
import { closeHistory } from '@tiptap/pm/history'
import { emptyRichQuery, richReplacement, richSearchKey } from './rich-search'

export default function RichSearchPanel({ editor, open, onOpen, onClose }: { editor: Editor; open: boolean; onOpen: () => void; onClose: () => void }) {
  const [query, setQuery] = useState(emptyRichQuery)
  const [replacement, setReplacement] = useState('')
  const [showReplace, setShowReplace] = useState(false), [showOptions, setShowOptions] = useState(false)
  const [announcement, setAnnouncement] = useState('')
  const [, refresh] = useState(0)
  const searchField = useRef<HTMLInputElement>(null), replaceField = useRef<HTMLInputElement>(null)
  useEffect(() => {
    const update = () => refresh(version => version + 1)
    editor.on('transaction', update)
    return () => { editor.off('transaction', update) }
  }, [editor])
  useEffect(() => {
    editor.view.dispatch(editor.state.tr.setMeta(richSearchKey, { query: open ? query : emptyRichQuery }).setMeta('addToHistory', false))
    setAnnouncement('')
  }, [editor, open, query])
  useEffect(() => { if (open) { searchField.current?.focus(); searchField.current?.select() } }, [open])
  useEffect(() => {
    const keyboard = (event: KeyboardEvent) => {
      const container = editor.view.dom.closest('.editor-container')
      if (!container?.contains(event.target as Node)) return
      if ((event.target as HTMLElement).closest('[role=dialog], .modal-shade')) return
      if ((event.ctrlKey || event.metaKey) && !event.altKey && !event.shiftKey && event.key.toLowerCase() === 'f') {
        event.preventDefault(); onOpen(); searchField.current?.focus(); searchField.current?.select()
      } else if (open && event.key === 'Escape') { event.preventDefault(); onClose(); editor.commands.focus() }
    }
    window.addEventListener('keydown', keyboard)
    return () => window.removeEventListener('keydown', keyboard)
  }, [editor, open, onOpen, onClose])
  if (!open) return null
  const state = richSearchKey.getState(editor.state)!
  const navigate = (direction: number) => {
    const current = richSearchKey.getState(editor.state)!
    if (!current.matches.length) return
    const active = (current.active + direction + current.matches.length) % current.matches.length
    const match = current.matches[active]
    editor.view.dispatch(editor.state.tr.setMeta(richSearchKey, { active }).setSelection(TextSelection.create(editor.state.doc, match.from, match.to)).setMeta('addToHistory', false).scrollIntoView())
  }
  const replace = (all: boolean) => {
    if (!editor.isEditable) return
    const current = richSearchKey.getState(editor.state)!
    if (!current.matches.length || current.limited) return
    const matches = all ? current.matches : [current.matches[Math.max(current.active, 0)]]
    let tr = closeHistory(editor.state.tr)
    for (const match of [...matches].reverse()) {
      const text = richReplacement(match, replacement, query.regexp)
      // Insert plain text with the starting text's marks, retaining paragraphs, cells and images.
      const marks = tr.doc.resolve(match.from).marks()
      tr = text ? tr.replaceWith(match.from, match.to, editor.schema.text(text, marks)) : tr.delete(match.from, match.to)
    }
    editor.view.dispatch(tr.scrollIntoView())
    setAnnouncement(`已替换 ${matches.length} 处`)
  }
  const unavailable = !state.matches.length || state.invalid || state.pending || state.timedOut
  return <div className="cm-panels-top rich-search-overlay"><div className="cm-search compact-search" role="search" aria-label="当前笔记查找替换">
    <div className="compact-find-row">
      <input ref={searchField} className="cm-textfield" name="search" aria-label="查找" placeholder="查找" aria-invalid={state.invalid} value={query.search} onChange={event => setQuery({ ...query, search: event.target.value })} onKeyDown={event => { if (event.key === 'Enter' && !event.nativeEvent.isComposing) { event.preventDefault(); navigate(event.shiftKey ? -1 : 1) } }}/>
      <button className="cm-button" name="prev" aria-label="上一处" title="上一处" disabled={unavailable} onClick={() => navigate(-1)}>↑</button>
      <button className="cm-button" name="next" aria-label="下一处" title="下一处" disabled={unavailable} onClick={() => navigate(1)}>↓</button>
      {editor.isEditable && <button className="cm-button" aria-label={showReplace ? '收起替换' : '展开替换'} aria-expanded={showReplace} onClick={() => { setShowReplace(!showReplace); if (!showReplace) requestAnimationFrame(() => replaceField.current?.focus()) }}>替换</button>}
      <button className={'cm-button' + (query.caseSensitive || query.regexp || query.wholeWord ? ' has-options' : '')} aria-label="查找选项" aria-expanded={showOptions} onClick={() => setShowOptions(!showOptions)}>选项</button>
      <button className="cm-button" name="close" aria-label="关闭查找" onClick={() => { onClose(); editor.commands.focus() }}>×</button>
    </div>
    {showReplace && editor.isEditable && <div className="compact-replace-row">
      <input ref={replaceField} className="cm-textfield" name="replace" aria-label="替换为" placeholder="替换为" value={replacement} onChange={event => setReplacement(event.target.value)} onKeyDown={event => { if (event.key === 'Enter' && !event.nativeEvent.isComposing) { event.preventDefault(); replace(false) } }}/>
      <button className="cm-button" name="replace" disabled={unavailable || state.limited} onClick={() => replace(false)}>替换</button>
      <button className="cm-button" name="replaceAll" disabled={unavailable || state.limited} onClick={() => replace(true)}>全部替换</button>
    </div>}
    {showOptions && <div className="compact-search-options">{([
      ['caseSensitive', '区分大小写'], ['regexp', '正则'], ['wholeWord', '整词'],
    ] as const).map(([key, label]) => <button className="cm-button" key={key} aria-pressed={query[key]} onClick={() => setQuery({ ...query, [key]: !query[key] })}>{label}</button>)}</div>}
    {query.search && <div className="rich-search-status" role="status">{state.pending ? '正在查找…' : state.timedOut ? '查找超时，请简化正则表达式' : state.invalid ? '正则表达式无效' : announcement || (state.limited ? '匹配过多，请缩小范围' : state.matches.length ? `${state.active + 1} / ${state.matches.length}` : '没有找到匹配')}</div>}
  </div></div>
}
