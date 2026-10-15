import type { EditorState as RichState } from '@tiptap/pm/state'
import type { EditorState as SourceState } from '@codemirror/state'

// One entry per open tab (maximum 20), released on close/delete. Retain states
// rather than hidden editor DOMs; history plugins share document structure.
export type EditorSession = {
  rich?: { state: RichState; content: string; scrollTop: number }
  source?: { state: SourceState; content: string; scrollTop: number }
}
