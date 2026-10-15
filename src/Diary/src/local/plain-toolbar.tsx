import { Undo2, Redo2, Search, Minus, Plus, WrapText } from 'lucide-react'
import type { SourceMarkdownEditorController } from '@/app/core/main/editor/markdown/source-markdown-editor'

export default function PlainToolbar({ controller, undoRedo, fontSize, wrapping, disabled, onFontSize, onWrapping }: {
  controller: React.RefObject<SourceMarkdownEditorController | null>;
  undoRedo: { undo: boolean; redo: boolean };
  fontSize: number; wrapping: boolean; disabled: boolean;
  onFontSize: (size: number) => void; onWrapping: (wrap: boolean) => void;
}) {
  return <div className="format-bar plain-format-bar" role="toolbar" aria-label="TXT 工具栏">
    <button type="button" title="撤销 Ctrl+Z" aria-label="撤销" disabled={disabled || !undoRedo.undo} onClick={()=>controller.current?.undo()}><Undo2 size={16}/></button>
    <button type="button" title="重做 Ctrl+Y" aria-label="重做" disabled={disabled || !undoRedo.redo} onClick={()=>controller.current?.redo()}><Redo2 size={16}/></button>
    <button type="button" title="查找 / 替换 Ctrl+F" aria-label="查找 / 替换" disabled={disabled} onClick={()=>controller.current?.openSearch()}><Search size={16}/></button>
    <span className="plain-toolbar-separator" aria-hidden="true"/>
    <button type="button" title="减小字号（仅调整显示）" aria-label="减小字号" disabled={disabled || fontSize <= 12} onMouseDown={event=>event.preventDefault()} onClick={()=>{onFontSize(fontSize-2);controller.current?.focus()}}><Minus size={16}/></button>
    <span className="plain-font-size" aria-label="当前字号">{fontSize}</span>
    <button type="button" title="增大字号（仅调整显示）" aria-label="增大字号" disabled={disabled || fontSize >= 28} onMouseDown={event=>event.preventDefault()} onClick={()=>{onFontSize(fontSize+2);controller.current?.focus()}}><Plus size={16}/></button>
    <button type="button" title="自动换行" aria-label="自动换行" aria-pressed={wrapping} disabled={disabled} onMouseDown={event=>event.preventDefault()} onClick={()=>{onWrapping(!wrapping);controller.current?.focus()}}><WrapText size={16}/></button>
    <span className="plain-format-label">纯文本</span>
  </div>
}
