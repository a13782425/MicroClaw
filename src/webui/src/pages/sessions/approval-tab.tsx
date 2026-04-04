import { useState } from 'react'
import {
  Box, Text, Badge, Button, HStack, VStack,
} from '@chakra-ui/react'
import { Check, Ban } from 'lucide-react'
import { toaster } from '@/components/ui/toaster'
import {
  approveSession,
  disableSession,
  type SessionInfo,
} from '@/api/gateway'

interface ApprovalTabProps {
  session: SessionInfo
  onUpdated: (session: SessionInfo) => void
}

export function ApprovalTab({ session, onUpdated }: ApprovalTabProps) {
  const [loading, setLoading] = useState(false)

  const approve = async () => {
    setLoading(true)
    try {
      const updated = await approveSession(session.id)
      onUpdated(updated)
      toaster.create({ type: 'success', title: '会话已批准' })
    } catch {
      toaster.create({ type: 'error', title: '操作失败' })
    } finally {
      setLoading(false)
    }
  }

  const disable = async () => {
    setLoading(true)
    try {
      const updated = await disableSession(session.id)
      onUpdated(updated)
      toaster.create({ type: 'success', title: '会话已禁用' })
    } catch {
      toaster.create({ type: 'error', title: '操作失败' })
    } finally {
      setLoading(false)
    }
  }

  return (
    <VStack align="start" p="4" gap="4">
      <HStack>
        <Text fontSize="sm" color="var(--mc-text-muted)">当前状态：</Text>
        {session.isApproved
          ? <Badge colorPalette="green" size="md">已批准</Badge>
          : <Badge colorPalette="orange" size="md">待审批</Badge>
        }
      </HStack>
      {session.approvalReason && (
        <Box>
          <Text fontSize="xs" color="var(--mc-text-muted)">审批原因：{session.approvalReason}</Text>
        </Box>
      )}
      <HStack>
        {!session.isApproved ? (
          <Button
            size="sm"
            colorPalette="green"
            loading={loading}
            onClick={approve}
          >
            <Check size={14} />
            批准此会话
          </Button>
        ) : (
          <Button
            size="sm"
            colorPalette="orange"
            variant="outline"
            loading={loading}
            onClick={disable}
          >
            <Ban size={14} />
            禁用此会话
          </Button>
        )}
      </HStack>
    </VStack>
  )
}
