'use client'
import { useCallback, useEffect, useRef, useState } from 'react'
import { invoke } from '@tauri-apps/api/core'
import { listen } from '@tauri-apps/api/event'
import ContextMenu, { type DiaryContext } from './context-menu'
import type { BodyEditorActions } from './body-editor-actions'

type Command = { id: number; name: string; enabled: boolean }
type Request = { request: number; items: Command[] }
const editing = [
  { name:'undo', label:'撤销', shortcut:'Ctrl＋Z' },
  { name:'cut', label:'剪切', shortcut:'Ctrl＋X' },
  { name:'copy', label:'复制', shortcut:'Ctrl＋C' },
  { name:'paste', label:'粘贴', shortcut:'Ctrl＋V' },
  { name:'pastePlain', label:'粘贴为纯文本', shortcut:'Ctrl＋Shift＋V' },
  { name:'selectAll', label:'全选', shortcut:'Ctrl＋A' },
];
export default function BodyContextMenu({ onError, getEditorActions }: { onError:(error:unknown)=>void; getEditorActions:()=>BodyEditorActions|null }) {
  const [menu,setMenu] = useState<{context:DiaryContext; request:Request}|null>(null);
  const current = useRef<Request|null>(null);
  const pending = useRef<{context:DiaryContext; time:number}|null>(null);
  const error = useRef(onError); error.current=onError;
  const finish = useCallback((id=-1,native=false,request=current.current) => {
    if (!request) return Promise.resolve();
    if (current.current===request) { current.current=null;setMenu(null) }
    return invoke('body_menu_command',{request:request.request,command:id,native});
  },[]);
  const close = useCallback(()=>{void finish().catch(cause=>error.current(cause))},[finish]);
  useEffect(()=>{
    let disposed=false;let unlisten:(()=>void)|undefined;
    const capture=(event:MouseEvent)=>{
      const target=event.target as HTMLElement;
      if (target.closest('.editor-container') && target.closest('.format-bar,.table-actions')) {
        pending.current=null;event.preventDefault();close();return;
      }
      if (!target.closest('.editor-container')) { pending.current=null;return }
      const anchor=target.closest<HTMLElement>('.note-content,.cm-editor,.rich-scroll');
      if (anchor) {
        const rect=anchor.getBoundingClientRect();
        pending.current={context:{path:'',folder:false,body:true,x:event.clientX||rect.left+12,y:event.clientY||rect.top+12,anchor},time:Date.now()};
      }
    };
    document.addEventListener('contextmenu',capture,true);
    void listen<Request>('diary-body-menu',event=>{
      const requested=pending.current;pending.current=null;
      if (disposed || !requested || Date.now()-requested.time>2000) { void finish(-1,true,event.payload).catch(cause=>error.current(cause));return }
      current.current=event.payload;setMenu({context:requested.context,request:event.payload});
    }).then(stop=>{if(disposed)stop();else unlisten=stop}).catch(cause=>error.current(cause));
    return ()=>{disposed=true;document.removeEventListener('contextmenu',capture,true);unlisten?.();if(current.current)close()};
  },[finish,close]);
  if (!menu) return null;
  const actions = getEditorActions();
  const custom = (action:()=>unknown) => {void finish().then(()=>action()).catch(cause=>error.current(cause))};
  const items = editing.map(item=>{
    const command=menu.request.items.find(command=>command.name===item.name);
    return {...item,disabled:!command?.enabled,action:()=>{void finish(command?.id??-1).catch(cause=>error.current(cause))}};
  });
  items.splice(1,0,{name:'redo',label:'重做',shortcut:'Ctrl＋Y',disabled:!actions?.canRedo(),action:()=>{if(actions)custom(actions.redo)}});
  items.push({name:'search',label:'查找／替换',shortcut:'Ctrl＋F',disabled:!actions,action:()=>{if(actions)custom(actions.openSearch)}});
  return <ContextMenu context={menu.context} preserveFocus onClose={close} items={items}/>;
}
