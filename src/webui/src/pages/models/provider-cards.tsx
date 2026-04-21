import { Card, Flex, Text, Badge, Switch, Button } from '@chakra-ui/react'
import { Cpu, Link as LinkIcon, Edit, Trash2 } from 'lucide-react'
import type { ProviderConfig } from '@/api/gateway'
import {
  latencyTierBg,
  latencyTierFg,
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
    <Card.Root opacity={p.isEnabled ? 1 : 0.6} borderWidth="1px" variant="outline" bg="var(--mc-card)" borderColor="var(--mc-border)">
      <Card.Body p="4">
        <Flex align="center" gap="2" mb="2">
          <Text fontWeight="semibold" flex="1" truncate color="var(--mc-text)">{p.displayName}</Text>
          {p.isDefault && <Badge size="sm" bg="var(--mc-warning-soft)" color="var(--mc-warning)">默认</Badge>}
          <Badge size="sm" bg={p.protocol === 'openai' ? 'var(--mc-primary-soft)' : 'var(--mc-accent-soft)'} color={p.protocol === 'openai' ? 'var(--mc-primary)' : 'var(--mc-accent)'}>
            {protocolLabel(p.protocol)}
          </Badge>
        </Flex>

        <Flex align="center" gap="1" color="var(--mc-text-muted)" fontSize="sm" mb="1">
          <Cpu size={13} />
          <Text>{p.modelName}</Text>
        </Flex>
        <Text fontSize="xs" color="var(--mc-text-muted)" mb="1">
          最大输出 {p.maxOutputTokens.toLocaleString()} tokens
        </Text>

        {p.baseUrl && (
          <Flex align="center" gap="1" color="var(--mc-text-muted)" fontSize="xs" mb="2">
            <LinkIcon size={11} />
            <Text truncate>{p.baseUrl}</Text>
          </Flex>
        )}

        <Flex gap="1" flexWrap="wrap" mb="3">
          {p.capabilities?.inputs?.includes('Image') && <Badge size="sm" bg="var(--mc-info-soft)" color="var(--mc-info)">图片输入</Badge>}
          {p.capabilities?.inputs?.includes('Audio') && <Badge size="sm" bg="var(--mc-accent-soft)" color="var(--mc-accent)">音频输入</Badge>}
          {p.capabilities?.inputs?.includes('Video') && <Badge size="sm" bg="var(--mc-primary-soft)" color="var(--mc-primary)">视频输入</Badge>}
          {p.capabilities?.inputs?.includes('File') && <Badge size="sm" bg="var(--mc-card-hover)" color="var(--mc-text-muted)">文件</Badge>}
          {p.capabilities?.outputs?.includes('Image') && <Badge size="sm" variant="outline" borderColor="var(--mc-info)" color="var(--mc-info)">图片输出</Badge>}
          {p.capabilities?.outputs?.includes('Audio') && <Badge size="sm" variant="outline" borderColor="var(--mc-accent)" color="var(--mc-accent)">音频输出</Badge>}
          {p.capabilities?.outputs?.includes('Video') && <Badge size="sm" variant="outline" borderColor="var(--mc-primary)" color="var(--mc-primary)">视频输出</Badge>}
          {p.capabilities?.features?.includes('FunctionCalling') && <Badge size="sm" bg="var(--mc-warning-soft)" color="var(--mc-warning)">Functions</Badge>}
          {p.capabilities?.features?.includes('ResponsesApi') && <Badge size="sm" bg="var(--mc-success-soft)" color="var(--mc-success)">Responses</Badge>}
          {(p.capabilities?.inputPricePerMToken || p.capabilities?.outputPricePerMToken) && (
            <Badge size="sm" variant="outline" borderColor="var(--mc-accent)" color="var(--mc-accent)">
              ${p.capabilities.inputPricePerMToken ?? '?'}/{p.capabilities.outputPricePerMToken ?? '?'}/M
            </Badge>
          )}
        </Flex>

        <Flex gap="2" mb="3" align="center">
          <Text fontSize="xs" color="var(--mc-text-muted)">路由:</Text>
          <Badge size="sm" bg="var(--mc-accent-soft)" color="var(--mc-accent)">质量 {p.capabilities?.qualityScore ?? 50}</Badge>
          <Badge size="sm" bg={latencyTierBg(p.capabilities?.latencyTier ?? 'Medium')} color={latencyTierFg(p.capabilities?.latencyTier ?? 'Medium')}>
            {latencyTierLabel(p.capabilities?.latencyTier ?? 'Medium')}延迟
          </Badge>
        </Flex>

        <Flex align="center" justify="space-between">
          <Switch.Root size="sm" checked={p.isEnabled} onCheckedChange={(details) => onToggle(p, details.checked)}>
            <Switch.HiddenInput />
            <Switch.Control><Switch.Thumb /></Switch.Control>
            <Switch.Label fontSize="xs" color="var(--mc-text)">{p.isEnabled ? '启用' : '停用'}</Switch.Label>
          </Switch.Root>

          <Flex gap="1">
            <Button size="xs" variant="ghost" color="var(--mc-primary)" _hover={{ bg: 'var(--mc-primary-soft)' }} onClick={() => onEdit(p)}>
              <Edit size={12} /> 编辑
            </Button>
            {!p.isDefault && (
              <Button size="xs" variant="ghost" color="var(--mc-warning)" _hover={{ bg: 'var(--mc-warning-soft)' }} onClick={() => onSetDefault(p)}>
                设默认
              </Button>
            )}
            <Button size="xs" variant="ghost" color="var(--mc-danger)" _hover={{ bg: 'var(--mc-danger-soft)' }} aria-label={`删除提供方 ${p.displayName}`} onClick={() => onDelete(p)}>
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
    <Card.Root opacity={p.isEnabled ? 1 : 0.6} borderWidth="1px" variant="outline" bg="var(--mc-card)" borderColor="var(--mc-border)">
      <Card.Body p="4">
        <Flex align="center" gap="2" mb="2">
          <Text fontWeight="semibold" flex="1" truncate color="var(--mc-text)">{p.displayName}</Text>
          <Badge size="sm" bg="var(--mc-accent-soft)" color="var(--mc-accent)">嵌入</Badge>
          <Badge size="sm" bg="var(--mc-primary-soft)" color="var(--mc-primary)">OpenAI</Badge>
        </Flex>

        <Flex align="center" gap="1" color="var(--mc-text-muted)" fontSize="sm" mb="2">
          <Cpu size={13} />
          <Text>{p.modelName}</Text>
        </Flex>

        {p.baseUrl && (
          <Flex align="center" gap="1" color="var(--mc-text-muted)" fontSize="xs" mb="2">
            <LinkIcon size={11} />
            <Text truncate>{p.baseUrl}</Text>
          </Flex>
        )}

        <Flex gap="1" flexWrap="wrap" mb="3">
          {p.capabilities?.outputDimensions && (
            <Badge size="sm" bg="var(--mc-accent-soft)" color="var(--mc-accent)">维度 {p.capabilities.outputDimensions}</Badge>
          )}
          {p.capabilities?.maxInputTokens && (
            <Badge size="sm" bg="var(--mc-primary-soft)" color="var(--mc-primary)">输入 {p.capabilities.maxInputTokens.toLocaleString()} tokens</Badge>
          )}
          {p.capabilities?.inputPricePerMToken && (
            <Badge size="sm" variant="outline" borderColor="var(--mc-success)" color="var(--mc-success)">${p.capabilities.inputPricePerMToken}/M</Badge>
          )}
        </Flex>

        <Flex align="center" justify="space-between">
          <Switch.Root size="sm" checked={p.isEnabled} onCheckedChange={(details) => onToggle(p, details.checked)}>
            <Switch.HiddenInput />
            <Switch.Control><Switch.Thumb /></Switch.Control>
            <Switch.Label fontSize="xs" color="var(--mc-text)">{p.isEnabled ? '启用' : '停用'}</Switch.Label>
          </Switch.Root>

          <Flex gap="1">
            <Button size="xs" variant="ghost" color="var(--mc-primary)" _hover={{ bg: 'var(--mc-primary-soft)' }} onClick={() => onEdit(p)}>
              <Edit size={12} /> 编辑
            </Button>
            <Button size="xs" variant="ghost" color="var(--mc-danger)" _hover={{ bg: 'var(--mc-danger-soft)' }} aria-label={`删除提供方 ${p.displayName}`} onClick={() => onDelete(p)}>
              <Trash2 size={12} />
            </Button>
          </Flex>
        </Flex>
      </Card.Body>
    </Card.Root>
  )
}
