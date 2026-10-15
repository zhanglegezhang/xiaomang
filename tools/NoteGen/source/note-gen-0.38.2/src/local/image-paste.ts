import { call } from './api'

const LIMIT = 20 * 1024 * 1024
const extensions: Record<string, string> = { 'image/png': 'png', 'image/jpeg': 'jpg', 'image/gif': 'gif', 'image/webp': 'webp' }

export interface ImagePasteBatch {
  ids: string[]
  commit: () => Promise<void>
  abort: (cause?: unknown) => Promise<void>
  done: Promise<void>
}

function batchLifecycle({ batchId, ids }: { batchId: string; ids: string[] }): ImagePasteBatch {
  let finish!: () => void
  const done = new Promise<void>(resolve => { finish = resolve })
  let settlement: Promise<void> | undefined
  const settle = (op: 'asset_batch_confirm' | 'asset_batch_abort') => {
    // Once insertion chose confirmation, even a failed acknowledgement must not
    // permit a later abort to remove pictures already referenced by the editor.
    if (!settlement) settlement = (async () => {
      try { if (batchId) await call(op, { batchId }) } finally { finish() }
    })()
    return settlement
  }
  if (!ids.length) finish()
  return { ids, done, commit: () => settle('asset_batch_confirm'), abort: () => settle('asset_batch_abort') }
}

// null leaves ordinary text/HTML paste to the editor. The native fallback is
// requested only for an empty file paste (Explorer does not expose files to WebView2).
export function clipboardImages(data: DataTransfer): Promise<ImagePasteBatch> | null {
  const files = Array.from(data.files).filter(file => extensions[file.type] || /\.(png|jpe?g|gif|webp)$/i.test(file.name))
  if (!files.length) {
    if (data.getData('text/plain') || data.getData('text/html')) return null
    return call<{ batchId: string; ids: string[] }>('clipboard_image_batch_begin').then(batchLifecycle)
  }
  return (async () => {
    for (const file of files) if (!file.size || file.size > LIMIT) throw new Error('图片为空或超过 20 MB，未插入')
    const items: { extension: string; data: string }[] = []
    for (const file of files) {
      const extension = extensions[file.type] || file.name.split('.').pop()!.toLowerCase()
      const bytes = new Uint8Array(await file.arrayBuffer())
      const parts: string[] = []
      for (let start = 0; start < bytes.length; start += 32768) parts.push(String.fromCharCode(...bytes.subarray(start, start + 32768)))
      items.push({ extension, data: btoa(parts.join('')) })
    }
    return batchLifecycle(await call<{ batchId: string; ids: string[] }>('asset_batch_begin', { items }))
  })()
}
