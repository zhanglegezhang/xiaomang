'use client'
import { useEffect, useLayoutEffect, useRef, useState } from 'react'
import { createPortal } from 'react-dom'

export type DiaryContext = { path: string; folder: boolean; x: number; y: number; anchor: HTMLElement; body?: boolean; tab?: boolean }
type Item = { label: string; shortcut?: string; disabled?: boolean; action: () => void }
export default function ContextMenu({ context, items, onClose, onReorder, preserveFocus = false }: { context: DiaryContext; items: Item[]; onClose: () => void; onReorder?: (direction: -1 | 1) => void; preserveFocus?: boolean }) {
  const menu = useRef<HTMLDivElement>(null);
  const restoreAnchor = useRef(false);
  const [position, setPosition] = useState({ x: context.x, y: context.y });
  const [highlight, setHighlight] = useState(-1);
  useEffect(() => () => {
    if (!restoreAnchor.current || preserveFocus) return;
    queueMicrotask(() => {
      const anchor = context.anchor;
      const target = anchor.querySelector<HTMLElement>('.tree-open,.tab-open') ?? anchor;
      if (target.isConnected && !target.matches(':disabled') && !target.closest('[inert]') && !document.querySelector('[role="dialog"][aria-modal="true"]')) target.focus({ preventScroll: true });
    });
  }, [context.anchor, preserveFocus]);
  useLayoutEffect(() => {
    const rect = menu.current!.getBoundingClientRect();
    setPosition({ x: Math.max(4, Math.min(context.x, window.innerWidth - rect.width - 4)), y: Math.max(4, Math.min(context.y, window.innerHeight - rect.height - 4)) });
  }, [context.x, context.y, items.length]);
  useEffect(() => {
    if (!preserveFocus) menu.current?.querySelector<HTMLButtonElement>('button')?.focus({ preventScroll: true });
    // A queued scroll from revealing a tab can arrive after the menu opens.
    // Close only when its anchor's scroll position actually changes again.
    const scrollPositions = new Map<EventTarget, { x: number; y: number }>();
    for (let element: HTMLElement | null = context.anchor; element; element = element.parentElement) scrollPositions.set(element, { x: element.scrollLeft, y: element.scrollTop });
    scrollPositions.set(document, { x: window.scrollX, y: window.scrollY });
    const scroll = (event: Event) => {
      const previous = scrollPositions.get(event.target!);
      const position = event.target === document ? { x: window.scrollX, y: window.scrollY } : { x: (event.target as HTMLElement).scrollLeft, y: (event.target as HTMLElement).scrollTop };
      if (!previous || previous.x !== position.x || previous.y !== position.y) onClose();
    };
    const outside = (event: PointerEvent) => { if (!menu.current?.contains(event.target as Node)) onClose() };
    const dismiss = () => onClose();
    document.addEventListener('pointerdown', outside);
    window.addEventListener('resize', dismiss); window.addEventListener('blur', dismiss);
    document.addEventListener('scroll', scroll, true);
    return () => { document.removeEventListener('pointerdown', outside); window.removeEventListener('resize', dismiss); window.removeEventListener('blur', dismiss); document.removeEventListener('scroll', scroll, true) };
  }, [onClose, context, preserveFocus]);
  useEffect(() => {
    if (!preserveFocus) return;
    const key = (event: KeyboardEvent) => {
      if (event.key === 'Escape' || event.key === 'Tab') { onClose(); if (event.key === 'Escape') { event.preventDefault(); event.stopPropagation() }; return }
      if (!['ArrowUp','ArrowDown','Home','End','Enter',' '].includes(event.key) || event.ctrlKey || event.altKey || event.metaKey) { onClose(); return }
      event.preventDefault(); event.stopPropagation();
      if (event.key === 'Enter' || event.key === ' ') { if (highlight >= 0 && !items[highlight]?.disabled) { items[highlight]?.action() }; return }
      const enabled = items.flatMap((item,i)=>item.disabled?[]:[i]); if (!enabled.length) return;
      const current = enabled.indexOf(highlight);
      setHighlight(enabled[event.key==='Home'?0:event.key==='End'?enabled.length-1:current<0?(event.key==='ArrowDown'?0:enabled.length-1):(current+(event.key==='ArrowDown'?1:-1)+enabled.length)%enabled.length]);
    };
    document.addEventListener('keydown',key,true); return ()=>document.removeEventListener('keydown',key,true);
  }, [preserveFocus, items, highlight, onClose]);
  return createPortal(<div className="diary-context-menu" ref={menu} role="menu" aria-label={context.body ? '正文操作' : context.tab ? '页签操作' : context.folder ? '文件夹操作' : '日记文件操作'} style={{ left: position.x, top: position.y }} onMouseDown={event=>{if(preserveFocus)event.preventDefault()}} onContextMenu={event => event.preventDefault()} onKeyDown={event => {
    if (event.ctrlKey && !event.altKey && !event.shiftKey && ['ArrowUp','ArrowDown'].includes(event.key)) { event.preventDefault(); event.stopPropagation(); onClose(); onReorder?.(event.key==='ArrowUp'?-1:1); return }
    if (event.key === 'Escape' || event.key === 'Tab') { if (event.key === 'Escape') { restoreAnchor.current=true; event.preventDefault(); event.stopPropagation() } onClose(); return }
    if (!['ArrowUp', 'ArrowDown', 'Home', 'End'].includes(event.key)) return;
    event.preventDefault(); const buttons = Array.from(menu.current!.querySelectorAll<HTMLButtonElement>('button:not(:disabled)'));
    const current = buttons.indexOf(document.activeElement as HTMLButtonElement);
    buttons[event.key === 'Home' ? 0 : event.key === 'End' ? buttons.length - 1 : (current + (event.key === 'ArrowDown' ? 1 : -1) + buttons.length) % buttons.length]?.focus();
  }}>{items.map((item,i) => <button type="button" role="menuitem" aria-label={item.label} aria-keyshortcuts={item.shortcut&&!context.body ? (item.label==='上移'?'Control+ArrowUp':'Control+ArrowDown') : undefined} className={preserveFocus&&highlight===i?'menu-highlight':undefined} disabled={item.disabled} key={item.label} onMouseEnter={()=>{if(preserveFocus)setHighlight(i)}} onClick={() => { if(!preserveFocus)onClose(); item.action() }}><span>{item.label}</span>{item.shortcut&&<small>{item.shortcut}</small>}</button>)}</div>, document.body);
}
