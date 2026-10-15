import { Extension } from '@tiptap/core'
import { Plugin, PluginKey } from '@tiptap/pm/state'
import { Decoration, DecorationSet } from '@tiptap/pm/view'
import type { Node } from '@tiptap/pm/model'
import { searchBlocks, type SearchBlock, type SearchResult, type RichQuery, type RichMatch } from './rich-search-engine'

export type { RichQuery, RichMatch } from './rich-search-engine'
export type RichSearchState = { query: RichQuery; matches: RichMatch[]; active: number; invalid: boolean; limited: boolean; pending: boolean; timedOut: boolean }
export const emptyRichQuery: RichQuery = { search: '', caseSensitive: false, regexp: false, wholeWord: false }
export const richSearchKey = new PluginKey<RichSearchState>('localRichSearch')

// Search each text block, joining inline marks but never consuming images or block boundaries.
function textBlocks(doc: Node): SearchBlock[] {
  const blocks: SearchBlock[] = []
  doc.descendants((node, pos) => {
    if (!node.isTextblock) return true
    blocks.push({ text: node.textBetween(0, node.content.size, '', '\uFFFC'), pos })
    return false
  })
  return blocks
}

// Expand regex replacement groups with their original context, including $&, $1 and named groups.
export function richReplacement(match: RichMatch, replacement: string, regexp: boolean): string {
  if (!regexp) return replacement
  return replacement.replace(/\$(\$|&|`|'|\d{1,2}|<[^>]+>)/g, (token, group: string) => {
    if (group === '$') return '$'
    if (group === '&') return match.text
    if (group === '`') return match.block.slice(0, match.captures.index)
    if (group === "'") return match.block.slice(match.captures.index + match.text.length)
    if (group.startsWith('<')) return match.captures.groups ? (match.captures.groups[group.slice(1, -1)] || '') : token
    const index = Number(group)
    if (index > 0 && index < match.captures.length) return match.captures[index] || ''
    if (group.length === 2 && Number(group[0]) > 0 && Number(group[0]) < match.captures.length) return (match.captures[Number(group[0])] || '') + group[1]
    return token
  })
}

export const RichSearch = Extension.create({
  name: 'localRichSearch',
  addProseMirrorPlugins() {
    return [new Plugin<RichSearchState>({
      key: richSearchKey,
      state: {
        init: () => ({ query: emptyRichQuery, matches: [], active: -1, invalid: false, limited: false, pending: false, timedOut: false }),
        apply(tr, previous) {
          const meta = tr.getMeta(richSearchKey) as { query?: RichQuery; active?: number; result?: SearchResult; timedOut?: boolean } | undefined
          if (!meta && !tr.docChanged) return previous
          const query = meta?.query || previous.query
          if (meta?.result) return { query, ...meta.result, active: meta.result.matches.length ? Math.min(Math.max(previous.active, 0), meta.result.matches.length - 1) : -1, pending: false, timedOut: !!meta.timedOut }
          if (meta?.query || tr.docChanged) {
            const pending = query.regexp && !!query.search
            const result = pending ? { matches: [], invalid: false, limited: false } : searchBlocks(textBlocks(tr.doc), query)
            return { query, ...result, active: result.matches.length ? Math.min(Math.max(previous.active, 0), result.matches.length - 1) : -1, pending, timedOut: false }
          }
          return { ...previous, active: meta?.active ?? previous.active }
        },
      },
      view(view) {
        let worker: Worker | null = null, timer = 0, deadline = 0, version = 0, destroyed = false
        let lastDoc: Node | undefined, lastQuery: RichQuery | undefined
        const cancel = () => { window.clearTimeout(timer); window.clearTimeout(deadline); worker?.terminate(); worker = null; version++ }
        const update = () => {
          const state = richSearchKey.getState(view.state)!
          if (lastDoc === view.state.doc && lastQuery === state.query) return
          lastDoc = view.state.doc; lastQuery = state.query; cancel()
          if (!state.pending) return
          const doc = view.state.doc, query = state.query, request = version
          const finish = (result: SearchResult, timedOut = false) => {
            if (destroyed || version !== request || view.state.doc !== doc || richSearchKey.getState(view.state)?.query !== query) return
            window.clearTimeout(deadline); worker?.terminate(); worker = null
            view.dispatch(view.state.tr.setMeta(richSearchKey, { result, timedOut }).setMeta('addToHistory', false))
          }
          timer = window.setTimeout(() => {
            try {
              worker = new Worker(new URL('./rich-search.worker.ts', import.meta.url), { type: 'module' })
              worker.onmessage = event => {
                const data = event.data as Omit<SearchResult, 'matches'> & { blocks: string[]; matches: (Omit<RichMatch, 'block'> & { blockIndex: number; index: number; groups?: Record<string, string> })[] }
                const result: SearchResult = { invalid: data.invalid, limited: data.limited, matches: data.matches.map(match => {
                  const block = data.blocks[match.blockIndex]
                  return { ...match, block, captures: Object.assign(match.captures, { index: match.index, input: block, groups: match.groups }) }
                }) }
                finish(result)
              }
              worker.onerror = () => finish({ matches: [], invalid: false, limited: false }, true)
              deadline = window.setTimeout(() => finish({ matches: [], invalid: false, limited: false }, true), 1000)
              worker.postMessage({ blocks: textBlocks(doc), query })
            } catch { finish({ matches: [], invalid: false, limited: false }, true) }
          }, 100)
        }
        queueMicrotask(() => { if (!destroyed) update() })
        return { update, destroy() { destroyed = true; cancel() } }
      },
      props: {
        decorations(state) {
          const current = richSearchKey.getState(state)!
          return DecorationSet.create(state.doc, current.matches.map((match, index) => Decoration.inline(match.from, match.to, {
            class: index === current.active ? 'rich-search-match rich-search-current' : 'rich-search-match',
          })))
        },
      },
    })]
  },
})
