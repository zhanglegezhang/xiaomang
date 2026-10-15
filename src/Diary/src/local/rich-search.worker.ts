import { searchBlocks, type RichQuery, type SearchBlock } from './rich-search-engine'

self.onmessage = (event: MessageEvent<{ blocks: SearchBlock[]; query: RichQuery }>) => {
  const result = searchBlocks(event.data.blocks, event.data.query)
  const blocks: string[] = [], indexes = new Map<string, number>()
  // Array custom properties are serialized explicitly for replacement groups.
  self.postMessage({ ...result, blocks, matches: result.matches.map(match => {
    if (!indexes.has(match.block)) { indexes.set(match.block, blocks.length); blocks.push(match.block) }
    return { from: match.from, to: match.to, text: match.text, blockIndex: indexes.get(match.block),
      captures: [...match.captures], index: match.captures.index, groups: match.captures.groups }
  }) })
}
