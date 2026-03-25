import { describe, it, expect, vi, beforeEach } from 'vitest'
import { isDisplayMessage } from '@/store/sessionStore'
import type { SessionMessage } from '@/api/gateway'

// ─── isDisplayMessage 工具函数测试 ───────────────────────────────────────────
describe('isDisplayMessage', () => {
  it('应显示普通用户消息', () => {
    const msg: SessionMessage = {
      role: 'user',
      content: 'hello',
      timestamp: '2024-01-01T00:00:00Z',
    }
    expect(isDisplayMessage(msg)).toBe(true)
  })

  it('应显示 assistant 消息', () => {
    const msg: SessionMessage = {
      role: 'assistant',
      content: '你好',
      timestamp: '2024-01-01T00:00:00Z',
    }
    expect(isDisplayMessage(msg)).toBe(true)
  })

  it('应过滤 source=cron 的用户消息', () => {
    const msg: SessionMessage = {
      role: 'user',
      content: 'cron triggered',
      timestamp: '2024-01-01T00:00:00Z',
      source: 'cron',
    }
    expect(isDisplayMessage(msg)).toBe(false)
  })

  it('应过滤 source=skill 的用户消息', () => {
    const msg: SessionMessage = {
      role: 'user',
      content: 'skill triggered',
      timestamp: '2024-01-01T00:00:00Z',
      source: 'skill',
    }
    expect(isDisplayMessage(msg)).toBe(false)
  })

  it('应过滤 source=tool 的用户消息', () => {
    const msg: SessionMessage = {
      role: 'user',
      content: 'tool triggered',
      timestamp: '2024-01-01T00:00:00Z',
      source: 'tool',
    }
    expect(isDisplayMessage(msg)).toBe(false)
  })

  it('assistant 消息即使有 source 也应显示', () => {
    const msg: SessionMessage = {
      role: 'assistant',
      content: 'response',
      timestamp: '2024-01-01T00:00:00Z',
      source: 'cron',
    }
    expect(isDisplayMessage(msg)).toBe(true)
  })

  it('source=null 的用户消息应显示', () => {
    const msg: SessionMessage = {
      role: 'user',
      content: 'hello',
      timestamp: '2024-01-01T00:00:00Z',
      source: null,
    }
    expect(isDisplayMessage(msg)).toBe(true)
  })
})
