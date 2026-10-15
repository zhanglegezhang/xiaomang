export type RichQuery = { search: string; caseSensitive: boolean; regexp: boolean; wholeWord: boolean }
export type SearchBlock = { text: string; pos: number }
export type RichMatch = { from: number; to: number; text: string; captures: RegExpExecArray; block: string }
export type SearchResult = { matches: RichMatch[]; invalid: boolean; limited: boolean }
const word = (text: string) => /[\p{L}\p{N}_]/u.test(text)

// User regexes execute only inside the disposable worker. Plain searches use
// the same offsets and replacement semantics without unsafe regex operators.
export function searchBlocks(blocks: SearchBlock[], query: RichQuery): SearchResult {
  const matches: RichMatch[] = []
  if (!query.search) return { matches, invalid: false, limited: false }
  let expression: RegExp
  try { expression = new RegExp(query.regexp ? query.search : query.search.replace(/[.*+?^${}()|[\]\\]/g, '\\$&'), query.caseSensitive ? 'gu' : 'giu') }
  catch { return { matches, invalid: true, limited: false } }
  let captureCharacters = 0
  for (const { text, pos } of blocks) {
    expression.lastIndex = 0
    let match: RegExpExecArray | null
    while ((match = expression.exec(text))) {
      if (!match[0].length) { expression.lastIndex = match.index + (text.codePointAt(match.index)! > 0xFFFF ? 2 : 1); continue }
      if (match[0].includes('\uFFFC')) continue
      if (query.wholeWord) {
        const previous = text.charCodeAt(match.index - 1)
        const before = text.slice(match.index - (previous >= 0xDC00 && previous <= 0xDFFF ? 2 : 1), match.index)
        const following = text.codePointAt(match.index + match[0].length)
        const after = following === undefined ? '' : String.fromCodePoint(following)
        if (word(before) || word(after)) continue
      }
      captureCharacters += match.reduce((total, value) => total + (value?.length || 0), 0)
      if (captureCharacters > 2 * 1024 * 1024) return { matches, invalid: false, limited: true }
      matches.push({ from: pos + 1 + match.index, to: pos + 1 + match.index + match[0].length, text: match[0], captures: match, block: text })
      if (matches.length >= 10000) return { matches, invalid: false, limited: true }
    }
  }
  return { matches, invalid: false, limited: false }
}
