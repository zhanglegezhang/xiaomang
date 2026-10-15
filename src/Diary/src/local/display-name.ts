// Shorten display labels only; full names and filesystem paths remain unchanged.
export function displayName(name: string): string {
  const characters = Array.from(new Intl.Segmenter('zh', { granularity: 'grapheme' }).segment(name), part => part.segment);
  return characters.length > 20 ? characters.slice(0, 19).join('') + '…' : name;
}
