import { Table, renderTableToMarkdown } from '@tiptap/extension-table'

// Escape cell content before the table renderer inserts its column delimiters.
// Escaped backslashes occur in pairs; an odd run already escapes the pipe.
export const LocalTable = Table.extend({
  renderMarkdown(node, helpers) {
    return renderTableToMarkdown(node, {
      ...helpers,
      renderChildren(content, separator) {
        return helpers.renderChildren(content, separator).replace(/\\*\|/g, match =>
          (match.length - 1) % 2 === 0 ? match.slice(0, -1) + '\\|' : match)
      },
    })
  },
})
