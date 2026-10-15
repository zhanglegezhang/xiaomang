import { markdownLanguage } from '@codemirror/lang-markdown'

// Inspect Markdown structure so literal examples remain editable. A single
// leading thematic break is not a front-matter document without a closing fence.
export function requiresSourceMode(text: string) {
  if (text.length > 150000) return true
  if (/^---[ \t]*\r?\n[\s\S]*?\r?\n(?:---|\.\.\.)[ \t]*(?:\r?\n|$)/.test(text)) return true
  const code: { from: number; to: number }[] = []
  let html = false
  markdownLanguage.parser.parse(text).iterate({ enter(node) {
    if (['FencedCode', 'CodeBlock', 'InlineCode'].includes(node.name)) {
      code.push({ from: node.from, to: node.to }); return false
    }
    if (node.name === 'HTMLBlock' || node.name === 'HTMLTag') html = true
  } })
  if (html) return true
  const outside: string[] = []; let cursor = 0
  for (const range of code) { outside.push(text.slice(cursor, range.from), '\n'); cursor = range.to }
  outside.push(text.slice(cursor))
  return /(?<!\\)\[\^[^\]]+\]|^\s*\$\$/m.test(outside.join(''))
}
