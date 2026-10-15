import StarterKit from '@tiptap/starter-kit'

// LocalImage is inline. Tiptap 3.19's default paragraph parser unwraps a
// lone image into the document root, which only accepts block nodes.
// Extend the bundled paragraph so all its commands and options stay intact.
export const LocalStarterKit = StarterKit.extend({
  addExtensions() {
    return (this.parent?.() || []).map(extension => extension.name === 'paragraph'
      ? extension.extend({
        parseMarkdown(token, helpers) {
          const content = helpers.parseInline(token.tokens || [])
          const empty = content.length === 1 && content[0].type === 'text'
            && ['&nbsp;', '\u00a0'].includes(content[0].text || '')
          return helpers.createNode('paragraph', undefined, empty ? [] : content)
        },
      })
      : extension)
  },
})
