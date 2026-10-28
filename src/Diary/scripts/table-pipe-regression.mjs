import assert from 'node:assert/strict'
import { performance } from 'node:perf_hooks'
import { MarkdownManager, Markdown } from '@tiptap/markdown'
import { Table, TableRow, TableCell, TableHeader } from '@tiptap/extension-table'
import { LocalStarterKit } from '../src/local/local-starter-kit.ts'
import { LocalTable } from '../src/local/local-table.ts'

const manager = table => new MarkdownManager({ extensions: [LocalStarterKit, Markdown, table, TableRow, TableCell, TableHeader] })
const fixed = manager(LocalTable), original = manager(Table)
const text = (value, marks) => ({ type: 'text', text: value, ...(marks ? { marks } : {}) })
const cell = (value, type = 'tableCell') => ({ type, content: [{ type: 'paragraph', content: Array.isArray(value) ? value : [text(value)] }] })
const doc = value => ({ type: 'doc', content: [{ type: 'table', content: [
  { type: 'tableRow', content: [cell('header', 'tableHeader'), cell('value', 'tableHeader')] },
  { type: 'tableRow', content: [cell('left'), cell(value)] },
] }] })
const cellText = parsed => parsed.content.find(n => n.type === 'table').content[1].content[1].content[0].content.map(n => n.text ?? '').join('')
assert.equal(cellText(original.parse(original.serialize(doc('a|b')))), 'a', 'baseline must reproduce actual loss')
const values = ['a|b', '左|右', '|', '||', '|头|尾|', 'a\\|b', 'a\\\\|b', 'a\\\\\\|b', 'normal', 'a * _ ~ [ ] | b', 'a`|b', 'a &amp; | b']
let count = 0
for (const value of values) {
  let current = doc(value)
  for (let pass = 0; pass < 5; pass++) {
    current = fixed.parse(fixed.serialize(current))
    assert.equal(cellText(current), value, `plain ${JSON.stringify(value)} pass ${pass}`)
    assert.equal(current.content[0].content[1].content.length, 2)
    count++
  }
}
for (const mark of ['bold', 'italic', 'strike']) {
  for (const value of ['a|b', 'a\\|b', 'a\\\\|b']) {
    const parsed = fixed.parse(fixed.serialize(doc([text(value, [{ type: mark }])])))
    assert.equal(cellText(parsed), value, `${mark} ${JSON.stringify(value)}`)
    assert.ok(parsed.content[0].content[1].content[1].content[0].content[0].marks.some(m => m.type === mark))
    count++
  }
}
assert.equal(cellText(fixed.parse(fixed.serialize(doc([text('a|b', [{ type: 'code' }])])))), 'a|b')
const linkDoc = doc([text('left|right', [{ type: 'link', attrs: { href: 'https://example.invalid/a|b' } }])])
assert.equal(cellText(fixed.parse(fixed.serialize(linkDoc))), 'left|right')
const headerDoc = doc('body'); headerDoc.content[0].content[0].content[1] = cell('head|value', 'tableHeader')
assert.equal(fixed.parse(fixed.serialize(headerDoc)).content[0].content[0].content[1].content[0].content[0].text, 'head|value')
const existing = '| head | value |\n| --- | --- |\n| left | a\\|b |\n'
assert.equal(cellText(fixed.parse(fixed.serialize(fixed.parse(existing)))), 'a|b')
const paragraph = { type: 'doc', content: [{ type: 'paragraph', content: [text('Outside | \\ | * plain')] }] }
assert.equal(fixed.serialize(paragraph), original.serialize(paragraph))
const large = doc('a|b')
large.content[0].content.push(...Array.from({ length: 99 }, () => ({ type: 'tableRow', content: [cell('sample'), cell('left|right')] })))
const measure = m => { const start = performance.now(); for (let i = 0; i < 100; i++) m.serialize(large); return (performance.now() - start) / 100 }
measure(original); measure(fixed)
console.log(JSON.stringify({ passed: count + 6, baselineMs: measure(original), fixedMs: measure(fixed) }))
