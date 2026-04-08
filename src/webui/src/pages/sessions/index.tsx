import {
  useEffect, useRef, useState, useCallback, useLayoutEffect, useMemo,
} from 'react'
import {
  Box, Flex, Text, Button, IconButton, Input, Textarea, Badge,
  Spinner, Select, Portal, createListCollection, Tabs,
} from '@chakra-ui/react'
import {
  MessageCircle, Plus, Trash2, Send, Square, Paperclip, X,
  ChevronUp, EyeOff, HardDrive,
} from 'lucide-react'
import {
  listProviders, listChannels, listAgents,
  switchSessionProvider,
  type ProviderConfig, type ChannelConfig, type AgentConfig,
  type MessageAttachment, type CreateSessionRequest,
  type SessionInfo,
  SYSTEM_SOURCES,
} from '@/api/gateway'
import { AppDialog } from '@/components/ui/app-dialog'
import { useSessionStore, isDisplayMessage, buildSessionTree, isSubAgentSession } from '@/store/sessionStore'
import { eventBus } from '@/services/eventBus'
import { toaster } from '@/components/ui/toaster'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import ChatMessage from '@/components/chat-message'
import SessionTreeItem from './session-tree-item'
import SandboxPanel from './sandbox-panel'
import { SessionDnaTab } from './session-dna-tab'
import { SessionMemoryTab } from './session-memory-tab'
import { ApprovalTab } from './approval-tab'
import { PetTab } from './pet-tab'

// ─── 新建 Session 弹窗 ────────────────────────────────────────────────────────
interface CreateDialogProps {
  open: boolean
  onClose: () => void
  providers: ProviderConfig[]
  channels: ChannelConfig[]
  agents: AgentConfig[]
  onCreated: (sessionId: string) => void
}

function CreateSessionDialog({
  open, onClose, providers, channels, agents, onCreated,
}: CreateDialogProps) {
  const { addSession } = useSessionStore()
  const [title, setTitle] = useState('')
  const [providerId, setProviderId] = useState('')
  const [channelId, setChannelId] = useState('')
  const [agentId, setAgentId] = useState('')
  const [creating, setCreating] = useState(false)

  useEffect(() => {
    if (open) {
      setTitle('')
      const def = providers.find((p) => p.isDefault) ?? providers[0]
      setProviderId(def?.id ?? '')
      setChannelId('')
      const defAgent = agents.find((a) => a.isDefault) ?? agents[0]
      setAgentId(defAgent?.id ?? '')
    }
  }, [open, providers, agents])

  if (!open) return null

  const handleCreate = async () => {
    if (!title.trim() || !providerId) return
    setCreating(true)
    try {
      const req: CreateSessionRequest = {
        title: title.trim(),
        providerId,
        channelId: channelId || undefined,
        agentId: agentId || undefined,
      }
      const session = await addSession(req)
      onCreated(session.id)
      onClose()
    } catch (err: unknown) {
      toaster.create({ type: 'error', title: '创建失败', description: String(err) })
    } finally {
      setCreating(false)
    }
  }

  return (
    <AppDialog
      open={open}
      onClose={onClose}
      title="新建会话"
      contentProps={{ maxW: '400px' }}
      footer={(
        <>
          <Button variant="outline" onClick={onClose}>取消</Button>
          <Button
            colorPalette="blue"
            loading={creating}
            disabled={!title.trim() || !providerId}
            onClick={handleCreate}
          >
            创建
          </Button>
        </>
      )}
    >
      <Box mb="3">
        <Text fontSize="sm" mb="1" fontWeight="medium">会话名称 *</Text>
        <Input
          value={title}
          onChange={(e) => setTitle(e.target.value)}
          placeholder="输入会话名称"
          onKeyDown={(e) => { if (e.key === 'Enter') handleCreate() }}
        />
      </Box>

      <Box mb="3">
        <Text fontSize="sm" mb="1" fontWeight="medium">AI 模型 *</Text>
        <Select.Root
          value={[providerId]}
          onValueChange={(v) => setProviderId(v.value[0] ?? '')}
          collection={createListCollection({ items: providers.map((p) => ({ value: p.id, label: `${p.displayName} (${p.modelName})` })) })}
        >
          <Select.Trigger>
            <Select.ValueText placeholder="选择模型" />
          </Select.Trigger>
          <Portal>
            <Select.Positioner>
              <Select.Content>
                {providers.map((p) => (
                  <Select.Item key={p.id} item={{ value: p.id, label: `${p.displayName} (${p.modelName})` }}>
                    {p.displayName} ({p.modelName})
                  </Select.Item>
                ))}
              </Select.Content>
            </Select.Positioner>
          </Portal>
        </Select.Root>
      </Box>

      <Box mb="3">
        <Text fontSize="sm" mb="1" fontWeight="medium">渠道（可选）</Text>
        <Select.Root
          value={channelId ? [channelId] : []}
          onValueChange={(v) => setChannelId(v.value[0] ?? '')}
          collection={createListCollection({ items: [{ value: '', label: '默认 Web' }, ...channels.map((c) => ({ value: c.id, label: c.displayName }))] })}
        >
          <Select.Trigger>
            <Select.ValueText placeholder="默认 Web" />
          </Select.Trigger>
          <Portal>
            <Select.Positioner>
              <Select.Content>
                <Select.Item item={{ value: '', label: '默认 Web' }}>默认 Web</Select.Item>
                {channels.map((c) => (
                  <Select.Item key={c.id} item={{ value: c.id, label: c.displayName }}>
                    {c.displayName}
                  </Select.Item>
                ))}
              </Select.Content>
            </Select.Positioner>
          </Portal>
        </Select.Root>
      </Box>

      <Box>
        <Text fontSize="sm" mb="1" fontWeight="medium">Agent（可选）</Text>
        <Select.Root
          value={agentId ? [agentId] : []}
          onValueChange={(v) => setAgentId(v.value[0] ?? '')}
          collection={createListCollection({ items: [{ value: '', label: '默认' }, ...agents.map((a) => ({ value: a.id, label: a.name }))] })}
        >
          <Select.Trigger>
            <Select.ValueText placeholder="默认" />
          </Select.Trigger>
          <Portal>
            <Select.Positioner>
              <Select.Content>
                <Select.Item item={{ value: '', label: '默认' }}>默认</Select.Item>
                {agents.map((a) => (
                  <Select.Item key={a.id} item={{ value: a.id, label: a.name }}>
                    {a.name}
                  </Select.Item>
                ))}
              </Select.Content>
            </Select.Positioner>
          </Portal>
        </Select.Root>
      </Box>
    </AppDialog>
  )
}

// ─── 主组件 ───────────────────────────────────────────────────────────────────
export default function SessionsPage() {
  const store = useSessionStore()
  const {
    sessions, messages, chatting, streamingContent, loading,
    messagesHasMore, loadingEarlier, subAgentProgress,
    fetchSessions, selectSession, removeSession, loadEarlierMessages,
    sendMessage, stopChat,
  } = store

  // streamingContent 已在 store 解构中，供 useLayoutEffect 依赖

  const [showCreate, setShowCreate] = useState(false)
  const [inputText, setInputText] = useState('')
  const [pendingAttachments, setPendingAttachments] = useState<MessageAttachment[]>([])
  const [providers, setProviders] = useState<ProviderConfig[]>([])
  const [channels, setChannels] = useState<ChannelConfig[]>([])
  const [agents, setAgents] = useState<AgentConfig[]>([])
  const [runningSessionIds, setRunningSessionIds] = useState<Set<string>>(new Set())
  const [deleteSessionId, setDeleteSessionId] = useState<string | null>(null)
  const [showSandbox, setShowSandbox] = useState(false)

  const messagesEl = useRef<HTMLDivElement>(null)
  const fileInputRef = useRef<HTMLInputElement>(null)
  const textareaRef = useRef<HTMLTextAreaElement>(null)
  const prevScrollHeight = useRef(0)
  const userScrolledUp = useRef(false)

  // 初始化数据
  useEffect(() => {
    fetchSessions()
    Promise.all([listProviders(), listChannels(), listAgents()]).then(([ps, cs, as]) => {
      setProviders(ps.filter((p) => p.isEnabled && p.modelType === 'chat'))
      setChannels(cs.filter((c) => c.isEnabled))
      setAgents(as.filter((a) => a.isEnabled))
    })

    const onSessionEvent = () => { fetchSessions() }

    const onCronJobExecuted = (payload: unknown) => {
      const { sessionId } = payload as { sessionId: string }
      if (sessionId === useSessionStore.getState().currentSessionId) {
        selectSession(sessionId).then(scrollToBottom)
      }
    }

    const onAgentStatusChanged = (payload: unknown) => {
      const { sessionId, status } = payload as { sessionId: string; status: string }
      setRunningSessionIds((prev) => {
        const next = new Set(prev)
        if (status === 'running') {
          next.add(sessionId)
        } else {
          next.delete(sessionId)
          const state = useSessionStore.getState()
          if (sessionId === state.currentSessionId && !state.chatting) {
            selectSession(sessionId).then(scrollToBottom)
          }
        }
        return next
      })
    }

    eventBus.on('session:created', onSessionEvent)
    eventBus.on('session:approved', onSessionEvent)
    eventBus.on('session:disabled', onSessionEvent)
    eventBus.on('cron:jobExecuted', onCronJobExecuted)
    eventBus.on('agent:statusChanged', onAgentStatusChanged)

    return () => {
      eventBus.off('session:created', onSessionEvent)
      eventBus.off('session:approved', onSessionEvent)
      eventBus.off('session:disabled', onSessionEvent)
      eventBus.off('cron:jobExecuted', onCronJobExecuted)
      eventBus.off('agent:statusChanged', onAgentStatusChanged)
    }
  }, [fetchSessions, selectSession])

  function scrollToBottom() {
    if (messagesEl.current) {
      messagesEl.current.scrollTop = messagesEl.current.scrollHeight
    }
  }

  // 消息列表更新时滚动到底（排除加载更早消息的情况）
  useLayoutEffect(() => {
    if (!messagesEl.current) return
    if (loadingEarlier) {
      // 加载更早消息完成后：保持滚动位置
      const newScrollHeight = messagesEl.current.scrollHeight
      messagesEl.current.scrollTop = newScrollHeight - prevScrollHeight.current
    } else if (!userScrolledUp.current) {
      scrollToBottom()
    }
  }, [messages.length, chatting, loading, streamingContent])

  function handleMessagesScroll() {
    if (!messagesEl.current) return
    const el = messagesEl.current
    // 用户是否手动上滚（距底部超过 80px）
    const distanceFromBottom = el.scrollHeight - el.scrollTop - el.clientHeight
    userScrolledUp.current = distanceFromBottom > 80

    if (el.scrollTop < 80 && messagesHasMore && !loadingEarlier) {
      prevScrollHeight.current = el.scrollHeight
      loadEarlierMessages()
    }
  }

  const handleSelect = useCallback(async (id: string) => {
    await selectSession(id)
    scrollToBottom()
  }, [selectSession])

  const handleDelete = useCallback(async (id: string) => {
    await removeSession(id)
    setDeleteSessionId(null)
  }, [removeSession])

  const handleSend = useCallback(() => {
    const content = inputText.trim()
    if (!content || chatting) return
    sendMessage({ content, attachments: pendingAttachments.length > 0 ? pendingAttachments : undefined })
    setInputText('')
    setPendingAttachments([])
  }, [inputText, chatting, pendingAttachments, sendMessage])

  const handleKeyDown = (e: React.KeyboardEvent<HTMLTextAreaElement>) => {
    if (e.key === 'Enter' && !e.shiftKey) {
      e.preventDefault()
      handleSend()
    }
  }

  const handleFileChange = async (e: React.ChangeEvent<HTMLInputElement>) => {
    const files = Array.from(e.target.files ?? [])
    const results = await Promise.all(files.map(async (file) => {
      const base64Data = await new Promise<string>((resolve, reject) => {
        const reader = new FileReader()
        reader.onload = () => {
          const result = reader.result as string
          resolve(result.split(',')[1] ?? '')
        }
        reader.onerror = reject
        reader.readAsDataURL(file)
      })
      return { fileName: file.name, mimeType: file.type, base64Data } satisfies MessageAttachment
    }))
    setPendingAttachments((prev) => [...prev, ...results])
    e.target.value = ''
  }

  const handlePaste = useCallback(async (e: React.ClipboardEvent<HTMLTextAreaElement>) => {
    const items = Array.from(e.clipboardData?.items ?? [])
    const imageItems = items.filter((item) => item.type.startsWith('image/'))
    if (imageItems.length === 0) return

    e.preventDefault()
    const results = await Promise.all(imageItems.map(async (item) => {
      const file = item.getAsFile()
      if (!file) return null
      const base64Data = await new Promise<string>((resolve, reject) => {
        const reader = new FileReader()
        reader.onload = () => {
          const result = reader.result as string
          resolve(result.split(',')[1] ?? '')
        }
        reader.onerror = reject
        reader.readAsDataURL(file)
      })
      const ext = file.type.split('/')[1] ?? 'png'
      return { fileName: `image.${ext}`, mimeType: file.type, base64Data } satisfies MessageAttachment
    }))
    const valid = results.filter((r): r is MessageAttachment => r !== null)
    if (valid.length > 0) {
      setPendingAttachments((prev) => [...prev, ...valid])
    }
  }, [])

  const handleApprovalUpdated = useCallback((_session: SessionInfo) => {
    fetchSessions()
  }, [fetchSessions])

  const handleSwitchProvider = async (providerId: string) => {
    const id = store.currentSessionId
    if (!id) return
    try {
      await switchSessionProvider(id, providerId)
      await fetchSessions()
    } catch (err) {
      toaster.create({ type: 'error', title: '切换模型失败', description: String(err) })
    }
  }

  const displayMessages = messages.filter(isDisplayMessage)
  const currentSession = store.currentSession()
  const isReadOnly = currentSession ? isSubAgentSession(currentSession) : false
  const sessionTree = useMemo(() => buildSessionTree(sessions), [sessions])
  const enabledProviders = providers

  return (
    <Flex h="full" overflow="hidden">
      {/* ── 左侧 Session 列表 ── */}
      <Box
        w="260px" flexShrink={0} borderRightWidth="1px"
        display="flex" flexDir="column" overflow="hidden"
      >
        {/* 列表头部 */}
        <Flex
          px="3" py="3" align="center" justify="space-between"
          borderBottomWidth="1px"
        >
          <Text fontWeight="semibold" fontSize="sm">会话列表</Text>
          <Button size="xs" variant="ghost" colorPalette="blue" onClick={() => setShowCreate(true)}>
            <Plus size={14} /> 添加
          </Button>
        </Flex>

        {/* 会话列表（树形） */}
        <Box flex="1" overflowY="auto" py="1">
          {sessions.length === 0 && (
            <Flex align="center" justify="center" h="full" flexDir="column" gap="2" color="var(--mc-text-muted)">
              <MessageCircle size={32} />
              <Text fontSize="sm">暂无会话</Text>
            </Flex>
          )}
          {sessionTree.map((node) => (
            <SessionTreeItem
              key={node.session.id}
              node={node}
              depth={0}
              activeId={store.currentSessionId}
              runningSessionIds={runningSessionIds}
              onSelect={handleSelect}
              onDelete={(id) => setDeleteSessionId(id)}
            />
          ))}
        </Box>
      </Box>

      {/* ── 右侧聊天区 ── */}
      <Flex flex="1" flexDir="column" overflow="hidden">
        {!store.currentSessionId ? (
          <Flex align="center" justify="center" flex="1" flexDir="column" gap="3" color="var(--mc-text-muted)">
            <MessageCircle size={48} />
            <Text>选择或创建一个会话开始对话</Text>
          </Flex>
        ) : (
          <Tabs.Root defaultValue="chat" flex="1" display="flex" flexDirection="column" overflow="hidden">
            <Tabs.List px="3" flexShrink={0} borderBottomWidth="1px">
              <Tabs.Trigger value="chat">💬 对话</Tabs.Trigger>
              <Tabs.Trigger value="dna">🧬 DNA</Tabs.Trigger>
              <Tabs.Trigger value="memory">🧠 记忆</Tabs.Trigger>
              <Tabs.Trigger value="pet">🐾 Pet</Tabs.Trigger>
              <Tabs.Trigger value="approval">✅ 审批</Tabs.Trigger>
            </Tabs.List>
            <Tabs.Content value="chat" flex="1" display="flex" flexDirection="column" overflow="hidden" p="0">
            {/* 聊天头部 */}
            <Flex
              px="4" py="2.5" borderBottomWidth="1px"
              align="center" justify="space-between" flexShrink={0}
            >
              <Flex align="center" gap="2">
                <MessageCircle size={16} />
                <Text fontWeight="medium" fontSize="sm">{currentSession?.title}</Text>
              </Flex>
              <Flex align="center" gap="2">
                <Select.Root
                  value={currentSession ? [currentSession.providerId] : []}
                  onValueChange={(v) => handleSwitchProvider(v.value[0] ?? '')}
                  disabled={chatting || isReadOnly}
                  collection={createListCollection({
                    items: enabledProviders.map((p) => ({ value: p.id, label: `${p.displayName} (${p.modelName})` })),
                  })}
                  size="sm"
                >
                  <Select.Trigger w="220px">
                    <Select.ValueText placeholder="切换模型" />
                  </Select.Trigger>
                  <Portal>
                    <Select.Positioner>
                      <Select.Content>
                        {enabledProviders.map((p) => (
                          <Select.Item key={p.id} item={{ value: p.id, label: `${p.displayName} (${p.modelName})` }}>
                            {p.displayName} ({p.modelName})
                          </Select.Item>
                        ))}
                      </Select.Content>
                    </Select.Positioner>
                  </Portal>
                </Select.Root>
                <IconButton
                  size="sm"
                  variant={showSandbox ? 'solid' : 'ghost'}
                  colorPalette={showSandbox ? 'blue' : undefined}
                  aria-label="沙盒文件"
                  title="沙盒文件"
                  onClick={() => setShowSandbox((v) => !v)}
                >
                  <HardDrive size={15} />
                </IconButton>
              </Flex>
            </Flex>

            {/* 消息列表 + 沙盒面板 */}
            <Flex flex="1" overflow="hidden">
            {/* 消息列表 */}
            <Box
              ref={messagesEl}
              flex="1" overflowY="auto" px="2" py="3"
              onScroll={handleMessagesScroll}
            >
              {loading ? (
                <Flex align="center" justify="center" py="8">
                  <Spinner />
                </Flex>
              ) : (
                <>
                  {/* 加载更早 */}
                  {(messagesHasMore || loadingEarlier) && (
                    <Flex justify="center" py="2">
                      {loadingEarlier ? (
                        <Spinner size="sm" />
                      ) : (
                        <Button
                          size="xs" variant="ghost" colorPalette="blue"
                          onClick={() => {
                            prevScrollHeight.current = messagesEl.current?.scrollHeight ?? 0
                            loadEarlierMessages()
                          }}
                        >
                          <ChevronUp size={12} /> 加载更早消息
                        </Button>
                      )}
                    </Flex>
                  )}

                  {/* 历史消息 */}
                  {displayMessages.map((msg, idx) => {
                    // 查找配对的结果消息
                    let resultMessage: typeof msg | undefined
                    if (msg.messageType === 'tool_call') {
                      const callId = msg.metadata?.callId
                      resultMessage = callId
                        ? displayMessages.find(m => m.messageType === 'tool_result' && m.metadata?.callId === callId)
                        : undefined
                    } else if (msg.messageType === 'sub_agent_start') {
                      const runId = msg.metadata?.runId as string | undefined
                      const agentId = msg.metadata?.agentId as string | undefined
                      resultMessage = runId
                        ? displayMessages.find(m => m.messageType === 'sub_agent_result' && m.metadata?.runId === runId)
                        : agentId
                          ? displayMessages.find(m => m.messageType === 'sub_agent_result' && m.metadata?.agentId === agentId)
                          : undefined
                    }
                    const subAgentRunId = msg.messageType === 'sub_agent_start'
                      ? (msg.metadata?.runId as string | undefined)
                      : undefined
                    const agentId = msg.messageType === 'sub_agent_start'
                      ? (msg.metadata?.agentId as string | undefined)
                        : undefined
                    const steps = subAgentRunId
                      ? subAgentProgress[subAgentRunId]
                      : agentId
                        ? subAgentProgress[agentId]
                        : undefined
                    return (
                      <ChatMessage
                        key={idx}
                        message={msg}
                        resultMessage={resultMessage}
                        isStreaming={chatting && idx === displayMessages.length - 1}
                        progressSteps={steps}
                      />
                    )
                  })}

                  {/* 流式输出中的消息 */}
                  {chatting && streamingContent && (
                    <ChatMessage
                      message={{
                        role: 'assistant',
                        content: streamingContent,
                        timestamp: new Date().toISOString(),
                      }}
                      isStreaming
                    />
                  )}

                  {/* AI 正在工作指示器（工具调用/子代理执行期间无 token 流时显示） */}
                  {chatting && !streamingContent && (
                    <Flex align="center" gap="2" px="4" py="3" mb="2">
                      <Flex gap="1" align="center">
                        <Box
                          w="6px" h="6px" borderRadius="full" bg="var(--mc-info)"
                          animation="bounce 1.4s infinite ease-in-out"
                          css={{ animationDelay: '0s' }}
                        />
                        <Box
                          w="6px" h="6px" borderRadius="full" bg="var(--mc-info)"
                          animation="bounce 1.4s infinite ease-in-out"
                          css={{ animationDelay: '0.2s' }}
                        />
                        <Box
                          w="6px" h="6px" borderRadius="full" bg="var(--mc-info)"
                          animation="bounce 1.4s infinite ease-in-out"
                          css={{ animationDelay: '0.4s' }}
                        />
                      </Flex>
                      <Text fontSize="xs" color="var(--mc-text-muted)">AI 正在思考…</Text>
                    </Flex>
                  )}
                </>
              )}
            </Box>

            {/* 沙盒文件面板 */}
            {showSandbox && store.currentSessionId && (
              <SandboxPanel
                sessionId={store.currentSessionId}
                onClose={() => setShowSandbox(false)}
              />
            )}
            </Flex>

            {/* 输入区 */}
            {isReadOnly ? (
              <Flex
                borderTopWidth="1px" px="4" py="3" flexShrink={0}
                align="center" justify="center" gap="2"
                color="var(--mc-text-muted)" bg="var(--mc-surface-muted)"
              >
                <EyeOff size={14} />
                <Text fontSize="sm">子代理会话，仅供查看</Text>
              </Flex>
            ) : (
              <Box borderTopWidth="1px" p="3" flexShrink={0}>
                {/* 附件预览 */}
                {pendingAttachments.length > 0 && (
                  <Flex gap="2" mb="2" flexWrap="wrap">
                    {pendingAttachments.map((att, i) => (
                      <Flex
                        key={i} align="center" gap="1"
                        px="2" py="1" borderRadius="md"
                        bg="var(--mc-surface-muted)"
                        fontSize="xs"
                      >
                        {att.mimeType.startsWith('image/') ? (
                          <img
                            src={`data:${att.mimeType};base64,${att.base64Data}`}
                            alt={att.fileName}
                            style={{ height: 32, borderRadius: 4 }}
                          />
                        ) : (
                          <><Paperclip size={10} />{att.fileName}</>
                        )}
                        <IconButton
                          size="2xs" variant="ghost" aria-label="移除附件"
                          onClick={() => setPendingAttachments((prev) => prev.filter((_, j) => j !== i))}
                        >
                          <X size={10} />
                        </IconButton>
                      </Flex>
                    ))}
                  </Flex>
                )}

                <Flex gap="2" align="flex-end">
                  {/* 附件按钮 */}
                  <input
                    ref={fileInputRef}
                    type="file"
                    multiple
                    accept="image/*,application/pdf,.txt,.md,.json,.csv"
                    style={{ display: 'none' }}
                    onChange={handleFileChange}
                  />
                  <IconButton
                    size="sm" variant="ghost" aria-label="添加附件"
                    onClick={() => fileInputRef.current?.click()}
                    disabled={chatting}
                  >
                    <Paperclip size={16} />
                  </IconButton>

                  {/* 输入框 */}
                  <Textarea
                    ref={textareaRef}
                    flex="1"
                    size="sm"
                    minH="60px"
                    maxH="200px"
                    resize="none"
                    placeholder="输入消息… (Enter 发送，Shift+Enter 换行)"
                    value={inputText}
                    onChange={(e) => setInputText(e.target.value)}
                    onKeyDown={handleKeyDown}
                    onPaste={handlePaste}
                    disabled={chatting}
                  />

                  {/* 停止 / 发送按钮 */}
                  {chatting ? (
                    <IconButton
                      size="sm" colorPalette="red" aria-label="停止"
                      onClick={stopChat}
                    >
                      <Square size={16} />
                    </IconButton>
                  ) : (
                    <IconButton
                      size="sm" colorPalette="blue" aria-label="发送"
                      disabled={!inputText.trim()}
                      onClick={handleSend}
                    >
                      <Send size={16} />
                    </IconButton>
                  )}
                </Flex>
              </Box>
            )}
            </Tabs.Content>
            <Tabs.Content value="dna" flex="1" overflow="hidden" p="0">
              <SessionDnaTab session={currentSession!} />
            </Tabs.Content>
            <Tabs.Content value="memory" flex="1" overflow="hidden" p="0">
              <SessionMemoryTab session={currentSession!} />
            </Tabs.Content>
            <Tabs.Content value="pet" flex="1" overflow="hidden" p="0">
              <PetTab session={currentSession!} />
            </Tabs.Content>
            <Tabs.Content value="approval" p="0">
              <ApprovalTab session={currentSession!} onUpdated={handleApprovalUpdated} />
            </Tabs.Content>
          </Tabs.Root>
        )}
      </Flex>

      {/* ── 新建会话弹窗 ── */}
      <CreateSessionDialog
        open={showCreate}
        onClose={() => setShowCreate(false)}
        providers={providers}
        channels={channels}
        agents={agents}
        onCreated={(id) => handleSelect(id)}
      />

      <ConfirmDialog
        open={!!deleteSessionId}
        onClose={() => setDeleteSessionId(null)}
        onConfirm={() => deleteSessionId && handleDelete(deleteSessionId)}
        title="删除会话"
        description="确认删除此会话？"
        confirmText="删除"
      />
    </Flex>
  )
}
