import Image, { type ImageOptions } from '@tiptap/extension-image'
import { closeHistory } from '@tiptap/pm/history'
import { call } from './api'

// Standard Markdown image titles carry display width. Old images remain valid,
// and the original attachment is never rewritten when its display size changes.
const WIDTH_SUFFIX = /(?:^|\s)\[xm-width=(\d+)\]$/
const validWidth = (width: unknown) => typeof width === 'number' && Number.isFinite(width) && width >= 24 && width <= 900
export const LocalImage = Image.extend<ImageOptions & { onPreview: (src: string, alt: string) => void }>({
  // Markdown image tokens live inside paragraphs. An inline schema keeps the
  // document valid after reopening, while the node view displays on its own line.
  addOptions() { return { allowBase64: false, HTMLAttributes: {}, resize: false, ...this.parent?.(), inline: true, onPreview: () => {} } },
  parseMarkdown(token, helpers) {
    const title = String(token.title || '')
    const match = title.match(WIDTH_SUFFIX), width = match ? Number(match[1]) : null
    return helpers.createNode('image', {
      src: token.href, alt: token.text,
      title: match && validWidth(width) ? title.replace(WIDTH_SUFFIX, '').trimEnd() || null : title || null,
      width: validWidth(width) ? width : null,
    })
  },
  renderMarkdown(node) {
    const src = String(node.attrs?.src || '')
    const alt = String(node.attrs?.alt || '').replace(/\\/g, '\\\\').replace(/\]/g, '\\]')
    const width = node.attrs?.width
    const title = [node.attrs?.title || '', validWidth(width) ? `[xm-width=${Math.round(width)}]` : ''].filter(Boolean).join(' ')
    const escaped = String(title).replace(/\\/g, '\\\\').replace(/"/g, '\\"')
    return `![${alt}](${src}${title ? ` "${escaped}"` : ''})`
  },
  addNodeView() {
    return ({ node: initial, getPos, editor }) => {
      let node = initial, live = true, drag: { id: number; x: number; width: number; current: number } | null = null
      const dom = document.createElement('span'); dom.className = 'local-image'; dom.contentEditable = 'false'
      const image = document.createElement('img'); image.draggable = false
      const handle = document.createElement('button'); handle.type = 'button'; handle.className = 'image-resize-handle'
      handle.title = '拖动缩放图片；方向键微调'; handle.setAttribute('aria-label', '等比例缩放图片')
      const hint = document.createElement('span'); hint.className = 'image-hint'; hint.textContent = '拖动右下角缩放 · 双击预览'
      dom.append(image, hint, handle)
      const apply = () => {
        image.alt = node.attrs.alt || '本地图片'; image.title = node.attrs.title || ''
        dom.style.width = validWidth(node.attrs.width) ? `${node.attrs.width}px` : `${Math.min(480, image.naturalWidth || 480)}px`
        handle.disabled = !editor.isEditable
      }
      const commit = (width: number) => {
        const pos = getPos(); if (!live || !editor.isEditable || typeof pos !== 'number') return
        if (editor.state.doc.nodeAt(pos)?.type !== node.type) return
        editor.chain().command(({tr})=>{closeHistory(tr);return true}).setNodeSelection(pos).updateAttributes('image', { width: Math.round(width), height: null }).run()
      }
      const limit = (width: number) => Math.max(24, Math.min(900, dom.parentElement?.clientWidth || 900, Math.round(width)))
      image.onload = apply
      const src = String(node.attrs.src || '')
      if (src.startsWith('diary-asset:')) {
        call<string>('asset_data', { id: src.slice(12) }).then(data => { if (live) image.src = data }).catch(() => { if (live) image.alt = '本地图片未找到' })
      } else image.alt = '外部图片未加载；请复制图片后粘贴到正文'
      image.ondblclick = event => {
        if (!image.naturalWidth) return
        event.preventDefault(); event.stopPropagation(); this.options.onPreview(image.src, image.alt)
      }
      handle.onpointerdown = event => {
        if (!editor.isEditable || !image.naturalWidth || event.button !== 0) return
        event.preventDefault(); event.stopPropagation()
        const width = dom.getBoundingClientRect().width
        drag = { id: event.pointerId, x: event.clientX, width, current: width }
        handle.setPointerCapture(event.pointerId)
      }
      handle.onpointermove = event => {
        if (!drag || drag.id !== event.pointerId) return
        drag.current = limit(drag.width + event.clientX - drag.x); dom.style.width = `${drag.current}px`
      }
      handle.onpointerup = event => {
        if (!drag || drag.id !== event.pointerId) return
        const width = drag.current; drag = null; handle.releasePointerCapture(event.pointerId); commit(width)
      }
      handle.onpointercancel = () => { drag = null; apply() }
      handle.onlostpointercapture = () => { if (drag) { drag = null; apply() } }
      handle.onkeydown = event => {
        if (event.key !== 'ArrowLeft' && event.key !== 'ArrowRight') return
        event.preventDefault(); event.stopPropagation()
        commit(limit(dom.getBoundingClientRect().width + (event.key === 'ArrowRight' ? 10 : -10)))
      }
      apply()
      return {
        dom,
        selectNode() { dom.classList.add('image-selected'); apply() },
        deselectNode() { dom.classList.remove('image-selected') },
        update(next) { if (next.type !== node.type || next.attrs.src !== node.attrs.src) return false; node = next; if (!drag) apply(); return true },
        stopEvent(event) { return event.target === handle || event.target === hint || event.type === 'dblclick' },
        ignoreMutation() { return true },
        destroy() { live = false; drag = null; image.onload = null },
      }
    }
  },
})
