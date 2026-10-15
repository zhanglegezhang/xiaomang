import type { TreeNode } from './api'

export const iconColors = [
  { label: '红色', value: '#ff2020' }, { label: '橙色', value: '#ff7a00' },
  { label: '黄色', value: '#ffcc00' }, { label: '绿色', value: '#00c853' },
  { label: '青色', value: '#00cfee' }, { label: '蓝色', value: '#0066ff' },
  { label: '紫色', value: '#8338ff' }, { label: '玫红', value: '#ed00e6' },
  { label: '粉色', value: '#ff3b91' }, { label: '棕色', value: '#c07800' },
  { label: '灰色', value: '#64748b' }, { label: '黑色', value: '#111827' },
];
export function itemIndex(tree: TreeNode[]) {
  const index = new Map<string, TreeNode>();
  const visit = (nodes: TreeNode[]) => nodes.forEach(node => { index.set(node.path, node); visit(node.children) });
  visit(tree); return index;
}
export function destinationFolders(index: Map<string, TreeNode>, source: string, folder: boolean) {
  const lower = source.toLowerCase();
  return [{ label: '本地日记（根目录）', value: '' }, ...[...index.values()]
    .filter(node => node.folder && (!folder || (node.path.toLowerCase() !== lower && !node.path.toLowerCase().startsWith(lower + '/'))))
    .map(node => ({ label: node.path, value: node.path }))];
}
