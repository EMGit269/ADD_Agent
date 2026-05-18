import React, { Component, ReactNode, useEffect, useMemo, useRef, useState } from 'react'
import { Tldraw, getSnapshot, loadSnapshot } from '@tldraw/tldraw'
import '@tldraw/tldraw/tldraw.css'

type HostEnvelope = {
  type: string
  payload?: any
}

type CanvasMessageItem = {
  sourceRef: string
  role?: string
  kind?: string
  title?: string
  summary?: string
  body?: string
  tags?: string[]
  collapsed?: boolean
  pinned?: boolean
}

type InspectorSnapshot = {
  mode?: string
  text?: string
  asPlainComment?: boolean
  canvasIssues?: string
  generatedAtUtc?: string
}

declare global {
  interface Window {
    chrome?: {
      webview?: {
        postMessage?: (message: unknown) => void
        addEventListener?: (type: string, listener: (event: MessageEvent) => void) => void
        removeEventListener?: (type: string, listener: (event: MessageEvent) => void) => void
      }
    }
  }
}

const hostBridge = window.chrome?.webview ?? null

function postHostMessage(type: string, payload: unknown = {}) {
  hostBridge?.postMessage?.({ type, payload })
}

type ErrorBoundaryProps = {
  children: ReactNode
}

type ErrorBoundaryState = {
  error: Error | null
}

class CanvasErrorBoundary extends Component<ErrorBoundaryProps, ErrorBoundaryState> {
  state: ErrorBoundaryState = { error: null }

  static getDerivedStateFromError(error: Error) {
    return { error }
  }

  componentDidCatch(error: Error, info: React.ErrorInfo) {
    postHostMessage('canvas_error', {
      kind: 'react',
      message: error.message,
      stack: `${error.stack ?? ''}\n${info.componentStack ?? ''}`,
    })
  }

  render() {
    if (this.state.error) {
      return (
        <div className="fatal-panel">
          <h2>Canvas failed</h2>
          <p>{this.state.error.message}</p>
        </div>
      )
    }

    return this.props.children
  }
}

function clampText(text: string | undefined, maxLen: number) {
  const normalized = (text ?? '').replace(/\s+/g, ' ').trim()
  if (normalized.length <= maxLen) return normalized
  return normalized.slice(0, maxLen) + '...'
}

function slugify(text: string) {
  return String(text)
    .replace(/[^a-zA-Z0-9:_-]+/g, '-')
    .replace(/-+/g, '-')
    .replace(/^-|-$/g, '')
    .toLowerCase()
}

function shapeColor(role?: string, kind?: string) {
  if (kind === 'note') return 'green'
  if (kind === 'tool_result' || role === 'tool') return 'light-green'
  if (role === 'assistant') return 'blue'
  return 'yellow'
}

function composeCardText(meta: any) {
  const title = meta.title || 'Untitled'
  const summary = clampText(meta.summary || '', 120)
  const body = clampText(meta.body || '', 500)
  const tags = Array.isArray(meta.tags) && meta.tags.length ? `#${meta.tags.join(' #')}` : ''

  if (meta.collapsed) {
    return [title, clampText(meta.summary || '', 60), meta.sourceRef || ''].filter(Boolean).join('\n')
  }

  return [title, summary, '', body, '', tags].filter(Boolean).join('\n')
}

function extractShapeMeta(shape: any) {
  return shape?.meta ?? {}
}

function getShapeBySourceRef(editor: any, sourceRef: string) {
  const shapes = Array.from(editor?.getCurrentPageShapes?.() ?? [])
  return shapes.find((shape: any) => extractShapeMeta(shape).sourceRef === sourceRef) ?? null
}

function asArray<T>(value: Iterable<T> | T[] | null | undefined) {
  if (!value) return [] as T[]
  return Array.isArray(value) ? value : Array.from(value)
}

function CanvasWorkbench() {
  const [editor, setEditor] = useState<any>(null)
  const [isDarkMode, setIsDarkMode] = useState(() => {
    if (typeof window === 'undefined' || typeof window.matchMedia !== 'function') return false
    return window.matchMedia('(prefers-color-scheme: dark)').matches
  })
  const [conversationId, setConversationId] = useState('draft')
  const [conversationTitle, setConversationTitle] = useState('Canvas')
  const [messages, setMessages] = useState<CanvasMessageItem[]>([])
  const [inspectorSnapshot, setInspectorSnapshot] = useState<InspectorSnapshot | null>(null)
  const [selectionVersion, setSelectionVersion] = useState(0)
  const [cameraVersion, setCameraVersion] = useState(0)
  const cardMetaRef = useRef<Record<string, any>>({})
  const loadedConversationRef = useRef<string | null>(null)
  const saveTimerRef = useRef<number | null>(null)
  const selectionPollTimerRef = useRef<number | null>(null)
  const lastSelectionKeyRef = useRef('')
  const lastCameraKeyRef = useRef('')
  const gridCanvasRef = useRef<HTMLCanvasElement | null>(null)
  const hostRef = useRef<HTMLDivElement | null>(null)

  useEffect(() => {
    const reportError = (kind: string, detail: unknown) => {
      const error = detail instanceof Error ? detail : null
      postHostMessage('canvas_error', {
        kind,
        message: error?.message ?? String(detail ?? ''),
        stack: error?.stack ?? '',
      })
    }

    const handleError = (event: ErrorEvent) => reportError('error', event.error ?? event.message)
    const handleRejection = (event: PromiseRejectionEvent) => reportError('unhandledrejection', event.reason)

    window.addEventListener('error', handleError)
    window.addEventListener('unhandledrejection', handleRejection)
    return () => {
      window.removeEventListener('error', handleError)
      window.removeEventListener('unhandledrejection', handleRejection)
    }
  }, [])

  const selectedShape = useMemo(() => {
    if (!editor) return null
    const ids = asArray<string>(editor.getSelectedShapeIds?.())
    if (!ids.length) return null
    return editor.getShape?.(ids[0]) ?? null
  }, [editor, selectionVersion, messages])

  const selectedMeta = selectedShape ? extractShapeMeta(selectedShape) : null

  useEffect(() => {
    if (!editor) return

    const stopDocument = editor.store.listen(
      () => {
        syncShapeLayoutIntoMeta(editor, cardMetaRef.current)
        scheduleSave()
      },
      { scope: 'document', source: 'user' },
    )

    const pollEditorState = () => {
      const selectionKey = asArray<string>(editor.getSelectedShapeIds?.()).join('|')
      if (selectionKey !== lastSelectionKeyRef.current) {
        lastSelectionKeyRef.current = selectionKey
        setSelectionVersion((value) => value + 1)
      }

      const camera = normalizeCamera(editor.getCamera?.())
      const cameraKey = `${camera.x.toFixed(3)}|${camera.y.toFixed(3)}|${camera.z.toFixed(4)}`
      if (cameraKey !== lastCameraKeyRef.current) {
        lastCameraKeyRef.current = cameraKey
        setCameraVersion((value) => value + 1)
      }
    }

    pollEditorState()
    selectionPollTimerRef.current = window.setInterval(pollEditorState, 120)

    return () => {
      stopDocument?.()
      if (selectionPollTimerRef.current !== null) {
        window.clearInterval(selectionPollTimerRef.current)
        selectionPollTimerRef.current = null
      }
    }
  }, [editor])

  useEffect(() => {
    return () => {
      if (saveTimerRef.current !== null) {
        window.clearTimeout(saveTimerRef.current)
        saveTimerRef.current = null
      }
      if (selectionPollTimerRef.current !== null) {
        window.clearInterval(selectionPollTimerRef.current)
        selectionPollTimerRef.current = null
      }
    }
  }, [])

  useEffect(() => {
    const handleMessage = (event: MessageEvent) => {
      const envelope = event.data as HostEnvelope
      if (!envelope?.type) return

      if (envelope.type === 'bootstrap') {
        const payload = envelope.payload ?? {}
        setConversationId(payload.conversationId ?? 'draft')
        setConversationTitle(payload.conversationTitle ?? 'Canvas')
        setMessages(Array.isArray(payload.messages) ? payload.messages : [])
        setInspectorSnapshot(payload.inspectorSnapshot ?? null)
        cardMetaRef.current = {
          ...(payload.canvasSnapshot?.cardMetaPatches ?? {}),
        }

        const snapshot = payload.canvasSnapshot?.snapshot
        if (editor && payload.conversationId !== loadedConversationRef.current) {
          resetEditorDocument(editor)
          if (snapshot) {
            try {
              loadSnapshot(editor.store, sanitizeCanvasSnapshot(snapshot))
            } catch {
              resetEditorDocument(editor)
            }
          }
          loadedConversationRef.current = payload.conversationId ?? 'draft'
        }
      }

      if (envelope.type === 'conversation_delta') {
        const payload = envelope.payload ?? {}
        setConversationId(payload.conversationId ?? conversationId)
        setConversationTitle(payload.conversationTitle ?? conversationTitle)
        setMessages(Array.isArray(payload.messages) ? payload.messages : [])
      }

      if (envelope.type === 'inspector_update') {
        setInspectorSnapshot((envelope.payload ?? null) as InspectorSnapshot | null)
      }
    }

    hostBridge?.addEventListener?.('message', handleMessage)
    return () => hostBridge?.removeEventListener?.('message', handleMessage)
  }, [editor, conversationId, conversationTitle])

  useEffect(() => {
    if (!editor) return
    syncCardsIntoEditor(editor, messages, cardMetaRef.current)
  }, [editor, messages])

  useEffect(() => {
    if (typeof window === 'undefined' || typeof window.matchMedia !== 'function') return

    const mediaQuery = window.matchMedia('(prefers-color-scheme: dark)')
    const handleChange = () => setIsDarkMode(mediaQuery.matches)
    handleChange()

    if (typeof mediaQuery.addEventListener === 'function') {
      mediaQuery.addEventListener('change', handleChange)
      return () => mediaQuery.removeEventListener('change', handleChange)
    }

    mediaQuery.addListener(handleChange)
    return () => mediaQuery.removeListener(handleChange)
  }, [])

  useEffect(() => {
    if (!editor || !hostRef.current || !gridCanvasRef.current) return
    drawGridOverlay(gridCanvasRef.current, hostRef.current, editor, isDarkMode)
  }, [editor, cameraVersion, isDarkMode])

  useEffect(() => {
    document.documentElement.dataset.theme = isDarkMode ? 'dark' : 'light'
  }, [isDarkMode])

  function resetEditorDocument(targetEditor: any) {
    try {
      const ids = asArray<string>(targetEditor.getCurrentPageShapeIds?.())
      if (ids.length) targetEditor.deleteShapes(ids)
      targetEditor.setCamera?.({ x: 0, y: 0, z: 1 })
    } catch {
      // ignore and let next bootstrap rebuild from scratch
    }
  }

  function scheduleSave() {
    if (!editor) return
    if (saveTimerRef.current) window.clearTimeout(saveTimerRef.current)
    saveTimerRef.current = window.setTimeout(() => {
      const snapshot = getSnapshot(editor.store)
      postHostMessage('document_snapshot_save', { snapshot })
    }, 500)
  }

  function updateCardMeta(sourceRef: string, patch: Record<string, unknown>) {
    if (editor) syncShapeLayoutIntoMeta(editor, cardMetaRef.current, sourceRef)
    cardMetaRef.current[sourceRef] = { ...(cardMetaRef.current[sourceRef] ?? {}), ...patch }
    postHostMessage('card_meta_patch', { sourceRef, ...cardMetaRef.current[sourceRef] })
    syncCardsIntoEditor(editor, messages, cardMetaRef.current)
    scheduleSave()
  }

  function handleMount(nextEditor: any) {
    setEditor(nextEditor)
    loadedConversationRef.current = null
    postHostMessage('canvas_ready', { version: 'tldraw-formal-v1' })
    if (!hostBridge) {
      setMessages([
        {
          sourceRef: 'note:demo-1',
          role: 'assistant',
          kind: 'assistant_message',
          title: 'Canvas Demo',
          summary: 'Standalone mode without Rhino host bridge.',
          body: 'This is the formal tldraw workbench. Open inside Rhino to receive live conversation cards.',
          tags: ['demo'],
          collapsed: false,
          pinned: false,
        },
      ])
    }
  }

  function onFit() {
    editor?.zoomToFit?.()
  }

  function onNewNote() {
    if (!editor) return
    const sourceRef = `note:${Date.now()}`
    const shapeId = `shape:${slugify(sourceRef)}`
    const nextMeta = {
      sourceRef,
      title: 'New Note',
      summary: 'Manual canvas note',
      body: '',
      tags: [],
      collapsed: false,
      pinned: false,
      kind: 'note',
      role: 'note',
      userEditedTitle: true,
    }
    cardMetaRef.current[sourceRef] = nextMeta
    editor.createShapes?.([
      {
        id: shapeId,
        type: 'geo',
        x: 80,
        y: 80,
        props: {
          geo: 'rectangle',
          w: 300,
          h: 180,
          richText: composeCardRichText(nextMeta),
          color: 'green',
          labelColor: 'black',
          fill: 'semi',
          dash: 'draw',
          size: 's',
          align: 'start',
          verticalAlign: 'start',
          font: 'draw',
          url: '',
          growY: 0,
          scale: 1,
        },
        meta: nextMeta,
      },
    ])
    editor.select?.(shapeId)
    updateCardMeta(sourceRef, nextMeta)
  }

  function onGroup() {
    if (!editor) return
    const bounds = editor.getSelectionPageBounds?.()
    const selectedIds = asArray<string>(editor.getSelectedShapeIds?.())
    if (!bounds || selectedIds.length < 2) return

    const sourceRef = `group:${Date.now()}`
    const frameId = `shape:${slugify(sourceRef)}`
    editor.createShapes?.([
      {
        id: frameId,
        type: 'frame',
        x: bounds.x - 24,
        y: bounds.y - 44,
        props: {
          w: bounds.w + 48,
          h: bounds.h + 68,
          name: 'Group',
        },
        meta: {
          sourceRef,
          groupId: frameId,
          title: 'Group',
          memberIds: selectedIds,
          addghType: 'addgh-group',
        },
      },
    ])
    editor.select?.(frameId)
    scheduleSave()
  }

  function onCollapse() {
    if (!editor || !selectedShape) return
    const meta = extractShapeMeta(selectedShape)
    if (!meta?.sourceRef) return
    const nextCollapsed = !Boolean(meta.collapsed)
    updateCardMeta(meta.sourceRef, { collapsed: nextCollapsed })
  }

  function onInspector() {
    if (!selectedMeta?.sourceRef) return
    postHostMessage('open_inspector_for_source', { sourceRef: selectedMeta.sourceRef })
  }

  function onSync() {
    postHostMessage('canvas_ready', { reason: 'manual-sync' })
    scheduleSave()
  }

  return (
    <div className={`app-shell ${isDarkMode ? 'theme-dark' : 'theme-light'}`} ref={hostRef}>
      <div className="tldraw-host">
        <Tldraw hideUi colorScheme="system" onMount={handleMount} />
        <canvas ref={gridCanvasRef} className="grid-overlay" />
      </div>

      <div className="toolbar">
        <button onClick={onFit}>Fit</button>
        <button onClick={onNewNote}>New Note</button>
        <button onClick={onGroup}>Group</button>
        <button onClick={onCollapse}>Collapse</button>
        <button className="secondary" onClick={onInspector}>Inspector</button>
        <button className="secondary" onClick={onSync}>Sync</button>
      </div>

      <div className="status-pill">
        <span>{conversationTitle}</span>
        <span>{messages.length} cards</span>
        <span>{editor ? `${editor.getCamera?.().z?.toFixed?.(2) ?? '1.00'}x` : '1.00x'}</span>
      </div>

      {selectedMeta?.sourceRef ? (
        <aside className="inspector-panel">
          <h3>Card</h3>
          <label>
            <span>Title</span>
            <input
              value={selectedMeta.title ?? ''}
              onChange={(event) => updateCardMeta(selectedMeta.sourceRef, { title: event.target.value, userEditedTitle: true })}
            />
          </label>
          <label>
            <span>Tags</span>
            <input
              value={Array.isArray(selectedMeta.tags) ? selectedMeta.tags.join(', ') : ''}
              onChange={(event) =>
                updateCardMeta(selectedMeta.sourceRef, {
                  tags: event.target.value.split(',').map((tag) => tag.trim()).filter(Boolean),
                })}
            />
          </label>
          <label>
            <span>Body</span>
            <textarea
              value={selectedMeta.body ?? ''}
              onChange={(event) => updateCardMeta(selectedMeta.sourceRef, { body: event.target.value })}
            />
          </label>
          <label className="checkbox-row">
            <input
              type="checkbox"
              checked={Boolean(selectedMeta.collapsed)}
              onChange={(event) => updateCardMeta(selectedMeta.sourceRef, { collapsed: event.target.checked })}
            />
            <span>Collapsed</span>
          </label>
          <p className="meta-line">{selectedMeta.sourceRef}</p>
        </aside>
      ) : (
        <aside className="inspector-panel muted">
          <h3>Inspector</h3>
          <p>Select a card to edit its metadata.</p>
          {inspectorSnapshot?.canvasIssues ? <pre>{inspectorSnapshot.canvasIssues}</pre> : null}
        </aside>
      )}
    </div>
  )
}

export default function App() {
  return (
    <CanvasErrorBoundary>
      <CanvasWorkbench />
    </CanvasErrorBoundary>
  )
}

function syncCardsIntoEditor(editor: any, messages: CanvasMessageItem[], metaBySourceRef: Record<string, any>) {
  if (!editor) return

  const createShapes: any[] = []
  const updateShapes: any[] = []

  messages.forEach((item, index) => {
    const existing = getShapeBySourceRef(editor, item.sourceRef)
    const userMeta = metaBySourceRef[item.sourceRef] ?? {}
    const mergedMeta = {
      sourceRef: item.sourceRef,
      role: item.role ?? '',
      kind: item.kind ?? 'message',
      title: userMeta.userEditedTitle ? userMeta.title : (userMeta.title ?? item.title ?? 'Card'),
      summary: item.summary ?? '',
      body: typeof userMeta.body === 'string' ? userMeta.body : (item.body ?? ''),
      tags: Array.isArray(userMeta.tags) ? userMeta.tags : (item.tags ?? []),
      collapsed: Boolean(userMeta.collapsed ?? item.collapsed),
      pinned: Boolean(userMeta.pinned ?? item.pinned),
      userEditedTitle: Boolean(userMeta.userEditedTitle),
    }

    const x = typeof userMeta.x === 'number' ? userMeta.x : (60 + (index % 3) * 332)
    const y = typeof userMeta.y === 'number' ? userMeta.y : (80 + Math.floor(index / 3) * 220)
    const w = typeof userMeta.w === 'number' ? userMeta.w : 300
    const h = typeof userMeta.h === 'number' ? userMeta.h : (mergedMeta.collapsed ? 96 : 180)
    const richText = composeCardRichText(mergedMeta)
    const color = shapeColor(mergedMeta.role, mergedMeta.kind)

    if (!existing) {
      createShapes.push({
        id: `shape:${slugify(item.sourceRef)}`,
        type: 'geo',
        x,
        y,
        props: {
          geo: 'rectangle',
          w,
          h,
          richText,
          color,
          labelColor: 'black',
          fill: 'semi',
          dash: 'draw',
          size: 's',
          align: 'start',
          verticalAlign: 'start',
          font: 'draw',
          url: '',
          growY: 0,
          scale: 1,
        },
        meta: mergedMeta,
      })
      return
    }

    metaBySourceRef[item.sourceRef] = {
      ...metaBySourceRef[item.sourceRef],
      x: existing.x,
      y: existing.y,
      w: existing.props?.w ?? w,
      h: existing.props?.h ?? h,
    }

    const { text: _legacyText, ...existingProps } = existing.props ?? {}
    const nextProps = {
      ...existingProps,
      h,
      richText,
      color,
    }

    if (existing.x === x && existing.y === y && shallowJsonEqual(existing.meta, mergedMeta) && shallowJsonEqual(existingProps, nextProps)) {
      return
    }

    updateShapes.push({
      id: existing.id,
      type: existing.type,
      x,
      y,
      props: nextProps,
      meta: mergedMeta,
    })
  })

  const flushChanges = () => {
    if (createShapes.length) editor.createShapes?.(createShapes)
    if (updateShapes.length) editor.updateShapes?.(updateShapes)
  }

  if (typeof editor.batch === 'function') {
    editor.batch(flushChanges)
  } else {
    flushChanges()
  }
}

function shallowJsonEqual(left: any, right: any) {
  return JSON.stringify(left ?? null) === JSON.stringify(right ?? null)
}

function syncShapeLayoutIntoMeta(editor: any, metaBySourceRef: Record<string, any>, onlySourceRef?: string) {
  const shapes = asArray<any>(editor?.getCurrentPageShapes?.())
  for (const shape of shapes) {
    const meta = extractShapeMeta(shape)
    const sourceRef = meta.sourceRef
    if (!sourceRef) continue
    if (onlySourceRef && sourceRef !== onlySourceRef) continue

    metaBySourceRef[sourceRef] = {
      ...(metaBySourceRef[sourceRef] ?? {}),
      x: typeof shape.x === 'number' ? shape.x : metaBySourceRef[sourceRef]?.x,
      y: typeof shape.y === 'number' ? shape.y : metaBySourceRef[sourceRef]?.y,
      w: typeof shape.props?.w === 'number' ? shape.props.w : metaBySourceRef[sourceRef]?.w,
      h: typeof shape.props?.h === 'number' ? shape.props.h : metaBySourceRef[sourceRef]?.h,
    }
  }
}

function composeCardRichText(meta: any) {
  return toTldrawRichText(composeCardText(meta))
}

function toTldrawRichText(text: string) {
  return {
    type: 'doc',
    content: String(text ?? '').split('\n').map((line) => {
      if (!line) return { type: 'paragraph' }
      return {
        type: 'paragraph',
        content: [{ type: 'text', text: line }],
      }
    }),
  }
}

function sanitizeCanvasSnapshot(snapshot: any): any {
  if (!snapshot || typeof snapshot !== 'object') return snapshot
  if (Array.isArray(snapshot)) return snapshot.map(sanitizeCanvasSnapshot)

  const next: Record<string, any> = {}
  for (const [key, value] of Object.entries(snapshot)) {
    next[key] = sanitizeCanvasSnapshot(value)
  }

  if (next.type === 'geo' && next.props && typeof next.props === 'object' && 'text' in next.props) {
    const { text, ...props } = next.props
    next.props = {
      ...props,
      richText: props.richText ?? toTldrawRichText(String(text ?? '')),
    }
  }

  return next
}

function drawGridOverlay(canvas: HTMLCanvasElement, host: HTMLDivElement, editor: any, isDarkMode: boolean) {
  const rect = host.getBoundingClientRect()
  const dpr = window.devicePixelRatio || 1
  const ctx = canvas.getContext('2d')
  if (!ctx || rect.width <= 0 || rect.height <= 0) return

  canvas.width = Math.floor(rect.width * dpr)
  canvas.height = Math.floor(rect.height * dpr)
  canvas.style.width = `${rect.width}px`
  canvas.style.height = `${rect.height}px`

  ctx.clearRect(0, 0, canvas.width, canvas.height)
  ctx.save()
  ctx.scale(dpr, dpr)

  const camera = normalizeCamera(editor.getCamera?.())
  const minorStep = Math.max(12, 24 * camera.z)
  const majorStep = minorStep * 5
  const offsetX = camera.x * camera.z
  const offsetY = camera.y * camera.z

  if (!Number.isFinite(minorStep) || !Number.isFinite(majorStep) || minorStep <= 0 || majorStep <= 0) {
    ctx.restore()
    return
  }

  const minorColor = isDarkMode ? 'rgba(126, 144, 170, 0.18)' : 'rgba(126, 144, 170, 0.14)'
  const majorColor = isDarkMode ? 'rgba(164, 198, 255, 0.34)' : 'rgba(152, 182, 235, 0.24)'
  const crossColor = isDarkMode ? 'rgba(210, 232, 255, 0.68)' : 'rgba(124, 154, 207, 0.42)'

  for (let x = offsetX % minorStep; x < rect.width; x += minorStep) {
    const isMajor = Math.abs((x - offsetX) % majorStep) < 1 || Math.abs((x - offsetX) % majorStep - majorStep) < 1
    ctx.beginPath()
    ctx.moveTo(x, 0)
    ctx.lineTo(x, rect.height)
    ctx.strokeStyle = isMajor ? majorColor : minorColor
    ctx.lineWidth = isMajor ? 1.15 : 1
    ctx.stroke()
  }

  for (let y = offsetY % minorStep; y < rect.height; y += minorStep) {
    const isMajor = Math.abs((y - offsetY) % majorStep) < 1 || Math.abs((y - offsetY) % majorStep - majorStep) < 1
    ctx.beginPath()
    ctx.moveTo(0, y)
    ctx.lineTo(rect.width, y)
    ctx.strokeStyle = isMajor ? majorColor : minorColor
    ctx.lineWidth = isMajor ? 1.15 : 1
    ctx.stroke()
  }

  for (let x = offsetX % majorStep; x < rect.width; x += majorStep) {
    for (let y = offsetY % majorStep; y < rect.height; y += majorStep) {
      ctx.beginPath()
      ctx.moveTo(x - 5, y)
      ctx.lineTo(x + 5, y)
      ctx.moveTo(x, y - 5)
      ctx.lineTo(x, y + 5)
      ctx.strokeStyle = crossColor
      ctx.lineWidth = 1.2
      ctx.stroke()
    }
  }

  ctx.restore()
}

function normalizeCamera(camera: any) {
  const x = Number.isFinite(camera?.x) ? camera.x : 0
  const y = Number.isFinite(camera?.y) ? camera.y : 0
  const zRaw = Number.isFinite(camera?.z) ? camera.z : 1
  const z = Math.min(8, Math.max(0.25, zRaw))
  return { x, y, z }
}
