import { mergeAttributes, Node } from '@tiptap/core'

export interface MarkdownParagraphOptions {
  HTMLAttributes: Record<string, any>
}

declare module '@tiptap/core' {
  interface Commands<ReturnType> {
    markdownParagraph: {
      setParagraph: () => ReturnType
    }
  }
}

export const EMPTY_PARAGRAPH_MARKDOWN = '&nbsp;'
const NBSP_CHAR = '\u00A0'
const EMPTY_SPACE_ENTITIES = ['&#x20;', '&#32;'] as const
const TABLE_MARKDOWN_LINE = /^\|(?:[^|\n]*\|)+\s*$/
const TRAILING_EMPTY_PARAGRAPHS_RE = /(?:^|\n)(?:(?:&nbsp;|&#x20;|&#32;)(?:\n|$))+$/
// Keep the whitespace controls Markdown uses for formatting, but never persist
// the remaining C0/C1 controls as note content. In particular, U+001F can be
// introduced by clipboard data and destabilize table serialization on save.
const UNSUPPORTED_MARKDOWN_CONTROL_CHARACTERS_RE = /[\u0000-\u0008\u000B\u000C\u000E-\u001F\u007F-\u009F]/g

export function stripUnsupportedMarkdownControlCharacters(markdown: string): string {
  return markdown.replace(UNSUPPORTED_MARKDOWN_CONTROL_CHARACTERS_RE, '')
}

export function normalizeMarkdownPlaceholders(markdown: string): string {
  let fence: { character: string; length: number } | null = null
  const normalized = stripUnsupportedMarkdownControlCharacters(markdown)
    .split('\n')
    .map((line) => {
      const marker = line.replace(/^(?: {0,3}> ?)+/, '').match(/^ {0,3}(`{3,}|~{3,})(.*)$/)
      if (fence) {
        if (marker && marker[1][0] === fence.character && marker[1].length >= fence.length && !marker[2].trim()) fence = null
        return line
      }
      if (marker) { fence = { character: marker[1][0], length: marker[1].length }; return line }
      if (line.trim() === EMPTY_PARAGRAPH_MARKDOWN) {
        return line
      }

      if (TABLE_MARKDOWN_LINE.test(line)) {
        // Inline code is also literal content. Its delimiter can contain more
        // than one backtick; replace entities only outside matching delimiters.
        const pieces = line.split(/(`+)/)
        let ticks = 0
        return pieces.map(piece => {
          if (/^`+$/.test(piece)) { if (!ticks) ticks = piece.length; else if (ticks === piece.length) ticks = 0; return piece }
          return ticks ? piece : piece.replace(/&nbsp;/g, ' ')
        }).join('')
      }

      return line
    })
    .join('\n')

  return normalized.replace(TRAILING_EMPTY_PARAGRAPHS_RE, '')
}

export const MarkdownParagraph = Node.create<MarkdownParagraphOptions>({
  name: 'paragraph',

  priority: 1000,

  addOptions() {
    return {
      HTMLAttributes: {},
    }
  },

  group: 'block',

  content: 'inline*',

  parseHTML() {
    return [{ tag: 'p' }]
  },

  renderHTML({ HTMLAttributes }) {
    return ['p', mergeAttributes(this.options.HTMLAttributes, HTMLAttributes), 0]
  },

  parseMarkdown: (token, helpers) => {
    const tokens = token.tokens || []
    const content = helpers.parseInline(tokens)

    if (
      content.length === 1 &&
      content[0].type === 'text' &&
      typeof content[0].text === 'string' &&
      (
        content[0].text === EMPTY_PARAGRAPH_MARKDOWN
        || content[0].text === NBSP_CHAR
        || content[0].text.trim() === ''
        || EMPTY_SPACE_ENTITIES.includes(content[0].text as typeof EMPTY_SPACE_ENTITIES[number])
      )
    ) {
      return helpers.createNode('paragraph', undefined, [])
    }

    return helpers.createNode('paragraph', undefined, content)
  },

  renderMarkdown: (node, h) => {
    if (!node) {
      return ''
    }

    const content = Array.isArray(node.content) ? node.content : []

    if (content.length === 0) {
      return EMPTY_PARAGRAPH_MARKDOWN
    }

    return h.renderChildren(content)
  },

  addCommands() {
    return {
      setParagraph:
        () =>
        ({ commands }) => {
          return commands.setNode(this.name)
        },
    }
  },

  addKeyboardShortcuts() {
    return {
      'Mod-Alt-0': () => this.editor.commands.setParagraph(),
    }
  },
})
