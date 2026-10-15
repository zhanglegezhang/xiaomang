'use client'
import { Component, type ReactNode } from 'react'

// Keep the tree and tabs usable when the visual editor cannot render a note.
// Recovery only displays the parent's original text; it never calls onChange.
export default class EditorBoundary extends Component<{
  children: ReactNode; value: string; toolbarActions?: ReactNode
}, { failed: boolean }> {
  state = { failed: false }
  static getDerivedStateFromError() { return { failed: true } }
  componentDidCatch(error: Error) { console.error('本地日记编辑器加载失败', error) }
  render() {
    if (!this.state.failed) return this.props.children
    return <>
      <div className="format-bar" role="toolbar" aria-label="编辑器恢复">
        <button type="button" onClick={() => this.setState({ failed: false })}>重新加载</button>
        <div className="toolbar-mode-switch">{this.props.toolbarActions}</div>
      </div>
      <div className="error-banner" role="alert">编辑器未能加载，原文已保留。可以切换“源码”查看和编辑，或打开其他日记。</div>
      <pre className="editor-recovery" aria-label="保留的日记原文" style={{ margin: 0, padding: 20, overflow: 'auto', flex: 1, whiteSpace: 'pre-wrap', overflowWrap: 'anywhere' }}>{this.props.value}</pre>
    </>
  }
}
