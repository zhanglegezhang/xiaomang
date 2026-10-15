import { useLayoutEffect, useRef } from 'react'

// Keep shared diary dialogs isolated from the editor, including keyboard focus.
export function useModalFocus<T extends HTMLElement = HTMLFormElement>(open: boolean, dismiss: () => void) {
  const dialog = useRef<T>(null)
  const previousFocus = useRef<HTMLElement | null>(null)
  const dismissRef = useRef(dismiss)
  dismissRef.current = dismiss

  useLayoutEffect(() => {
    if (!open) {
      const remember = (event: FocusEvent) => {
        if (event.target instanceof HTMLElement && !event.target.closest('[role="dialog"]')) previousFocus.current = event.target
      }
      document.addEventListener('focusin', remember, true)
      return () => document.removeEventListener('focusin', remember, true)
    }
    const form = dialog.current
    if (!form) return
    if (document.activeElement instanceof HTMLElement && !form.contains(document.activeElement) && !document.activeElement.closest('[role="dialog"]')) previousFocus.current = document.activeElement
    const shade = form.closest<HTMLElement>('.modal-shade, .image-preview-shade') ?? form
    const app = shade.closest<HTMLElement>('.diary-app') ?? shade.parentElement!
    const siblings: HTMLElement[] = []
    // Editor dialogs are nested inside the workspace. Isolate siblings at each
    // ancestor without making the ancestor containing the dialog inert.
    let branch: HTMLElement = shade
    while (branch !== app && branch.parentElement) {
      siblings.push(...[...branch.parentElement.children].filter((node): node is HTMLElement => node instanceof HTMLElement && node !== branch))
      branch = branch.parentElement
    }
    const before = siblings.map(node => node.inert)
    siblings.forEach(node => { node.inert = true })
    const focusable = () => [...form.querySelectorAll<HTMLElement>('button, input, select, textarea, [tabindex]')]
      .filter(node => !node.matches(':disabled') && node.tabIndex >= 0 && node.getClientRects().length > 0)
    const focusFirst = () => (focusable()[0] ?? form).focus()
    const containFocus = (event: FocusEvent) => {
      if (!form.contains(event.target as Node)) focusFirst()
    }
    const keydown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        event.preventDefault(); event.stopPropagation(); dismissRef.current()
      } else if (event.key === 'Tab') {
        const items = focusable(), index = items.indexOf(document.activeElement as HTMLElement)
        if (!items.length || index < 0 || (event.shiftKey ? index === 0 : index === items.length - 1)) {
          event.preventDefault(); ((event.shiftKey ? items.at(-1) : items[0]) ?? form).focus()
        }
      }
    }
    document.addEventListener('focusin', containFocus, true)
    document.addEventListener('keydown', keydown, true)
    if (!form.contains(document.activeElement)) focusFirst()
    return () => {
      document.removeEventListener('focusin', containFocus, true)
      document.removeEventListener('keydown', keydown, true)
      siblings.forEach((node, index) => { node.inert = before[index] })
      const savedFocus = previousFocus.current
      // Restore after React has finished replacing toolbar buttons in this commit.
      queueMicrotask(() => {
        if (!app.isConnected || app.querySelector('[role="dialog"][aria-modal="true"]')) return
        let target = savedFocus
        if (target && !target.isConnected) {
          for (const attribute of ['id', 'aria-label', 'title']) {
            const value = target.getAttribute(attribute)
            if (!value) continue
            const replacement = app.querySelector<HTMLElement>(`${target.tagName.toLowerCase()}[${attribute}="${CSS.escape(value)}"]`)
            if (replacement) { target = replacement; break }
          }
        }
        if (target?.isConnected && !target.matches(':disabled')) target.focus()
      })
    }
  }, [open])
  return dialog
}
