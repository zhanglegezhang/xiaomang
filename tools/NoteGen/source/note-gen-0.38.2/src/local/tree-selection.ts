import type { TreeNode } from './api'

export function visiblePaths(tree: TreeNode[], expanded: Set<string>): string[] {
  return tree.flatMap(node => [node.path, ...(node.folder && expanded.has(node.path) ? visiblePaths(node.children, expanded) : [])])
}

export function selectRange(visible: string[], anchor: string, path: string, current: Set<string>, additive: boolean) {
  const first = visible.indexOf(anchor), last = visible.indexOf(path)
  const range = first < 0 || last < 0 ? [path] : visible.slice(Math.min(first, last), Math.max(first, last) + 1)
  return new Set([...(additive ? current : []), ...range])
}

export function topLevelSelection(paths: string[]) {
  const selected = new Set(paths.map(path => path.toLowerCase()))
  return paths.filter(path => !path.split('/').slice(0, -1).some((_, i, parts) => selected.has(parts.slice(0, i + 1).join('/').toLowerCase())))
}
