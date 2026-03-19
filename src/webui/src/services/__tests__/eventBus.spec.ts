import { describe, it, expect, vi, beforeEach } from 'vitest'
import { eventBus } from '../eventBus'

describe('eventBus', () => {
  beforeEach(() => {
    // 清理所有监听器（通过新建一个 handler 然后 off 的方式无法全量清理，
    // 但每个测试用独立的 handler 不会互相干扰）
  })

  it('on + emit 触发回调', () => {
    const handler = vi.fn()
    eventBus.on('test:event', handler)

    eventBus.emit('test:event', { id: '1' })

    expect(handler).toHaveBeenCalledOnce()
    expect(handler).toHaveBeenCalledWith({ id: '1' })

    eventBus.off('test:event', handler)
  })

  it('emit 多个参数', () => {
    const handler = vi.fn()
    eventBus.on('test:multi', handler)

    eventBus.emit('test:multi', 'a', 'b', 'c')

    expect(handler).toHaveBeenCalledWith('a', 'b', 'c')

    eventBus.off('test:multi', handler)
  })

  it('off 取消后不再触发', () => {
    const handler = vi.fn()
    eventBus.on('test:off', handler)
    eventBus.off('test:off', handler)

    eventBus.emit('test:off')

    expect(handler).not.toHaveBeenCalled()
  })

  it('同一事件多个监听器都会触发', () => {
    const h1 = vi.fn()
    const h2 = vi.fn()
    eventBus.on('test:multi-handler', h1)
    eventBus.on('test:multi-handler', h2)

    eventBus.emit('test:multi-handler', 'payload')

    expect(h1).toHaveBeenCalledWith('payload')
    expect(h2).toHaveBeenCalledWith('payload')

    eventBus.off('test:multi-handler', h1)
    eventBus.off('test:multi-handler', h2)
  })

  it('未注册的事件 emit 不报错', () => {
    expect(() => eventBus.emit('nonexistent')).not.toThrow()
  })

  it('off 未注册的 handler 不报错', () => {
    expect(() => eventBus.off('whatever', vi.fn())).not.toThrow()
  })
})
