'use client'
import { useEffect, useRef, useState } from 'react'
import { Minus, Plus, X } from 'lucide-react'
import { useModalFocus } from './use-modal-focus'

export default function ImagePreview({ src, alt, onClose }: { src: string; alt: string; onClose: () => void }) {
  const [scale, setScale] = useState(1), [size, setSize] = useState({ width: 0, height: 0 })
  const stage = useRef<HTMLDivElement>(null), close = useRef<HTMLButtonElement>(null)
  const dialog = useModalFocus<HTMLDivElement>(true, onClose)
  const fit = (width = size.width, height = size.height) => {
    if (width && height && stage.current) setScale(Math.min(1, (stage.current.clientWidth - 40) / width, (stage.current.clientHeight - 40) / height))
  }
  const zoom = (factor: number) => setScale(old => Math.max(.1, Math.min(4, old * factor)))
  useEffect(() => { close.current?.focus() }, [])
  return <div ref={dialog} tabIndex={-1} className="image-preview-shade" role="dialog" aria-modal="true" aria-label="图片预览" onKeyDown={event => {
    if ((event.ctrlKey || event.metaKey) && !event.altKey && !event.shiftKey && event.key.toLowerCase() === 'w') { event.preventDefault(); event.stopPropagation(); onClose() }
  }}>
    <div className="image-preview-bar"><strong>图片预览</strong><button type="button" aria-label="缩小预览" onClick={()=>zoom(1/1.25)}><Minus size={18}/></button><span aria-live="polite">{Math.round(scale*100)}%</span><button type="button" aria-label="放大预览" onClick={()=>zoom(1.25)}><Plus size={18}/></button><button type="button" onClick={()=>fit()}>适应窗口</button><button type="button" onClick={()=>setScale(1)}>还原100%</button><button type="button" ref={close} aria-label="关闭图片预览" onClick={onClose}><X size={20}/></button></div>
    <div className="image-preview-stage" ref={stage}><img src={src} alt={alt} draggable={false} style={size.width?{width:size.width*scale,height:size.height*scale}:undefined} onLoad={event=>{const image=event.currentTarget;setSize({width:image.naturalWidth,height:image.naturalHeight});fit(image.naturalWidth,image.naturalHeight)}}/></div>
    <div className="image-preview-footer">Esc 关闭 · 此处缩放不改变正文图片大小</div>
  </div>
}
