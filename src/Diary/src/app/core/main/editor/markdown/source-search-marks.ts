import { StateEffect, StateField } from '@codemirror/state'
import { Decoration, EditorView, type DecorationSet } from '@codemirror/view'
import { searchPanelOpen } from '@codemirror/search'

export const sourceSearchMarks = StateEffect.define<{from:number;to:number}[]>()
export const sourceSearchHighlight = StateField.define<DecorationSet>({
  create: () => Decoration.none,
  update(value, transaction) {
    if (transaction.docChanged) value = Decoration.none
    for (const effect of transaction.effects) if (effect.is(sourceSearchMarks)) {
      value = Decoration.set(effect.value.filter(m=>m.to>m.from && m.to<=transaction.state.doc.length)
        .map(m=>Decoration.mark({class:'cm-searchMatch'}).range(m.from,m.to)), true)
    }
    return searchPanelOpen(transaction.state) ? value : Decoration.none
  },
  provide: field => EditorView.decorations.from(field),
})
