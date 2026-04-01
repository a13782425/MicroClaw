/**
 * EdgeConfigDialog — 点击画布连线后弹出的条件配置面板。
 * 支持编辑连线的 condition（路由条件）和 label（显示标签）。
 */
import { useState, useEffect } from 'react'
import {
  Box,
  Button,
  Dialog,
  HStack,
  Input,
  Text,
  Textarea,
  VStack,
} from '@chakra-ui/react'
import type { WorkflowEdgeConfig } from '@/api/gateway'

interface EdgeConfigDialogProps {
  edge: WorkflowEdgeConfig | null
  onClose: () => void
  onSave: (updated: WorkflowEdgeConfig) => void
}

export function EdgeConfigDialog({ edge, onClose, onSave }: EdgeConfigDialogProps) {
  const [condition, setCondition] = useState('')
  const [label, setLabel] = useState('')

  useEffect(() => {
    if (!edge) return
    setCondition(edge.condition ?? '')
    setLabel(edge.label ?? '')
  }, [edge])

  const handleSave = () => {
    if (!edge) return
    onSave({
      ...edge,
      condition: condition.trim() || null,
      label: label.trim() || null,
    })
    onClose()
  }

  return (
    <Dialog.Root
      open={!!edge}
      onOpenChange={(e) => { if (!e.open) onClose() }}
      size="sm"
    >
      <Dialog.Backdrop />
      <Dialog.Positioner>
        <Dialog.Content
          maxW="420px"
          bg="white"
          _dark={{ bg: 'gray.900', borderColor: 'gray.700' }}
          borderWidth="1px"
          borderColor="gray.200"
        >
          <Dialog.Header
            borderBottomWidth="1px"
            borderColor="gray.200"
            _dark={{ borderColor: 'gray.700' }}
            pb="3"
          >
            <Dialog.Title color="gray.900" _dark={{ color: 'gray.100' }} fontSize="md">
              连线属性
            </Dialog.Title>
          </Dialog.Header>
          <Dialog.Body pt="4">
            <VStack gap="4" align="stretch">
              {/* 路由条件 */}
              <Box>
                <Text
                  fontSize="xs"
                  color="gray.600"
                  _dark={{ color: 'gray.400' }}
                  mb="1"
                  fontWeight="medium"
                >
                  路由条件
                </Text>
                <Textarea
                  size="sm"
                  value={condition}
                  onChange={(e) => setCondition(e.target.value)}
                  placeholder="例如：status == 'success' 或自然语言描述分支条件"
                  rows={3}
                  bg="gray.50"
                  _dark={{ bg: 'gray.800', borderColor: 'gray.600', color: 'gray.100' }}
                  borderColor="gray.300"
                  color="gray.900"
                  fontFamily="mono"
                  fontSize="sm"
                />
                <Text fontSize="xs" color="gray.400" _dark={{ color: 'gray.600' }} mt="1">
                  Router 节点出口条件；空则为无条件连接
                </Text>
              </Box>

              {/* 显示标签 */}
              <Box>
                <Text
                  fontSize="xs"
                  color="gray.600"
                  _dark={{ color: 'gray.400' }}
                  mb="1"
                  fontWeight="medium"
                >
                  显示标签
                </Text>
                <Input
                  size="sm"
                  value={label}
                  onChange={(e) => setLabel(e.target.value)}
                  placeholder="连线上显示的简短说明（可选，留空则显示条件）"
                  bg="gray.50"
                  _dark={{ bg: 'gray.800', borderColor: 'gray.600', color: 'gray.100' }}
                  borderColor="gray.300"
                  color="gray.900"
                />
              </Box>

              {/* 连线路径（只读） */}
              {edge && (
                <Box>
                  <Text fontSize="xs" color="gray.400" _dark={{ color: 'gray.600' }} fontFamily="mono">
                    {edge.sourceNodeId} → {edge.targetNodeId}
                  </Text>
                </Box>
              )}
            </VStack>
          </Dialog.Body>
          <Dialog.Footer
            borderTopWidth="1px"
            borderColor="gray.200"
            _dark={{ borderColor: 'gray.700' }}
            pt="3"
          >
            <HStack gap="2" justify="flex-end">
              <Button size="sm" variant="outline" onClick={onClose} colorPalette="gray">
                取消
              </Button>
              <Button size="sm" colorPalette="blue" onClick={handleSave}>
                保存
              </Button>
            </HStack>
          </Dialog.Footer>
          <Dialog.CloseTrigger />
        </Dialog.Content>
      </Dialog.Positioner>
    </Dialog.Root>
  )
}
