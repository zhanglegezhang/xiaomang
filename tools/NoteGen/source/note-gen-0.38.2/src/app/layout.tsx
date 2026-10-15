import type { ReactNode } from 'react'
import './local.css'
import './diary-theme.css'
export const metadata = { title: '小蟒 · 本地日记' }
export default function Layout({ children }: { children: ReactNode }) {
  return <html lang="zh-CN"><body>{children}</body></html>
}
