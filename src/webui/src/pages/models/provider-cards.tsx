import { Card, Flex, Text, Badge, Switch, Button } from '@chakra-ui/react'
import { Cpu, Link as LinkIcon, Edit, Trash2 } from 'lucide-react'
import type { ProviderConfig } from '@/api/gateway'
import {
  latencyTierColor,
  latencyTierLabel,
  protocolLabel,
} from './model-form-helpers'

export function ChatCard({ p, onEdit, onDelete, onToggle, onSetDefault }: {
  p: ProviderConfig
  onEdit: (provider: ProviderConfig) => void
  onDelete: (provider: ProviderConfig) => void
  onToggle: (provider: ProviderConfig, enabled: boolean) => void
  onSetDefault: (provider: ProviderConfig) => void
}) {
  return (
    <Card.Root opacity={p.isEnabled ? 1 : 0.6} borderWidth="1px" variant="outline">
      <Card.Body p="4">
        <Flex align="center" gap="2" mb="2">
          <Text fontWeight="semibold" flex="1" truncate>{p.displayName}</Text>
          {p.isDefault && <Badge colorPalette="yellow" size="sm">默认</Badge>}
          <Badge colorPalette={p.protocol === 'openai' ? 'blue' : 'purple'} size="sm">
            {protocolLabel(p.protocol)}
          </Badge>
        </Flex>

        <Flex align="center" gap="1" color="gray.600" _dark={{ color: 'gray.400' }} fontSize="sm" mb="1">
          <Cpu size={13} />
          <Text>{p.modelName}</Text>
        </Flex>
        <Text fontSize="xs" color="gray.400" mb="1">
          最大输出 {p.maxOutputTokens.toLocaleString()} tokens
        </Text>

        {p.baseUrl && (
          <Flex align="center" gap="1" color="gray.500" fontSize="xs" mb="2">
            <LinkIcon size={11} />
            <Text truncate>{p.baseUrl}</Text>
          </Flex>
        )}

        <Flex gap="1" flexWrap="wrap" mb="3">
          {p.capabilities?.inputImage && <Badge size="sm" colorPalette="cyan" variant="subtle">图片输入</Badge>}
          {p.capabilities?.inputAudio && <Badge size="sm" colorPalette="teal" variant="subtle">音频输入</Badge>}
          {p.capabilities?.inputVideo && <Badge size="sm" colorPalette="blue" variant="subtle">视频输入</Badge>}
          {p.capabilities?.inputFile && <Badge size="sm" colorPalette="gray" variant="subtle">文件</Badge>}
          {p.capabilities?.outputImage && <Badge size="sm" colorPalette="cyan" variant="outline">图片输出</Badge>}
          {p.capabilities?.outputAudio && <Badge size="sm" colorPalette="teal" variant="outline">音频输出</Badge>}
          {p.capabilities?.outputVideo && <Badge size="sm" colorPalette="blue" variant="outline">视频输出</Badge>}
          {p.capabilities?.supportsFunctionCalling && <Badge size="sm" colorPalette="orange" variant="subtle">Functions</Badge>}
          {p.capabilities?.supportsResponsesApi && <Badge size="sm" colorPalette="green" variant="subtle">Responses</Badge>}
          {(p.capabilities?.inputPricePerMToken || p.capabilities?.outputPricePerMToken) && (
            <Badge size="sm" variant="outline" colorPalette="purple">
              ${p.capabilities.inputPricePerMToken ?? '?'}/{p.capabilities.outputPricePerMToken ?? '?'}/M
            </Badge>
          )}
        </Flex>

        <Flex gap="2" mb="3" align="center">
          <Text fontSize="xs" color="gray.400">路由:</Text>
          <Badge size="sm" colorPalette="violet" variant="subtle">质量 {p.capabilities?.qualityScore ?? 50}</Badge>
          <Badge size="sm" colorPalette={latencyTierColor(p.capabilities?.latencyTier ?? 'Medium')} variant="subtle">
            {latencyTierLabel(p.capabilities?.latencyTier ?? 'Medium')}延迟
          </Badge>
        </Flex>

        <Flex align="center" justify="space-between">
          <Switch.Root size="sm" checked={p.isEnabled} onCheckedChange={(details) => onToggle(p, details.checked)}>
            <Switch.HiddenInput />
            <Switch.Control><Switch.Thumb /></Switch.Control>
            <Switch.Label fontSize="xs">{p.isEnabled ? '启用' : '停用'}</Switch.Label>
          </Switch.Root>

          <Flex gap="1">
            <Button size="xs" variant="ghost" colorPalette="blue" onClick={() => onEdit(p)}>
              <Edit size={12} /> 编辑
            </Button>
            {!p.isDefault && (
              <Button size="xs" variant="ghost" colorPalette="yellow" onClick={() => onSetDefault(p)}>
                设默认
              </Button>
            )}
            <Button size="xs" variant="ghost" colorPalette="red" aria-label={`删除提供方 ${p.displayName}`} onClick={() => onDelete(p)}>
              <Trash2 size={12} />
            </Button>
          </Flex>
        </Flex>
      </Card.Body>
    </Card.Root>
  )
}

export function EmbeddingCard({ p, onEdit, onDelete, onToggle }: {
  p: ProviderConfig
  onEdit: (provider: ProviderConfig) => void
  onDelete: (provider: ProviderConfig) => void
  onToggle: (provider: ProviderConfig, enabled: boolean) => void
}) {
  return (
    <Card.Root opacity={p.isEnabled ? 1 : 0.6} borderWidth="1px" variant="outline">
      <Card.Body p="4">
        <Flex align="center" gap="2" mb="2">
          <Text fontWeight="semibold" flex="1" truncate>{p.displayName}</Text>
          <Badge colorPalette="purple" size="sm">嵌入</Badge>
          <Badge colorPalette="blue" size="sm">OpenAI</Badge>
        </Flex>

        <Flex align="center" gap="1" color="gray.600" _dark={{ color: 'gray.400' }} fontSize="sm" mb="2">
          <Cpu size={13} />
          <Text>{p.modelName}</Text>
        </Flex>

        {p.baseUrl && (
          <Flex align="center" gap="1" color="gray.500" fontSize="xs" mb="2">
            <LinkIcon size={11} />
            <Text truncate>{p.baseUrl}</Text>
          </Flex>
        )}

        <Flex gap="1" flexWrap="wrap" mb="3">
          {p.capabilities?.outputDimensions && (
            <Badge size="sm" colorPalette="purple" variant="subtle">维度 {p.capabilities.outputDimensions}</Badge>
          )}
          {p.capabilities?.maxInputTokens && (
            <Badge size="sm" colorPalette="blue" variant="subtle">输入 {p.capabilities.maxInputTokens.toLocaleString()} tokens</Badge>
          )}
          {p.capabilities?.inputPricePerMToken && (
            <Badge size="sm" variant="outline" colorPalette="green">${p.capabilities.inputPricePerMToken}/M</Badge>
          )}
        </Flex>

        <Flex align="center" justify="space-between">
          <Switch.Root size="sm" checked={p.isEnabled} onCheckedChange={(details) => onToggle(p, details.checked)}>
            <Switch.HiddenInput />
            <Switch.Control><Switch.Thumb /></Switch.Control>
            <Switch.Label fontSize="xs">{p.isEnabled ? '启用' : '停用'}</Switch.Label>
          </Switch.Root>

          <Flex gap="1">
            <Button size="xs" variant="ghost" colorPalette="blue" onClick={() => onEdit(p)}>
              <Edit size={12} /> 编辑
            </Button>
            <Button size="xs" variant="ghost" colorPalette="red" aria-label={`删除提供方 ${p.displayName}`} onClick={() => onDelete(p)}>
              <Trash2 size={12} />
            </Button>
          </Flex>
        </Flex>
      </Card.Body>
    </Card.Root>
  )
}
