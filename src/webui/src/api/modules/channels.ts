import request from '../request'
import type { ChannelType } from './shared'

export type { ChannelType } from './shared'

export type FeishuChannelSettings = {
  appId: string
  appSecret: string
  encryptKey: string
  verificationToken: string
  connectionMode: 'websocket' | 'webhook'
  apiBaseUrl?: string
  allowedDocTokens?: string
  allowedBitableTokens?: string
  allowedWikiSpaceIds?: string
}

export type ChannelConfig = {
  id: string
  displayName: string
  channelType: ChannelType
  isEnabled: boolean
  settings: string
}

export type ChannelCreateRequest = {
  displayName: string
  channelType: ChannelType
  isEnabled: boolean
  settings?: string
}

export type ChannelUpdateRequest = {
  id: string
  displayName?: string
  channelType?: ChannelType
  isEnabled: boolean
  settings?: string
}

export type ChannelTestResult = {
  success: boolean
  message: string
  latencyMs: number
  connectivityHint?: string
}

export type ChannelPublishRequest = {
  targetId: string
  content: string
}

export type ChannelStats = {
  channelId: string
  signatureFailures: number
  aiCallFailures: number
  replyFailures: number
}

export type ChannelHealth = {
  channelId: string
  connectionMode: string
  connectionStatus: string
  tokenRemainingSeconds: number | null
  lastMessageAt: string | null
  lastMessageSuccess: boolean | null
  lastMessageError: string | null
}

export type ChannelTypeInfo = {
  type: string
  displayName: string
  canCreate: boolean
}

export type ChannelToolInfo = {
  name: string
  description: string
}

export async function listChannels(): Promise<ChannelConfig[]> {
  const { data } = await request.get<ChannelConfig[]>('/api/channels')
  return data
}

export async function createChannel(req: ChannelCreateRequest): Promise<{ id: string }> {
  const { data } = await request.post<{ id: string }>('/api/channels', req)
  return data
}

export async function updateChannel(req: ChannelUpdateRequest): Promise<{ id: string }> {
  const { data } = await request.post<{ id: string }>('/api/channels/update', req)
  return data
}

export async function deleteChannel(id: string): Promise<void> {
  await request.post('/api/channels/delete', { id })
}

export async function testChannel(id: string): Promise<ChannelTestResult> {
  const { data } = await request.post<ChannelTestResult>(`/api/channels/${id}/test`)
  return data
}

export async function publishChannelMessage(id: string, req: ChannelPublishRequest): Promise<void> {
  await request.post(`/api/channels/${id}/publish`, req)
}

export async function getChannelStats(id: string): Promise<ChannelStats> {
  const { data } = await request.get<ChannelStats>(`/api/channels/${id}/stats`)
  return data
}

export async function getChannelHealth(id: string): Promise<ChannelHealth> {
  const { data } = await request.get<ChannelHealth>(`/api/channels/${id}/health`)
  return data
}

export async function getChannelTypes(): Promise<ChannelTypeInfo[]> {
  const { data } = await request.get<ChannelTypeInfo[]>('/api/channels/types')
  return data
}

export async function getChannelTools(channelType: string): Promise<ChannelToolInfo[]> {
  const { data } = await request.get<ChannelToolInfo[]>(`/api/channels/${channelType}/tools`)
  return data
}