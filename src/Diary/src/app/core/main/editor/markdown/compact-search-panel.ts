import {
  closeSearchPanel, findNext, findPrevious, getSearchQuery, replaceAll,
  replaceNext, SearchQuery, selectMatches, setSearchQuery,
} from '@codemirror/search'
import { EditorState } from '@codemirror/state'
import { EditorView, runScopeHandlers, type Panel, type ViewUpdate } from '@codemirror/view'

// Retain CodeMirror's query, matching, undo and keyboard behavior; only replace its panel.
export function compactSearchPanel(view: EditorView): Panel {
  const dom = document.createElement('div')
  dom.className = 'cm-search compact-search'
  dom.setAttribute('role', 'search')
  dom.setAttribute('aria-label', '当前笔记查找替换')
  let query = getSearchQuery(view.state)
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
    button('replace', '替换', '替换', () => { commit(); replaceNext(view) }),
    button('replaceAll', '全部替换', '全部替换', () => { commit(); replaceAll(view) }))
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
    commit()
    if (selectMatches(view)) view.focus()
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
    button('prev', '上一处', '↑', () => { commit(); findPrevious(view) }),
    button('next', '下一处', '↓', () => { commit(); findNext(view) }),
    toggleReplace, toggleOptions,
    button('close', '关闭查找', '×', () => { closeSearchPanel(view) }))
  dom.append(row, replacement, options)

  function commit() {
    const next = new SearchQuery({ search: searchField.value, replace: replaceField.value,
      caseSensitive: caseField.getAttribute('aria-pressed') === 'true',
      regexp: reField.getAttribute('aria-pressed') === 'true',
      wholeWord: wordField.getAttribute('aria-pressed') === 'true',
      literal: query.literal })
    if (!query.eq(next)) view.dispatch({ effects: setSearchQuery.of(next) })
  }
  function sync() {
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
    if (runScopeHandlers(view, event, 'search-panel')) { event.preventDefault(); return }
    if (event.key !== 'Enter' || event.isComposing) return
    if (event.target === searchField) {
      event.preventDefault(); commit(); (event.shiftKey ? findPrevious : findNext)(view)
    } else if (event.target === replaceField) { event.preventDefault(); commit(); replaceNext(view) }
  })
  sync()
  return { dom, top: true,
    mount() { searchField.focus(); searchField.select() },
    update(_update: ViewUpdate) { sync() },
  }
}
