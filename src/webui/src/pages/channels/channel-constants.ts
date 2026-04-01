import { createListCollection } from '@chakra-ui/react'
import type { ChannelType } from '@/api/gateway'

export const TYPE_COLORS: Record<string, string> = {
  feishu: 'cyan', wecom: 'green', wechat: 'teal', web: 'blue',
}

export const TYPE_LABELS: Record<string, string> = {
  feishu: '飞书', wecom: '企业微信', wechat: '微信', web: 'Web',
}

export function parseSettings(settings: string): Record<string, string> {
  try { return JSON.parse(settings) ?? {} } catch { return {} }
}

export const CONNECTION_MODE_OPTIONS = [
  { value: 'websocket', label: 'WebSocket' },
  { value: 'webhook', label: 'Webhook' },
]

export const connectionModeCollection = createListCollection({ items: CONNECTION_MODE_OPTIONS })

export const CHANNEL_FIELDS: Record<ChannelType, { key: string; label: string; type?: string; required?: boolean; select?: true }[]> = {
  feishu: [
    { key: 'appId', label: 'App ID', required: true },
    { key: 'appSecret', label: 'App Secret', type: 'password', required: true },
    { key: 'encryptKey', label: 'Encrypt Key' },
    { key: 'verificationToken', label: 'Verification Token' },
    { key: 'connectionMode', label: '连接方式', select: true },
    { key: 'apiBaseUrl', label: 'API Base URL（可选）' },
  ],
  wecom: [
    { key: 'corpId', label: 'Corp ID' },
    { key: 'agentId', label: 'Agent ID' },
    { key: 'secret', label: 'Secret', type: 'password' },
  ],
  wechat: [
    { key: 'appId', label: 'App ID' },
    { key: 'appSecret', label: 'App Secret', type: 'password' },
    { key: 'token', label: 'Token' },
    { key: 'encodingAESKey', label: 'Encoding AES Key（可选）' },
  ],
  web: [
    { key: 'description', label: '描述' },
    { key: 'allowedOrigins', label: '允许来源（逗号分隔）' },
  ],
}
