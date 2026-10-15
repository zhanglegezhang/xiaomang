import { invoke } from '@tauri-apps/api/core'
export interface TreeNode { name: string; path: string; folder: boolean; children: TreeNode[]; color?: string; solid?: boolean; warning?: string }
export interface Draft { content: string; saved: string; revision: string }
export interface SearchHit { path: string; folder?: boolean; matches: { line: number; snippet: string }[] }
export interface SearchResults { results: SearchHit[]; limited: boolean; skipped: number; literalOnly: boolean }
export interface TrashItem { id: string; path: string; time: number }
export function call<T>(op: string, args: Record<string, unknown> = {}): Promise<T> {
  return invoke<T>('diary_command', { op, args })
}
export function basename(path: string) { return path.split('/').pop()!.replace(/\.(md|txt)$/i, '') }
export function parentOf(path: string) { return path.includes('/') ? path.slice(0, path.lastIndexOf('/')) : '' }
export function allPaths(nodes: TreeNode[]): string[] { return nodes.flatMap(n => [n.path, ...allPaths(n.children)]) }
