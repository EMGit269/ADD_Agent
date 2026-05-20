import React, { Component, ReactNode, useEffect, useMemo, useRef, useState } from 'react'

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

type Viewport = {
  x: number
  y: number
  z: number
}

type CanvasNodeType = 'message' | 'note' | 'image' | 'prompt' | 'file_upload' | 'slider' | 'code'

type PortDirection = 'input' | 'output'

type PortDataType = 'any' | 'image' | 'text' | 'path' | 'number' | 'code'

type NodePort = {
  id: string
  label: string
  direction: PortDirection
  dataType: PortDataType
  slot: number
}

type CanvasConnection = {
  id: string
  fromNodeId: string
  fromPortId: string
  toNodeId: string
  toPortId: string
}

type CanvasNode = {
  id: string
  sourceRef: string
  nodeType: CanvasNodeType
  x: number
  y: number
  w: number
  h: number
  meta: Record<string, any>
}

type LightweightSnapshot = {
  kind: 'addgh-lightweight-canvas-v1'
  viewport: Viewport
  nodes: CanvasNode[]
  connections?: CanvasConnection[]
}

type DragState =
  | { mode: 'pan'; pointerId: number; startX: number; startY: number; startViewport: Viewport }
  | { mode: 'node'; pointerId: number; sourceRef: string; startX: number; startY: number; startNodeX: number; startNodeY: number }
  | null

type PendingConnection = {
  nodeId: string
  portId: string
}

type CanvasSnapshotState = {
  nodes: CanvasNode[]
  connections: CanvasConnection[]
  viewport: Viewport
  selectedSourceRef: string | null
}

type CanvasHistoryState = {
  past: CanvasSnapshotState[]
  future: CanvasSnapshotState[]
}

type ContextMenuState = {
  x: number
  y: number
  worldX: number
  worldY: number
  mode: 'canvas' | 'node'
  sourceRef?: string
} | null

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
const snapshotKind = 'addgh-lightweight-canvas-v1'

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

function CanvasWorkbench() {
  const [isDarkMode, setIsDarkMode] = useState(() => {
    if (typeof window === 'undefined' || typeof window.matchMedia !== 'function') return false
    return window.matchMedia('(prefers-color-scheme: dark)').matches
  })
  const [conversationId, setConversationId] = useState('draft')
  const [conversationTitle, setConversationTitle] = useState('Canvas')
  const [messages, setMessages] = useState<CanvasMessageItem[]>([])
  const [nodes, setNodes] = useState<CanvasNode[]>([])
  const [connections, setConnections] = useState<CanvasConnection[]>([])
  const [viewport, setViewport] = useState<Viewport>({ x: 80, y: 90, z: 1 })
  const [selectedSourceRef, setSelectedSourceRef] = useState<string | null>(null)
  const [pendingConnection, setPendingConnection] = useState<PendingConnection | null>(null)
  const [detailSourceRef, setDetailSourceRef] = useState<string | null>(null)
  const [contextMenu, setContextMenu] = useState<ContextMenuState>(null)
  const [inspectorSnapshot, setInspectorSnapshot] = useState<InspectorSnapshot | null>(null)
  const [interactionMode, setInteractionMode] = useState('idle')
  const [imagePreviewSrc, setImagePreviewSrc] = useState<string | null>(null)
  const [imagePreviewTitle, setImagePreviewTitle] = useState<string>('Image Preview')
  const surfaceRef = useRef<HTMLDivElement | null>(null)
  const dragRef = useRef<DragState>(null)
  const saveTimerRef = useRef<number | null>(null)
  const nodesRef = useRef(nodes)
  const connectionsRef = useRef(connections)
  const viewportRef = useRef(viewport)
  const selectedSourceRefRef = useRef<string | null>(selectedSourceRef)
  const cardMetaRef = useRef<Record<string, any>>({})
  const historyRef = useRef<CanvasHistoryState>({ past: [], future: [] })
  const lastMessagesSignatureRef = useRef('')
  const currentConversationIdRef = useRef('draft')
  const currentConversationTitleRef = useRef('Canvas')

  useEffect(() => {
    nodesRef.current = nodes
  }, [nodes])

  useEffect(() => {
    connectionsRef.current = connections
  }, [connections])

  useEffect(() => {
    viewportRef.current = viewport
  }, [viewport])

  useEffect(() => {
    selectedSourceRefRef.current = selectedSourceRef
  }, [selectedSourceRef])

  useEffect(() => {
    currentConversationIdRef.current = conversationId
    currentConversationTitleRef.current = conversationTitle
  }, [conversationId, conversationTitle])

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
    document.documentElement.dataset.theme = isDarkMode ? 'dark' : 'light'
  }, [isDarkMode])

  useEffect(() => {
    const handleKeyDown = (event: KeyboardEvent) => {
      const target = event.target as HTMLElement | null
      const isTyping = Boolean(target?.closest('input,textarea,[contenteditable="true"]'))

      if ((event.ctrlKey || event.metaKey) && !event.shiftKey && event.key.toLowerCase() === 'z') {
        event.preventDefault()
        const last = historyRef.current.past[historyRef.current.past.length - 1]
        if (!last) return
        historyRef.current = {
          past: historyRef.current.past.slice(0, -1),
          future: [createSnapshotState(), ...historyRef.current.future],
        }
        applySnapshotState(last)
        return
      }

      if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === 'y') {
        event.preventDefault()
        const next = historyRef.current.future[0]
        if (!next) return
        historyRef.current = {
          past: [...historyRef.current.past, createSnapshotState()],
          future: historyRef.current.future.slice(1),
        }
        applySnapshotState(next)
        return
      }

      if (isTyping) return

      if (event.key === 'Delete' && selectedSourceRefRef.current) {
        event.preventDefault()
        deleteNodeBySourceRef(selectedSourceRefRef.current)
      }
    }

    window.addEventListener('keydown', handleKeyDown)
    return () => window.removeEventListener('keydown', handleKeyDown)
  }, [])

  useEffect(() => {
    const applyEnvelope = (envelope: HostEnvelope) => {
      if (!envelope?.type) return

      if (envelope.type === 'bootstrap') {
        const payload = envelope.payload ?? {}
        const nextConversationId = payload.conversationId ?? 'draft'
        const nextConversationTitle = payload.conversationTitle ?? 'Canvas'
        const nextMessages = Array.isArray(payload.messages) ? payload.messages : []
        const nextSignature = computeMessagesSignature(nextMessages)
        const savedSnapshot = parseSnapshot(payload.canvasSnapshot?.snapshot)
        const savedMeta = payload.canvasSnapshot?.cardMetaPatches ?? {}

        currentConversationIdRef.current = nextConversationId
        currentConversationTitleRef.current = nextConversationTitle
        setConversationId(nextConversationId)
        setConversationTitle(nextConversationTitle)
        setInspectorSnapshot(payload.inspectorSnapshot ?? null)
        cardMetaRef.current = { ...savedMeta }
        if (savedSnapshot?.viewport) setViewport(normalizeViewport(savedSnapshot.viewport))
        if (Array.isArray(savedSnapshot?.connections)) setConnections(savedSnapshot.connections.map(normalizeConnection))

        setNodes((previous) => {
          const baseNodes = savedSnapshot?.nodes?.length ? savedSnapshot.nodes : previous
          return reconcileNodes(nextMessages, cardMetaRef.current, baseNodes, savedSnapshot?.connections ?? connectionsRef.current)
        })

        if (nextSignature !== lastMessagesSignatureRef.current) {
          lastMessagesSignatureRef.current = nextSignature
          setMessages(nextMessages)
        }
      }

      if (envelope.type === 'conversation_delta') {
        const payload = envelope.payload ?? {}
        const nextConversationId = payload.conversationId ?? currentConversationIdRef.current
        const nextConversationTitle = payload.conversationTitle ?? currentConversationTitleRef.current
        const nextMessages = Array.isArray(payload.messages) ? payload.messages : []
        const nextSignature = computeMessagesSignature(nextMessages)

        currentConversationIdRef.current = nextConversationId
        currentConversationTitleRef.current = nextConversationTitle
        setConversationId(nextConversationId)
        setConversationTitle(nextConversationTitle)
        if (nextSignature !== lastMessagesSignatureRef.current) {
          lastMessagesSignatureRef.current = nextSignature
          setMessages(nextMessages)
          setNodes((previous) => reconcileNodes(nextMessages, cardMetaRef.current, previous, connectionsRef.current))
        }
      }

      if (envelope.type === 'inspector_update') {
        setInspectorSnapshot((envelope.payload ?? null) as InspectorSnapshot | null)
      }
    }

    const handleMessage = (event: MessageEvent) => applyEnvelope(event.data as HostEnvelope)
    hostBridge?.addEventListener?.('message', handleMessage)

    postHostMessage('canvas_ready', { version: 'lightweight-canvas-v1' })
    if (!hostBridge) {
      const demoMessages = [
        {
          sourceRef: 'note:demo-1',
          role: 'assistant',
          kind: 'assistant_message',
          title: 'Canvas Demo',
          summary: 'Standalone lightweight canvas.',
          body: 'This canvas uses plain DOM nodes instead of tldraw. Drag nodes, pan the background, and zoom with the wheel.',
          tags: ['demo'],
          collapsed: false,
          pinned: false,
        },
      ]
      setMessages(demoMessages)
      setNodes(reconcileNodes(demoMessages, {}, []))
    }

    return () => {
      hostBridge?.removeEventListener?.('message', handleMessage)
      if (saveTimerRef.current !== null) {
        window.clearTimeout(saveTimerRef.current)
        saveTimerRef.current = null
      }
    }
  }, [])

  const selectedNode = useMemo(
    () => nodes.find((node) => node.sourceRef === selectedSourceRef) ?? null,
    [nodes, selectedSourceRef],
  )
  const detailNode = useMemo(
    () => nodes.find((node) => node.sourceRef === detailSourceRef) ?? null,
    [nodes, detailSourceRef],
  )
  const detailMeta = detailNode?.meta ?? null

  function createSnapshotState(
    override: Partial<CanvasSnapshotState> = {},
  ): CanvasSnapshotState {
    return {
      nodes: cloneNodes(override.nodes ?? nodesRef.current),
      connections: cloneConnections(override.connections ?? connectionsRef.current),
      viewport: { ...(override.viewport ?? viewportRef.current) },
      selectedSourceRef: override.selectedSourceRef ?? selectedSourceRefRef.current,
    }
  }

  function applySnapshotState(snapshot: CanvasSnapshotState, shouldSave = true) {
    nodesRef.current = cloneNodes(snapshot.nodes)
    connectionsRef.current = cloneConnections(snapshot.connections)
    viewportRef.current = { ...snapshot.viewport }
    selectedSourceRefRef.current = snapshot.selectedSourceRef
    setNodes(nodesRef.current)
    setConnections(connectionsRef.current)
    setViewport(viewportRef.current)
    setSelectedSourceRef(snapshot.selectedSourceRef)
    if (shouldSave) scheduleSave()
  }

  function pushHistorySnapshot(snapshot: CanvasSnapshotState) {
    historyRef.current = {
      past: [...historyRef.current.past, snapshot],
      future: [],
    }
  }

  function commitMutation(mutator: (snapshot: CanvasSnapshotState) => CanvasSnapshotState) {
    const before = createSnapshotState()
    const after = mutator(before)
    pushHistorySnapshot(before)
    applySnapshotState(after)
  }

  function scheduleSave() {
    if (saveTimerRef.current !== null) window.clearTimeout(saveTimerRef.current)
    saveTimerRef.current = window.setTimeout(() => {
      saveTimerRef.current = null
      const snapshot: LightweightSnapshot = {
        kind: snapshotKind,
        viewport: viewportRef.current,
        nodes: nodesRef.current,
        connections: connectionsRef.current,
      }
      postHostMessage('document_snapshot_save', { snapshot })
    }, 350)
  }

  function updateNodes(next: CanvasNode[] | ((current: CanvasNode[]) => CanvasNode[]), shouldSave = true) {
    setNodes((current) => {
      const resolved = typeof next === 'function' ? next(current) : next
      nodesRef.current = resolved
      if (shouldSave) scheduleSave()
      return resolved
    })
  }

  function updateConnections(
    next: CanvasConnection[] | ((current: CanvasConnection[]) => CanvasConnection[]),
    shouldSave = true,
  ) {
    setConnections((current) => {
      const resolved = dedupeConnections(typeof next === 'function' ? next(current) : next)
      connectionsRef.current = resolved
      if (shouldSave) scheduleSave()
      return resolved
    })
  }

  function updateViewport(next: Viewport | ((current: Viewport) => Viewport), shouldSave = true) {
    setViewport((current) => {
      const resolved = normalizeViewport(typeof next === 'function' ? next(current) : next)
      viewportRef.current = resolved
      if (shouldSave) scheduleSave()
      return resolved
    })
  }

  function updateNodeMeta(sourceRef: string, patch: Record<string, unknown>) {
    cardMetaRef.current[sourceRef] = { ...(cardMetaRef.current[sourceRef] ?? {}), ...patch }
    updateNodes((current) =>
      current.map((node) => {
        if (node.sourceRef !== sourceRef) return node
        const nextMeta = { ...node.meta, ...patch }
        nextMeta.ports = getNodePorts(node.nodeType, nextMeta)
        const { w, h } = resolveNodeSize(nextMeta, node.nodeType, node)
        return {
          ...node,
          w,
          h,
          meta: nextMeta,
        }
      }),
    )
    postHostMessage('card_meta_patch', { sourceRef, ...cardMetaRef.current[sourceRef] })
  }

  function deleteNodeBySourceRef(sourceRef: string) {
    commitMutation((snapshot) => {
      const remainingNodes = snapshot.nodes.filter((node) => node.sourceRef !== sourceRef)
      const remainingNodeIds = new Set(remainingNodes.map((node) => node.id))
      const remainingConnections = snapshot.connections.filter(
        (connection) => remainingNodeIds.has(connection.fromNodeId) && remainingNodeIds.has(connection.toNodeId),
      )
      return {
        ...snapshot,
        nodes: remainingNodes,
        connections: remainingConnections,
        selectedSourceRef: snapshot.selectedSourceRef === sourceRef ? null : snapshot.selectedSourceRef,
      }
    })
    setDetailSourceRef((current) => (current === sourceRef ? null : current))
    setContextMenu(null)
  }

  function createTypedNodeAt(nodeType: CanvasNodeType, x: number, y: number) {
    const sourceRef = `${nodeType}:${Date.now()}`
    const meta = buildDefaultNodeMeta(nodeType, sourceRef)
    const node = createNode(meta, nodesRef.current.length, x, y, nodeType)
    cardMetaRef.current[sourceRef] = meta
    commitMutation((snapshot) => ({
      ...snapshot,
      nodes: [...snapshot.nodes, node],
      selectedSourceRef: sourceRef,
    }))
    setContextMenu(null)
  }

  function onSurfacePointerDown(event: React.PointerEvent<HTMLDivElement>) {
    if (event.button !== 0) return
    if ((event.target as HTMLElement).closest('.canvas-node')) return
    setContextMenu(null)
    if (pendingConnection) setPendingConnection(null)
    dragRef.current = {
      mode: 'pan',
      pointerId: event.pointerId,
      startX: event.clientX,
      startY: event.clientY,
      startViewport: viewportRef.current,
    }
    setSelectedSourceRef(null)
    setInteractionMode('pan')
    event.currentTarget.setPointerCapture(event.pointerId)
  }

  function onNodePointerDown(event: React.PointerEvent<HTMLElement>, node: CanvasNode) {
    if (event.button !== 0) return
    const target = event.target as HTMLElement
    if (target.closest('button,input,textarea,select')) return
    setContextMenu(null)
    event.stopPropagation()
    setSelectedSourceRef(node.sourceRef)
    dragRef.current = {
      mode: 'node',
      pointerId: event.pointerId,
      sourceRef: node.sourceRef,
      startX: event.clientX,
      startY: event.clientY,
      startNodeX: node.x,
      startNodeY: node.y,
    }
    setInteractionMode('drag')
    surfaceRef.current?.setPointerCapture(event.pointerId)
  }

  function handlePortClick(node: CanvasNode, port: NodePort) {
    setContextMenu(null)
    setSelectedSourceRef(node.sourceRef)
    if (port.direction === 'output') {
      setPendingConnection({ nodeId: node.id, portId: port.id })
      setInteractionMode('connect')
      return
    }
    if (!pendingConnection) return
    if (pendingConnection.nodeId === node.id && pendingConnection.portId === port.id) {
      setPendingConnection(null)
      setInteractionMode('idle')
      return
    }
    connectNodes(pendingConnection, { nodeId: node.id, portId: port.id })
    setInteractionMode('idle')
  }

  function connectNodes(from: PendingConnection, to: PendingConnection) {
    if (from.nodeId === to.nodeId && from.portId === to.portId) return
    updateConnections((current) => [
      ...current,
      {
        id: `conn:${from.nodeId}:${from.portId}->${to.nodeId}:${to.portId}`,
        fromNodeId: from.nodeId,
        fromPortId: from.portId,
        toNodeId: to.nodeId,
        toPortId: to.portId,
      },
    ])
    setPendingConnection(null)
  }

  function onPointerMove(event: React.PointerEvent<HTMLDivElement>) {
    const drag = dragRef.current
    if (!drag || drag.pointerId !== event.pointerId) return

    if (drag.mode === 'pan') {
      updateViewport({
        ...drag.startViewport,
        x: drag.startViewport.x + event.clientX - drag.startX,
        y: drag.startViewport.y + event.clientY - drag.startY,
      })
      return
    }

    const zoom = viewportRef.current.z
    const dx = (event.clientX - drag.startX) / zoom
    const dy = (event.clientY - drag.startY) / zoom
    updateNodes((current) =>
      current.map((node) =>
        node.sourceRef === drag.sourceRef
          ? {
              ...node,
              x: drag.startNodeX + dx,
              y: drag.startNodeY + dy,
              meta: {
                ...node.meta,
                x: drag.startNodeX + dx,
                y: drag.startNodeY + dy,
              },
            }
          : node,
      ),
    )
  }

  function onPointerUp(event: React.PointerEvent<HTMLDivElement>) {
    if (dragRef.current?.pointerId !== event.pointerId) return
    dragRef.current = null
    setInteractionMode('idle')
    try {
      event.currentTarget.releasePointerCapture(event.pointerId)
    } catch {
      // The pointer may have already been released by the browser.
    }
    syncNodeLayoutsToMeta()
    scheduleSave()
  }

  function onWheel(event: React.WheelEvent<HTMLDivElement>) {
    setContextMenu(null)
    event.preventDefault()
    const rect = event.currentTarget.getBoundingClientRect()
    const point = {
      x: event.clientX - rect.left,
      y: event.clientY - rect.top,
    }
    const current = viewportRef.current

    if (!event.ctrlKey && !event.metaKey) {
      updateViewport({
        ...current,
        x: current.x - event.deltaX,
        y: current.y - event.deltaY,
      })
      return
    }

    const nextZoom = clamp(current.z * Math.exp(-event.deltaY * 0.0012), 0.25, 2.8)
    const worldX = (point.x - current.x) / current.z
    const worldY = (point.y - current.y) / current.z
    updateViewport({
      x: point.x - worldX * nextZoom,
      y: point.y - worldY * nextZoom,
      z: nextZoom,
    })
  }

  function syncNodeLayoutsToMeta() {
    for (const node of nodesRef.current) {
      cardMetaRef.current[node.sourceRef] = {
        ...(cardMetaRef.current[node.sourceRef] ?? {}),
        x: node.x,
        y: node.y,
        w: node.w,
        h: node.h,
      }
    }
  }

  function onFit() {
    if (!nodes.length || !surfaceRef.current) return
    const rect = surfaceRef.current.getBoundingClientRect()
    const bounds = getNodesBounds(nodes)
    const padding = 90
    const z = clamp(Math.min((rect.width - padding * 2) / bounds.w, (rect.height - padding * 2) / bounds.h), 0.35, 1.3)
    updateViewport({
      z,
      x: rect.width / 2 - (bounds.x + bounds.w / 2) * z,
      y: rect.height / 2 - (bounds.y + bounds.h / 2) * z,
    })
  }

  function onNewNote() {
    const center = screenToWorld(
      surfaceRef.current?.clientWidth ? surfaceRef.current.clientWidth / 2 : 300,
      surfaceRef.current?.clientHeight ? surfaceRef.current.clientHeight / 2 : 220,
      viewportRef.current,
    )
    createTypedNodeAt('note', center.x - 160, center.y - 90)
  }

  function onAddTypedNode(nodeType: CanvasNodeType) {
    const center = screenToWorld(
      surfaceRef.current?.clientWidth ? surfaceRef.current.clientWidth / 2 : 300,
      surfaceRef.current?.clientHeight ? surfaceRef.current.clientHeight / 2 : 220,
      viewportRef.current,
    )
    createTypedNodeAt(nodeType, center.x - 180, center.y - 120)
  }

  function onCollapse() {
    if (!selectedNode) return
    updateNodeMeta(selectedNode.sourceRef, { collapsed: !Boolean(selectedNode.meta.collapsed) })
  }

  function onSync() {
    syncNodeLayoutsToMeta()
    scheduleSave()
    postHostMessage('canvas_ready', { reason: 'manual-sync' })
  }

  function onCanvasContextMenu(event: React.MouseEvent<HTMLDivElement>) {
    event.preventDefault()
    const target = event.target as HTMLElement
    const nodeHost = target.closest('.canvas-node') as HTMLElement | null
    if (nodeHost?.dataset.sourceRef) {
      setSelectedSourceRef(nodeHost.dataset.sourceRef)
      setContextMenu({
        x: event.clientX,
        y: event.clientY,
        worldX: 0,
        worldY: 0,
        mode: 'node',
        sourceRef: nodeHost.dataset.sourceRef,
      })
      return
    }

    const rect = event.currentTarget.getBoundingClientRect()
    const point = screenToWorld(event.clientX - rect.left, event.clientY - rect.top, viewportRef.current)
    setContextMenu({
      x: event.clientX,
      y: event.clientY,
      worldX: point.x,
      worldY: point.y,
      mode: 'canvas',
    })
  }

  return (
    <div className={`app-shell ${isDarkMode ? 'theme-dark' : 'theme-light'}`}>
      <div
        ref={surfaceRef}
        className={`light-canvas ${interactionMode !== 'idle' ? `is-${interactionMode}` : ''}`}
        style={gridStyle(viewport, isDarkMode)}
        onContextMenu={onCanvasContextMenu}
        onPointerDown={onSurfacePointerDown}
        onPointerMove={onPointerMove}
        onPointerUp={onPointerUp}
        onPointerCancel={onPointerUp}
        onWheel={onWheel}
      >
        <div className="canvas-content">
          <svg className="connection-layer" width="100%" height="100%">
            {connections.map((connection) => renderConnection(connection, nodes, viewport))}
          </svg>
          {nodes.map((node) => (
            <article
              key={node.id}
              className={`canvas-node node-${nodeKind(node.meta)} type-${node.nodeType} ${selectedSourceRef === node.sourceRef ? 'selected' : ''} ${node.nodeType === 'image' ? 'is-image-node' : ''}`}
              style={nodeStyle(node, viewport)}
              data-source-ref={node.sourceRef}
              onPointerDown={(event) => onNodePointerDown(event, node)}
              onDoubleClick={() => setDetailSourceRef(node.sourceRef)}
            >
              {node.nodeType === 'image' ? (
                renderImageNode(node, (src, title) => {
                  setImagePreviewSrc(src)
                  setImagePreviewTitle(title)
                })
              ) : (
                <>
                  <header className="node-header">
                    <span>{node.meta.title ?? 'Untitled'}</span>
                    <small>{node.nodeType}</small>
                  </header>
                  <p className="node-summary">{node.meta.summary || node.meta.sourceRef}</p>
                  {renderNodePreview(node, (src, title) => {
                    setImagePreviewSrc(src)
                    setImagePreviewTitle(title)
                  })}
                  {renderPorts(node, pendingConnection, handlePortClick)}
                  {!node.meta.collapsed ? <pre className="node-body">{getNodePreviewText(node)}</pre> : null}
                  {Array.isArray(node.meta.tags) && node.meta.tags.length ? (
                    <div className="node-tags">
                      {node.meta.tags.map((tag: string) => (
                        <span key={tag}>#{tag}</span>
                      ))}
                    </div>
                  ) : null}
                </>
              )}
            </article>
          ))}
        </div>
      </div>

      <div className="floating-toolbar">
        <button onClick={onFit}>Fit</button>
        <button onClick={onNewNote}>Note</button>
        <button onClick={() => detailSourceRef ? setDetailSourceRef(null) : setDetailSourceRef(selectedSourceRef)}>Details</button>
        <button onClick={onCollapse} disabled={!selectedNode}>Collapse</button>
        <button onClick={onSync}>Sync</button>
      </div>

      <div className="status-pill">
        <span>{conversationTitle}</span>
        <span>{nodes.length} nodes</span>
        <span>{viewport.z.toFixed(2)}x</span>
      </div>

      {detailMeta?.sourceRef ? (
        <div className="detail-modal-backdrop" onClick={() => setDetailSourceRef(null)}>
          <aside className="detail-modal" onClick={(event) => event.stopPropagation()}>
            <div className="detail-header">
              <h3>{detailNode?.meta.title ?? 'Node'}</h3>
              <button type="button" onClick={() => setDetailSourceRef(null)}>Close</button>
            </div>
            <label>
              <span>Title</span>
              <input
                value={detailMeta.title ?? ''}
                onChange={(event) => updateNodeMeta(detailMeta.sourceRef, { title: event.target.value, userEditedTitle: true })}
              />
            </label>
            <label>
              <span>Tags</span>
              <input
                value={Array.isArray(detailMeta.tags) ? detailMeta.tags.join(', ') : ''}
                onChange={(event) =>
                  updateNodeMeta(detailMeta.sourceRef, {
                    tags: event.target.value.split(',').map((tag) => tag.trim()).filter(Boolean),
                  })}
              />
            </label>
            {detailNode ? renderNodeBody(detailNode, updateNodeMeta, (src, title) => {
              setImagePreviewSrc(src)
              setImagePreviewTitle(title)
            }) : null}
            <label>
              <span>Body</span>
              <textarea
                value={detailMeta.body ?? ''}
                onChange={(event) => updateNodeMeta(detailMeta.sourceRef, { body: event.target.value })}
              />
            </label>
            <label className="checkbox-row">
              <input
                type="checkbox"
                checked={Boolean(detailMeta.collapsed)}
                onChange={(event) => updateNodeMeta(detailMeta.sourceRef, { collapsed: event.target.checked })}
              />
              <span>Collapsed</span>
            </label>
          </aside>
        </div>
      ) : null}

      {contextMenu ? (
        <div className="context-menu" style={{ left: contextMenu.x, top: contextMenu.y }}>
          {contextMenu.mode === 'canvas' ? (
            <>
              <button onClick={() => createTypedNodeAt('note', contextMenu.worldX, contextMenu.worldY)}>New Note</button>
              <button onClick={() => createTypedNodeAt('image', contextMenu.worldX, contextMenu.worldY)}>Image Node</button>
              <button onClick={() => createTypedNodeAt('prompt', contextMenu.worldX, contextMenu.worldY)}>Prompt Node</button>
              <button onClick={() => createTypedNodeAt('file_upload', contextMenu.worldX, contextMenu.worldY)}>File Node</button>
              <button onClick={() => createTypedNodeAt('slider', contextMenu.worldX, contextMenu.worldY)}>Slider Node</button>
              <button onClick={() => createTypedNodeAt('code', contextMenu.worldX, contextMenu.worldY)}>Code Node</button>
            </>
          ) : (
            <>
              <button onClick={() => contextMenu.sourceRef ? setDetailSourceRef(contextMenu.sourceRef) : null}>Edit Details</button>
              <button onClick={() => contextMenu.sourceRef ? deleteNodeBySourceRef(contextMenu.sourceRef) : null}>Delete</button>
            </>
          )}
        </div>
      ) : null}

      {imagePreviewSrc ? (
        <div className="image-preview-overlay" onClick={() => setImagePreviewSrc(null)}>
          <div className="image-preview-dialog" onClick={(event) => event.stopPropagation()}>
            <div className="image-preview-header">
              <span>{imagePreviewTitle}</span>
              <button type="button" onClick={() => setImagePreviewSrc(null)}>Close</button>
            </div>
            <img src={imagePreviewSrc} alt={imagePreviewTitle} />
          </div>
        </div>
      ) : null}
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

function reconcileNodes(
  messages: CanvasMessageItem[],
  metaBySourceRef: Record<string, any>,
  existingNodes: CanvasNode[],
  _existingConnections: CanvasConnection[] = [],
) {
  const bySourceRef = new Map(existingNodes.map((node) => [node.sourceRef, node]))
  const hasTypedNodes = existingNodes.some((node) => node.nodeType !== 'message')
  const nextNodes = [...existingNodes.filter((node) => node.nodeType !== 'message')]

  if (hasTypedNodes) {
    return dedupeNodes(nextNodes)
  }

  messages.forEach((item, index) => {
    const existing = bySourceRef.get(item.sourceRef)
    const userMeta = metaBySourceRef[item.sourceRef] ?? {}
    const meta = mergeMessageMeta(item, userMeta)
    const x = numberOr(existing?.x, userMeta.x, 70 + (index % 3) * 350)
    const y = numberOr(existing?.y, userMeta.y, 90 + Math.floor(index / 3) * 250)
    const w = numberOr(existing?.w, userMeta.w, 320)
    const h = meta.collapsed ? 112 : numberOr(existing?.h, userMeta.h, 205)

    nextNodes.push({
      id: existing?.id ?? `node:${slugify(item.sourceRef)}`,
      sourceRef: item.sourceRef,
      nodeType: 'message',
      x,
      y,
      w,
      h,
      meta: {
        ...meta,
        x,
        y,
        w,
        h,
      },
    })
  })

  return dedupeNodes(nextNodes)
}

function reconcileTypedNodes(existingNodes: CanvasNode[]) {
  return dedupeNodes(existingNodes.map((node) => {
    if (node.nodeType === 'message' || node.nodeType === 'note') return node
    const meta = { ...node.meta }
    if (node.nodeType === 'image' && !meta.imageDataUrl && meta.imagePath) {
      meta.imageDataUrl = meta.imagePath
    }
    const { w, h } = resolveNodeSize(meta, node.nodeType, node)
    return {
      ...node,
      w,
      h,
      meta: {
        ...meta,
        w,
        h,
      },
    }
  }))
}

function mergeMessageMeta(item: CanvasMessageItem, userMeta: Record<string, any>) {
  return {
    sourceRef: item.sourceRef,
    role: item.role ?? '',
    kind: item.kind ?? 'message',
    title: userMeta.userEditedTitle ? userMeta.title : (userMeta.title ?? item.title ?? 'Card'),
    summary: item.summary ?? userMeta.summary ?? '',
    body: typeof userMeta.body === 'string' ? userMeta.body : (item.body ?? ''),
    tags: Array.isArray(userMeta.tags) ? userMeta.tags : (item.tags ?? []),
    collapsed: Boolean(userMeta.collapsed ?? item.collapsed),
    pinned: Boolean(userMeta.pinned ?? item.pinned),
    userEditedTitle: Boolean(userMeta.userEditedTitle),
  }
}

function createNode(meta: Record<string, any>, index: number, x?: number, y?: number, nodeType: CanvasNodeType = 'message'): CanvasNode {
  const size = resolveNodeSize(meta, nodeType)
  const ports = getNodePorts(nodeType, meta)
  return {
    id: `node:${slugify(meta.sourceRef ?? `manual-${index}`)}`,
    sourceRef: meta.sourceRef,
    nodeType,
    x: typeof x === 'number' ? x : 70 + (index % 3) * 350,
    y: typeof y === 'number' ? y : 90 + Math.floor(index / 3) * 250,
    w: size.w,
    h: size.h,
    meta: {
      ...meta,
      nodeType,
      ports,
      w: size.w,
      h: size.h,
    },
  }
}

function parseSnapshot(value: any): LightweightSnapshot | null {
  if (!value || typeof value !== 'object') return null
  if (value.kind !== snapshotKind) return null
  const nodes = Array.isArray(value.nodes)
    ? value.nodes.filter((node: any) => node?.sourceRef).map(normalizeNode)
    : []
  return {
    kind: snapshotKind,
    viewport: normalizeViewport(value.viewport),
    nodes,
    connections: Array.isArray(value.connections) ? value.connections.map(normalizeConnection) : [],
  }
}

function normalizeNode(node: any): CanvasNode {
  const sourceRef = String(node.sourceRef)
  const meta = typeof node.meta === 'object' && node.meta ? node.meta : {}
  const nodeType = normalizeNodeType(node.nodeType ?? meta.nodeType ?? meta.kind)
  const resolvedMeta = { ...meta, sourceRef, nodeType }
  const size = resolveNodeSize(resolvedMeta, nodeType, node)
  return {
    id: String(node.id ?? `node:${slugify(sourceRef)}`),
    sourceRef,
    nodeType,
    x: numberOr(node.x, meta.x, 80),
    y: numberOr(node.y, meta.y, 80),
    w: size.w,
    h: size.h,
    meta: {
      ...resolvedMeta,
      ports: Array.isArray(meta.ports) ? meta.ports.map(normalizePort) : getNodePorts(nodeType, resolvedMeta),
      w: size.w,
      h: size.h,
    },
  }
}

function normalizeConnection(connection: any): CanvasConnection {
  return {
    id: String(connection?.id ?? `conn:${connection?.fromNodeId ?? 'a'}:${connection?.fromPortId ?? 'x'}->${connection?.toNodeId ?? 'b'}:${connection?.toPortId ?? 'y'}`),
    fromNodeId: String(connection?.fromNodeId ?? ''),
    fromPortId: String(connection?.fromPortId ?? ''),
    toNodeId: String(connection?.toNodeId ?? ''),
    toPortId: String(connection?.toPortId ?? ''),
  }
}

function dedupeConnections(connections: CanvasConnection[]) {
  const seen = new Set<string>()
  return connections.filter((connection) => {
    if (seen.has(connection.id)) return false
    seen.add(connection.id)
    return Boolean(connection.fromNodeId && connection.fromPortId && connection.toNodeId && connection.toPortId)
  })
}

function normalizePort(port: any): NodePort {
  return {
    id: String(port?.id ?? 'port'),
    label: String(port?.label ?? 'Port'),
    direction: port?.direction === 'input' ? 'input' : 'output',
    dataType: normalizePortDataType(port?.dataType),
    slot: numberOr(port?.slot, undefined, 0),
  }
}

function normalizePortDataType(value: any): PortDataType {
  if (value === 'image' || value === 'text' || value === 'path' || value === 'number' || value === 'code') return value
  return 'any'
}

function resolveNodeSize(meta: Record<string, any>, nodeType: CanvasNodeType, fallback?: Partial<CanvasNode>) {
  if (nodeType === 'prompt') return { w: 300, h: 180 }
  if (nodeType === 'file_upload') return { w: 340, h: 210 }
  if (nodeType === 'slider') return { w: 290, h: 170 }
  if (nodeType === 'code') return { w: 420, h: 260 }
  if (nodeType === 'image') return { w: 360, h: 260 }
  if (meta.collapsed) return { w: numberOr(fallback?.w, meta.w, 320), h: 112 }
  return { w: numberOr(fallback?.w, meta.w, 320), h: numberOr(fallback?.h, meta.h, 205) }
}

function normalizeNodeType(value: any): CanvasNodeType {
  if (value === 'image' || value === 'prompt' || value === 'file_upload' || value === 'slider' || value === 'code' || value === 'note') {
    return value
  }
  return 'message'
}

function getNodePorts(nodeType: CanvasNodeType, meta: Record<string, any>): NodePort[] {
  if (nodeType === 'message') return []
  if (nodeType === 'image') {
    return [
      { id: 'in', label: 'Input', direction: 'input', dataType: 'image', slot: 0 },
      { id: 'out', label: 'Output', direction: 'output', dataType: 'image', slot: 1 },
    ]
  }
  if (nodeType === 'prompt') {
    return [{ id: 'out', label: 'Prompt', direction: 'output', dataType: 'text', slot: 0 }]
  }
  if (nodeType === 'file_upload') {
    return [{ id: 'out', label: 'File Path', direction: 'output', dataType: 'path', slot: 0 }]
  }
  if (nodeType === 'slider') {
    return [{ id: 'out', label: 'Value', direction: 'output', dataType: 'number', slot: 0 }]
  }
  if (nodeType === 'code') {
    const inputCount = Math.max(2, numberOr(meta.inputCount, meta.ports?.filter?.((port: any) => port.direction === 'input').length, 2))
    const outputCount = Math.max(1, numberOr(meta.outputCount, meta.ports?.filter?.((port: any) => port.direction === 'output').length, 1))
    const ports: NodePort[] = []
    for (let i = 0; i < inputCount; i += 1) ports.push({ id: `in-${i}`, label: `In ${i + 1}`, direction: 'input', dataType: 'any', slot: i })
    for (let i = 0; i < outputCount; i += 1) ports.push({ id: `out-${i}`, label: `Out ${i + 1}`, direction: 'output', dataType: 'any', slot: i })
    return ports
  }
  if (nodeType === 'note') return []
  return [
    { id: 'in', label: 'Input', direction: 'input', dataType: 'any', slot: 0 },
    { id: 'out', label: 'Output', direction: 'output', dataType: 'any', slot: 1 },
  ]
}

function getNodePort(node: CanvasNode, portId: string) {
  return (Array.isArray(node.meta.ports) ? node.meta.ports : getNodePorts(node.nodeType, node.meta)).find((port: NodePort) => port.id === portId) ?? null
}

function renderPorts(
  node: CanvasNode,
  pending: PendingConnection | null,
  onPortClick: (node: CanvasNode, port: NodePort) => void,
) {
  const ports = Array.isArray(node.meta.ports) ? node.meta.ports : getNodePorts(node.nodeType, node.meta)
  if (!ports.length) return null
  return (
    <div className="port-stack">
      {ports.map((port) => (
        <button
          key={port.id}
          type="button"
          className={`port port-${port.direction} ${pending?.nodeId === node.id && pending.portId === port.id ? 'active' : ''}`}
          data-port-id={port.id}
          onClick={() => onPortClick(node, port)}
        >
          {port.label}
        </button>
      ))}
    </div>
  )
}

function renderNodeBody(
  node: CanvasNode,
  updateMeta: (sourceRef: string, patch: Record<string, unknown>) => void,
  onPreviewImage: (src: string, title: string) => void,
) {
  if (node.nodeType === 'prompt') {
    return (
      <label className="node-field">
        <span>Prompt</span>
        <textarea value={node.meta.prompt ?? ''} onChange={(event) => updateMeta(node.sourceRef, { prompt: event.target.value })} />
      </label>
    )
  }
  if (node.nodeType === 'file_upload') {
    return (
      <label className="node-field">
        <span>File Path</span>
        <input value={node.meta.filePath ?? ''} onChange={(event) => updateMeta(node.sourceRef, { filePath: event.target.value })} />
      </label>
    )
  }
  if (node.nodeType === 'slider') {
    return (
      <label className="node-field">
        <span>Value: {Number(node.meta.value ?? 0).toFixed(2)}</span>
        <input
          type="range"
          min={Number(node.meta.min ?? 0)}
          max={Number(node.meta.max ?? 1)}
          step={Number(node.meta.step ?? 0.01)}
          value={Number(node.meta.value ?? 0)}
          onChange={(event) => updateMeta(node.sourceRef, { value: Number(event.target.value) })}
        />
      </label>
    )
  }
  if (node.nodeType === 'code') {
    return (
      <label className="node-field">
        <span>C# Body</span>
        <textarea value={node.meta.body ?? ''} onChange={(event) => updateMeta(node.sourceRef, { body: event.target.value })} />
      </label>
    )
  }
  if (node.nodeType === 'image') {
    const imageSrc = resolveCanvasImageSource(node.meta.imageDataUrl ?? node.meta.imagePath)
    return (
      <div className="node-image-field">
        {imageSrc ? (
          <button type="button" className="node-image-preview" onClick={() => onPreviewImage(imageSrc, String(node.meta.title ?? 'Image'))}>
            <img src={imageSrc} alt={String(node.meta.title ?? 'Image')} />
          </button>
        ) : (
          <div className="node-image-preview node-image-empty">No image</div>
        )}
        <label className="node-field">
          <span>Image Path</span>
          <input value={node.meta.imagePath ?? ''} onChange={(event) => updateMeta(node.sourceRef, { imagePath: event.target.value })} />
        </label>
      </div>
    )
  }
  return null
}

function renderImageNode(node: CanvasNode, onPreviewImage: (src: string, title: string) => void) {
  const imageSrc = resolveCanvasImageSource(node.meta.imageDataUrl ?? node.meta.imagePath)
  if (!imageSrc) {
    return <div className="node-image-empty node-image-plain-empty">No image</div>
  }

  return (
    <button type="button" className="node-image-plain" onClick={() => onPreviewImage(imageSrc, String(node.meta.title ?? 'Image'))}>
      <img src={imageSrc} alt={String(node.meta.title ?? 'Image')} />
    </button>
  )
}

function renderNodePreview(node: CanvasNode, onPreviewImage: (src: string, title: string) => void) {
  if (node.nodeType === 'slider') {
    return <div className="node-preview-chip">Value {Number(node.meta.value ?? 0).toFixed(2)}</div>
  }
  if (node.nodeType === 'file_upload' && node.meta.filePath) {
    return <div className="node-preview-chip">{String(node.meta.filePath)}</div>
  }
  if (node.nodeType === 'image' && node.meta.imagePath) {
    const imageSrc = resolveCanvasImageSource(node.meta.imageDataUrl ?? node.meta.imagePath)
    return imageSrc ? (
      <button type="button" className="node-image-chip" onClick={() => onPreviewImage(imageSrc, String(node.meta.title ?? 'Image'))}>
        <img src={imageSrc} alt={String(node.meta.title ?? 'Image')} />
      </button>
    ) : (
      <div className="node-preview-chip">{String(node.meta.imagePath)}</div>
    )
  }
  if (node.nodeType === 'prompt' && node.meta.prompt) {
    return <div className="node-preview-chip">{String(node.meta.prompt).slice(0, 40)}</div>
  }
  if (node.nodeType === 'code') {
    return <div className="node-preview-chip">{`${node.meta.inputCount ?? 2} in / ${node.meta.outputCount ?? 1} out`}</div>
  }
  return null
}

function getNodePreviewText(node: CanvasNode) {
  if (node.nodeType === 'prompt') return node.meta.prompt || node.meta.body || 'No prompt'
  if (node.nodeType === 'file_upload') return node.meta.filePath || 'No file path'
  if (node.nodeType === 'image') return node.meta.imagePath || 'No image path'
  if (node.nodeType === 'slider') return `Range ${node.meta.min ?? 0} - ${node.meta.max ?? 1}`
  return node.meta.body || 'No content'
}

function renderConnection(connection: CanvasConnection, nodes: CanvasNode[], viewport: Viewport) {
  const fromNode = nodes.find((node) => node.id === connection.fromNodeId)
  const toNode = nodes.find((node) => node.id === connection.toNodeId)
  if (!fromNode || !toNode) return null
  const from = getPortPosition(fromNode, connection.fromPortId, viewport)
  const to = getPortPosition(toNode, connection.toPortId, viewport)
  if (!from || !to) return null
  const midX = (from.x + to.x) / 2
  return (
    <path
      key={connection.id}
      d={`M ${from.x} ${from.y} C ${midX} ${from.y}, ${midX} ${to.y}, ${to.x} ${to.y}`}
      className="connection-path"
    />
  )
}

function getPortPosition(node: CanvasNode, portId: string, viewport: Viewport) {
  const ports = Array.isArray(node.meta.ports) ? node.meta.ports : getNodePorts(node.nodeType, node.meta)
  const port = ports.find((item) => item.id === portId)
  if (!port) return null
  const top = viewport.y + (node.y + 56 + port.slot * 22) * viewport.z
  const left = viewport.x + (node.x + (port.direction === 'input' ? 0 : node.w)) * viewport.z
  return { x: left, y: top }
}

function buildDefaultNodeMeta(nodeType: CanvasNodeType, sourceRef: string) {
  if (nodeType === 'image') {
    return {
      sourceRef,
      nodeType,
      title: 'Image',
      summary: 'Image input/output node',
      body: 'Drag in an image reference or paste a path/URL.',
      imagePath: '',
      collapsed: false,
      ports: getNodePorts(nodeType, {}),
    }
  }
  if (nodeType === 'prompt') {
    return {
      sourceRef,
      nodeType,
      title: 'Prompt',
      summary: 'Single output prompt node',
      body: 'Write the prompt here.',
      prompt: '',
      collapsed: false,
      ports: getNodePorts(nodeType, {}),
    }
  }
  if (nodeType === 'file_upload') {
    return {
      sourceRef,
      nodeType,
      title: 'File Upload',
      summary: 'Outputs a file path',
      body: 'Paste a file path here.',
      filePath: '',
      collapsed: false,
      ports: getNodePorts(nodeType, {}),
    }
  }
  if (nodeType === 'slider') {
    return {
      sourceRef,
      nodeType,
      title: 'Slider',
      summary: 'Single numeric output',
      body: 'Use the value as a number input.',
      value: 0.5,
      min: 0,
      max: 1,
      step: 0.01,
      collapsed: false,
      ports: getNodePorts(nodeType, {}),
    }
  }
  if (nodeType === 'code') {
    return {
      sourceRef,
      nodeType,
      title: 'C# Battery',
      summary: 'Multi-port Grasshopper C# style node',
      body: '// C# code here',
      inputCount: 2,
      outputCount: 1,
      collapsed: false,
      ports: getNodePorts(nodeType, { inputCount: 2, outputCount: 1 }),
    }
  }
  return {
    sourceRef,
    nodeType: 'note',
    title: 'New Note',
    summary: 'Manual canvas note',
    body: '',
    tags: [],
    collapsed: false,
    pinned: false,
    kind: 'note',
    role: 'note',
    userEditedTitle: true,
    ports: [],
  }
}

function resolveCanvasImageSource(value: any) {
  const raw = String(value ?? '').trim()
  if (!raw) return ''
  if (/^(data:|https?:|file:)/i.test(raw)) return raw
  if (/^[a-zA-Z]:[\\/]/.test(raw) || raw.startsWith('\\\\')) {
    const normalized = raw.replace(/\\/g, '/')
    if (normalized.startsWith('//')) {
      return encodeURI(`file:${normalized}`)
    }
    return encodeURI(`file:///${normalized}`)
  }
  return raw
}

function cloneNodes(nodes: CanvasNode[]) {
  return nodes.map((node) => ({
    ...node,
    meta: { ...node.meta, ports: Array.isArray(node.meta.ports) ? node.meta.ports.map((port: NodePort) => ({ ...port })) : [] },
  }))
}

function cloneConnections(connections: CanvasConnection[]) {
  return connections.map((connection) => ({ ...connection }))
}

function normalizeViewport(value: any): Viewport {
  return {
    x: numberOr(value?.x, undefined, 80),
    y: numberOr(value?.y, undefined, 90),
    z: clamp(numberOr(value?.z, undefined, 1), 0.25, 2.8),
  }
}

function dedupeNodes(nodes: CanvasNode[]) {
  const seen = new Set<string>()
  return nodes.filter((node) => {
    if (seen.has(node.sourceRef)) return false
    seen.add(node.sourceRef)
    return true
  })
}

function getNodesBounds(nodes: CanvasNode[]) {
  const minX = Math.min(...nodes.map((node) => node.x))
  const minY = Math.min(...nodes.map((node) => node.y))
  const maxX = Math.max(...nodes.map((node) => node.x + node.w))
  const maxY = Math.max(...nodes.map((node) => node.y + node.h))
  return {
    x: minX,
    y: minY,
    w: Math.max(1, maxX - minX),
    h: Math.max(1, maxY - minY),
  }
}

function screenToWorld(x: number, y: number, viewport: Viewport) {
  return {
    x: (x - viewport.x) / viewport.z,
    y: (y - viewport.y) / viewport.z,
  }
}

function gridStyle(viewport: Viewport, isDarkMode: boolean): React.CSSProperties {
  const spacing = 22 * viewport.z
  const dotColor = isDarkMode ? 'rgba(146,158,151,0.18)' : 'rgba(111,124,116,0.16)'
  return {
    backgroundColor: 'var(--app-bg)',
    backgroundImage: `radial-gradient(circle, ${dotColor} 1px, transparent 1.2px)`,
    backgroundSize: `${spacing}px ${spacing}px`,
    backgroundPosition: `${viewport.x}px ${viewport.y}px`,
  }
}

function nodeStyle(node: CanvasNode, viewport: Viewport): React.CSSProperties {
  return {
    width: node.w,
    minHeight: node.h,
    transform: `translate(${viewport.x + node.x * viewport.z}px, ${viewport.y + node.y * viewport.z}px) scale(${viewport.z})`,
  }
}

function nodeKind(meta: Record<string, any>) {
  if (meta.kind === 'note') return 'note'
  if (meta.kind === 'tool_result' || meta.role === 'tool') return 'tool'
  if (meta.role === 'assistant') return 'assistant'
  if (meta.role === 'user') return 'user'
  return 'default'
}

function computeMessagesSignature(messages: CanvasMessageItem[]) {
  return JSON.stringify(
    messages.map((item) => [
      item.sourceRef ?? '',
      item.role ?? '',
      item.kind ?? '',
      item.title ?? '',
      item.summary ?? '',
      item.body ?? '',
      Array.isArray(item.tags) ? item.tags.join('|') : '',
      Boolean(item.collapsed),
      Boolean(item.pinned),
    ]),
  )
}

function slugify(text: string) {
  return String(text)
    .replace(/[^a-zA-Z0-9:_-]+/g, '-')
    .replace(/-+/g, '-')
    .replace(/^-|-$/g, '')
    .toLowerCase()
}

function numberOr(primary: unknown, secondary: unknown, fallback: number) {
  if (typeof primary === 'number' && Number.isFinite(primary)) return primary
  if (typeof secondary === 'number' && Number.isFinite(secondary)) return secondary
  return fallback
}

function clamp(value: number, min: number, max: number) {
  return Math.min(max, Math.max(min, value))
}
