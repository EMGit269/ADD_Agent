import React, { Component, ReactNode, useEffect, useMemo, useRef, useState } from 'react'
import {
  Background,
  Controls,
  Handle,
  NodeResizer,
  Position,
  ReactFlow,
  getBezierPath,
  type Connection,
  type ConnectionLineComponentProps,
  type Edge,
  type Node,
  type NodeProps,
  type Viewport as FlowViewport,
} from '@xyflow/react'
import '@xyflow/react/dist/style.css'

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

type CanvasNodeType = 'message' | 'note' | 'image' | 'prompt' | 'file_upload' | 'slider' | 'code' | 'panel' | 'img' | 'gen_img' | 'c_sharp'

type PortDirection = 'input' | 'output'

type PortDataType = 'any' | 'image' | 'text' | 'path' | 'number' | 'code'

type NodePort = {
  id: string
  label: string
  direction: PortDirection
  dataType: PortDataType
  slot: number
  visible?: boolean
  required?: boolean
  dynamic?: boolean
}

type CanvasConnection = {
  id: string
  fromNodeId: string
  fromPortId: string
  toNodeId: string
  toPortId: string
}

type AiImageConfig = {
  success?: boolean
  providerId?: string
  provider?: string
  baseUrl?: string
  model?: string
  apiKey?: string
  proxyUrl?: string
  error?: string
}

type PendingAiImageRun = {
  sourceRef: string
  prompt: string
  intent: 'generate' | 'edit'
  aspectRatio?: string
  size?: string
  count?: number
  image: any
}

type CanvasHistoryItem = {
  canvasId: string
  title: string
  updatedAtUtc?: string
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

type FlowNodeData = Record<string, unknown> & {
  node: CanvasNode
  updateNodeMeta: (sourceRef: string, patch: Record<string, unknown>) => void
  onNodeResize: (sourceRef: string, width: number, height: number, shouldSave?: boolean) => void
  onImageLoaded: (sourceRef: string, naturalWidth: number, naturalHeight: number) => void
  onPreviewImage: (src: string, title: string) => void
  onRunAiImage: (sourceRef: string) => void
  onDownloadNodeImage: (sourceRef: string) => void
  onOpenNodeSettings: (sourceRef: string) => void
  keepImageAspectRatio: boolean
}

type LightweightSnapshot = {
  kind: 'addgh-lightweight-canvas-v1'
  viewport: Viewport
  nodes: CanvasNode[]
  connections?: CanvasConnection[]
}

type DragState =
  | { mode: 'pan'; pointerId: number; startX: number; startY: number; startViewport: Viewport; moved: boolean }
  | { mode: 'node'; pointerId: number; sourceRef: string; startX: number; startY: number; startNodeX: number; startNodeY: number; moved: boolean }
  | { mode: 'resize'; pointerId: number; sourceRef: string; startX: number; startY: number; startW: number; startH: number; aspect: number; moved: boolean }
  | { mode: 'connect'; pointerId: number; from: PendingConnection; currentX: number; currentY: number }
  | { mode: 'select'; pointerId: number; startX: number; startY: number; currentX: number; currentY: number }
  | null

type PendingConnection = {
  nodeId: string
  portId: string
}

type PortPositionMap = Record<string, { x: number; y: number }>

type CanvasSnapshotState = {
  nodes: CanvasNode[]
  connections: CanvasConnection[]
  viewport: Viewport
  selectedSourceRef: string | null
  selectedSourceRefs: string[]
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
  const [themeMode, setThemeMode] = useState<'light' | 'dark'>(() => {
    if (typeof window === 'undefined') return 'dark'
    const saved = window.localStorage.getItem('addgh-canvas-theme')
    return saved === 'light' ? 'light' : 'dark'
  })
  const [conversationId, setConversationId] = useState('draft')
  const [conversationTitle, setConversationTitle] = useState('Canvas')
  const [canvasId, setCanvasId] = useState('canvas-default')
  const [canvasTitle, setCanvasTitle] = useState('Canvas')
  const [canvasHistory, setCanvasHistory] = useState<CanvasHistoryItem[]>([])
  const [messages, setMessages] = useState<CanvasMessageItem[]>([])
  const [nodes, setNodes] = useState<CanvasNode[]>([])
  const [connections, setConnections] = useState<CanvasConnection[]>([])
  const [viewport, setViewport] = useState<Viewport>({ x: 80, y: 90, z: 1 })
  const [selectedSourceRef, setSelectedSourceRef] = useState<string | null>(null)
  const [selectedSourceRefs, setSelectedSourceRefs] = useState<string[]>([])
  const [pendingConnection, setPendingConnection] = useState<PendingConnection | null>(null)
  const [pendingConnectionPoint, setPendingConnectionPoint] = useState<{ x: number; y: number } | null>(null)
  const [detailSourceRef, setDetailSourceRef] = useState<string | null>(null)
  const [contextMenu, setContextMenu] = useState<ContextMenuState>(null)
  const [inspectorSnapshot, setInspectorSnapshot] = useState<InspectorSnapshot | null>(null)
  const [interactionMode, setInteractionMode] = useState('idle')
  const [imagePreviewSrc, setImagePreviewSrc] = useState<string | null>(null)
  const [imagePreviewTitle, setImagePreviewTitle] = useState<string>('Image Preview')
  const [captureStatus, setCaptureStatus] = useState('')
  const [marqueeRect, setMarqueeRect] = useState<{ left: number; top: number; width: number; height: number } | null>(null)
  const surfaceRef = useRef<HTMLDivElement | null>(null)
  const dragRef = useRef<DragState>(null)
  const saveTimerRef = useRef<number | null>(null)
  const nodesRef = useRef(nodes)
  const connectionsRef = useRef(connections)
  const viewportRef = useRef(viewport)
  const selectedSourceRefRef = useRef<string | null>(selectedSourceRef)
  const selectedSourceRefsRef = useRef<string[]>(selectedSourceRefs)
  const cardMetaRef = useRef<Record<string, any>>({})
  const historyRef = useRef<CanvasHistoryState>({ past: [], future: [] })
  const lastMessagesSignatureRef = useRef('')
  const currentConversationIdRef = useRef('draft')
  const currentConversationTitleRef = useRef('Canvas')
  const canvasIdRef = useRef('canvas-default')
  const suppressNextContextMenuRef = useRef(false)
  const shiftKeyRef = useRef(false)
  const aiImageConfigRef = useRef<AiImageConfig | null>(null)
  const pendingAiImageRunRef = useRef<PendingAiImageRun | null>(null)
  const isDarkMode = themeMode === 'dark'

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
    selectedSourceRefsRef.current = selectedSourceRefs
  }, [selectedSourceRefs])

  useEffect(() => {
    currentConversationIdRef.current = conversationId
    currentConversationTitleRef.current = conversationTitle
  }, [conversationId, conversationTitle])

  useEffect(() => {
    canvasIdRef.current = canvasId
  }, [canvasId])

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
    document.documentElement.dataset.theme = isDarkMode ? 'dark' : 'light'
    if (typeof window !== 'undefined') {
      window.localStorage.setItem('addgh-canvas-theme', themeMode)
    }
  }, [isDarkMode, themeMode])

  useEffect(() => {
    const handleKeyDown = (event: KeyboardEvent) => {
      shiftKeyRef.current = event.shiftKey
      const target = event.target as HTMLElement | null
      const isTyping = Boolean(target?.closest('input,textarea,[contenteditable="true"]'))

      if (isTyping) return

      if (event.key === 'Escape') {
        setContextMenu(null)
        setPendingConnection(null)
        return
      }

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

      if (event.key === 'Delete' && selectedSourceRefsRef.current.length) {
        event.preventDefault()
        deleteNodesBySourceRefs(selectedSourceRefsRef.current)
      }
    }
    const handleKeyUp = (event: KeyboardEvent) => {
      shiftKeyRef.current = event.shiftKey
    }

    window.addEventListener('keydown', handleKeyDown)
    window.addEventListener('keyup', handleKeyUp)
    const preventImageFileDrop = (event: DragEvent) => {
      if (!event.dataTransfer || !hasDraggedImage(event.dataTransfer)) return
      event.preventDefault()
    }
    window.addEventListener('dragover', preventImageFileDrop)
    window.addEventListener('drop', preventImageFileDrop)
    return () => {
      window.removeEventListener('keydown', handleKeyDown)
      window.removeEventListener('keyup', handleKeyUp)
      window.removeEventListener('dragover', preventImageFileDrop)
      window.removeEventListener('drop', preventImageFileDrop)
    }
  }, [])

  useEffect(() => {
    const applyEnvelope = (envelope: HostEnvelope) => {
      if (!envelope?.type) return

      if (envelope.type === 'bootstrap') {
        const payload = envelope.payload ?? {}
        const nextConversationId = payload.conversationId ?? 'draft'
        const nextConversationTitle = payload.conversationTitle ?? 'Canvas'
        const nextCanvasId = payload.canvasId ?? 'canvas-default'
        const nextCanvasTitle = payload.canvasTitle ?? 'Canvas'
        const nextMessages = Array.isArray(payload.messages) ? payload.messages : []
        const nextSignature = computeMessagesSignature(nextMessages)
        const savedSnapshot = parseSnapshot(payload.canvasSnapshot?.snapshot)
        const savedMeta = payload.canvasSnapshot?.cardMetaPatches ?? {}

        currentConversationIdRef.current = nextConversationId
        currentConversationTitleRef.current = nextConversationTitle
        setConversationId(nextConversationId)
        setConversationTitle(nextConversationTitle)
        setCanvasId(nextCanvasId)
        setCanvasTitle(nextCanvasTitle)
        setCanvasHistory(Array.isArray(payload.canvasHistory) ? payload.canvasHistory : [])
        setInspectorSnapshot(payload.inspectorSnapshot ?? null)
        cardMetaRef.current = { ...savedMeta }
        if (savedSnapshot?.viewport) setViewport(normalizeViewport(savedSnapshot.viewport))
        if (Array.isArray(savedSnapshot?.connections)) setConnections(savedSnapshot.connections.map(normalizeConnection))

        setNodes(reconcileTypedNodes(savedSnapshot?.nodes ?? []))

        if (nextSignature !== lastMessagesSignatureRef.current) {
          lastMessagesSignatureRef.current = nextSignature
          setMessages(nextMessages)
        }
      }

      if (envelope.type === 'conversation_delta') {
        const payload = envelope.payload ?? {}
        const nextConversationId = payload.conversationId ?? currentConversationIdRef.current
        const nextConversationTitle = payload.conversationTitle ?? currentConversationTitleRef.current
        if (payload.canvasId) setCanvasId(payload.canvasId)
        const nextMessages = Array.isArray(payload.messages) ? payload.messages : []
        const nextSignature = computeMessagesSignature(nextMessages)

        currentConversationIdRef.current = nextConversationId
        currentConversationTitleRef.current = nextConversationTitle
        setConversationId(nextConversationId)
        setConversationTitle(nextConversationTitle)
        if (nextSignature !== lastMessagesSignatureRef.current) {
          lastMessagesSignatureRef.current = nextSignature
          setMessages(nextMessages)
        }
      }

      if (envelope.type === 'inspector_update') {
        setInspectorSnapshot((envelope.payload ?? null) as InspectorSnapshot | null)
      }

      if (envelope.type === 'rhino_capture_result') {
        const payload = envelope.payload ?? {}
        if (payload.error) {
          setCaptureStatus(String(payload.error))
          return
        }
        if (payload.path) {
          createCapturedImageNode(
            String(payload.path),
            payload.dataUrl ? String(payload.dataUrl) : '',
            payload.title ? String(payload.title) : 'Rhino View',
          )
          setCaptureStatus('Rhino view captured')
        }
      }

      if (envelope.type === 'canvas_ai_image_result') {
        applyAiImageResult(envelope.payload ?? {})
      }

      if (envelope.type === 'canvas_ai_image_config') {
        aiImageConfigRef.current = envelope.payload ?? null
        const pending = pendingAiImageRunRef.current
        pendingAiImageRunRef.current = null
        if (pending) void runAiImageInBrowser(pending, aiImageConfigRef.current)
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
          summary: 'React Flow canvas.',
          body: 'This canvas uses React Flow for nodes, handles, edges, pan, and zoom while keeping ADDGH canvas documents unchanged.',
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
  const flowNodes = useMemo<Node<FlowNodeData>[]>(() =>
    nodes.map((node) => ({
      id: node.id,
      type: 'canvasNode',
      position: { x: node.x, y: node.y },
      selected: selectedSourceRefs.includes(node.sourceRef),
      data: {
        node,
        updateNodeMeta,
        onNodeResize,
        onImageLoaded,
        onPreviewImage: (src: string, title: string) => {
          setImagePreviewSrc(src)
          setImagePreviewTitle(title)
        },
        onRunAiImage,
        onDownloadNodeImage,
        onOpenNodeSettings,
        keepImageAspectRatio: shiftKeyRef.current,
      },
    })),
    [nodes, selectedSourceRefs],
  )
  const flowEdges = useMemo<Edge[]>(() =>
    connections.map((connection) => {
      const sourceNode = nodes.find((node) => node.id === connection.fromNodeId)
      const targetNode = nodes.find((node) => node.id === connection.toNodeId)
      const portType = sourceNode ? getPortDataType(sourceNode, connection.fromPortId, 'output') : 'any'
      const isRunningInput = Boolean(targetNode?.meta.isRunning)
      return {
        id: connection.id,
        source: connection.fromNodeId,
        sourceHandle: connection.fromPortId,
        target: connection.toNodeId,
        targetHandle: connection.toPortId,
        type: 'default',
        className: `connection-path${isRunningInput ? ' connection-path-running' : ''}`,
        style: { '--connection-color': portColor(portType) } as React.CSSProperties,
      }
    }),
    [connections, nodes],
  )

  function createSnapshotState(
    override: Partial<CanvasSnapshotState> = {},
  ): CanvasSnapshotState {
    return {
      nodes: cloneNodes(override.nodes ?? nodesRef.current),
      connections: cloneConnections(override.connections ?? connectionsRef.current),
      viewport: { ...(override.viewport ?? viewportRef.current) },
      selectedSourceRef: override.selectedSourceRef ?? selectedSourceRefRef.current,
      selectedSourceRefs: [...(override.selectedSourceRefs ?? selectedSourceRefsRef.current)],
    }
  }

  function applySnapshotState(snapshot: CanvasSnapshotState, shouldSave = true) {
    nodesRef.current = cloneNodes(snapshot.nodes)
    connectionsRef.current = cloneConnections(snapshot.connections)
    viewportRef.current = { ...snapshot.viewport }
    selectedSourceRefRef.current = snapshot.selectedSourceRef
    selectedSourceRefsRef.current = [...snapshot.selectedSourceRefs]
    setNodes(nodesRef.current)
    setConnections(connectionsRef.current)
    setViewport(viewportRef.current)
    setSelectedSourceRef(snapshot.selectedSourceRef)
    setSelectedSourceRefs([...snapshot.selectedSourceRefs])
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
      postHostMessage('document_snapshot_save', { canvasId: canvasIdRef.current, snapshot })
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
    postHostMessage('card_meta_patch', { canvasId: canvasIdRef.current, sourceRef, ...cardMetaRef.current[sourceRef] })
  }

  function applyNodeMetaPatch(sourceRef: string, patch: Record<string, unknown>) {
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
          meta: {
            ...nextMeta,
            w,
            h,
          },
        }
      }),
    )
    postHostMessage('card_meta_patch', { canvasId: canvasIdRef.current, sourceRef, ...cardMetaRef.current[sourceRef] })
  }

  function onNodeResize(sourceRef: string, width: number, height: number, shouldSave = true) {
    updateNodes((current) =>
      current.map((node) => {
        if (node.sourceRef !== sourceRef) return node
        const nextWidth = Math.max(120, width)
        const nextHeight = Math.max(80, height)
        const nextMeta = { ...node.meta, w: nextWidth, h: nextHeight, userResized: true }
        return {
          ...node,
          w: nextWidth,
          h: nextHeight,
          meta: nextMeta,
        }
      }),
      shouldSave,
    )
  }

  function onImageLoaded(sourceRef: string, naturalWidth: number, naturalHeight: number) {
    if (naturalWidth <= 0 || naturalHeight <= 0) return
    updateNodes((current) =>
      current.map((node) => {
        if (node.sourceRef !== sourceRef || node.nodeType !== 'image') return node
        if (node.meta.userResized || node.meta.naturalWidth === naturalWidth && node.meta.naturalHeight === naturalHeight) return node
        const size = fitImageInitialSize(naturalWidth, naturalHeight)
        const nextMeta = {
          ...node.meta,
          naturalWidth,
          naturalHeight,
          w: size.w,
          h: size.h,
        }
        cardMetaRef.current[sourceRef] = { ...(cardMetaRef.current[sourceRef] ?? {}), ...nextMeta }
        return {
          ...node,
          w: size.w,
          h: size.h,
          meta: nextMeta,
        }
      }),
    )
  }

  function onNodesChange(changes: Array<{ id?: string; type: string; position?: { x: number; y: number }; selected?: boolean }>) {
    setNodes((current) => {
      let next = current
      let changedPosition = false
      let changedSelection = false
      const selectedRefs = new Set(selectedSourceRefsRef.current)
      for (const change of changes) {
        if (change.type === 'position' && change.id && change.position) {
          changedPosition = true
          next = next.map((node) =>
            node.id === change.id
              ? {
                  ...node,
                  x: change.position!.x,
                  y: change.position!.y,
                  meta: {
                    ...node.meta,
                    x: change.position!.x,
                    y: change.position!.y,
                  },
                }
              : node,
          )
        }
        if (change.type === 'select' && change.id) {
          const node = next.find((item) => item.id === change.id)
          if (node) {
            changedSelection = true
            if (change.selected) {
              selectedRefs.add(node.sourceRef)
            } else {
              selectedRefs.delete(node.sourceRef)
            }
          }
        }
      }
      if (changedSelection) {
        const orderedSelection = next.filter((node) => selectedRefs.has(node.sourceRef)).map((node) => node.sourceRef)
        selectedSourceRefsRef.current = orderedSelection
        selectedSourceRefRef.current = orderedSelection[0] ?? null
        setSelectedSourceRefs(orderedSelection)
        setSelectedSourceRef(orderedSelection[0] ?? null)
      }
      nodesRef.current = next
      if (changedPosition) scheduleSave()
      return next
    })
  }

  function deleteNodeBySourceRef(sourceRef: string) {
    deleteNodesBySourceRefs([sourceRef])
  }

  function deleteNodesBySourceRefs(sourceRefs: string[]) {
    const set = new Set((sourceRefs ?? []).filter(Boolean))
    if (!set.size) return
    commitMutation((snapshot) => {
      const remainingNodes = snapshot.nodes.filter((node) => !set.has(node.sourceRef))
      const remainingNodeIds = new Set(remainingNodes.map((node) => node.id))
      const remainingConnections = snapshot.connections.filter(
        (connection) => remainingNodeIds.has(connection.fromNodeId) && remainingNodeIds.has(connection.toNodeId),
      )
      return {
        ...snapshot,
        nodes: remainingNodes,
        connections: remainingConnections,
        selectedSourceRef: snapshot.selectedSourceRef && set.has(snapshot.selectedSourceRef) ? null : snapshot.selectedSourceRef,
        selectedSourceRefs: snapshot.selectedSourceRefs.filter((item) => !set.has(item)),
      }
    })
    setDetailSourceRef((current) => (current && set.has(current) ? null : current))
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
      selectedSourceRefs: [sourceRef],
    }))
    setContextMenu(null)
  }

  function createImageNodeAt(x: number, y: number, image: { title?: string; path?: string; dataUrl: string; mimeType?: string }) {
    const sourceRef = `image:drop:${Date.now()}`
    const meta = {
      ...buildDefaultNodeMeta('image', sourceRef),
      title: image.title || 'Image',
      summary: 'Dropped image',
      body: image.path || image.title || '',
      imagePath: image.path || '',
      imageDataUrl: image.dataUrl,
      mimeType: image.mimeType || getMimeTypeFromDataUrl(image.dataUrl) || 'image/png',
      userEditedTitle: true,
    }
    const node = createNode(meta, nodesRef.current.length, x, y, 'image')
    cardMetaRef.current[sourceRef] = meta
    commitMutation((snapshot) => ({
      ...snapshot,
      nodes: [...snapshot.nodes, node],
      selectedSourceRef: sourceRef,
      selectedSourceRefs: [sourceRef],
    }))
    setContextMenu(null)
  }

  function onSurfacePointerDown(event: React.PointerEvent<HTMLDivElement>) {
    const target = event.target as HTMLElement
    if (target.closest('.canvas-node')) return
    if (event.button !== 0 && event.button !== 2) return
    event.preventDefault()
    if ((event.target as HTMLElement).closest('.canvas-node')) return
    setContextMenu(null)
    if (pendingConnection) setPendingConnection(null)
    if (event.button === 2) {
      dragRef.current = {
        mode: 'pan',
        pointerId: event.pointerId,
        startX: event.clientX,
        startY: event.clientY,
        startViewport: viewportRef.current,
        moved: false,
      }
      setInteractionMode('pan')
    } else {
      dragRef.current = {
        mode: 'select',
        pointerId: event.pointerId,
        startX: event.clientX,
        startY: event.clientY,
        currentX: event.clientX,
        currentY: event.clientY,
      }
      setMarqueeRect({ left: event.clientX, top: event.clientY, width: 0, height: 0 })
      setSelectedSourceRef(null)
      setSelectedSourceRefs([])
      setInteractionMode('select')
    }
    event.currentTarget.setPointerCapture(event.pointerId)
  }

  function onNodePointerDown(event: React.PointerEvent<HTMLElement>, node: CanvasNode) {
    if (event.button !== 0) return
    const target = event.target as HTMLElement
    if (target.closest('input,textarea,select,[data-port-id],[data-resize-handle]')) return
    setContextMenu(null)
    event.stopPropagation()
    setSelectedSourceRef(node.sourceRef)
    setSelectedSourceRefs([node.sourceRef])
    dragRef.current = {
      mode: 'node',
      pointerId: event.pointerId,
      sourceRef: node.sourceRef,
      startX: event.clientX,
      startY: event.clientY,
      startNodeX: node.x,
      startNodeY: node.y,
      moved: false,
    }
    setInteractionMode('drag')
    surfaceRef.current?.setPointerCapture(event.pointerId)
  }

  function onImageResizePointerDown(event: React.PointerEvent<HTMLButtonElement>, node: CanvasNode) {
    if (event.button !== 0) return
    event.preventDefault()
    event.stopPropagation()
    setSelectedSourceRef(node.sourceRef)
    setSelectedSourceRefs([node.sourceRef])
    dragRef.current = {
      mode: 'resize',
      pointerId: event.pointerId,
      sourceRef: node.sourceRef,
      startX: event.clientX,
      startY: event.clientY,
      startW: node.w,
      startH: node.h,
      aspect: node.w / Math.max(1, node.h),
      moved: false,
    }
    setInteractionMode('resize')
    surfaceRef.current?.setPointerCapture(event.pointerId)
  }

  function handlePortClick(node: CanvasNode, port: NodePort, event?: React.MouseEvent<HTMLButtonElement>) {
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
    const to = { nodeId: node.id, portId: port.id }
    if (event?.shiftKey) {
      deleteConnectionBetween(pendingConnection, to)
    } else {
      connectNodes(pendingConnection, to)
    }
    setInteractionMode('idle')
  }

  function handlePortPointerDown(event: React.PointerEvent<HTMLButtonElement>, node: CanvasNode, port: NodePort) {
    event.preventDefault()
    event.stopPropagation()
    setContextMenu(null)
    setSelectedSourceRef(node.sourceRef)

    if (port.direction === 'input') {
      if (pendingConnection) {
        const to = { nodeId: node.id, portId: port.id }
        if (event.shiftKey) {
          deleteConnectionBetween(pendingConnection, to)
        } else {
          connectNodes(pendingConnection, to)
        }
        setInteractionMode('idle')
      }
      return
    }

    const rect = surfaceRef.current?.getBoundingClientRect()
    const point = {
      x: event.clientX - (rect?.left ?? 0),
      y: event.clientY - (rect?.top ?? 0),
    }
    const from = { nodeId: node.id, portId: port.id }
    setPendingConnection(from)
    setPendingConnectionPoint(point)
    dragRef.current = {
      mode: 'connect',
      pointerId: event.pointerId,
      from,
      currentX: point.x,
      currentY: point.y,
    }
    setInteractionMode('connect')
    try {
      surfaceRef.current?.setPointerCapture(event.pointerId)
    } catch {
      // Pointer capture can fail if the browser already released it.
    }
  }

  function connectNodes(from: PendingConnection, to: PendingConnection) {
    if (from.nodeId === to.nodeId && from.portId === to.portId) return
    const sourceNode = nodesRef.current.find((node) => node.id === from.nodeId)
    const targetNode = nodesRef.current.find((node) => node.id === to.nodeId)
    if (!sourceNode || !targetNode) return
    const sourcePort = getNodePort(sourceNode, from.portId)
    const targetPort = getNodePort(targetNode, to.portId)
    if (!sourcePort || !targetPort || sourcePort.direction !== 'output' || targetPort.direction !== 'input') return
    if (!arePortTypesCompatible(sourcePort, targetPort)) return
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

  function onFlowConnect(connection: Connection) {
    if (!connection.source || !connection.target || !connection.sourceHandle || !connection.targetHandle) return
    const from = { nodeId: connection.source, portId: connection.sourceHandle }
    const to = { nodeId: connection.target, portId: connection.targetHandle }
    if (shiftKeyRef.current) {
      deleteConnectionBetween(from, to)
      return
    }
    connectNodes(from, to)
  }

  function onEdgeClick(event: React.MouseEvent, edge: Edge) {
    if (!event.shiftKey) return
    event.stopPropagation()
    updateConnections((current) => current.filter((connection) => connection.id !== edge.id))
  }

  function onViewportChange(next: FlowViewport) {
    updateViewport({ x: next.x, y: next.y, z: next.zoom }, false)
  }

  function deleteConnectionBetween(from: PendingConnection, to: PendingConnection) {
    updateConnections((current) => {
      const hasExactMatch = current.some((connection) =>
        connection.fromNodeId === from.nodeId
        && connection.fromPortId === from.portId
        && connection.toNodeId === to.nodeId
        && connection.toPortId === to.portId,
      )
      return current.filter((connection) => {
        if (hasExactMatch) {
          return !(connection.fromNodeId === from.nodeId
            && connection.fromPortId === from.portId
            && connection.toNodeId === to.nodeId
            && connection.toPortId === to.portId)
        }
        return !(connection.fromNodeId === from.nodeId && connection.toNodeId === to.nodeId)
      })
    })
    setPendingConnection(null)
  }

  function onRunAiImage(sourceRef: string) {
    const node = nodesRef.current.find((item) => item.sourceRef === sourceRef)
    if (!node || node.nodeType !== 'gen_img') return
    const prompt = String(getNodeInputValue(node, 'txt', nodesRef.current, connectionsRef.current) ?? node.meta.prompt ?? '').trim()
    const imageInput = getNodeInputImage(node, 'img_in', nodesRef.current, connectionsRef.current)
    if (!prompt) {
      applyNodeMetaPatch(sourceRef, { runStatus: 'Prompt required', isRunning: false })
      return
    }
    applyNodeMetaPatch(sourceRef, { runStatus: 'Generating...', isRunning: true, error: '' })
    const run: PendingAiImageRun = {
      sourceRef,
      prompt,
      intent: imageInput ? 'edit' : 'generate',
      aspectRatio: node.meta.aspectRatio ?? '',
      size: normalizeAiImageSize(node.meta.imageSize ?? node.meta.size ?? '1024x1024'),
      count: clamp(Math.round(Number(node.meta.imageCount ?? node.meta.count ?? 1)), 1, 4),
      image: imageInput,
    }
    if (aiImageConfigRef.current?.apiKey) {
      void runAiImageInBrowser(run, aiImageConfigRef.current)
    } else {
      pendingAiImageRunRef.current = run
      postHostMessage('canvas_ai_image_config_request', { canvasId: canvasIdRef.current })
    }
  }

  async function runAiImageInBrowser(run: PendingAiImageRun, config: AiImageConfig | null) {
    try {
      if (!config?.apiKey) throw new Error(config?.error || 'Image API key is empty.')
      const result = await createCanvasAiImage(run, config)
      applyAiImageResult({
        sourceRef: run.sourceRef,
        success: true,
        provider: config.provider,
        model: config.model,
        prompt: run.prompt,
        size: run.size,
        images: result.images,
        generatedAtUtc: new Date().toISOString(),
      })
    } catch (error) {
      applyAiImageResult({
        sourceRef: run.sourceRef,
        success: false,
        error: error instanceof Error ? error.message : String(error),
      })
    }
  }

  function applyAiImageResult(payload: any) {
    const sourceRef = String(payload?.sourceRef ?? '')
    if (!sourceRef) return
    if (payload?.error || payload?.success === false) {
      applyNodeMetaPatch(sourceRef, {
        isRunning: false,
        runStatus: 'Failed',
        error: String(payload?.error ?? 'Image generation failed'),
      })
      return
    }

    const image = Array.isArray(payload?.images) ? payload.images[0] : null
    if (!image) {
      applyNodeMetaPatch(sourceRef, { isRunning: false, runStatus: 'No image returned' })
      return
    }
    const requestedSize = parseAiImageSize(payload?.size ?? image.size ?? '')
    const naturalSize = getImageSizeFromPayload(image) ?? requestedSize
    const fittedSize = naturalSize ? fitImageInitialSize(naturalSize.w, naturalSize.h) : null

    applyNodeMetaPatch(sourceRef, {
      isRunning: false,
      runStatus: 'Generated',
      imagePath: image.path ?? '',
      imageDataUrl: normalizeImageSourceValue(image.dataUrl ?? image.imageDataUrl ?? image.b64_json ?? image.base64 ?? image.url ?? image.path ?? ''),
      mimeType: image.mimeType ?? 'image/png',
      images: Array.isArray(payload?.images) ? payload.images : [image],
      naturalWidth: naturalSize?.w,
      naturalHeight: naturalSize?.h,
      w: fittedSize?.w,
      h: fittedSize?.h,
      provider: payload.provider ?? image.provider ?? '',
      model: payload.model ?? image.model ?? '',
      generatedAtUtc: payload.generatedAtUtc ?? new Date().toISOString(),
      error: '',
    })
  }

  function onPointerMove(event: React.PointerEvent<HTMLDivElement>) {
    const drag = dragRef.current
    if (!drag || drag.pointerId !== event.pointerId) return

    if (drag.mode === 'pan') {
      const moved = Math.abs(event.clientX - drag.startX) > 3 || Math.abs(event.clientY - drag.startY) > 3
      drag.moved = drag.moved || moved
      if (drag.moved) suppressNextContextMenuRef.current = true
      updateViewport({
        ...drag.startViewport,
        x: drag.startViewport.x + event.clientX - drag.startX,
        y: drag.startViewport.y + event.clientY - drag.startY,
      })
      return
    }

    if (drag.mode === 'select') {
      const left = Math.min(drag.startX, event.clientX)
      const top = Math.min(drag.startY, event.clientY)
      const width = Math.abs(event.clientX - drag.startX)
      const height = Math.abs(event.clientY - drag.startY)
      drag.currentX = event.clientX
      drag.currentY = event.clientY
      setMarqueeRect({ left, top, width, height })
      return
    }

    if (drag.mode === 'connect') {
      const rect = event.currentTarget.getBoundingClientRect()
      const point = {
        x: event.clientX - rect.left,
        y: event.clientY - rect.top,
      }
      drag.currentX = point.x
      drag.currentY = point.y
      setPendingConnectionPoint(point)
      return
    }

    if (drag.mode === 'resize') {
      const zoom = viewportRef.current.z
      const dx = (event.clientX - drag.startX) / zoom
      const dy = (event.clientY - drag.startY) / zoom
      drag.moved = drag.moved || Math.abs(dx) > 1 || Math.abs(dy) > 1
      const rawW = drag.startW + dx
      const rawH = drag.startH + dy
      const nextW = clamp(rawW, 120, 1200)
      const nextH = event.shiftKey
        ? clamp(nextW / drag.aspect, 90, 900)
        : clamp(rawH, 90, 900)
      updateNodes((current) =>
        current.map((node) =>
          node.sourceRef === drag.sourceRef
            ? {
                ...node,
                w: nextW,
                h: nextH,
                meta: {
                  ...node.meta,
                  w: nextW,
                  h: nextH,
                },
              }
            : node,
        ),
      )
      return
    }

    const zoom = viewportRef.current.z
    const dx = (event.clientX - drag.startX) / zoom
    const dy = (event.clientY - drag.startY) / zoom
    drag.moved = drag.moved || Math.abs(dx) > 1 || Math.abs(dy) > 1
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
    const drag = dragRef.current
    dragRef.current = null
    setInteractionMode('idle')
    try {
      event.currentTarget.releasePointerCapture(event.pointerId)
    } catch {
      // The pointer may have already been released by the browser.
    }
    if (drag?.mode === 'select') {
      const selected = nodesRef.current
        .filter((node) => intersectsSelection(node, viewportRef.current, drag.startX, drag.startY, drag.currentX, drag.currentY))
        .map((node) => node.sourceRef)
      setSelectedSourceRefs(selected)
      setSelectedSourceRef(selected[0] ?? null)
      selectedSourceRefsRef.current = [...selected]
      selectedSourceRefRef.current = selected[0] ?? null
      setMarqueeRect(null)
      return
    }
    if (drag?.mode === 'connect') {
      const target = findPortAtPoint(event.clientX, event.clientY, nodesRef.current)
      if (target?.port.direction === 'input') {
        const to = { nodeId: target.node.id, portId: target.port.id }
        if (event.shiftKey) {
          deleteConnectionBetween(drag.from, to)
        } else {
          connectNodes(drag.from, to)
        }
      } else {
        setPendingConnection(null)
      }
      setPendingConnectionPoint(null)
      setInteractionMode('idle')
      return
    }
    setMarqueeRect(null)
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

  function onNewCanvas() {
    postHostMessage('canvas_new', { title: `Canvas ${new Date().toLocaleTimeString()}` })
  }

  function onOpenCanvas(nextCanvasId: string) {
    if (!nextCanvasId || nextCanvasId === canvasId) return
    postHostMessage('canvas_open', { canvasId: nextCanvasId })
  }

  function onOpenNodeSettings(sourceRef: string) {
    setDetailSourceRef(sourceRef)
  }

  function onDownloadNodeImage(sourceRef: string) {
    const node = nodesRef.current.find((item) => item.sourceRef === sourceRef)
    const imageSrc = resolveCanvasImageSource(node?.meta.imageDataUrl ?? node?.meta.imagePath)
    if (!imageSrc) return
    const link = document.createElement('a')
    link.href = imageSrc
    link.download = `${slugify(String(node?.meta.title ?? 'image')) || 'image'}.${mimeTypeToExtension(String(node?.meta.mimeType ?? 'image/png'))}`
    document.body.appendChild(link)
    link.click()
    link.remove()
  }

  function onCaptureRhinoView() {
    setCaptureStatus('Capturing Rhino view...')
    postHostMessage('capture_rhino_view', { framing: 'current_view', width: 1600, height: 900 })
  }

  function createCapturedImageNode(path: string, dataUrl: string, title: string) {
    const center = screenToWorld(
      surfaceRef.current?.clientWidth ? surfaceRef.current.clientWidth / 2 : 360,
      surfaceRef.current?.clientHeight ? surfaceRef.current.clientHeight / 2 : 240,
      viewportRef.current,
    )
    const sourceRef = `image:rhino-capture:${Date.now()}`
    const meta = {
      ...buildDefaultNodeMeta('image', sourceRef),
      title,
      summary: 'Rhino viewport capture',
      body: path,
      imagePath: path,
      imageDataUrl: dataUrl || path,
      userEditedTitle: true,
    }
    const node = createNode(meta, nodesRef.current.length, center.x - 180, center.y - 130, 'image')
    cardMetaRef.current[sourceRef] = meta
    commitMutation((snapshot) => ({
      ...snapshot,
      nodes: [...snapshot.nodes, node],
      selectedSourceRef: sourceRef,
      selectedSourceRefs: [sourceRef],
    }))
  }

  function onCanvasContextMenu(event: MouseEvent | React.MouseEvent<Element, MouseEvent>) {
    event.preventDefault()
    const target = event.target as HTMLElement
    const nodeHost = target.closest('.canvas-node') as HTMLElement | null
    if (nodeHost?.dataset.sourceRef) {
      setSelectedSourceRef(nodeHost.dataset.sourceRef)
      setSelectedSourceRefs([nodeHost.dataset.sourceRef])
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

    const rect = surfaceRef.current?.getBoundingClientRect() ?? new DOMRect()
    const point = screenToWorld(event.clientX - rect.left, event.clientY - rect.top, viewportRef.current)
    setContextMenu({
      x: event.clientX,
      y: event.clientY,
      worldX: point.x,
      worldY: point.y,
      mode: 'canvas',
    })
  }

  function onFlowNodeContextMenu(event: React.MouseEvent, flowNode: Node) {
    event.preventDefault()
    const node = nodesRef.current.find((item) => item.id === flowNode.id)
    if (!node) return
    setSelectedSourceRef(node.sourceRef)
    setSelectedSourceRefs([node.sourceRef])
    setContextMenu({
      x: event.clientX,
      y: event.clientY,
      worldX: node.x,
      worldY: node.y,
      mode: 'node',
      sourceRef: node.sourceRef,
    })
  }

  function closeContextMenu() {
    setContextMenu(null)
  }

  function runContextMenuAction(action: () => void) {
    action()
    setContextMenu(null)
  }

  function onCanvasDragOver(event: React.DragEvent<HTMLDivElement>) {
    if (!hasDraggedImage(event.dataTransfer)) return
    event.preventDefault()
    event.dataTransfer.dropEffect = 'copy'
  }

  async function onCanvasDrop(event: React.DragEvent<HTMLDivElement>) {
    if (!hasDraggedImage(event.dataTransfer)) return
    event.preventDefault()
    event.stopPropagation()
    const file = [...event.dataTransfer.files].find((item) => item.type.startsWith('image/'))
    if (!file) return
    try {
      const dataUrl = await readFileAsDataUrl(file)
      const rect = surfaceRef.current?.getBoundingClientRect() ?? new DOMRect()
      const point = screenToWorld(event.clientX - rect.left, event.clientY - rect.top, viewportRef.current)
      createImageNodeAt(point.x - 180, point.y - 130, {
        title: file.name || 'Image',
        path: file.name || '',
        dataUrl,
        mimeType: file.type || getMimeTypeFromDataUrl(dataUrl) || 'image/png',
      })
    } catch (error) {
      postHostMessage('canvas_error', {
        kind: 'drop_image',
        message: error instanceof Error ? error.message : String(error),
      })
    }
  }

  return (
    <div className={`app-shell ${isDarkMode ? 'theme-dark' : 'theme-light'}`}>
      <div ref={surfaceRef} className="light-canvas" onDragOver={onCanvasDragOver} onDrop={onCanvasDrop}>
        <ReactFlow
          className="flow-canvas"
          nodes={flowNodes}
          edges={flowEdges}
          nodeTypes={canvasNodeTypes}
          connectionLineComponent={ColoredConnectionLine}
          onNodesChange={onNodesChange}
          onConnect={onFlowConnect}
          onEdgeClick={onEdgeClick}
          onNodeContextMenu={onFlowNodeContextMenu}
          onPaneContextMenu={onCanvasContextMenu}
          onPaneClick={closeContextMenu}
          onMoveEnd={(_, viewportState) => updateViewport({ x: viewportState.x, y: viewportState.y, z: viewportState.zoom })}
          onViewportChange={onViewportChange}
          viewport={{ x: viewport.x, y: viewport.y, zoom: viewport.z }}
          defaultViewport={{ x: viewport.x, y: viewport.y, zoom: viewport.z }}
          minZoom={0.25}
          maxZoom={2.8}
          fitView={false}
          panOnDrag
          selectionOnDrag
          nodeOrigin={[0, 0]}
          proOptions={{ hideAttribution: true }}
        >
          <Background gap={22} size={1.35} color={isDarkMode ? 'rgba(176,190,181,0.34)' : 'rgba(82,96,88,0.28)'} />
          <Controls showZoom showFitView={false} showInteractive={false} />
        </ReactFlow>
      </div>

      <div className="floating-toolbar">
        <select className="canvas-history-select" value={canvasId} onChange={(event) => onOpenCanvas(event.target.value)}>
          {canvasHistory.length ? canvasHistory.map((item) => (
            <option key={item.canvasId} value={item.canvasId}>{item.title || item.canvasId}</option>
          )) : <option value={canvasId}>{canvasTitle}</option>}
        </select>
        <button onClick={onNewCanvas}>New Canvas</button>
        <button onClick={() => setThemeMode((current) => current === 'dark' ? 'light' : 'dark')}>
          {isDarkMode ? 'Light' : 'Dark'}
        </button>
        <button onClick={onFit}>Fit</button>
        <button onClick={onCaptureRhinoView}>Capture Rhino</button>
        <button onClick={onNewNote}>Note</button>
        <button onClick={onCollapse} disabled={!selectedNode}>Collapse</button>
        <button onClick={onSync}>Sync</button>
      </div>

      <div className="status-pill">
        <span>{canvasTitle}</span>
        <span>{conversationTitle}</span>
        <span>{nodes.length} nodes</span>
        <span>{viewport.z.toFixed(2)}x</span>
        {captureStatus ? <span>{captureStatus}</span> : null}
      </div>

      {detailMeta?.sourceRef ? (
        <div className="detail-panel-anchor">
          <aside className="detail-modal">
            {detailNode ? renderNodeSettings(detailNode, updateNodeMeta, (src, title) => {
              setImagePreviewSrc(src)
              setImagePreviewTitle(title)
            }, () => setDetailSourceRef(null)) : null}
          </aside>
        </div>
      ) : null}

      {contextMenu ? (
        <div className="context-menu" style={{ left: contextMenu.x, top: contextMenu.y }} onClick={(event) => event.stopPropagation()}>
          {contextMenu.mode === 'canvas' ? (
            <>
              <button onClick={() => runContextMenuAction(() => createTypedNodeAt('prompt', contextMenu.worldX, contextMenu.worldY))}>Prompt Node</button>
              <button onClick={() => runContextMenuAction(() => createTypedNodeAt('file_upload', contextMenu.worldX, contextMenu.worldY))}>File Node</button>
              <button onClick={() => runContextMenuAction(() => createTypedNodeAt('code', contextMenu.worldX, contextMenu.worldY))}>Code Node</button>
              <button onClick={() => runContextMenuAction(() => createTypedNodeAt('panel', contextMenu.worldX, contextMenu.worldY))}>Panel</button>
              <button onClick={() => runContextMenuAction(() => createTypedNodeAt('image', contextMenu.worldX, contextMenu.worldY))}>Image</button>
              <button onClick={() => runContextMenuAction(() => createTypedNodeAt('gen_img', contextMenu.worldX, contextMenu.worldY))}>AI Image</button>
              <button onClick={() => runContextMenuAction(() => createTypedNodeAt('slider', contextMenu.worldX, contextMenu.worldY))}>Slider Node</button>
              <button onClick={() => runContextMenuAction(() => createTypedNodeAt('c_sharp', contextMenu.worldX, contextMenu.worldY))}>C# Node</button>
              <button onClick={() => runContextMenuAction(() => createTypedNodeAt('note', contextMenu.worldX, contextMenu.worldY))}>New Note</button>
            </>
          ) : (
            <>
              <button onClick={() => runContextMenuAction(() => contextMenu.sourceRef ? deleteNodeBySourceRef(contextMenu.sourceRef) : undefined)}>Delete</button>
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

const canvasNodeTypes = {
  canvasNode: CanvasFlowNode,
}

function ColoredConnectionLine({
  fromNode,
  fromHandle,
  fromX,
  fromY,
  toX,
  toY,
  fromPosition,
  toPosition,
}: ConnectionLineComponentProps<Node<FlowNodeData>>) {
  const node = fromNode?.internals.userNode?.data?.node
  const dataType = node && fromHandle?.id ? getPortDataType(node, fromHandle.id, 'output') : 'any'
  const [path] = getBezierPath({ sourceX: fromX, sourceY: fromY, sourcePosition: fromPosition, targetX: toX, targetY: toY, targetPosition: toPosition })
  return (
    <path
      d={path}
      className="connection-path connection-path-pending"
      style={{ '--connection-color': portColor(dataType) } as React.CSSProperties}
    />
  )
}

function CanvasFlowNode({ data, selected }: NodeProps<Node<FlowNodeData>>) {
  const node = data.node
  const isImage = node.nodeType === 'image'
  const isAiImage = node.nodeType === 'gen_img'
  const imageSrc = isImage || node.nodeType === 'gen_img' ? resolveCanvasImageSource(node.meta.imageDataUrl ?? node.meta.imagePath) : ''

  return (
    <article
      className={`canvas-node flow-node node-${nodeKind(node.meta)} type-${node.nodeType} ${selected ? 'selected' : ''} ${isImage || isAiImage ? 'is-image-node' : ''}`}
      style={{ width: node.w, height: node.h }}
      data-source-ref={node.sourceRef}
    >
      <NodeResizer
        isVisible={Boolean(selected)}
        minWidth={120}
        minHeight={80}
        maxWidth={1200}
        maxHeight={900}
        keepAspectRatio={false}
        handleClassName="node-edge-resizer-corner"
        lineClassName="node-edge-resizer-line"
        onResize={(_, params) => data.onNodeResize(node.sourceRef, params.width, params.height, false)}
        onResizeEnd={(_, params) => data.onNodeResize(node.sourceRef, params.width, params.height, true)}
      />
      {isImage || isAiImage ? (
        <>
          <header className="node-header">
            <strong className="node-title-text">{node.meta.title ?? (isAiImage ? 'AI Image' : 'Image')}</strong>
            <small>{node.nodeType}</small>
          </header>
          <div className={`node-image-plain ${isAiImage ? 'ai-image-plain' : ''}`}>
            {imageSrc ? (
              <img
                src={imageSrc}
                alt={String(node.meta.title ?? (isAiImage ? 'AI Image' : 'Image'))}
                loading="lazy"
                decoding="async"
                draggable={false}
                onLoad={(event) => {
                  if (!isAiImage) data.onImageLoaded(node.sourceRef, event.currentTarget.naturalWidth, event.currentTarget.naturalHeight)
                }}
                onDoubleClick={(event) => {
                  event.stopPropagation()
                  data.onPreviewImage(imageSrc, String(node.meta.title ?? (isAiImage ? 'AI Image' : 'Image')))
                }}
              />
            ) : (
              <div className="node-image-empty node-image-plain-empty">{isAiImage ? null : 'No image'}</div>
            )}
          </div>
          {renderFlowPorts(node)}
          {selected ? (
            <NodeActionBar
              node={node}
              onSettings={data.onOpenNodeSettings}
              onDownload={data.onDownloadNodeImage}
              onGenerate={data.onRunAiImage}
            />
          ) : null}
        </>
      ) : (
        <>
          <header className="node-header">
            <strong className="node-title-text">{node.meta.title ?? 'Untitled'}</strong>
            <small>{node.nodeType}</small>
          </header>
          <div className="node-panel-surface">
          {renderFlowPorts(node)}
          {!node.meta.collapsed ? renderInlineNodeEditor(node, data.updateNodeMeta, data.onRunAiImage, data.onPreviewImage) : null}
          {node.nodeType === 'gen_img' && imageSrc ? (
            <button type="button" className="node-generated-image nodrag" onClick={() => data.onPreviewImage(imageSrc, String(node.meta.title ?? 'AI Image'))}>
              <img src={imageSrc} alt={String(node.meta.title ?? 'AI Image')} loading="lazy" decoding="async" draggable={false} />
            </button>
          ) : null}
          {Array.isArray(node.meta.tags) && node.meta.tags.length ? (
            <div className="node-tags">
              {node.meta.tags.map((tag: string) => (
                <span key={tag}>#{tag}</span>
              ))}
            </div>
          ) : null}
          </div>
          {selected ? (
            <NodeActionBar
              node={node}
              onSettings={data.onOpenNodeSettings}
              onDownload={data.onDownloadNodeImage}
              onGenerate={data.onRunAiImage}
            />
          ) : null}
        </>
      )}
    </article>
  )
}

function NodeActionBar({
  node,
  onSettings,
  onDownload,
  onGenerate,
}: {
  node: CanvasNode
  onSettings: (sourceRef: string) => void
  onDownload: (sourceRef: string) => void
  onGenerate: (sourceRef: string) => void
}) {
  const canDownload = Boolean(resolveCanvasImageSource(node.meta.imageDataUrl ?? node.meta.imagePath))
  const canGenerate = node.nodeType === 'gen_img'
  return (
    <div className="node-action-bar nodrag">
      <button type="button" className="node-action-icon" title="Settings" onClick={() => onSettings(node.sourceRef)}>⚙</button>
      <button type="button" className="node-action-icon" title="Download" disabled={!canDownload} onClick={() => onDownload(node.sourceRef)}>↓</button>
      <button
        type="button"
        className="node-action-generate"
        disabled={!canGenerate || Boolean(node.meta.isRunning)}
        onClick={() => canGenerate ? onGenerate(node.sourceRef) : undefined}
      >
        {node.meta.isRunning ? 'Generating' : 'Generate'}
      </button>
    </div>
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
    const nodeType = node.nodeType === 'img' ? 'image' : node.nodeType
    if (nodeType === 'image' && !meta.imageDataUrl && meta.imagePath) {
      meta.imageDataUrl = meta.imagePath
    }
    const { w, h } = resolveNodeSize(meta, nodeType, node)
    return {
      ...node,
      nodeType,
      w,
      h,
      meta: {
        ...meta,
        nodeType,
        ports: getNodePorts(nodeType, meta),
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
      ports: shouldUseStoredPorts(nodeType, meta.ports) ? meta.ports.map(normalizePort) : getNodePorts(nodeType, resolvedMeta),
      w: size.w,
      h: size.h,
    },
  }
}

function shouldUseStoredPorts(nodeType: CanvasNodeType, ports: any) {
  if (!Array.isArray(ports)) return false
  return nodeType === 'code' || nodeType === 'c_sharp' || nodeType === 'panel' || nodeType === 'image' || nodeType === 'gen_img'
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
    visible: Boolean(port?.visible ?? true),
    required: Boolean(port?.required ?? false),
    dynamic: Boolean(port?.dynamic ?? false),
  }
}

function normalizePortDataType(value: any): PortDataType {
  if (value === 'image' || value === 'text' || value === 'path' || value === 'number' || value === 'code') return value
  return 'any'
}

function getPortDataType(node: CanvasNode, portId: string, direction?: PortDirection): PortDataType {
  const ports = Array.isArray(node.meta.ports) ? node.meta.ports.map(normalizePort) : getNodePorts(node.nodeType, node.meta)
  const port = ports.find((item) => item.id === portId && (!direction || item.direction === direction))
  return port?.dataType ?? 'any'
}

function portColor(dataType: PortDataType) {
  if (dataType === 'text') return '#73b7ff'
  if (dataType === 'image') return '#d7bd35'
  if (dataType === 'path') return '#68d39b'
  if (dataType === 'number') return '#ff9f5f'
  if (dataType === 'code') return '#b99cff'
  return '#b7c1ba'
}

function resolveNodeSize(meta: Record<string, any>, nodeType: CanvasNodeType, fallback?: Partial<CanvasNode>) {
  if (nodeType === 'panel') return { w: numberOr(fallback?.w, meta.w, meta.default_width ?? 320), h: numberOr(fallback?.h, meta.h, meta.default_height ?? 280) }
  if (nodeType === 'gen_img') return { w: numberOr(meta.w, fallback?.w, meta.default_width ?? 260), h: numberOr(meta.h, fallback?.h, meta.default_height ?? 180) }
  if (nodeType === 'c_sharp') return { w: numberOr(fallback?.w, meta.w, meta.default_width ?? 520), h: numberOr(fallback?.h, meta.h, meta.default_height ?? 360) }
  if (nodeType === 'prompt') return { w: 300, h: 180 }
  if (nodeType === 'file_upload') return { w: 340, h: 210 }
  if (nodeType === 'slider') return { w: 290, h: 170 }
  if (nodeType === 'code') return { w: 420, h: 260 }
  if (nodeType === 'image') return { w: numberOr(fallback?.w, meta.w, 360), h: numberOr(fallback?.h, meta.h, 260) }
  if (meta.collapsed) return { w: numberOr(fallback?.w, meta.w, 320), h: 112 }
  return { w: numberOr(fallback?.w, meta.w, 320), h: numberOr(fallback?.h, meta.h, 205) }
}

function normalizeNodeType(value: any): CanvasNodeType {
  if (
    value === 'image' ||
    value === 'prompt' ||
    value === 'file_upload' ||
    value === 'slider' ||
    value === 'code' ||
    value === 'note' ||
    value === 'panel' ||
    value === 'img' ||
    value === 'gen_img' ||
    value === 'c_sharp'
  ) {
    return value === 'img' ? 'image' : value
  }
  return 'message'
}

function getNodePorts(nodeType: CanvasNodeType, meta: Record<string, any>): NodePort[] {
  if (nodeType === 'code' || nodeType === 'c_sharp') {
    const inputCount = clamp(Math.round(numberOr(meta.inputCount, meta.ports?.filter?.((port: any) => port.direction === 'input').length, nodeType === 'c_sharp' ? 2 : 2)), 0, 12)
    const outputCount = clamp(Math.round(numberOr(meta.outputCount, meta.ports?.filter?.((port: any) => port.direction === 'output').length, nodeType === 'c_sharp' ? 2 : 1)), 1, 12)
    const ports: NodePort[] = []
    for (let i = 0; i < inputCount; i += 1) ports.push({ id: nodeType === 'c_sharp' && i < 2 ? ['x', 'y'][i] : `in-${i}`, label: nodeType === 'c_sharp' && i < 2 ? ['x', 'y'][i] : `In ${i + 1}`, direction: 'input', dataType: 'any', slot: i, visible: true, required: false, dynamic: true })
    for (let i = 0; i < outputCount; i += 1) ports.push({ id: nodeType === 'c_sharp' && i < 2 ? ['out', 'a'][i] : `out-${i}`, label: nodeType === 'c_sharp' && i < 2 ? ['out', 'a'][i] : `Out ${i + 1}`, direction: 'output', dataType: 'any', slot: i, visible: true, required: false, dynamic: true })
    return ports
  }
  if (Array.isArray(meta.ports) && (nodeType === 'panel' || nodeType === 'gen_img')) {
    return meta.ports.map(normalizePort)
  }
  if (nodeType === 'message') return []
  if (nodeType === 'image' || nodeType === 'img') {
    return [
      { id: 'in', label: 'image', direction: 'input', dataType: 'image', slot: 0, visible: true, required: false, dynamic: false },
      { id: 'out', label: 'image', direction: 'output', dataType: 'image', slot: 0, visible: true, required: false, dynamic: false },
    ]
  }
  if (nodeType === 'panel') {
    return [
      { id: 'in', label: 'Input', direction: 'input', dataType: 'any', slot: 0, visible: true, required: false, dynamic: false },
      { id: 'out', label: 'Output', direction: 'output', dataType: 'any', slot: 0, visible: true, required: false, dynamic: false },
    ]
  }
  if (nodeType === 'gen_img') {
    return [
      { id: 'txt', label: 'prompt', direction: 'input', dataType: 'text', slot: 0, visible: true, required: true, dynamic: false },
      { id: 'img_in', label: 'image', direction: 'input', dataType: 'image', slot: 1, visible: true, required: false, dynamic: false },
      { id: 'img_out', label: 'image', direction: 'output', dataType: 'image', slot: 0, visible: true, required: false, dynamic: false },
    ]
  }
  if (nodeType === 'prompt') {
    return [
      { id: 'out', label: 'prompt', direction: 'output', dataType: 'text', slot: 0, visible: true, required: false, dynamic: false },
    ]
  }
  if (nodeType === 'file_upload') {
    return [{ id: 'out', label: 'File Path', direction: 'output', dataType: 'path', slot: 0 }]
  }
  if (nodeType === 'slider') {
    return [{ id: 'out', label: 'Value', direction: 'output', dataType: 'number', slot: 0 }]
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

function arePortTypesCompatible(sourcePort: NodePort, targetPort: NodePort) {
  if (sourcePort.dataType === 'any' || targetPort.dataType === 'any') return true
  return sourcePort.dataType === targetPort.dataType
}

function getNodeInputValue(node: CanvasNode, portId: string, nodes: CanvasNode[], connections: CanvasConnection[]) {
  const connection = connections.find((item) => item.toNodeId === node.id && item.toPortId === portId)
  if (!connection) return undefined
  const sourceNode = nodes.find((item) => item.id === connection.fromNodeId)
  if (!sourceNode) return undefined
  return getNodeOutputValue(sourceNode, connection.fromPortId)
}

function getNodeOutputValue(node: CanvasNode, portId: string) {
  if (node.nodeType === 'prompt') return node.meta.prompt ?? node.meta.body ?? ''
  if (node.nodeType === 'file_upload') return node.meta.filePath ?? ''
  if (node.nodeType === 'slider') return Number(node.meta.value ?? 0)
  if (node.nodeType === 'panel') return node.meta.body ?? ''
  if (node.nodeType === 'image' || node.nodeType === 'img' || node.nodeType === 'gen_img') {
    return getImagePayload(node)
  }
  if (portId && node.meta.outputs && Object.prototype.hasOwnProperty.call(node.meta.outputs, portId)) {
    return node.meta.outputs[portId]
  }
  return node.meta.body ?? ''
}

function getNodeInputImage(node: CanvasNode, portId: string, nodes: CanvasNode[], connections: CanvasConnection[]) {
  const value = getNodeInputValue(node, portId, nodes, connections)
  if (!value) return null
  if (typeof value === 'string') {
    return { path: value, dataUrl: value.startsWith('data:') ? value : '', mimeType: 'image/png' }
  }
  if (typeof value === 'object') return value
  return null
}

function getImagePayload(node: CanvasNode) {
  return {
    path: String(node.meta.imagePath ?? ''),
    dataUrl: String(node.meta.imageDataUrl ?? ''),
    mimeType: String(node.meta.mimeType ?? 'image/png'),
    title: String(node.meta.title ?? 'Image'),
  }
}

function getImageSizeFromPayload(image: any) {
  const w = Number(image?.width ?? image?.naturalWidth ?? 0)
  const h = Number(image?.height ?? image?.naturalHeight ?? 0)
  if (Number.isFinite(w) && Number.isFinite(h) && w > 0 && h > 0) return { w, h }
  return null
}

function fitImageInitialSize(naturalWidth: number, naturalHeight: number) {
  const maxW = 520
  const maxH = 420
  const minW = 160
  const minH = 110
  const scale = Math.min(maxW / naturalWidth, maxH / naturalHeight, 1)
  const w = Math.max(minW, Math.round(naturalWidth * scale))
  const h = Math.max(minH, Math.round(naturalHeight * scale))
  return { w, h }
}

function normalizeAiImageSize(value: unknown) {
  const text = String(value ?? '').trim().toLowerCase().replace(/\s+/g, '')
  if (/^\d{2,4}x\d{2,4}$/.test(text)) return text
  return '1024x1024'
}

function parseAiImageSize(value: unknown) {
  const text = normalizeAiImageSize(value)
  const match = text.match(/^(\d{2,4})x(\d{2,4})$/)
  if (!match) return null
  const w = Number(match[1])
  const h = Number(match[2])
  if (!Number.isFinite(w) || !Number.isFinite(h) || w <= 0 || h <= 0) return null
  return { w, h }
}

async function createCanvasAiImage(run: PendingAiImageRun, config: AiImageConfig) {
  const endpoint = buildImageEndpoint(config.baseUrl ?? '', run.intent === 'edit')
  const response = run.intent === 'edit' && run.image
    ? await sendCanvasImageEditRequest(endpoint, run, config)
    : await sendCanvasImageGenerationRequest(endpoint, run, config)

  const responseText = await response.text()
  if (!response.ok) {
    throw new Error(`Image request failed: HTTP ${response.status} ${response.statusText}\n${responseText.slice(0, 1200)}`)
  }
  return parseCanvasImageResponse(responseText, run, config)
}

function buildImageEndpoint(baseUrl: string, isEdit: boolean) {
  let raw = String(baseUrl || '').trim().replace(/\/+$/, '')
  if (!raw) raw = 'https://api.openai.com/v1'

  if (/\/v1\/images\/(generations|edits)$/i.test(raw)) {
    raw = raw.replace(/\/v1\/images\/(generations|edits)$/i, '')
  }

  if (/\/v1$/i.test(raw)) return `${raw}${isEdit ? '/images/edits' : '/images/generations'}`
  return `${raw}${isEdit ? '/v1/images/edits' : '/v1/images/generations'}`
}

async function sendCanvasImageGenerationRequest(endpoint: string, run: PendingAiImageRun, config: AiImageConfig) {
  const body: Record<string, any> = {
    model: config.model ?? '',
    prompt: run.prompt,
    response_format: 'b64_json',
  }
  if (run.aspectRatio) body.aspect_ratio = run.aspectRatio
  if (run.size) body.size = run.size
  if (run.count) body.n = clamp(Math.round(run.count), 1, 4)
  if (run.image) {
    const imageDataUrl = await resolveImageDataUrl(run.image)
    if (imageDataUrl) body.image = [imageDataUrl]
  }

  return fetch(endpoint, {
    method: 'POST',
    headers: {
      Authorization: `Bearer ${config.apiKey ?? ''}`,
      'Content-Type': 'application/json',
    },
    body: JSON.stringify(body),
  })
}

async function sendCanvasImageEditRequest(endpoint: string, run: PendingAiImageRun, config: AiImageConfig) {
  const imageDataUrl = await resolveImageDataUrl(run.image)
  if (!imageDataUrl) throw new Error('AI image edit requires a connected image input.')
  const { blob, mimeType } = dataUrlToBlob(imageDataUrl)
  const form = new FormData()
  form.append('model', config.model ?? '')
  form.append('prompt', run.prompt)
  form.append('response_format', 'b64_json')
  if (run.aspectRatio) form.append('aspect_ratio', run.aspectRatio)
  if (run.size) form.append('size', run.size)
  if (run.count) form.append('n', String(clamp(Math.round(run.count), 1, 4)))
  form.append('image', blob, `canvas-input.${mimeTypeToExtension(mimeType)}`)

  return fetch(endpoint, {
    method: 'POST',
    headers: {
      Authorization: `Bearer ${config.apiKey ?? ''}`,
    },
    body: form,
  })
}

async function parseCanvasImageResponse(responseText: string, run: PendingAiImageRun, config: AiImageConfig) {
  const root = JSON.parse(responseText)
  const data = collectImageResponseItems(root)
  const images = data
    .map((item: any, index: number) => {
      const b64 = item?.b64_json ?? item?.base64 ?? item?.image_base64
      const url = item?.url ?? item?.image_url ?? item?.image
      if (b64) {
        const dataUrl = normalizeImageSourceValue(b64)
        const mimeType = getMimeTypeFromDataUrl(dataUrl) ?? detectBase64ImageMimeType(b64) ?? 'image/png'
        return {
          dataUrl,
          path: '',
          mimeType,
          prompt: run.prompt,
          provider: config.provider ?? '',
          model: config.model ?? '',
          intent: run.intent,
          index,
        }
      }
      if (url) {
        const dataUrl = normalizeImageSourceValue(url)
        return {
          dataUrl,
          path: String(url),
          mimeType: getMimeTypeFromDataUrl(dataUrl) ?? guessMimeTypeFromUrl(String(url)),
          prompt: run.prompt,
          provider: config.provider ?? '',
          model: config.model ?? '',
          intent: run.intent,
          index,
        }
      }
      return null
    })
    .filter(Boolean)

  if (!images.length) throw new Error('Image API returned no usable image data.')
  return { images }
}

function collectImageResponseItems(root: any) {
  if (Array.isArray(root?.data)) return root.data
  if (Array.isArray(root?.images)) return root.images
  if (Array.isArray(root?.output)) return root.output
  if (root?.b64_json || root?.base64 || root?.image_base64 || root?.url || root?.image_url || root?.image) return [root]
  return []
}

async function resolveImageDataUrl(image: any) {
  const dataUrl = normalizeImageSourceValue(image?.dataUrl ?? image?.imageDataUrl ?? '')
  if (dataUrl.startsWith('data:')) return dataUrl
  const pathOrUrl = String(image?.path ?? image?.imagePath ?? dataUrl).trim()
  if (pathOrUrl.startsWith('data:')) return pathOrUrl
  if (/^https?:/i.test(pathOrUrl)) return fetchImageAsDataUrl(pathOrUrl)
  return ''
}

async function fetchImageAsDataUrl(url: string) {
  const response = await fetch(url)
  if (!response.ok) throw new Error(`Unable to load image URL: HTTP ${response.status}`)
  const blob = await response.blob()
  return new Promise<string>((resolve, reject) => {
    const reader = new FileReader()
    reader.onload = () => resolve(String(reader.result ?? ''))
    reader.onerror = () => reject(reader.error ?? new Error('Unable to read image URL.'))
    reader.readAsDataURL(blob)
  })
}

function dataUrlToBlob(dataUrl: string) {
  const match = /^data:([^;,]+)?(;base64)?,(.*)$/i.exec(dataUrl)
  if (!match) throw new Error('Connected image must be a data URL for browser-side edit requests.')
  const mimeType = match[1] || 'image/png'
  const payload = match[3] || ''
  const bytes = match[2]
    ? Uint8Array.from(atob(payload.replace(/\s+/g, '')), (char) => char.charCodeAt(0))
    : new TextEncoder().encode(decodeURIComponent(payload))
  return { blob: new Blob([bytes], { type: mimeType }), mimeType }
}

function detectBase64ImageMimeType(base64: string) {
  const value = String(base64 ?? '').replace(/^data:[^,]+,/i, '').replace(/\s+/g, '')
  if (value.startsWith('/9j/')) return 'image/jpeg'
  if (value.startsWith('iVBORw0KGgo')) return 'image/png'
  if (value.startsWith('UklGR')) return 'image/webp'
  if (value.startsWith('R0lGOD')) return 'image/gif'
  return 'image/png'
}

function guessMimeTypeFromUrl(url: string) {
  const lower = url.toLowerCase()
  if (lower.includes('.jpg') || lower.includes('.jpeg')) return 'image/jpeg'
  if (lower.includes('.webp')) return 'image/webp'
  if (lower.includes('.gif')) return 'image/gif'
  if (lower.includes('.bmp')) return 'image/bmp'
  return 'image/png'
}

function mimeTypeToExtension(mimeType: string) {
  if (mimeType === 'image/jpeg' || mimeType === 'image/jpg') return 'jpg'
  if (mimeType === 'image/webp') return 'webp'
  if (mimeType === 'image/gif') return 'gif'
  if (mimeType === 'image/bmp') return 'bmp'
  return 'png'
}

function normalizeImageSourceValue(value: any, fallbackMimeType = 'image/png') {
  const raw = String(value ?? '').trim()
  if (!raw) return ''
  if (/^data:/i.test(raw) || /^https?:/i.test(raw) || /^file:/i.test(raw)) return raw
  const compact = raw.replace(/\s+/g, '')
  if (looksLikeBase64Image(compact)) {
    const mimeType = detectBase64ImageMimeType(compact) ?? fallbackMimeType
    return `data:${mimeType};base64,${compact}`
  }
  return raw
}

function looksLikeBase64Image(value: string) {
  if (value.length < 80) return false
  if (!/^[A-Za-z0-9+/]+=*$/.test(value)) return false
  return value.startsWith('/9j/') || value.startsWith('iVBORw0KGgo') || value.startsWith('UklGR') || value.startsWith('R0lGOD')
}

function getMimeTypeFromDataUrl(value: string) {
  const match = /^data:([^;,]+)/i.exec(String(value ?? '').trim())
  return match?.[1] ?? ''
}

function hasDraggedImage(dataTransfer: DataTransfer | null) {
  if (!dataTransfer) return false
  if ([...dataTransfer.files].some((file) => file.type.startsWith('image/'))) return true
  return [...dataTransfer.items].some((item) => item.kind === 'file' && item.type.startsWith('image/'))
}

function readFileAsDataUrl(file: File) {
  return new Promise<string>((resolve, reject) => {
    const reader = new FileReader()
    reader.onload = () => resolve(String(reader.result ?? ''))
    reader.onerror = () => reject(reader.error ?? new Error('Unable to read dropped image.'))
    reader.readAsDataURL(file)
  })
}

function renderPorts(
  node: CanvasNode,
  pending: PendingConnection | null,
  onPortClick: (node: CanvasNode, port: NodePort, event?: React.MouseEvent<HTMLButtonElement>) => void,
  onPortPointerDown: (event: React.PointerEvent<HTMLButtonElement>, node: CanvasNode, port: NodePort) => void,
) {
  const ports = Array.isArray(node.meta.ports) ? node.meta.ports : getNodePorts(node.nodeType, node.meta)
  if (!ports.length) return null
  const inputs = ports.filter((port) => port.direction === 'input')
  const outputs = ports.filter((port) => port.direction === 'output')
  return (
    <div className="port-stack">
      <div className="port-column port-column-inputs">
        {inputs.map((port) => (
          <button
            key={port.id}
            type="button"
            className={`port port-${port.direction} ${pending?.nodeId === node.id && pending.portId === port.id ? 'active' : ''}`}
            data-port-id={port.id}
            onPointerDown={(event) => onPortPointerDown(event, node, port)}
            onMouseDown={(event) => event.stopPropagation()}
            onClick={(event) => {
              event.stopPropagation()
              onPortClick(node, port, event)
            }}
          >
            {port.label}
          </button>
        ))}
      </div>
      <div className="port-column port-column-outputs">
        {outputs.map((port) => (
          <button
            key={port.id}
            type="button"
            className={`port port-${port.direction} ${pending?.nodeId === node.id && pending.portId === port.id ? 'active' : ''}`}
            data-port-id={port.id}
            onPointerDown={(event) => onPortPointerDown(event, node, port)}
            onMouseDown={(event) => event.stopPropagation()}
            onClick={(event) => {
              event.stopPropagation()
              onPortClick(node, port, event)
            }}
          >
            {port.label}
          </button>
        ))}
      </div>
    </div>
  )
}

function renderFlowPorts(node: CanvasNode) {
  const ports = Array.isArray(node.meta.ports) ? node.meta.ports : getNodePorts(node.nodeType, node.meta)
  if (!ports.length) return null
  const inputs = ports.filter((port) => port.direction === 'input')
  const outputs = ports.filter((port) => port.direction === 'output')

  const renderPort = (port: NodePort) => (
    <div
      key={port.id}
      className={`port port-${port.direction} port-type-${port.dataType} nodrag`}
    >
      <Handle
        id={port.id}
        type={port.direction === 'input' ? 'target' : 'source'}
        position={port.direction === 'input' ? Position.Left : Position.Right}
        className="port-handle"
      />
      <span>{port.label}</span>
    </div>
  )

  return (
    <div className="port-stack">
      <div className="port-column port-column-inputs">{inputs.map(renderPort)}</div>
      <div className="port-column port-column-outputs">{outputs.map(renderPort)}</div>
    </div>
  )
}

function renderNodeSettings(
  node: CanvasNode,
  updateMeta: (sourceRef: string, patch: Record<string, unknown>) => void,
  onPreviewImage: (src: string, title: string) => void,
  onClose: () => void,
) {
  const updateTitle = (title: string) => updateMeta(node.sourceRef, { title, userEditedTitle: true })
  const header = (
    <>
      <div className="detail-header">
        <h3>{node.meta.title ?? 'Node'}</h3>
        <button type="button" onClick={onClose}>Close</button>
      </div>
      <label>
        <span>Title</span>
        <input value={node.meta.title ?? ''} onChange={(event) => updateTitle(event.target.value)} />
      </label>
    </>
  )
  const collapsedToggle = (
    <label className="checkbox-row">
      <input
        type="checkbox"
        checked={Boolean(node.meta.collapsed)}
        onChange={(event) => updateMeta(node.sourceRef, { collapsed: event.target.checked })}
      />
      <span>Collapsed</span>
    </label>
  )

  if (node.nodeType === 'prompt') {
    return (
      <>
        {header}
        <label className="node-field">
          <span>Prompt</span>
          <textarea value={node.meta.prompt ?? ''} onChange={(event) => updateMeta(node.sourceRef, { prompt: event.target.value })} />
        </label>
      </>
    )
  }
  if (node.nodeType === 'panel') {
    return (
      <>
        {header}
        <label className="node-field">
          <span>Panel Body</span>
          <textarea value={node.meta.body ?? ''} onChange={(event) => updateMeta(node.sourceRef, { body: event.target.value })} />
        </label>
        {collapsedToggle}
      </>
    )
  }
  if (node.nodeType === 'gen_img') {
    return (
      <>
        {header}
        <label className="node-field">
          <span>Prompt</span>
          <textarea value={node.meta.prompt ?? ''} onChange={(event) => updateMeta(node.sourceRef, { prompt: event.target.value })} />
        </label>
        <label className="node-field">
          <span>Generation Size</span>
          <select value={normalizeAiImageSize(node.meta.imageSize ?? node.meta.size ?? '1024x1024')} onChange={(event) => updateMeta(node.sourceRef, { imageSize: event.target.value })}>
            <option value="512x512">512 x 512</option>
            <option value="768x768">768 x 768</option>
            <option value="1024x1024">1024 x 1024</option>
            <option value="1024x1536">1024 x 1536</option>
            <option value="1536x1024">1536 x 1024</option>
          </select>
        </label>
        <label className="node-field">
          <span>Generation Count</span>
          <input
            type="number"
            min={1}
            max={4}
            value={clamp(Math.round(Number(node.meta.imageCount ?? node.meta.count ?? 1)), 1, 4)}
            onChange={(event) => updateMeta(node.sourceRef, { imageCount: clamp(Math.round(Number(event.target.value || 1)), 1, 4) })}
          />
        </label>
        {node.meta.runStatus || node.meta.error ? (
          <div className="node-readonly-stack">
            {node.meta.runStatus ? <span>Status: {String(node.meta.runStatus)}</span> : null}
            {node.meta.error ? <span className="node-error-text">Error: {String(node.meta.error)}</span> : null}
          </div>
        ) : null}
      </>
    )
  }
  if (node.nodeType === 'file_upload') {
    return (
      <>
        {header}
        <label className="node-field">
          <span>File Path</span>
          <input value={node.meta.filePath ?? ''} onChange={(event) => updateMeta(node.sourceRef, { filePath: event.target.value })} />
        </label>
      </>
    )
  }
  if (node.nodeType === 'img') {
    return (
      <>
        {header}
        <input
          className="node-inline-input"
          value={node.meta.imagePath ?? ''}
          placeholder="Image path or URL..."
          onChange={(event) => updateMeta(node.sourceRef, { imagePath: event.target.value })}
        />
      </>
    )
  }
  if (node.nodeType === 'slider') {
    const min = Number(node.meta.min ?? 0)
    const max = Number(node.meta.max ?? 1)
    const value = clamp(Number(node.meta.value ?? 0), Math.min(min, max), Math.max(min, max))
    const updateSliderBounds = (patch: Record<string, number>) => {
      const nextMin = Object.prototype.hasOwnProperty.call(patch, 'min') ? Number(patch.min) : min
      const nextMax = Object.prototype.hasOwnProperty.call(patch, 'max') ? Number(patch.max) : max
      const nextValue = Object.prototype.hasOwnProperty.call(patch, 'value') ? Number(patch.value) : value
      updateMeta(node.sourceRef, {
        ...patch,
        value: clamp(nextValue, Math.min(nextMin, nextMax), Math.max(nextMin, nextMax)),
      })
    }
    return (
      <>
        {header}
        <label className="node-field">
          <span>Value: {value.toFixed(2)}</span>
          <input
            type="range"
            min={min}
            max={max}
            step={Number(node.meta.step ?? 0.01)}
            value={value}
            onChange={(event) => updateSliderBounds({ value: Number(event.target.value) })}
          />
        </label>
        <div className="node-field-grid">
          <label className="node-field">
            <span>Min</span>
            <input type="number" value={min} onChange={(event) => updateSliderBounds({ min: Number(event.target.value) })} />
          </label>
          <label className="node-field">
            <span>Max</span>
            <input type="number" value={max} onChange={(event) => updateSliderBounds({ max: Number(event.target.value) })} />
          </label>
          <label className="node-field">
            <span>Step</span>
            <input type="number" min={0.0001} value={Number(node.meta.step ?? 0.01)} onChange={(event) => updateMeta(node.sourceRef, { step: Math.max(0.0001, Number(event.target.value || 0.0001)) })} />
          </label>
        </div>
      </>
    )
  }
  if (node.nodeType === 'code' || node.nodeType === 'c_sharp') {
    const inputCount = Math.max(0, Math.min(12, Math.round(Number(node.meta.inputCount ?? getNodePorts(node.nodeType, node.meta).filter((port) => port.direction === 'input').length))))
    const outputCount = Math.max(1, Math.min(12, Math.round(Number(node.meta.outputCount ?? getNodePorts(node.nodeType, node.meta).filter((port) => port.direction === 'output').length))))
    return (
      <>
        {header}
        <label className="node-field">
          <span>C# Body</span>
          <textarea value={node.meta.body ?? ''} onChange={(event) => updateMeta(node.sourceRef, { body: event.target.value })} />
        </label>
        <div className="node-field-grid">
          <label className="node-field">
            <span>Input Count</span>
            <input type="number" min={0} max={12} value={inputCount} onChange={(event) => updateMeta(node.sourceRef, { inputCount: clamp(Math.round(Number(event.target.value || 0)), 0, 12) })} />
          </label>
          <label className="node-field">
            <span>Output Count</span>
            <input type="number" min={1} max={12} value={outputCount} onChange={(event) => updateMeta(node.sourceRef, { outputCount: clamp(Math.round(Number(event.target.value || 1)), 1, 12) })} />
          </label>
        </div>
      </>
    )
  }
  if (node.nodeType === 'image') {
    const imageSrc = resolveCanvasImageSource(node.meta.imageDataUrl ?? node.meta.imagePath)
    return (
      <>
        {header}
        <div className="node-image-field">
          {imageSrc ? (
            <button type="button" className="node-image-preview" onClick={() => onPreviewImage(imageSrc, String(node.meta.title ?? 'Image'))}>
              <img src={imageSrc} alt={String(node.meta.title ?? 'Image')} loading="lazy" decoding="async" draggable={false} />
            </button>
          ) : (
            <div className="node-image-preview node-image-empty">No image</div>
          )}
          <label className="node-field">
            <span>Image Path / URL / Data URL</span>
            <input value={node.meta.imagePath ?? ''} onChange={(event) => updateMeta(node.sourceRef, { imagePath: event.target.value })} />
          </label>
          <div className="node-readonly-stack">
            {node.meta.mimeType ? <span>Type: {String(node.meta.mimeType)}</span> : null}
            {node.meta.naturalWidth && node.meta.naturalHeight ? <span>Natural size: {Number(node.meta.naturalWidth)} x {Number(node.meta.naturalHeight)}</span> : null}
          </div>
        </div>
      </>
    )
  }
  if (node.nodeType === 'message') {
    return (
      <>
        {header}
        <label className="node-field">
          <span>Message</span>
          <textarea value={node.meta.body ?? node.meta.summary ?? ''} readOnly />
        </label>
      </>
    )
  }
  if (node.nodeType === 'note') {
    return (
      <>
        {header}
        <label className="node-field">
          <span>Body</span>
          <textarea value={node.meta.body ?? ''} onChange={(event) => updateMeta(node.sourceRef, { body: event.target.value })} />
        </label>
        {collapsedToggle}
      </>
    )
  }
  return null
}

function StableNodeTextarea({
  className,
  value,
  placeholder,
  onCommit,
}: {
  className: string
  value: string
  placeholder?: string
  onCommit: (value: string) => void
}) {
  const [draft, setDraft] = useState(value)
  const composingRef = useRef(false)

  useEffect(() => {
    if (!composingRef.current) setDraft(value)
  }, [value])

  return (
    <textarea
      className={className}
      value={draft}
      placeholder={placeholder}
      onPointerDown={(event) => event.stopPropagation()}
      onMouseDown={(event) => event.stopPropagation()}
      onKeyDown={(event) => event.stopPropagation()}
      onCompositionStart={() => {
        composingRef.current = true
      }}
      onCompositionEnd={(event) => {
        composingRef.current = false
        const next = event.currentTarget.value
        setDraft(next)
        onCommit(next)
      }}
      onChange={(event) => {
        const next = event.target.value
        setDraft(next)
        if (!composingRef.current) onCommit(next)
      }}
    />
  )
}

function renderInlineNodeEditor(
  node: CanvasNode,
  updateMeta: (sourceRef: string, patch: Record<string, unknown>) => void,
  onRunAiImage?: (sourceRef: string) => void,
  _onPreviewImage?: (src: string, title: string) => void,
) {
  if (node.nodeType === 'prompt') {
    return (
      <StableNodeTextarea
        className="node-inline-textarea prompt nodrag"
        value={String(node.meta.prompt ?? '')}
        placeholder="Prompt text..."
        onCommit={(value) => updateMeta(node.sourceRef, { prompt: value })}
      />
    )
  }
  if (node.nodeType === 'panel') {
    return (
      <textarea
        className="node-inline-textarea"
        value={node.meta.body ?? ''}
        placeholder="Panel text..."
        onChange={(event) => updateMeta(node.sourceRef, { body: event.target.value })}
      />
    )
  }
  if (node.nodeType === 'gen_img') {
    return (
      <div className="ai-image-node-body">
        {node.meta.runStatus ? <span className="ai-image-status">{String(node.meta.runStatus)}</span> : null}
        {node.meta.error ? <div className="node-error-text">{String(node.meta.error)}</div> : null}
      </div>
    )
  }
  if (node.nodeType === 'code' || node.nodeType === 'c_sharp') {
    return (
      <textarea
        className="node-inline-textarea code nodrag"
        value={node.meta.body ?? ''}
        placeholder="C# code..."
        spellCheck={false}
        onPointerDown={(event) => event.stopPropagation()}
        onMouseDown={(event) => event.stopPropagation()}
        onKeyDown={(event) => event.stopPropagation()}
        onChange={(event) => updateMeta(node.sourceRef, { body: event.target.value })}
      />
    )
  }
  if (node.nodeType === 'file_upload') {
    return (
      <input
        className="node-inline-input"
        value={node.meta.filePath ?? ''}
        placeholder="File path..."
        onChange={(event) => updateMeta(node.sourceRef, { filePath: event.target.value })}
      />
    )
  }
  if (node.nodeType === 'slider') {
    return (
      <label className="node-inline-slider">
        <span>{Number(node.meta.value ?? 0).toFixed(2)}</span>
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
  return (
    <textarea
      className="node-inline-textarea"
      value={node.meta.body ?? node.meta.summary ?? ''}
      placeholder="Node text..."
      onChange={(event) => updateMeta(node.sourceRef, { body: event.target.value })}
    />
  )
}

function renderImageNode(
  node: CanvasNode,
  onResizePointerDown: (event: React.PointerEvent<HTMLButtonElement>, node: CanvasNode) => void,
  onPreviewImage: (src: string, title: string) => void,
) {
  const imageSrc = resolveCanvasImageSource(node.meta.imageDataUrl ?? node.meta.imagePath)

  return (
    <div className="node-image-plain">
      {imageSrc ? (
        <img
          src={imageSrc}
          alt={String(node.meta.title ?? 'Image')}
          loading="lazy"
          decoding="async"
          draggable={false}
          onDoubleClick={(event) => {
            event.stopPropagation()
            onPreviewImage(imageSrc, String(node.meta.title ?? 'Image'))
          }}
        />
      ) : (
        <div className="node-image-empty node-image-plain-empty">No image</div>
      )}
      <button
        type="button"
        className="image-resize-handle"
        data-resize-handle="true"
        aria-label="Resize image"
        title="Drag to resize. Hold Shift to keep aspect ratio."
        onPointerDown={(event) => onResizePointerDown(event, node)}
      />
    </div>
  )
}

function intersectsSelection(node: CanvasNode, viewport: Viewport, x1: number, y1: number, x2: number, y2: number) {
  const left = Math.min(x1, x2)
  const right = Math.max(x1, x2)
  const top = Math.min(y1, y2)
  const bottom = Math.max(y1, y2)
  const nodeLeft = viewport.x + node.x * viewport.z
  const nodeTop = viewport.y + node.y * viewport.z
  const nodeRight = nodeLeft + node.w * viewport.z
  const nodeBottom = nodeTop + node.h * viewport.z
  return nodeLeft < right && nodeRight > left && nodeTop < bottom && nodeBottom > top
}

function renderNodePreview(node: CanvasNode, onPreviewImage: (src: string, title: string) => void) {
  if (node.nodeType === 'slider') {
    return <div className="node-preview-chip">Value {Number(node.meta.value ?? 0).toFixed(2)}</div>
  }
  if (node.nodeType === 'file_upload' && node.meta.filePath) {
    return <div className="node-preview-chip">{String(node.meta.filePath)}</div>
  }
  if ((node.nodeType === 'image' || node.nodeType === 'img') && node.meta.imagePath) {
    const imageSrc = resolveCanvasImageSource(node.meta.imageDataUrl ?? node.meta.imagePath)
    return imageSrc ? (
      <button type="button" className="node-image-chip" onClick={() => onPreviewImage(imageSrc, String(node.meta.title ?? 'Image'))}>
        <img src={imageSrc} alt={String(node.meta.title ?? 'Image')} loading="lazy" decoding="async" draggable={false} />
      </button>
    ) : (
      <div className="node-preview-chip">{String(node.meta.imagePath)}</div>
    )
  }
  if (node.nodeType === 'prompt' && node.meta.prompt) {
    return <div className="node-preview-chip">{String(node.meta.prompt).slice(0, 40)}</div>
  }
  if (node.nodeType === 'gen_img') {
    return <div className="node-preview-chip">{node.meta.prompt ? String(node.meta.prompt).slice(0, 40) : 'Prompt required'}</div>
  }
  if (node.nodeType === 'code' || node.nodeType === 'c_sharp') {
    return <div className="node-preview-chip">{`${node.meta.inputCount ?? 2} in / ${node.meta.outputCount ?? 1} out`}</div>
  }
  return null
}

function getNodePreviewText(node: CanvasNode) {
  if (node.nodeType === 'prompt') return node.meta.prompt || node.meta.body || 'No prompt'
  if (node.nodeType === 'file_upload') return node.meta.filePath || 'No file path'
  if (node.nodeType === 'image' || node.nodeType === 'img') return node.meta.imagePath || 'No image path'
  if (node.nodeType === 'slider') return `Range ${node.meta.min ?? 0} - ${node.meta.max ?? 1}`
  if (node.nodeType === 'gen_img') return node.meta.prompt || 'No image prompt'
  return node.meta.body || 'No content'
}

function portPositionKey(sourceRef: string, portId: string) {
  return `${sourceRef}::${portId}`
}

function portPositionMapsEqual(a: PortPositionMap, b: PortPositionMap) {
  const aKeys = Object.keys(a)
  const bKeys = Object.keys(b)
  if (aKeys.length !== bKeys.length) return false
  return aKeys.every((key) => {
    const left = a[key]
    const right = b[key]
    return Boolean(right) && Math.abs(left.x - right.x) < 0.5 && Math.abs(left.y - right.y) < 0.5
  })
}

function renderConnection(connection: CanvasConnection, nodes: CanvasNode[], viewport: Viewport, portPositions: PortPositionMap) {
  const fromNode = nodes.find((node) => node.id === connection.fromNodeId)
  const toNode = nodes.find((node) => node.id === connection.toNodeId)
  if (!fromNode || !toNode) return null
  const from = getPortPosition(fromNode, connection.fromPortId, viewport, portPositions)
  const to = getPortPosition(toNode, connection.toPortId, viewport, portPositions)
  if (!from || !to) return null
  const portType = getPortDataType(fromNode, connection.fromPortId, 'output')
  const isRunningInput = Boolean(toNode.meta.isRunning)
  return (
    <path
      key={connection.id}
      d={createConnectionPath(from, to)}
      className={`connection-path${isRunningInput ? ' connection-path-running' : ''}`}
      style={{ '--connection-color': portColor(portType) } as React.CSSProperties}
    />
  )
}

function renderPendingConnection(
  pending: PendingConnection | null,
  point: { x: number; y: number } | null,
  nodes: CanvasNode[],
  viewport: Viewport,
  portPositions: PortPositionMap,
) {
  if (!pending || !point) return null
  const fromNode = nodes.find((node) => node.id === pending.nodeId)
  if (!fromNode) return null
  const from = getPortPosition(fromNode, pending.portId, viewport, portPositions)
  if (!from) return null
  const portType = getPortDataType(fromNode, pending.portId, 'output')
  return (
    <path
      d={createConnectionPath(from, point)}
      className="connection-path connection-path-pending"
      style={{ '--connection-color': portColor(portType) } as React.CSSProperties}
    />
  )
}

function createConnectionPath(from: { x: number; y: number }, to: { x: number; y: number }) {
  const dx = Math.abs(to.x - from.x)
  const dy = Math.abs(to.y - from.y)
  const horizontal = Math.max(72, Math.min(220, dx * 0.52 + dy * 0.18))
  const c1x = from.x + horizontal
  const c2x = to.x - horizontal
  return `M ${from.x} ${from.y} C ${c1x} ${from.y}, ${c2x} ${to.y}, ${to.x} ${to.y}`
}

function getPortPosition(node: CanvasNode, portId: string, viewport: Viewport, portPositions?: PortPositionMap) {
  const measured = portPositions?.[portPositionKey(node.sourceRef, portId)]
  if (measured) return measured
  const ports = Array.isArray(node.meta.ports) ? node.meta.ports : getNodePorts(node.nodeType, node.meta)
  const port = ports.find((item) => item.id === portId)
  if (!port) return null
  const sidePorts = ports.filter((item) => item.direction === port.direction)
  const sideIndex = sidePorts.findIndex((item) => item.id === port.id)
  const top = viewport.y + (node.y + 42 + sideIndex * 18) * viewport.z
  const left = viewport.x + (node.x + (port.direction === 'input' ? 4 : node.w - 4)) * viewport.z
  return { x: left, y: top }
}

function findPortAtPoint(clientX: number, clientY: number, nodes: CanvasNode[]) {
  const element = document.elementFromPoint(clientX, clientY) as HTMLElement | null
  const portElement = element?.closest?.('[data-port-id]') as HTMLElement | null
  const nodeElement = element?.closest?.('.canvas-node') as HTMLElement | null
  const portId = portElement?.dataset.portId
  const sourceRef = nodeElement?.dataset.sourceRef
  if (!portId || !sourceRef) return null
  const node = nodes.find((item) => item.sourceRef === sourceRef)
  if (!node) return null
  const port = getNodePort(node, portId)
  if (!port) return null
  return { node, port }
}

function buildDefaultNodeMeta(nodeType: CanvasNodeType, sourceRef: string) {
  if (nodeType === 'panel') {
    return {
      sourceRef,
      nodeType,
      title: 'Panel',
      summary: 'Display container',
      body: 'Use this panel to show text, links, data, or connection results.',
      role: 'note',
      kind: 'note',
      collapsed: false,
      default_width: 320,
      default_height: 280,
      ports: getNodePorts(nodeType, {}),
    }
  }
  if (nodeType === 'image' || nodeType === 'img') {
    return {
      sourceRef,
      nodeType: 'image',
      title: 'Image',
      summary: 'Image preview',
      body: 'Paste an image path, URL, or data URL.',
      imagePath: '',
      role: 'tool',
      kind: 'image',
      collapsed: false,
      default_width: 420,
      default_height: 360,
      ports: getNodePorts('image', {}),
    }
  }
  if (nodeType === 'gen_img') {
    return {
      sourceRef,
      nodeType,
      title: 'AI Image',
      summary: 'Generate image',
      body: 'Generate an image from a prompt and optional reference image.',
      prompt: '',
      imageSize: '1024x1024',
      imageCount: 1,
      seed: 0,
      role: 'tool',
      kind: 'tool',
      collapsed: false,
      default_width: 260,
      default_height: 118,
      ports: getNodePorts(nodeType, {}),
    }
  }
  if (nodeType === 'c_sharp') {
    return {
      sourceRef,
      nodeType,
      title: 'C#',
      summary: 'C# script',
      body: '// write code here',
      inputCount: 2,
      outputCount: 2,
      role: 'tool',
      kind: 'tool',
      collapsed: false,
      default_width: 520,
      default_height: 360,
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
  const raw = normalizeImageSourceValue(value)
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
