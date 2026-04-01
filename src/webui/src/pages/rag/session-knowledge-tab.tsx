import { useState, useEffect, useCallback } from 'react'
import {
  Box, Text, HStack, Spinner, NativeSelect, Card, SimpleGrid, Separator, Button,
} from '@chakra-ui/react'
import { RefreshCw, MessageSquare, Clock, Database } from 'lucide-react'
import { toaster } from '@/components/ui/toaster'
import {
  listSessions,
  getSessionRagStatus,
  vectorizeSessionMessages,
  type SessionInfo,
  type SessionRagStatus,
} from '@/api/gateway'

export function SessionKnowledgeTab() {
  const [sessions, setSessions] = useState<SessionInfo[]>([])
  const [selectedSessionId, setSelectedSessionId] = useState<string>('')
  const [sessionsLoading, setSessionsLoading] = useState(false)
  const [status, setStatus] = useState<SessionRagStatus | null>(null)
  const [statusLoading, setStatusLoading] = useState(false)
  const [vectorizing, setVectorizing] = useState(false)

  useEffect(() => {
    setSessionsLoading(true)
    listSessions()
      .then((data) => {
        setSessions(data)
        if (data.length > 0) setSelectedSessionId(data[0].id)
      })
      .catch(() => toaster.create({ type: 'error', title: '加载会话列表失败' }))
      .finally(() => setSessionsLoading(false))
  }, [])

  const loadStatus = useCallback(async (sessionId: string) => {
    if (!sessionId) return
    setStatusLoading(true)
    try {
      const nextStatus = await getSessionRagStatus(sessionId)
      setStatus(nextStatus)
    } catch {
      toaster.create({ type: 'error', title: '加载索引状态失败' })
    } finally {
      setStatusLoading(false)
    }
  }, [])

  useEffect(() => {
    if (selectedSessionId) loadStatus(selectedSessionId)
    else setStatus(null)
  }, [selectedSessionId, loadStatus])

  const selectedSession = sessions.find((session) => session.id === selectedSessionId)

  return (
    <Box>
      <HStack mb="5" gap="3" align="center">
        <Text fontSize="sm" fontWeight="medium" whiteSpace="nowrap">选择会话：</Text>
        {sessionsLoading ? (
          <Spinner size="sm" />
        ) : sessions.length === 0 ? (
          <Text fontSize="sm" color="gray.400">暂无会话</Text>
        ) : (
          <NativeSelect.Root size="sm" maxW="400px" flex="1">
            <NativeSelect.Field
              value={selectedSessionId}
              onChange={(e) => setSelectedSessionId(e.target.value)}
            >
              {sessions.map((session) => (
                <option key={session.id} value={session.id}>
                  {session.title} ({session.channelType})
                </option>
              ))}
            </NativeSelect.Field>
            <NativeSelect.Indicator />
          </NativeSelect.Root>
        )}
        <Button
          size="sm"
          variant="outline"
          onClick={() => selectedSessionId && loadStatus(selectedSessionId)}
          loading={statusLoading}
          disabled={!selectedSessionId}
        >
          <RefreshCw size={14} />
          刷新
        </Button>
        <Button
          size="sm"
          variant="outline"
          colorPalette="blue"
          onClick={async () => {
            if (!selectedSessionId) return
            setVectorizing(true)
            try {
              const result = await vectorizeSessionMessages(selectedSessionId)
              toaster.create({ type: 'success', title: `已将 ${result.messageCount} 条消息写入待处理队列` })
              loadStatus(selectedSessionId)
            } catch {
              toaster.create({ type: 'error', title: '手动向量化失败' })
            } finally {
              setVectorizing(false)
            }
          }}
          loading={vectorizing}
          disabled={!selectedSessionId}
        >
          <Database size={14} />
          手动向量化
        </Button>
      </HStack>

      <Separator mb="5" />

      {!selectedSessionId ? (
        <Box py="16" textAlign="center" border="1px dashed" borderColor="gray.200" borderRadius="lg">
          <MessageSquare size={40} color="var(--chakra-colors-gray-300)" style={{ margin: '0 auto 12px' }} />
          <Text color="gray.500" fontWeight="medium">请先选择一个会话</Text>
          <Text fontSize="sm" color="gray.400" mt="1">选择会话后可查看其 RAG 索引状态</Text>
        </Box>
      ) : statusLoading ? (
        <Box py="12" textAlign="center">
          <Spinner size="lg" color="blue.500" />
          <Text mt="3" color="gray.500">加载中…</Text>
        </Box>
      ) : status ? (
        <Box>
          <Box mb="4">
            <Text fontSize="sm" fontWeight="semibold">{selectedSession?.title ?? selectedSessionId}</Text>
            <Text fontSize="xs" color="gray.400">
              渠道：{selectedSession?.channelType} · ID：{selectedSessionId.slice(0, 8)}…
            </Text>
          </Box>

          <SimpleGrid columns={2} gap="4">
            <Card.Root variant="outline">
              <Card.Body py="4" px="5">
                <HStack gap="3">
                  <Box color="blue.500"><MessageSquare size={22} /></Box>
                  <Box>
                    <Text fontSize="2xl" fontWeight="bold" lineHeight="1">
                      {status.categoryCount}
                    </Text>
                    <Text fontSize="xs" color="gray.500" mt="0.5">已建立分类数</Text>
                  </Box>
                </HStack>
              </Card.Body>
            </Card.Root>

            <Card.Root variant="outline">
              <Card.Body py="4" px="5">
                <HStack gap="3">
                  <Box color="green.500"><Clock size={22} /></Box>
                  <Box>
                    <Text fontSize="sm" fontWeight="semibold" lineHeight="1.2">
                      {status.lastUpdatedAtMs
                        ? new Date(status.lastUpdatedAtMs).toLocaleString('zh-CN', {
                            year: 'numeric', month: '2-digit', day: '2-digit',
                            hour: '2-digit', minute: '2-digit',
                          })
                        : '—'}
                    </Text>
                    <Text fontSize="xs" color="gray.500" mt="0.5">最近更新时间</Text>
                  </Box>
                </HStack>
              </Card.Body>
            </Card.Root>
          </SimpleGrid>

          {status.categoryCount === 0 && (
            <Box mt="4" p="4" bg="orange.50" borderRadius="md" border="1px solid" borderColor="orange.200">
              <Text fontSize="sm" color="orange.700">
                该会话尚无已索引的消息。对话结束后系统会自动索引。
              </Text>
            </Box>
          )}
        </Box>
      ) : null}
    </Box>
  )
}
