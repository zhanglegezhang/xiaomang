'use client'
import { readPlainPreferences, writePlainPreferences } from './plain-preferences'
import { useCallback, useEffect, useLayoutEffect, useMemo, useRef, useState } from 'react'
import { open, save } from '@tauri-apps/plugin-dialog'
import { getCurrentWindow } from '@tauri-apps/api/window'
import { listen } from '@tauri-apps/api/event'
import { Check, Home, Minus, Plus, FolderRoot, FolderPlus, FilePlus2, Search, X, Pencil, Trash2, Upload, Archive, RotateCcw, RefreshCw, PanelLeftClose, PanelLeftOpen } from 'lucide-react'
import { SourceMarkdownEditor, type SourceMarkdownEditorController } from '@/app/core/main/editor/markdown/source-markdown-editor'
import RichEditor from './rich-editor'
import PlainToolbar from './plain-toolbar'
import ItemIcon from './item-icon'
import ContextMenu, { type DiaryContext } from './context-menu'
import BodyContextMenu from './body-context-menu'
import { clipboardImages, type ImagePasteBatch } from './image-paste'
import { allPaths, basename, call, parentOf, type Draft, type SearchResults, type TrashItem, type TreeNode } from './api'
import { displayName } from './display-name'
import { destinationFolders, iconColors, itemIndex } from './item-options'
import { selectRange, topLevelSelection, visiblePaths } from './tree-selection'
import type { BodyEditorActions } from './body-editor-actions'
import { useModalFocus } from './use-modal-focus'
import type { EditorSession } from './editor-session'
import { requiresSourceMode } from './markdown-protection'

type Modal = { title: string; value: string; onAccept: (value: string, solid?: boolean, format?: 'md'|'txt') => Promise<void>; input?: boolean; message?: string; options?: { label: string; value: string }[]; optionLabel?: string; format?: 'md'|'txt'; palette?: boolean; solid?: boolean; folder?: boolean; iconPath?: string }
type BatchResult = { completed: { path: string; next?: string }[]; failed: { path: string; error: string }[] }
type DataConflict = { current: string; requested: string }
export default function Diary() {
  const [tree, setTree] = useState<TreeNode[]>([]), [root, setRoot] = useState('');
  const [initialized, setInitialized] = useState(false);
  const [storageWarning, setStorageWarning] = useState('');
  const [tabs, setTabs] = useState<string[]>([]), [active, setActive] = useState(''), [selected, setSelectedPath] = useState('');
  const [selection, setSelection] = useState<Set<string>>(new Set());
  const selectionAnchor = useRef('');
  const setSelected = useCallback((path: string) => { setSelectedPath(path); setSelection(new Set(path ? [path] : [])); selectionAnchor.current = path; }, []);
  const [expanded, setExpanded] = useState<Set<string>>(new Set());
  const [text, setText] = useState(''), [mode, setMode] = useState<'rich' | 'source'>('rich');
  const [status, setStatus] = useState('正在打开本地日记…'), [error, setError] = useState(''), [busy, setBusy] = useState(false);
  const [conflictPath, setConflictPath] = useState('');
  const [reloadEpoch, setReloadEpoch] = useState(0);
  const exportedDrafts = useRef(new Map<string, string>());
  const [navigating, setNavigating] = useState(false);
  const [searching, setSearching] = useState(false), [query, setQuery] = useState(''), [hits, setHits] = useState<(SearchResults & { requestQuery: string }) | null>(null);
  const [showTrash, setShowTrash] = useState(false), [trash, setTrash] = useState<TrashItem[]>([]);
  const [modal, setModal] = useState<Modal | null>(null), [sidebar, setSidebar] = useState(true);
  const [context, setContext] = useState<DiaryContext | null>(null);
  const closeContext = useCallback(() => setContext(null), []);
  const exporting = useRef(false);
  const working = useRef(false);
  const modalForm = useModalFocus(!!modal, () => { if (!busy && !working.current) setModal(null) });
  const index = useMemo(() => itemIndex(tree), [tree]);
  const treeWarning = [...index.values()].find(node => node.warning)?.warning;
  const selectedPaths = [...selection].filter(path => index.has(path));
  const multiple = selectedPaths.length > 1;
  const visible = useMemo(() => visiblePaths(tree, expanded), [tree, expanded]);
  const drafts = useRef(new Map<string, Draft>()), activeRef = useRef(''), tabsRef = useRef<string[]>([]);
  const activeGeneration = useRef(0);
  const editorSessions = useRef(new Map<string, EditorSession>());
  const activeSession = (() => { if (!active) return undefined; if (!editorSessions.current.has(active)) editorSessions.current.set(active, {}); return editorSessions.current.get(active); })();
  const queue = useRef(Promise.resolve()), closing = useRef(false), opening = useRef(0), searchId = useRef(0);
  const controller = useRef<SourceMarkdownEditorController | null>(null);
  const tabScroll = useRef<HTMLDivElement>(null);
  const richActions = useRef<BodyEditorActions|null>(null);
  const setRichActions = useCallback((actions:BodyEditorActions|null)=>{richActions.current=actions},[]);
  const [plainFontSize, setPlainFontSize] = useState(14), [plainWrapping, setPlainWrapping] = useState(true);
  useEffect(() => {
    if (!root) return;
    const saved = readPlainPreferences(localStorage, root);
    setPlainFontSize(saved.fontSize); setPlainWrapping(saved.wrapping);
  }, [root]);
  const savePlainPreferences = (fontSize: number, wrapping: boolean) => {
    setPlainFontSize(fontSize); setPlainWrapping(wrapping);
    if (!writePlainPreferences(localStorage, root, { fontSize, wrapping }))
      setStorageWarning('显示偏好未能保存，本次调整仍有效。');
  };
  const [plainUndoRedo, setPlainUndoRedo] = useState({undo:false,redo:false});
  const plainText = /\.txt$/i.test(active) && !index.get(active)?.folder;
  const sourceRequired = useMemo(() => !plainText && requiresSourceMode(text), [plainText, text]);
  const sourceMode = mode === 'source' || sourceRequired;
  const actions = useRef({ save: async () => {}, create: () => {}, search: () => {}, closeTab: async () => {} });
  useLayoutEffect(() => {
    const pane = tabScroll.current; if (!pane || !active) return;
    const reveal = () => {
      const tab = [...pane.querySelectorAll<HTMLElement>('.tab')].find(tab => tab.querySelector<HTMLButtonElement>('.tab-open')?.title === active);
      if (!tab) return;
      const item = tab.getBoundingClientRect(), view = pane.getBoundingClientRect();
      if (item.left < view.left) pane.scrollLeft -= view.left - item.left;
      else if (item.right > view.right) pane.scrollLeft += item.right - view.right;
    };
    reveal(); const observer = new ResizeObserver(reveal); observer.observe(pane);
    return () => observer.disconnect();
  }, [active, tabs, sidebar]);
  const fail = (cause: unknown) => { setError(String(cause)); setStatus('操作未完成，内容保留'); };
  const showDataConflict = (conflict: DataConflict) => setModal({ title: '尚未打开另一资料库', value: '', input: false,
    message: `当前窗口仍属于：\n${conflict.current}\n\n请求打开：\n${conflict.requested}\n\n请先保存并关闭当前日记窗口，再从另一份小蟒点击“本地日记”。本次没有切换资料库。`, onAccept: async () => {} });
  const pendingPastes = useRef(new Set<Promise<void>>());
  const waitingToClose = useRef(false);
  const pasteImages = (data: DataTransfer) => {
    const pending = clipboardImages(data); if (!pending) return null;
    const path = activeRef.current;
    const generation = activeGeneration.current;
    const tracked: Promise<ImagePasteBatch | null> = pending.then(async batch => {
      if (closing.current || path !== activeRef.current || generation !== activeGeneration.current) {
        await batch.abort();
        if (batch.ids.length) fail('已切换日记，图片未插入，请重新粘贴');
        return null;
      }
      // A failed acknowledgement must never remove pictures already inserted in the draft.
      return { ...batch, commit: async () => { try { await batch.commit() } catch (error) { fail(error) } },
        abort: async (cause?: unknown) => { try { await batch.abort() } catch (error) { fail(error) } if (cause) fail(cause) } };
    }).catch(error => { fail(error); return null });
    const completion = tracked.then(batch => batch?.done).then(() => {});
    pendingPastes.current.add(completion);
    void completion.finally(() => pendingPastes.current.delete(completion));
    return tracked;
  };
  const refresh = useCallback(async () => { setTree(await call<TreeNode[]>('tree')); }, []);
  const flush = useCallback(async (path?: string) => {
    const task = queue.current.catch(() => {}).then(async () => {
      for (const [key, draft] of drafts.current) {
        if ((path && key !== path) || draft.saved === draft.content) continue;
        while (draft.content !== draft.saved) {
          const content = draft.content;
          setStatus('保存中…');
          let result: { revision: string };
          try { result = await call<{ revision: string }>('save', { path: key, content, revision: draft.revision }); }
          catch (cause) { if (String(cause).includes('文件已被其他程序修改')) setConflictPath(key); throw cause; }
          draft.revision = result.revision; draft.saved = content;
        }
      }
      setStatus([...drafts.current.values()].some(d => d.content !== d.saved) ? '正在编辑…' : '已保存到本地');
    });
    queue.current = task;
    await task;
  }, []);
  const remember = useCallback((nextTabs: string[], nextActive: string) => {
    if (nextActive !== activeRef.current) activeGeneration.current++;
    tabsRef.current = nextTabs; activeRef.current = nextActive;
    setTabs(nextTabs); setActive(nextActive);
    if (root) localStorage.setItem('diary-session:' + root, JSON.stringify({ tabs: nextTabs, active: nextActive }));
  }, [root]);
  const openNote = useCallback(async (path: string, throwOnError = false) => {
    const recordVisit = () => call('visit', {path}).catch(cause => setError('日记已打开，但使用次数未更新：' + String(cause)));
    const sequence = ++opening.current;
    const hasCapacity = () => tabsRef.current.includes(path) || tabsRef.current.length < 20;
    try {
      if (path === activeRef.current) { setNavigating(false); setSelected(path); await recordVisit(); return }
      if (!hasCapacity()) throw new Error('最多同时打开 20 个文件，请先关闭一些');
      setNavigating(true);
      await Promise.all([...pendingPastes.current]);
      await flush();
      if (sequence !== opening.current || closing.current) return;
      if (!hasCapacity()) throw new Error('最多同时打开 20 个文件，请先关闭一些');
      const cached = drafts.current.get(path);
      const result = !cached || cached.saved === cached.content
        ? await call<{ content: string; revision: string }>('read', { path }) : null;
      if (sequence !== opening.current || closing.current) return;
      const nextTabs = tabsRef.current.includes(path) ? tabsRef.current : [...tabsRef.current, path];
      if (nextTabs.length > 20) throw new Error('最多同时打开 20 个文件，请先关闭一些');
      const current = drafts.current.get(path);
      // Commit the read only once its request is still current and has a visible file slot.
      // Keep edits made while an existing file was being re-read.
      if (result && (!current || current.saved === current.content)) drafts.current.set(path, { ...result, saved: result.content });
      remember(nextTabs, path); setSelected(path); setText(drafts.current.get(path)!.content); setError('');
      setExpanded(old => new Set([...old, ...path.split('/').slice(0, -1).map((_, i, parts) => parts.slice(0, i + 1).join('/'))]));
      await recordVisit();
    } catch (e) { if (sequence === opening.current && !closing.current) fail(e); if (throwOnError) throw e; } finally { if (sequence === opening.current) setNavigating(false) }
  }, [flush, remember]);
  const update = useCallback((content: string) => {
    const draft = drafts.current.get(activeRef.current); if (!draft) return;
    draft.content = content; setText(content); setStatus(content === draft.saved ? '已保存到本地' : '正在编辑…');
  }, []);
  useEffect(() => {
    if (!active) return;
    let live = true, failures = 0, ownError = '';
    let timer: number;
    const savePending = async () => {
      if (!live) return;
      if (working.current || exporting.current || waitingToClose.current) { timer = window.setTimeout(savePending, 5000); return; }
      try {
        await flush();
        if (live && ownError) setError(current => current === ownError ? '' : current);
      } catch (cause) {
        if (!live) return;
        ownError = String(cause); fail(cause);
        // Keep the draft pending without requiring another keystroke. Back off
        // persistent errors; do not race close, import or rename operations.
        timer = window.setTimeout(savePending, Math.min(30000, 5000 * 2 ** Math.min(failures++, 3)));
      }
    };
    timer = window.setTimeout(savePending, 5000);
    return () => { live = false; window.clearTimeout(timer); };
  }, [text, active, flush]);
  useEffect(() => {
    let live = true;
    call<{ root: string; tree: TreeNode[]; dataConflict?: DataConflict | null; storageWarning?: string }>('init').then(async result => {
      if (!live) return; setRoot(result.root); setTree(result.tree); setStatus('已就绪 · 纯本地'); setStorageWarning(result.storageWarning || '');
      if (result.dataConflict) showDataConflict(result.dataConflict);
      const saved = localStorage.getItem('diary-session:' + result.root);
      if (saved) {
        try {
          const session = JSON.parse(saved); const valid = new Set(allPaths(result.tree));
          const nextTabs: string[] = Array.isArray(session.tabs) ? session.tabs.filter((p: unknown) => typeof p === 'string' && valid.has(p)).slice(0, 20) : [];
          const path: string | undefined = typeof session.active === 'string' && nextTabs.includes(session.active) ? session.active : nextTabs[0];
          if (path) { const note = await call<{ content: string; revision: string }>('read', { path }); if (!live) return;
            drafts.current.set(path, { ...note, saved: note.content }); tabsRef.current = nextTabs; activeRef.current = path;
            setTabs(nextTabs); setActive(path); setSelected(path); setText(note.content);
            setExpanded(new Set(path.split('/').slice(0, -1).map((_, i, parts) => parts.slice(0, i + 1).join('/'))));
          }
        } catch (e) { fail(e); }
      }
    }).catch(fail).finally(() => { if (live) setInitialized(true) });
    return () => { live = false };
  }, []);
  useEffect(() => {
    if (!initialized || !root) return;
    let live = true, stop: (() => void) | undefined;
    const takeRequest = async () => {
      const path = await call<string|null>('take_open_request');
      if (live && path) await openNote(path);
    };
    listen('diary-open-request', () => { void takeRequest().catch(fail) }).then(unlisten => {
      if (live) { stop=unlisten; void takeRequest().catch(fail) } else unlisten();
    }).catch(fail);
    return () => { live=false; stop?.() };
  }, [initialized, root, openNote]);
  useEffect(() => {
    let live = true, stop: (() => void) | undefined;
    listen<DataConflict>('diary-data-conflict', event => { if (live) showDataConflict(event.payload); })
      .then(unlisten => { if (live) stop = unlisten; else unlisten(); }).catch(fail);
    return () => { live = false; stop?.(); };
  }, []);
  useEffect(() => {
    let disposed = false; let stop: (() => void) | undefined;
    getCurrentWindow().onCloseRequested(async event => {
      if (closing.current) return;
      event.preventDefault();
      if (working.current || exporting.current) { setStatus('正在处理操作，请完成后再关闭'); return }
      if (waitingToClose.current) return;
      waitingToClose.current = true;
      try { await Promise.all([...pendingPastes.current]); setBusy(true); await flush(); closing.current = true; await getCurrentWindow().destroy(); }
      catch (e) { closing.current = false; fail(e); if (!String(e).includes('文件已被其他程序修改')) setModal({ title: '尚有内容未保存', value: '', message: '保存或关闭失败，窗口已保留。请取消关闭，导出当前内容后处理错误。', onAccept: async () => {}, input: false }); }
      finally { waitingToClose.current = false; setBusy(false) }
    }).then(fn => { if (disposed) fn(); else stop = fn }).catch(fail);
    return () => { disposed = true; stop?.() };
  }, [flush]);
  const parent = multiple ? '' : treeNode(tree, selected)?.folder ? selected : parentOf(selected);
  function create(folder: boolean, targetParent = parent, initialFormat:'md'|'txt' = 'md') {
    if (tabsRef.current.length >= 20) { fail('最多打开20个页签，请先关闭一个页签再新建。'); return; }
    setModal({ title: folder ? '新建文件夹' : '新建日记', value: folder ? '' : new Date().toLocaleDateString('sv-SE'), input: true, format: folder ? undefined : initialFormat,
      onAccept: async (name, _solid, format='md') => { if(tabsRef.current.length >= 20) throw new Error('最多打开20个页签，请先关闭一个页签再新建。'); await flush(); const path = await call<string>('create', { parent:targetParent, name: name.trim(), folder, format }); await refresh();
        setExpanded(old => new Set([...old, targetParent])); await openNote(path); } });
  }
  async function closeTab(path: string) {
    await closeTabs([path]);
  }
  async function closeTabs(paths:string[]) {
    if (busy || navigating || working.current || exporting.current) return;
    await run(async()=>{
      await flush();
      const removed = new Set(paths), next = tabsRef.current.filter(path=>!removed.has(path));
      const current = activeRef.current, position = tabsRef.current.indexOf(current);
      const nextActive = removed.has(current)
        ? tabsRef.current.slice(position+1).find(path=>!removed.has(path)) || tabsRef.current.slice(0,Math.max(0,position)).reverse().find(path=>!removed.has(path)) || ''
        : current;
      // Read the replacement before changing tabs, so a read/save failure preserves the workspace.
      if (nextActive && nextActive!==activeRef.current) {
        const result = await call<{content:string;revision:string}>('read',{path:nextActive});
        drafts.current.set(nextActive,{...result,saved:result.content});
      }
      paths.forEach(path=>{drafts.current.delete(path);editorSessions.current.delete(path)});
      remember(next,nextActive); setText(nextActive?drafts.current.get(nextActive)!.content:''); setStatus('已保存到本地');
      if (nextActive !== current) {
        setSelected(nextActive);
        setExpanded(old=>new Set([...old,...nextActive.split('/').slice(0,-1).map((_,i,parts)=>parts.slice(0,i+1).join('/'))]));
      }
    });
  }
  function locateInTree(path:string) {
    setSidebar(true);setSearching(false);setShowTrash(false);setSelected(path);
    const ancestors=path.split('/').slice(0,-1).map((_,i,parts)=>parts.slice(0,i+1).join('/'));
    setExpanded(old=>new Set([...old,...ancestors]));
    requestAnimationFrame(()=>{const row=[...document.querySelectorAll<HTMLElement>('.tree-row')].find(row=>row.title===path);
      row?.scrollIntoView({block:'nearest'});row?.querySelector<HTMLElement>('.tree-open')?.focus({preventScroll:true});});
  }
  function duplicate(path:string) {
    if (tabsRef.current.length >= 20) { fail('最多打开20个页签，请先关闭一个页签再复制。'); return; }
    const folder=index.get(path)?.folder===true;
    setError('');
    setModal({title:'复制副本',value:(folder?path.split('/').pop()!:basename(path))+' - 副本',input:true,
      message:folder?'复制目录说明、子目录、文件和图标样式到同级目录。':'复制当前已保存内容到同级目录，保留原格式和图标样式。',
      onAccept:async name=>{if(tabsRef.current.length >= 20) throw new Error('最多打开20个页签，请先关闭一个页签再复制。');await flush();const next=await call<string>('duplicate',{path,name:name.trim()});await refresh();
        setExpanded(old=>new Set([...old,parentOf(next)]));await openNote(next);setStatus('已创建副本');}});
  }
  function remapItem(path: string, next: string) {
    const remap = (p: string) => p === path || p.startsWith(path + '/') ? next + p.slice(path.length) : p;
    const map = new Map<string, Draft>(); drafts.current.forEach((d,p) => map.set(remap(p),d)); drafts.current = map;
    editorSessions.current = new Map([...editorSessions.current].map(([p, state]) => [remap(p), state]));
    remember(tabsRef.current.map(remap), remap(activeRef.current));
    const ancestors = next.split('/').slice(0, -1).map((_, i, parts) => parts.slice(0, i + 1).join('/'));
    setExpanded(old => new Set([...old].map(remap).concat(ancestors))); setSelected(next);
  }
  function rename(path = selected) {
    if (!path || busy || navigating || multiple) return;
    const folder = index.get(path)?.folder === true;
    setError('');
    setModal({ title: '重命名', value: folder ? path.split('/').pop()! : basename(path), input: true, onAccept: async name => {
      const title = name.trim();
      const filename = folder ? title : title.replace(/\.(md|txt)$/i, '') + (/\.txt$/i.test(path)?'.txt':'.md');
      await flush(); const next = await call<string>('rename', { path, name: filename });
      remapItem(path, next); await refresh(); setStatus('已重命名');
    } });
  }
  function moveItem(path: string, folder: boolean) {
    if (multiple) { moveSelected(); return }
    setError('');
    setModal({ title: '移动到', value: parentOf(path), message: `选择“${basename(path)}”的目标目录`, options: destinationFolders(index, path, folder), onAccept: async parent => {
      await flush(); const next = await call<string>('move', { path, parent });
      remapItem(path, next); await refresh(); setStatus('已移动');
    } });
  }
  function setIconColor(path: string) {
    const paths = multiple ? selectedPaths : [path];
    setError('');
    setModal({ title: '设置颜色', value: index.get(path)?.color || '', solid: index.get(path)?.solid || false, folder: index.get(path)?.folder, iconPath: path, palette: true, onAccept: async (color, solid = false) => {
      await flush();
      if (paths.length > 1) await call('batch_color', { paths, color, solid });
      else await call('set_color', { path, color, solid });
      await refresh(); setStatus(paths.length > 1 ? `已更新 ${paths.length} 项图标` : '已更新图标颜色与样式');
    } });
  }
  function applyIconColor(color: string, solid = false) {
    if (!modal?.palette || busy || working.current || (!multiple && color === modal.value && solid === !!modal.solid)) return;
    const picker = modal;
    setError('');
    void run(async () => {
      await picker.onAccept(color, solid);
      setModal(current => current === picker ? { ...current, value: color, solid } : current);
    });
  }
  function remove() {
    if (multiple) { removeSelected(); return }
    removeOne(selected);
  }
  function removeOne(path: string) {
    if (!path) return;
    setModal({ title: '删除', value: '', message: `“${basename(path)}”及其内容将移到日记回收站，可恢复。`, onAccept: async () => {
      await flush(); await call('delete', { path });
      const removed = (p: string) => p === path || p.startsWith(path + '/');
      [...drafts.current.keys()].filter(removed).forEach(p => drafts.current.delete(p));
      [...editorSessions.current.keys()].filter(removed).forEach(p => editorSessions.current.delete(p));
      const next = tabsRef.current.filter(p => !removed(p));
      if (removed(activeRef.current)) { remember(next, ''); setText(''); if (next.length) await openNote(next[0]); }
      else remember(next, activeRef.current);
      setSelected(''); await refresh();
    } });
  }
  function selectTreeItem(event: React.MouseEvent, path: string) {
    if (busy || navigating || working.current || exporting.current) return;
    if (event.shiftKey) {
      setSelection(selectRange(visible, selectionAnchor.current, path, selection, event.ctrlKey));
      setSelectedPath(path);
    } else if (event.ctrlKey) {
      const next = new Set(selection); if (next.has(path)) next.delete(path); else next.add(path);
      setSelection(next); setSelectedPath(next.has(path) ? path : [...next].at(-1) || ''); selectionAnchor.current = path;
    } else { setSelected(path); void openNote(path) }
    setContext(null);
  }
  async function applyBatchResult(result: BatchResult, operation: 'move'|'delete') {
    const removed = (path: string) => result.completed.some(item => path === item.path || path.startsWith(item.path + '/'));
    const remap = (path: string) => {
      const item = result.completed.find(item => path === item.path || path.startsWith(item.path + '/'));
      return item?.next ? item.next + path.slice(item.path.length) : path;
    };
    if (operation === 'move') {
      drafts.current = new Map([...drafts.current].map(([path, draft]) => [remap(path), draft]));
      editorSessions.current = new Map([...editorSessions.current].map(([path, state]) => [remap(path), state]));
      remember(tabsRef.current.map(remap), remap(activeRef.current));
      setExpanded(old => new Set([...old].map(remap).concat(result.completed.flatMap(item => item.next!.split('/').slice(0,-1).map((_,i,parts)=>parts.slice(0,i+1).join('/'))))));
      setSelection(new Set(selectedPaths.map(remap))); setSelectedPath(remap(selected)); selectionAnchor.current = remap(selectionAnchor.current);
    } else {
      [...drafts.current.keys()].filter(removed).forEach(path => drafts.current.delete(path));
      [...editorSessions.current.keys()].filter(removed).forEach(path => editorSessions.current.delete(path));
      const nextTabs = tabsRef.current.filter(path => !removed(path));
      if (removed(activeRef.current)) { remember(nextTabs, ''); setText(''); if(nextTabs.length) await openNote(nextTabs[0]) }
      else remember(nextTabs, activeRef.current);
      const remaining = selectedPaths.filter(path => !removed(path)); setSelection(new Set(remaining)); setSelectedPath(remaining.at(-1)||''); selectionAnchor.current = remaining.at(-1)||'';
      setExpanded(old => new Set([...old].filter(path => !removed(path))));
    }
    await refresh();
    setStatus(`已${operation === 'move' ? '移动' : '删除'} ${result.completed.length} 项`);
    if (result.failed.length) throw new Error(`已完成 ${result.completed.length} 项，未完成 ${result.failed.length} 项：\n` + result.failed.slice(0,10).map(item=>`${item.path}：${item.error}`).join('\n'));
  }
  function moveSelected() {
    const paths = topLevelSelection(selectedPaths);
    const options = destinationFolders(index, '', false).filter(option => paths.every(path => !index.get(path)?.folder || (option.value.toLowerCase() !== path.toLowerCase() && !option.value.toLowerCase().startsWith(path.toLowerCase()+'/'))));
    setError('');
    setModal({title:'批量移动到',value:options[0]?.value||'',message:`移动 ${paths.length} 项（已合并父子重复选择），同名项目不会覆盖。`,options,onAccept:async parent=>{
      await flush(); const result = await call<BatchResult>('batch_move',{paths,parent}); await applyBatchResult(result,'move');
    }});
  }
  function removeSelected() {
    const paths = topLevelSelection(selectedPaths);
    setError('');
    setModal({title:'批量删除',value:'',message:`将 ${paths.length} 项及其子内容移到日记回收站，可逐项恢复。\n${paths.slice(0,10).join('\n')}${paths.length>10?'\n…':''}`,onAccept:async()=>{
      await flush(); const result = await call<BatchResult>('batch_delete',{paths}); await applyBatchResult(result,'delete');
    }});
  }
  async function exportSelected() {
    const paths = topLevelSelection(selectedPaths);
    if (!paths.length || busy || navigating || exporting.current || working.current) return;
    exporting.current = true;
    try {
      await Promise.all([...pendingPastes.current]); setBusy(true); await flush();
      const destination = await open({directory:true,multiple:false,title:'选择批量导出保存目录'});
      if (typeof destination !== 'string') return;
      const result = await call<{destination:string;files:number}>('batch_export',{paths,destination});
      setError(''); setStatus(`已导出 ${result.files} 个文件：${result.destination}`);
    } catch(error) { fail(error) } finally { exporting.current = false; setBusy(false) }
  }
  function canReorder(path:string,direction:-1|1) {
    const parent=parentOf(path); const siblings=parent?index.get(parent)?.children:tree;
    const position=siblings?.findIndex(node=>node.path===path)??-1;
    return position>=0 && position+direction>=0 && position+direction<(siblings?.length??0);
  }
  function reorder(path:string,direction:-1|1) {
    if(busy||navigating||multiple||!canReorder(path,direction))return;
    void run(async()=>{
      await call('reorder',{path,direction});await refresh();setSelected(path);setStatus(direction===-1?'已上移':'已下移');
      requestAnimationFrame(()=>{const row=[...document.querySelectorAll<HTMLElement>('.tree-row')].find(el=>el.title===path);row?.querySelector<HTMLElement>('.tree-open')?.focus({preventScroll:true});row?.scrollIntoView({block:'nearest'})});
    });
  }
  async function importNote() {
    if (tabsRef.current.length >= 20) throw new Error('最多打开20个页签，请先关闭一个页签再导入。');
    const source = await open({ multiple: false, filters: [{ name: 'Markdown / 纯文本', extensions: ['md','txt'] }] });
    if (typeof source !== 'string') return;
    const targetParent = parent;
    const importWith = async (encoding:string) => {
      if (tabsRef.current.length >= 20) throw new Error('最多打开20个页签，请先关闭一个页签再导入。');
      await flush(); const path = await call<string>('import', { source, parent:targetParent, encoding }); await refresh();
      try { await openNote(path, true); } catch (cause) { throw new Error(`文件已导入到 ${path}，但尚未打开：${String(cause)}`); }
    };
    const detected = await call<{encoding:string|null}>('import_encoding',{source});
    if (detected.encoding) await importWith(detected.encoding);
    else setModal({title:'选择文本编码',value:'gbk',message:'无法自动确定编码。请选择原文件编码；导入副本保存为 UTF-8，原文件保持不变。',optionLabel:'文本编码',options:[{label:'GBK（简体中文旧文本）',value:'gbk'},{label:'UTF-8',value:'utf8'},{label:'UTF-16 LE',value:'utf16le'},{label:'UTF-16 BE',value:'utf16be'}],onAccept:importWith});
  }
  async function exportNote(path: string) {
    if (!path || busy || navigating || exporting.current) return;
    exporting.current = true;
    try {
      // Finish paste insertion while the editor remains editable, then lock it.
      await Promise.all([...pendingPastes.current]);
      setBusy(true);
      const draft = drafts.current.get(path);
      const content = draft ? draft.content : (await call<{ content: string }>('read', { path })).content;
      const format = /\.txt$/i.test(path) && !index.get(path)?.folder ? 'txt' : 'md';
      const destination = await save({ defaultPath: index.get(path)?.folder ? path.split('/').pop() + '-目录说明.md' : path.split('/').pop(), filters: [{ name: format==='txt'?'纯文本':'Markdown', extensions: [format] }] });
      if (!destination) return;
      await call('export', { destination, content, format }); if (path === conflictPath) exportedDrafts.current.set(path, content); setError(''); setStatus('已导出日记');
    } catch (error) { fail(error) } finally { setBusy(false); exporting.current = false }
  }
  function reloadConflictedDraft() {
    const path = conflictPath, draft = drafts.current.get(path);
    if (!draft || exportedDrafts.current.get(path) !== draft.content) { fail('请先导出当前冲突草稿副本；草稿有新修改时需要重新导出。'); return; }
    setModal({ title: '重新读取磁盘版本', value: '', input: false,
      message: `“${basename(path)}”的当前草稿已导出副本。确认舍弃编辑器中的这份草稿，重新读取磁盘版本？磁盘文件和导出的副本均保留。`,
      onAccept: async () => {
        const current = drafts.current.get(path);
        if (!current || exportedDrafts.current.get(path) !== current.content) throw new Error('草稿已变化，请取消并重新导出副本。');
        const latest = await call<{ content: string; revision: string }>('read', { path });
        drafts.current.set(path, { ...latest, saved: latest.content }); editorSessions.current.delete(path);
        if (activeRef.current === path) { activeGeneration.current++; setText(latest.content); setReloadEpoch(epoch => epoch + 1); }
        exportedDrafts.current.delete(path); setConflictPath(''); setStatus('已重新读取磁盘版本；草稿副本已保留');
      } });
  }
  async function backup() {
    const destination = await open({ directory: true, multiple: false, title: '选择备份保存目录' });
    if (typeof destination !== 'string') return;
    await flush(); const path = await call<string>('backup', { destination });
    setModal({ title: '备份完成', value: '', message: `日记、图片、历史快照和回收站已复制到：\n${path}`, onAccept: async () => {} });
  }
  async function importBackup() {
    const source = await open({ directory: true, multiple: false, title: '选择“小蟒日记备份”文件夹' });
    if (typeof source !== 'string') return;
    await flush(); const path = await call<string>('import_backup', { source }); setStorageWarning(await call<string>('storage_warning')); await refresh(); setExpanded(old => new Set([...old,path])); setStatus(path ? '已导入到独立文件夹，原日记保留' : '已恢复到日记根目录');
  }
  const run = async (fn: () => Promise<void>) => { if (working.current || exporting.current) return; working.current = true; try { await Promise.all([...pendingPastes.current]); setBusy(true); await fn(); setError(''); } catch(e) { fail(e) } finally { setBusy(false); working.current = false } };
  actions.current = { save: async () => { await flush(); setError(''); }, create: () => create(false), search: () => { setSearching(true); setShowTrash(false); setSidebar(true); }, closeTab: () => closeTab(activeRef.current) };
  useEffect(() => {
    const handle = (event: KeyboardEvent) => {
      if (event.defaultPrevented || event.isComposing || !(event.ctrlKey || event.metaKey) || event.altKey) return;
      if (modal || document.querySelector('[role="dialog"][aria-modal="true"]')) return;
      const key = event.key.toLowerCase();
      if (event.shiftKey && key !== 'f') return;
      if (key === 's') { event.preventDefault(); actions.current.save().catch(fail) }
      if (key === 'n') { event.preventDefault(); actions.current.create() }
      if (key === 'f' && event.shiftKey) { event.preventDefault(); actions.current.search() }
      if (key === 'w') { event.preventDefault(); actions.current.closeTab().catch(fail) }
    };
    window.addEventListener('keydown',handle); return () => window.removeEventListener('keydown',handle);
  }, [modal]);
  useEffect(() => {
    const id = ++searchId.current;
    if (!searching || !query.trim()) { setHits(null); return; }
    const requestId = crypto.randomUUID();
    let stopped = false, sent = false;
    const timer = window.setTimeout(async () => {
      try {
        await flush();
        if (stopped || id !== searchId.current) return;
        sent = true;
        const result = await call<SearchResults>('search',{ query, requestId });
        if (!stopped && id === searchId.current) setHits({ ...result, requestQuery: query });
      } catch(e) { if (!stopped && id === searchId.current) fail(e) }
    },300);
    return () => {
      stopped = true; window.clearTimeout(timer);
      if (sent) void call('search_cancel', { requestId }).catch(() => {});
    };
  },[query,searching,flush]);
  function openContext(event: React.MouseEvent<HTMLElement>, path: string, folder: boolean) {
    event.preventDefault(); event.stopPropagation();
    if (busy || navigating || exporting.current || working.current || modal) return;
    const rect = event.currentTarget.getBoundingClientRect();
    if (!event.currentTarget.closest('.tree-row') || !selection.has(path)) setSelected(path);
    setContext({ path, folder, tab:!!event.currentTarget.closest('.tabs'), x: event.clientX || rect.left + 12, y: event.clientY || rect.bottom, anchor: event.currentTarget });
  }
  function expandFolder(path: string) {
    const node = treeNode(tree, path); if (!node?.folder) return;
    setExpanded(old => {
      const next = new Set(old);
      const visit = (folder: TreeNode) => { next.add(folder.path); folder.children.filter(child => child.folder).forEach(visit) };
      visit(node); return next;
    });
  }
  function focusTree(path: string) {
    requestAnimationFrame(() => {
      const row=[...document.querySelectorAll<HTMLElement>('.tree-row')].find(row=>row.title===path);
      row?.querySelector<HTMLElement>('.tree-open')?.focus({preventScroll:true});row?.scrollIntoView({block:'nearest'});
    });
  }
  function treeKeyDown(event: React.KeyboardEvent<HTMLDivElement>) {
    if ((event.target as HTMLElement).closest('.tree-toggle') && ['Enter',' '].includes(event.key)) return;
    if (busy || navigating || working.current || exporting.current || modal || event.nativeEvent.isComposing || event.altKey || event.metaKey) return;
    const row=(event.target as HTMLElement).closest<HTMLElement>('.tree-row');
    const path=row?.title || selected, node=index.get(path);
    const consume=()=>{event.preventDefault();event.stopPropagation()};
    if (event.ctrlKey && !event.shiftKey && event.key.toLowerCase()==='a') {
      consume(); window.getSelection()?.removeAllRanges(); setSelection(new Set(visible));setSelectedPath(visible.includes(path)?path:visible[0]||'');selectionAnchor.current=path;return;
    }
    if (event.key==='Escape') { consume();setSelected('');setContext(null);return }
    if (!node) return;
    if (event.ctrlKey) {
      if (!event.shiftKey && ['ArrowUp','ArrowDown'].includes(event.key)) {consume();reorder(path,event.key==='ArrowUp'?-1:1)}
      return;
    }
    const choose=(next:string)=>{
      if(event.shiftKey){const anchor=selectionAnchor.current||path;selectionAnchor.current=anchor;setSelection(selectRange(visible,anchor,next,selection,false));setSelectedPath(next)}
      else setSelected(next);
      focusTree(next);
    };
    if (['ArrowUp','ArrowDown','Home','End'].includes(event.key)) {
      consume(); const position=visible.indexOf(path);
      const next=event.key==='Home'?0:event.key==='End'?visible.length-1:Math.max(0,Math.min(visible.length-1,position+(event.key==='ArrowDown'?1:-1)));
      if(visible[next])choose(visible[next]);return;
    }
    if (event.shiftKey) return;
    if (event.key==='ArrowRight') {
      consume();if(node.folder&&node.children.length){if(!expanded.has(path))setExpanded(old=>new Set([...old,path]));else choose(node.children[0].path)}return;
    }
    if (event.key==='ArrowLeft') {
      consume();if(node.folder&&expanded.has(path))setExpanded(old=>{const next=new Set(old);next.delete(path);return next});else if(parentOf(path))choose(parentOf(path));return;
    }
    if(event.key==='Enter'){consume();setSelected(path);void openNote(path);return}
    if(event.key==='F2'){consume();if(!multiple){setSelected(path);rename(path)}return}
    if(event.key==='Delete'){consume();if(selection.has(path))remove();else{setSelected(path);removeOne(path)}}
  }
  function renderTree(nodes: TreeNode[], level = 0): React.ReactNode {
    return nodes.map(node => <div key={node.path}><div tabIndex={-1} role="treeitem" aria-selected={selection.has(node.path)} className={'tree-row ' + (selection.has(node.path)?'selected':'')} style={{ paddingLeft: 12+level*14 }} title={node.path}
      aria-expanded={node.folder && node.children.length > 0 ? expanded.has(node.path) : undefined} onContextMenu={event => openContext(event, node.path, node.folder)}>
      {node.folder && node.children.length > 0 ? <button type="button" className="tree-toggle" disabled={busy} aria-label={(expanded.has(node.path)?'收起 ':'展开 ')+node.name} aria-expanded={expanded.has(node.path)} onClick={()=>setExpanded(old=>{const next=new Set(old);if(next.has(node.path))next.delete(node.path);else next.add(node.path);return next})}>{expanded.has(node.path)?<Minus size={13} strokeWidth={3}/>:<Plus size={13} strokeWidth={3}/>}</button> : <span className="tree-indent" aria-hidden="true"/>}
      <button type="button" className="tree-open" aria-label={node.folder?node.name:basename(node.name)} disabled={busy} aria-disabled={busy||navigating} onClick={event=>selectTreeItem(event,node.path)}><ItemIcon folder={node.folder} path={node.path} size={16} color={node.color} solid={node.solid}/><span>{displayName(node.folder?node.name:basename(node.name))}</span></button>
    </div>{node.folder&&expanded.has(node.path)&&renderTree(node.children,level+1)}</div>);
  }
  const IconButton = ({ title, icon: Icon, action, disabled = false, pressed }: { title: string; icon: typeof Search; action: () => void; disabled?: boolean; pressed?: boolean }) =>
    <button type="button" className={'icon-button'+(pressed?' selected':'')} title={title} aria-label={title} aria-pressed={pressed} disabled={disabled||busy} onClick={action}><Icon size={18}/></button>;
  const modeSwitch = <div className="view-mode" role="group" aria-label="编辑模式"><button type="button" className={!sourceMode?'selected':''} aria-pressed={!sourceMode} disabled={sourceRequired} title={sourceRequired?'此篇包含大文本或高级 Markdown，使用源码模式保留内容':''} onClick={()=>setMode('rich')}>编辑</button><button type="button" className={sourceMode?'selected':''} aria-pressed={sourceMode} onClick={()=>setMode('source')}>源码</button></div>;
  return <div className="diary-app">
    <BodyContextMenu onError={fail} getEditorActions={()=>plainText||sourceMode ? controller.current ? {
      canRedo:()=>controller.current?.getUndoRedoState().redo??false,
      redo:()=>controller.current?.redo(),openSearch:()=>controller.current?.openSearch(),
    }:null:richActions.current}/>
    <aside className="rail"><button type="button" className="rail-current" title="主页" aria-label="主页" aria-pressed={!searching&&!showTrash} disabled={busy} onClick={()=>{setSearching(false);setShowTrash(false);setSidebar(true)}}><Home size={18} aria-hidden="true"/></button><IconButton title="全文搜索 Ctrl+Shift+F" icon={Search} action={() => actions.current.search()}/><IconButton title="回收站" icon={Trash2} action={() => void run(async()=>{setTrash(await call<TrashItem[]>('trash'));setShowTrash(true);setSearching(false);setSidebar(true)})}/><div className="rail-bottom"><IconButton title="导入日记" icon={Upload} action={()=>void run(importNote)}/><IconButton title="备份" icon={Archive} action={()=>void run(backup)}/><IconButton title="导入备份" icon={RotateCcw} action={()=>void run(importBackup)}/></div></aside>
    {sidebar&&<aside className="sidebar">{(showTrash||searching)&&<div className="sidebar-title"><span>{showTrash?'回收站':'全文搜索'}</span><IconButton title="返回目录" icon={X} action={()=>{setSearching(false);setShowTrash(false)}}/></div>}
      {!searching&&!showTrash&&<><div className="tree-tools"><IconButton title="新建日记 Ctrl+N" icon={FilePlus2} action={()=>create(false)}/><IconButton title="新建文件夹" icon={FolderPlus} action={()=>create(true)}/><IconButton title="重命名所选项 F2" icon={Pencil} disabled={!selected||multiple} action={()=>void rename()}/><IconButton title="删除所选项 Delete" icon={Trash2} disabled={!selectedPaths.length} action={remove}/><IconButton title="刷新目录（保留未保存内容）" icon={RefreshCw} action={()=>void run(async()=>{await refresh();setStatus('目录已刷新，未保存内容保留')})}/><IconButton title="根目录（取消选择）" icon={FolderRoot} pressed={!selectedPaths.length} action={()=>setSelected('')}/>{multiple&&<span className="tree-selection-count" aria-live="polite">已选 {selectedPaths.length} 项</span>}</div><div className="tree-scroll" role="tree" aria-label="日记目录" aria-multiselectable="true" onContextMenu={event=>event.preventDefault()} onKeyDown={treeKeyDown}>{tree.length?renderTree(tree):<div className="sidebar-empty">还没有日记<br/>点击上方新建按钮开始记录</div>}</div></>}
      {searching&&<><div className="search-box"><Search size={16}/><input autoFocus aria-label="搜索全部日记" placeholder="搜索标题或正文…" value={query} onChange={e=>setQuery(e.target.value)}/></div><div className="search-hint" role="status">{hits?.requestQuery===query&&hits.literalOnly?'关键词超过16字，已按连续文字查找':'普通全文检索 · 支持中文'}<br/>Ctrl+F 搜索当前篇</div><div className="tree-scroll">{hits?.results.map(hit=><button key={hit.path} className="search-result" onClick={()=>void openNote(hit.path)}><strong>{hit.folder?hit.path.split('/').pop():basename(hit.path)}{hit.folder?' · 目录说明':''}</strong><small>{hit.path}</small>{hit.matches.map(match=><span key={match.line}>{match.line}：{match.snippet}</span>)}</button>)}{hits&&<div className="search-hint">{hits.results.length} 篇匹配{hits.limited?' · 仅显示前 200 篇，请缩小范围':''}{hits.skipped?` · ${hits.skipped} 个文件未能读取`:''}</div>}{query&&hits&&!hits.results.length&&<div className="sidebar-empty">没有找到匹配日记</div>}</div></>}
      {showTrash&&<div className="tree-scroll">{trash.length?trash.map(item=><div className="trash-item" key={item.id}><strong>{item.path}</strong><small>{new Date(item.time).toLocaleString()}</small><button onClick={()=>void run(async()=>{await call('restore',{id:item.id});setTrash(await call<TrashItem[]>('trash'));await refresh()})}>恢复</button></div>):<div className="sidebar-empty">回收站为空</div>}</div>}
    </aside>}
    <main className="workspace"><header className="tabs" onContextMenu={event=>event.preventDefault()}><IconButton title={sidebar?'收起目录':'展开目录'} icon={sidebar?PanelLeftClose:PanelLeftOpen} action={()=>setSidebar(!sidebar)}/><div className="tab-scroll" ref={tabScroll}>{tabs.map(path=><div className={'tab '+(active===path?'active':'')} key={path} onContextMenu={event=>openContext(event,path,index.get(path)?.folder===true)}><button className="tab-open" disabled={busy||navigating} title={path} aria-label={index.get(path)?.folder?index.get(path)!.name:basename(path)} onClick={()=>void openNote(path)}><ItemIcon folder={index.get(path)?.folder === true} path={path} size={14} color={index.get(path)?.color} solid={index.get(path)?.solid}/><span>{displayName(index.get(path)?.folder?index.get(path)!.name:basename(path))}</span>{drafts.current.get(path)?.content!==drafts.current.get(path)?.saved&&<i/>}</button>{active!==path&&<button className="tab-close" disabled={busy||navigating} title="关闭日记" aria-label={'关闭 '+basename(path)} onClick={()=>void closeTab(path)}><X size={13}/></button>}</div>)}</div></header>
      {storageWarning && <div className="error-banner" role="status">{storageWarning}</div>}
      {error&&<div role="alert" className="error-banner"><span>{error}</span><button aria-label="关闭错误提示" onClick={()=>setError('')}><X size={16}/></button></div>}
      {conflictPath&&<div role="status" className="draft-conflict"><span>“{basename(conflictPath)}”存在外部修改，当前草稿仍保留。</span><div><button disabled={busy||navigating} onClick={()=>void exportNote(conflictPath)}>导出草稿副本</button><button disabled={busy||navigating||exportedDrafts.current.get(conflictPath)!==drafts.current.get(conflictPath)?.content} onClick={reloadConflictedDraft}>重新读取磁盘版本</button></div></div>}
      {treeWarning&&<div role="alert" className="error-banner">{treeWarning}</div>}
      {active?<div className="editor-container" key={active+'-'+sourceMode+'-'+reloadEpoch}>{plainText?<><PlainToolbar controller={controller} undoRedo={plainUndoRedo} fontSize={plainFontSize} wrapping={plainWrapping} disabled={busy||navigating} onFontSize={size=>savePlainPreferences(size,plainWrapping)} onWrapping={wrap=>savePlainPreferences(plainFontSize,wrap)}/><div className="source-editor-body"><SourceMarkdownEditor session={activeSession} language="plain" value={text} onChange={update} editable={!busy&&!navigating} onUndoRedoChange={setPlainUndoRedo} showLineNumbers lineWrapping={plainWrapping} fontSize={plainFontSize} lineHeight={1.7} ariaLabel="日记 TXT 纯文本" onControllerChange={c=>controller.current=c}/></div></>:sourceMode?<><div className="format-bar source-format-bar" role="toolbar" aria-label="源码工具栏"><div className="toolbar-mode-switch">{modeSwitch}</div></div><div className="source-editor-body"><SourceMarkdownEditor session={activeSession} value={text} onChange={update} onPasteImages={pasteImages} editable={!busy&&!navigating} showLineNumbers lineWrapping fontSize={14} lineHeight={1.7} ariaLabel="日记 Markdown 源码" onControllerChange={c=>controller.current=c}/></div></>:<RichEditor session={activeSession} value={text} onChange={update} onPasteImages={pasteImages} readonly={busy||navigating} toolbarActions={modeSwitch} onContextActionsChange={setRichActions}/>}</div>:<div className="welcome"><img src="/xiaomang-logo.png" width={46} height={46} alt="小蟒标志" style={{marginBottom:10}}/><h1>留一点时间，给自己</h1><p>用目录整理日记，用搜索找回记忆。<br/>所有内容保存在这台电脑，随时可以导出和备份。</p><button className="primary" onClick={()=>create(false)} disabled={!root||busy}><FilePlus2 size={17}/>新建日记</button><small>Ctrl+N 新建 · Ctrl+S 保存 · Ctrl+Shift+F 全文搜索</small></div>}
      <footer><span>{status}</span><span>{active?`${text.length.toLocaleString()} 字符`:''} {active&&sourceRequired?' · 源码模式保护原文':''}</span><span>本地存储</span></footer>
    </main>
    {context&&<ContextMenu context={context} onClose={closeContext} onReorder={multiple||context.tab?undefined:direction=>reorder(context.path,direction)} items={context.tab ? [
      {label:'关闭当前页签',action:()=>void closeTab(context.path)},
      {label:'关闭其他页签',disabled:tabs.length<=1,action:()=>void closeTabs(tabs.filter(path=>path!==context.path))},
      {label:'关闭右侧页签',disabled:tabs.indexOf(context.path)===tabs.length-1,action:()=>void closeTabs(tabs.slice(tabs.indexOf(context.path)+1))},
      {label:'关闭全部页签',action:()=>void closeTabs([...tabs])},
      {label:'在树形目录中定位',action:()=>locateInTree(context.path)},
      {label:context.folder?'导出目录说明':'导出日记',action:()=>void exportNote(context.path)},
    ] : multiple ? [
      {label:'批量移动到',action:moveSelected},
      {label:'批量设置图标',action:()=>setIconColor(context.path)},
      {label:'批量删除',action:removeSelected},
      {label:'批量导出',action:()=>void exportSelected()},
    ] : [
      {label:'新建 MD',action:()=>create(false,context.folder?context.path:parentOf(context.path),'md')},
      {label:'新建 TXT',action:()=>create(false,context.folder?context.path:parentOf(context.path),'txt')},
      {label:'新建子文件夹',action:()=>create(true,context.folder?context.path:parentOf(context.path))},
      {label:'复制副本',action:()=>duplicate(context.path)},
      ...(context.folder ? [
      { label: '导出目录说明', action: () => void exportNote(context.path) },
      { label: '收起', action: () => setExpanded(old => { const next = new Set(old); next.delete(context.path); return next }) },
      { label: '展开', action: () => expandFolder(context.path) },
    ] : []),
      { label: '重命名', action: () => rename(context.path) },
      { label: '移动到', action: () => moveItem(context.path, context.folder) },
      { label: '设置颜色', action: () => setIconColor(context.path) },
      { label: '删除', action: remove },
      { label: '上移', shortcut:'Ctrl＋↑', disabled:!canReorder(context.path,-1), action:()=>reorder(context.path,-1) },
      { label: '下移', shortcut:'Ctrl＋↓', disabled:!canReorder(context.path,1), action:()=>reorder(context.path,1) },
      ...(context.folder ? [{label:'导出文件夹及内容',action:()=>void exportSelected()}] : [{ label: '导出日记', action: () => void exportNote(context.path) }]),
    ]} />}
    {modal&&<div className={'modal-shade'+(modal.palette?' color-shade':'')} onPointerDown={event=>{if(modal.palette&&event.target===event.currentTarget&&!busy&&!working.current)setModal(null)}}>
      <form ref={modalForm} tabIndex={-1} className={'dialog'+(modal.palette?' color-dialog':'')+(modal.format?' create-note-dialog':'')} role="dialog" aria-label={modal.title} aria-modal="true"
        onSubmit={event=>{event.preventDefault();if(modal.palette||busy||working.current)return;void run(async()=>{await modal.onAccept(modal.value,undefined,modal.format);setModal(null)})}}>
        {!modal.palette&&<h2>{modal.title}</h2>}
        {modal.message&&<p>{modal.message}</p>}
        {modal.input&&<input autoFocus aria-label={modal.title} value={modal.value} onChange={e=>setModal({...modal,value:e.target.value})}/>}
        {modal.format&&<label className="destination-picker">笔记格式<select aria-label="笔记格式" value={modal.format} onChange={e=>setModal({...modal,format:e.target.value as 'md'|'txt'})}><option value="md">Markdown（MD，可视化编辑）</option><option value="txt">纯文本（TXT）</option></select></label>}
        {modal.options&&<label className="destination-picker">{modal.optionLabel||'目标目录'}<select autoFocus aria-label={modal.optionLabel||'目标目录'} value={modal.value} onChange={e=>setModal({...modal,value:e.target.value})}>{modal.options.map(option=><option key={option.value} value={option.value}>{option.label}</option>)}</select></label>}
        {modal.palette&&<>
          <div className="icon-color-picker" role="radiogroup" aria-label="图标颜色" aria-busy={busy}>
            {iconColors.map(color=>{const checked=modal.value===color.value;return <button type="button" role="radio" aria-checked={checked} aria-label={color.label} title={color.label} aria-disabled={busy} autoFocus={checked} key={color.value} onClick={()=>applyIconColor(color.value,!!modal.solid)}><ItemIcon folder={!!modal.folder} path={modal.iconPath} size={24} color={color.value} solid={modal.solid}/>{checked&&<Check className="color-check" size={11} strokeWidth={3} aria-hidden="true"/>}</button>})}
          </div>
          <div className="color-controls"><label className="color-solid-option"><input type="checkbox" checked={!!modal.solid} disabled={busy} onChange={event=>applyIconColor(modal.value,event.target.checked)}/>实心图标</label>
            <button type="button" className="color-reset" aria-pressed={!modal.value&&!modal.solid} aria-disabled={busy} autoFocus={!modal.value} onClick={()=>applyIconColor('')}>恢复默认</button>
          </div>
        </>}
        {error&&<p role="alert" style={{color:'#a34f38'}}>{error}</p>}
        {!modal.palette&&<div className="dialog-actions"><button type="button" onClick={()=>setModal(null)} disabled={busy}>取消</button><button type="submit" className="primary" disabled={busy||(modal.input&&!modal.value.trim())}>{busy?'处理中…':'确定'}</button></div>}
      </form>
    </div>}
  </div>;
}
function treeNode(nodes: TreeNode[], path: string): TreeNode | undefined {
  for(const node of nodes) { if(node.path===path)return node;const result=treeNode(node.children,path);if(result)return result }
}




