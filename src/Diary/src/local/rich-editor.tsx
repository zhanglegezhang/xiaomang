'use client'
// The local subset uses the same Tiptap/Markdown editing stack as NoteGen 0.38.2.
import { useEditor, EditorContent } from '@tiptap/react'
import { LocalStarterKit } from './local-starter-kit'
import EditorBoundary from './editor-boundary'
import { Markdown } from '@tiptap/markdown'
import TaskList from '@tiptap/extension-task-list'
import TaskItem from '@tiptap/extension-task-item'
import { LocalTable } from './local-table'
import TableRow from '@tiptap/extension-table-row'
import TableHeader from '@tiptap/extension-table-header'
import TableCell from '@tiptap/extension-table-cell'
import Placeholder from '@tiptap/extension-placeholder'
import { Fragment as ReactFragment, useEffect, useRef, useState, type ReactNode } from 'react'
import { Fragment, Slice } from '@tiptap/pm/model'
import type { CommandProps, Editor } from '@tiptap/core'
import { Bold, Italic, Heading1, Heading2, ChevronDown, IndentIncrease, IndentDecrease, Strikethrough, SquareCode, Minus, Link2, Eraser, List, ListOrdered, ListTodo, Quote, Code, Undo2, Redo2, Table2, Search } from 'lucide-react'
import { LocalImage } from './local-image'
import ImagePreview from './image-preview'
import TableOperationIcon, { type TableOperation } from './table-operation-icon'
import { RichSearch } from './rich-search'
import RichSearchPanel from './rich-search-panel'
import type { BodyEditorActions } from './body-editor-actions'
import { normalizeMarkdownPlaceholders } from '@/app/core/main/editor/markdown/markdown-paragraph'
import { useModalFocus } from './use-modal-focus'
import type { EditorSession } from './editor-session'

// Markdown tables require one header row. Keep row edits stable across saving/reopening.
function markdownTableHeaders({ tr }: CommandProps) {
  const from = tr.selection.$from;
  for (let depth = from.depth; depth > 0; depth--) {
    const table = from.node(depth);
    if (table.type.name !== 'table') continue;
    const start = from.before(depth) + 1;
    table.forEach((row, rowOffset, rowIndex) => row.forEach((cell, cellOffset) => {
      const type = tr.doc.type.schema.nodes[rowIndex === 0 ? 'tableHeader' : 'tableCell'];
      if (cell.type !== type) tr.setNodeMarkup(start + rowOffset + 1 + cellOffset, type, cell.attrs);
    }));
    break;
  }
  return true;
}
type RichEditorProps = { value: string; onChange: (text: string) => void; readonly?: boolean; onPasteImages?: (data: DataTransfer) => Promise<import('./image-paste').ImagePasteBatch | null> | null; toolbarActions?: ReactNode; onContextActionsChange?: (actions:BodyEditorActions|null)=>void; session?: EditorSession }
export default function RichEditor(props: RichEditorProps) {
  return <EditorBoundary value={props.value} toolbarActions={props.toolbarActions}><RichEditorContent {...props}/></EditorBoundary>
}
function RichEditorContent({ value, onChange, readonly = false, onPasteImages, toolbarActions, onContextActionsChange, session }: RichEditorProps) {
  const update = useRef(onChange); update.current = onChange;
  const paste = useRef(onPasteImages); paste.current = onPasteImages;
  const instance = useRef<Editor | null>(null);
  const [searchOpen, setSearchOpen] = useState(false);
  const [preview, setPreview] = useState<{src:string;alt:string}|null>(null);
  const [tableOpen, setTableOpen] = useState(false), [rows, setRows] = useState(3), [columns, setColumns] = useState(3);
  const [tableError, setTableError] = useState(''), [inTable, setInTable] = useState(false);
  const [linkOpen, setLinkOpen] = useState(false), [linkUrl, setLinkUrl] = useState(''), [linkError, setLinkError] = useState('');
  const linkForm = useModalFocus(linkOpen, () => { setLinkOpen(false); instance.current?.commands.focus() });
  const tableForm = useModalFocus(tableOpen, () => { setTableOpen(false); instance.current?.commands.focus() });
  const [, setSelectionVersion] = useState(0);
  const linkSelection = useRef<ReturnType<import('@tiptap/pm/state').Selection['getBookmark']> | null>(null);
  const tableSelection = useRef<ReturnType<import('@tiptap/pm/state').Selection['getBookmark']> | null>(null);
  const [tableMenuOpen, setTableMenuOpen] = useState(false);
  const tableMenu = useRef<HTMLDivElement>(null);
  const [headingOpen, setHeadingOpen] = useState(false);
  const headingMenu = useRef<HTMLDivElement>(null);
  const headingSelection = useRef<ReturnType<import('@tiptap/pm/state').Selection['getBookmark']> | null>(null);
  const editor = useEditor({
    immediatelyRender: false,
    extensions: [LocalStarterKit.configure({ link: { openOnClick: false }, undoRedo: { depth: 60 } }), Markdown, TaskList, TaskItem.configure({ nested: true }),
      LocalTable.configure({ resizable: true }), TableRow, TableHeader, TableCell, RichSearch, LocalImage.configure({onPreview:(src,alt)=>setPreview({src,alt})}), Placeholder.configure({ placeholder: '从这里开始，记录今天…' })],
    content: value, contentType: 'markdown', editable: !readonly,
    onBeforeCreate: ({ editor }) => {
      if (session?.rich?.content === value) editor.schema = session.rich.state.schema
    },
    onCreate: ({ editor }) => {
      const saved = session?.rich
      if (saved?.content !== value) return
      // Regenerate plugins against the retained schema and new callbacks, so
      // old editor views/workers are not revived along with undo history.
      editor.view.updateState(saved.state.reconfigure({ plugins: editor.extensionManager.plugins }))
      const scroll = editor.view.dom.closest('.rich-scroll')
      if (scroll) scroll.scrollTop = saved.scrollTop
    },
    onDestroy: () => {
      const editor = instance.current
      if (session && editor && !editor.isDestroyed) session.rich = { state: editor.state, content: normalizeMarkdownPlaceholders(editor.getMarkdown()), scrollTop: editor.view.dom.closest('.rich-scroll')?.scrollTop ?? 0 }
    },
    // A selection/search transaction may append the editor's trailing paragraph.
    // Opening or viewing a note must not serialize that housekeeping over its original.
    onUpdate: ({ editor, transaction }) => {
      if (transaction.docChanged) update.current(normalizeMarkdownPlaceholders(editor.getMarkdown()))
    },
    onTransaction: ({ editor }) => { setInTable(editor.isActive('table')); setSelectionVersion(version => version + 1) },
    editorProps: {
      attributes: { class: 'note-content', 'aria-label': '日记正文', spellcheck: 'false' },
      handlePaste: (view, event) => {
        if (!event.clipboardData || !view.editable) return false;
        const target = instance.current; if (!target) return false;
        const pending = paste.current?.(event.clipboardData); if (!pending) return false;
        let bookmark = view.state.selection.getBookmark();
        const map = ({ transaction }: { transaction: import('@tiptap/pm/state').Transaction }) => { bookmark = bookmark.map(transaction.mapping) };
        target.on('transaction', map);
        void pending.then(async batch => {
          if (!batch) return;
          let inserted = false;
          try {
            if (!batch.ids.length || target.isDestroyed || !target.isEditable) { await batch.abort(); return }
            const selection = bookmark.resolve(view.state.doc);
            const nodes = batch.ids.map(id => view.state.schema.nodes.image.create({ src: 'diary-asset:' + id, alt: '图片' }));
            view.dispatch(view.state.tr.setSelection(selection).replaceSelection(new Slice(Fragment.fromArray(nodes), 0, 0)).scrollIntoView());
            inserted = true;
            await batch.commit();
          } catch (error) {
            if (!inserted) {
              // A transaction listener can throw after ProseMirror applied the change.
              // Preserve the batch if the document already contains its new images.
              const sources = new Set(batch.ids.map(id => 'diary-asset:' + id));
              let referenced = false;
              view.state.doc.descendants(node => { if (node.type.name === 'image' && sources.has(node.attrs.src)) referenced = true; return !referenced });
              if (referenced) await batch.commit(); else await batch.abort(error);
            }
          }
        }).finally(() => target.off('transaction', map));
        return true;
      },
    },
  });
  instance.current = editor;
  useEffect(() => {
    if (!editor) return;
    onContextActionsChange?.({canRedo:()=>!editor.isDestroyed&&editor.isEditable&&editor.can().redo(),
      redo:()=>{if(!editor.isDestroyed&&editor.isEditable)editor.chain().focus().redo().run()},
      openSearch:()=>{if(!editor.isDestroyed)setSearchOpen(true)}});
    return ()=>onContextActionsChange?.(null);
  },[editor,onContextActionsChange]);
  useEffect(() => {
    if (editor && normalizeMarkdownPlaceholders(editor.getMarkdown()) !== value) editor.commands.setContent(value, { contentType: 'markdown', emitUpdate: false });
  }, [editor, value]);
  useEffect(() => { editor?.setEditable(!readonly, false) }, [editor, readonly]);
  useEffect(() => {
    if (!headingOpen) return;
    headingMenu.current?.querySelector<HTMLButtonElement>('[role=menuitemradio][aria-checked=true], [role=menuitemradio]')?.focus();
    const outside = (event: PointerEvent) => { if (!headingMenu.current?.contains(event.target as Node)) setHeadingOpen(false) };
    const escape = (event: KeyboardEvent) => { if (event.key === 'Escape') { event.preventDefault(); setHeadingOpen(false); editor?.commands.focus() } };
    document.addEventListener('pointerdown', outside); document.addEventListener('keydown', escape);
    return () => { document.removeEventListener('pointerdown', outside); document.removeEventListener('keydown', escape) };
  }, [headingOpen, editor]);
  useEffect(() => {
    if (!tableMenuOpen) return;
    const panel = tableMenu.current?.querySelector<HTMLElement>('.table-menu');
    const bar = tableMenu.current?.closest('.format-bar');
    const position = () => {
      if (!panel || !bar) return;
      panel.style.transform = ''; panel.style.top = ''; panel.style.bottom = '';
      const bounds = panel.getBoundingClientRect(), toolbar = bar.getBoundingClientRect();
      const left = Math.max(toolbar.left + 4, Math.min(bounds.left, toolbar.right - 4 - bounds.width));
      panel.style.transform = `translateX(${left - bounds.left}px)`;
      if (bounds.bottom > window.innerHeight - 4 && bounds.top > bounds.height + 4) { panel.style.top = 'auto'; panel.style.bottom = 'calc(100% + 4px)' }
    };
    position();
    const resize = new ResizeObserver(position); if (bar) resize.observe(bar);
    window.addEventListener('resize', position);
    tableMenu.current?.querySelector<HTMLButtonElement>('[role=menuitem]:not(:disabled)')?.focus();
    const outside = (event: PointerEvent) => { if (!tableMenu.current?.contains(event.target as Node)) setTableMenuOpen(false) };
    const escape = (event: KeyboardEvent) => { if (event.key === 'Escape') { event.preventDefault(); setTableMenuOpen(false); tableMenu.current?.querySelector<HTMLButtonElement>('.table-trigger')?.focus() } };
    document.addEventListener('pointerdown', outside); document.addEventListener('keydown', escape);
    return () => { resize.disconnect(); window.removeEventListener('resize', position); document.removeEventListener('pointerdown', outside); document.removeEventListener('keydown', escape) };
  }, [tableMenuOpen, editor]);
  useEffect(() => { if (readonly) { setHeadingOpen(false); setTableMenuOpen(false) } }, [readonly]);
  if (!editor) return <><div className="format-bar" role="toolbar" aria-label="正文格式"><div className="toolbar-mode-switch">{toolbarActions}</div></div><div className="empty">正在加载编辑器…</div></>;
  let tableRows=0, tableColumns=0;
  const from=editor.state.selection.$from;
  for(let depth=from.depth;depth>0;depth--) {
    const node=from.node(depth);if(node.type.name==='table'){tableRows=node.childCount;tableColumns=node.firstChild?.childCount||0;break}
  }
  const canAddRow=tableRows<100&&(tableRows+1)*tableColumns<=1000;
  const canAddColumn=tableColumns<20&&tableRows*(tableColumns+1)<=1000;
  function openTable() {
    setTableMenuOpen(false);
    setTableError(''); setTableOpen(true);
  }
  function restoreTableSelection() {
    if (tableSelection.current) editor!.view.dispatch(editor!.state.tr.setSelection(tableSelection.current.resolve(editor!.state.doc)));
  }
  function toggleTableMenu(open = !tableMenuOpen) {
    tableSelection.current = editor!.state.selection.getBookmark();
    setHeadingOpen(false); setTableMenuOpen(open);
  }
  function insertTable(event: React.FormEvent) {
    event.preventDefault();
    if (readonly) return;
    if (!Number.isInteger(rows) || !Number.isInteger(columns) || rows < 2 || rows > 100 || columns < 1 || columns > 20 || rows * columns > 1000) {
      setTableError('请输入 2–100 行、1–20 列，总计不超过 1000 个单元格。'); return;
    }
    restoreTableSelection();
    editor!.chain().focus().insertTable({ rows, cols: columns, withHeaderRow: true }).run();
    setTableOpen(false);
  }
  function openLink() {
    linkSelection.current = editor!.state.selection.getBookmark();
    setLinkUrl(String(editor!.getAttributes('link').href || '')); setLinkError(''); setLinkOpen(true);
  }
  function restoreLinkSelection() {
    if (linkSelection.current) editor!.view.dispatch(editor!.state.tr.setSelection(linkSelection.current.resolve(editor!.state.doc)));
  }
  function applyLink(event: React.FormEvent) {
    event.preventDefault(); if (readonly) return;
    let href = linkUrl.trim();
    try { const url = new URL(href); if (!['https:', 'http:', 'mailto:'].includes(url.protocol)) throw new Error(); href = url.href }
    catch { setLinkError('请输入完整的 https://、http:// 或 mailto: 链接。'); return }
    restoreLinkSelection();
    const chain = editor!.chain().focus();
    if (editor!.state.selection.empty && !editor!.isActive('link')) chain.insertContent({ type: 'text', text: href, marks: [{ type: 'link', attrs: { href } }] }).run();
    else chain.extendMarkRange('link').setLink({ href }).run();
    setLinkOpen(false);
  }
  const listItem = editor.isActive('taskItem') ? 'taskItem' : 'listItem';
  const headingLevel = [3, 4, 5].find(level => editor.isActive('heading', { level }));
  const tableOperations: { label: string; icon: TableOperation; act: () => void; enabled: boolean }[] = [
    { label: '上方加行', icon: 'row-before', act: () => editor.chain().focus().addRowBefore().command(markdownTableHeaders).run(), enabled: canAddRow },
    { label: '下方加行', icon: 'row-after', act: () => editor.chain().focus().addRowAfter().command(markdownTableHeaders).run(), enabled: canAddRow },
    { label: '左侧加列', icon: 'column-before', act: () => editor.chain().focus().addColumnBefore().command(markdownTableHeaders).run(), enabled: canAddColumn },
    { label: '右侧加列', icon: 'column-after', act: () => editor.chain().focus().addColumnAfter().command(markdownTableHeaders).run(), enabled: canAddColumn },
    { label: '删除当前行', icon: 'delete-row', act: () => editor.chain().focus().deleteRow().command(markdownTableHeaders).run(), enabled: true },
    { label: '删除当前列', icon: 'delete-column', act: () => editor.chain().focus().deleteColumn().command(markdownTableHeaders).run(), enabled: true },
  ];
  const tools: { label: string; icon: typeof Bold; act: () => void; active?: boolean; disabled?: boolean }[] = [
    { label: '撤销', icon: Undo2, act: () => editor.chain().focus().undo().run() },
    { label: '重做', icon: Redo2, act: () => editor.chain().focus().redo().run() },
    { label: '一级标题', icon: Heading1, active: editor.isActive('heading', { level: 1 }), act: () => editor.chain().focus().toggleHeading({ level: 1 }).run() },
    { label: '二级标题', icon: Heading2, active: editor.isActive('heading', { level: 2 }), act: () => editor.chain().focus().toggleHeading({ level: 2 }).run() },
    { label: '加粗', icon: Bold, active: editor.isActive('bold'), act: () => editor.chain().focus().toggleBold().run() },
    { label: '斜体', icon: Italic, active: editor.isActive('italic'), act: () => editor.chain().focus().toggleItalic().run() },
    { label: '删除线', icon: Strikethrough, active: editor.isActive('strike'), act: () => editor.chain().focus().toggleStrike().run() },
    { label: '行内代码', icon: SquareCode, active: editor.isActive('code'), act: () => editor.chain().focus().toggleCode().run() },
    { label: '无序列表', icon: List, active: editor.isActive('bulletList'), act: () => editor.chain().focus().toggleBulletList().run() },
    { label: '有序列表', icon: ListOrdered, active: editor.isActive('orderedList'), act: () => editor.chain().focus().toggleOrderedList().run() },
    { label: '待办列表', icon: ListTodo, active: editor.isActive('taskList'), act: () => editor.chain().focus().toggleTaskList().run() },
    { label: '增加缩进', icon: IndentIncrease, disabled: !editor.can().sinkListItem(listItem), act: () => editor.chain().focus().sinkListItem(listItem).run() },
    { label: '减少缩进', icon: IndentDecrease, disabled: !editor.can().liftListItem(listItem), act: () => editor.chain().focus().liftListItem(listItem).run() },
    { label: '引用', icon: Quote, active: editor.isActive('blockquote'), act: () => editor.chain().focus().toggleBlockquote().run() },
    { label: '代码块', icon: Code, active: editor.isActive('codeBlock'), act: () => editor.chain().focus().toggleCodeBlock().run() },
    { label: '分隔线', icon: Minus, act: () => editor.chain().focus().setHorizontalRule().run() },
    { label: '链接', icon: Link2, active: editor.isActive('link'), act: openLink },
    { label: '插入表格', icon: Table2, active: inTable, act: openTable },
    { label: '清除格式', icon: Eraser, act: () => editor.chain().focus().unsetAllMarks().clearNodes().run() },
  ];
  return <><div className="format-bar" role="toolbar" aria-label="正文格式">{tools.map(({ label, icon: Icon, act, active, disabled }, index) => <ReactFragment key={label}>
    {[2,7,12,14,18].includes(index)&&<span className="toolbar-separator" aria-hidden="true"/>}
    {index===4&&<div className="heading-select" ref={headingMenu}>
      <button type="button" className="heading-trigger" title="三级至五级标题" aria-label="三级至五级标题" aria-haspopup="menu" aria-expanded={headingOpen} aria-pressed={!!headingLevel} disabled={readonly} onMouseDown={e=>e.preventDefault()} onClick={()=>{headingSelection.current=editor.state.selection.getBookmark();setHeadingOpen(!headingOpen)}} onKeyDown={e=>{if(e.key==='ArrowDown'){e.preventDefault();headingSelection.current=editor.state.selection.getBookmark();setHeadingOpen(true)}}}>H{headingLevel||3}<ChevronDown size={10}/></button>
      {headingOpen&&<div className="heading-menu" role="menu" aria-label="标题级别" onKeyDown={e=>{const items=Array.from(e.currentTarget.querySelectorAll<HTMLButtonElement>('button'));const current=items.indexOf(document.activeElement as HTMLButtonElement);if(['ArrowDown','ArrowUp','Home','End'].includes(e.key)){e.preventDefault();items[e.key==='Home'?0:e.key==='End'?items.length-1:(current+(e.key==='ArrowDown'?1:-1)+items.length)%items.length]?.focus()}if(e.key==='Tab')setHeadingOpen(false)}}>
        {([3,4,5] as const).map(level=><button type="button" role="menuitemradio" aria-checked={headingLevel===level} key={level} onMouseDown={e=>e.preventDefault()} onClick={()=>{if(headingSelection.current)editor.view.dispatch(editor.state.tr.setSelection(headingSelection.current.resolve(editor.state.doc)));editor.chain().focus().setHeading({level}).run();setHeadingOpen(false)}}>{['三级标题','四级标题','五级标题'][level-3]}</button>)}
      </div>}
    </div>}
    {index===4&&<span className="toolbar-separator" aria-hidden="true"/>}
    {label==='插入表格'?<div className="table-select" ref={tableMenu}>
      <button type="button" className="table-trigger" title={label} aria-label={label} aria-haspopup="menu" aria-expanded={tableMenuOpen} aria-pressed={active} disabled={readonly} onMouseDown={e=>e.preventDefault()} onClick={()=>toggleTableMenu()} onKeyDown={e=>{if(e.key==='ArrowDown'){e.preventDefault();toggleTableMenu(true)}}}><Icon size={16}/><ChevronDown size={10}/></button>
      {tableMenuOpen&&<div className="table-menu" role="menu" aria-label="表格操作" onKeyDown={e=>{const items=Array.from(e.currentTarget.querySelectorAll<HTMLButtonElement>('button:not(:disabled)'));const current=items.indexOf(document.activeElement as HTMLButtonElement);if(['ArrowDown','ArrowUp','ArrowLeft','ArrowRight','Home','End'].includes(e.key)){e.preventDefault();items[e.key==='Home'?0:e.key==='End'?items.length-1:(current+(['ArrowDown','ArrowRight'].includes(e.key)?1:-1)+items.length)%items.length]?.focus()}if(e.key==='Tab')setTableMenuOpen(false)}}>
        <button type="button" className="table-create" role="menuitem" disabled={readonly} onMouseDown={e=>e.preventDefault()} onClick={openTable}><Table2 size={16}/>自定义表格</button>
        {tableOperations.map(({label: operationLabel, icon, act: action, enabled})=><button type="button" role="menuitem" key={icon} title={operationLabel} aria-label={operationLabel} disabled={readonly||!inTable||!enabled} onMouseDown={e=>e.preventDefault()} onClick={()=>{restoreTableSelection();action();setTableMenuOpen(false)}}><TableOperationIcon operation={icon}/></button>)}
      </div>}
    </div>:<button type="button" title={label} aria-label={label} aria-pressed={active} disabled={readonly||disabled} onMouseDown={e=>e.preventDefault()} onClick={act}><Icon size={16} /></button>}
  </ReactFragment>)}<button type="button" title="查找 / 替换 Ctrl+F" aria-label="查找 / 替换" onMouseDown={event=>event.preventDefault()} onClick={()=>setSearchOpen(true)}><Search size={16}/></button>{toolbarActions&&<div className="toolbar-mode-switch">{toolbarActions}</div>}</div>
    <div className="rich-editor-body cm-editor"><RichSearchPanel editor={editor} open={searchOpen} onOpen={()=>setSearchOpen(true)} onClose={()=>setSearchOpen(false)}/><div className="rich-scroll"><EditorContent editor={editor} /></div></div>
    {preview&&<ImagePreview src={preview.src} alt={preview.alt} onClose={()=>{setPreview(null);editor.commands.focus()}}/>}
    {linkOpen && <div className="modal-shade" onKeyDown={e=>{if(e.key==='Escape'){e.stopPropagation();setLinkOpen(false);editor.commands.focus()}}}>
      <form ref={linkForm} tabIndex={-1} className="dialog link-dialog" role="dialog" aria-modal="true" aria-label="设置链接" onSubmit={applyLink}>
        <h2>链接</h2><label htmlFor="diary-link-url">链接地址</label><input id="diary-link-url" autoFocus value={linkUrl} placeholder="https://…" onChange={e=>setLinkUrl(e.target.value)}/>
        {linkError&&<p role="alert">{linkError}</p>}<div className="modal-buttons"><button type="button" disabled={readonly} onClick={()=>{restoreLinkSelection();editor.chain().focus().extendMarkRange('link').unsetLink().run();setLinkOpen(false)}}>移除链接</button><button type="button" onClick={()=>{setLinkOpen(false);editor.commands.focus()}}>取消</button><button type="submit" className="primary" disabled={readonly}>确定</button></div>
      </form></div>}
    {tableOpen && <div className="modal-shade" onKeyDown={e=>{if(e.key==='Escape'){e.stopPropagation();setTableOpen(false);editor.commands.focus()}}}>
      <form ref={tableForm} tabIndex={-1} className="dialog table-dialog" role="dialog" aria-modal="true" aria-label="自定义表格" onSubmit={insertTable}>
        <h2>插入表格</h2><label>行数（含表头）<input autoFocus type="number" min={2} max={100} value={rows} onChange={e=>setRows(e.target.valueAsNumber)}/></label>
        <label>列数<input type="number" min={1} max={20} value={columns} onChange={e=>setColumns(e.target.valueAsNumber)}/></label>
        {tableError && <p role="alert">{tableError}</p>}<div className="modal-buttons"><button type="button" onClick={()=>{setTableOpen(false);editor.commands.focus()}}>取消</button><button className="primary" type="submit" disabled={readonly}>插入</button></div>
      </form></div>}
  </>;
}
