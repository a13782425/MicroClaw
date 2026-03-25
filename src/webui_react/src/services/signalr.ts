import { HubConnectionBuilder, LogLevel, type HubConnection } from '@microsoft/signalr'
import { useAuthStore } from '@/store/authStore'
import { eventBus } from './eventBus'

class SignalRService {
  private connection: HubConnection | null = null

  start() {
    if (this.connection) return
    const token = useAuthStore.getState().token

    const conn = new HubConnectionBuilder()
      .withUrl('/ws/gateway', { accessTokenFactory: () => token })
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)
      .build()

    conn.on(
      'sessionPendingApproval',
      (payload: { sessionId: string; sessionTitle: string; channelType: string; timestamp: string }) => {
        eventBus.emit('session:pendingApproval', payload)
      },
    )

    conn.on(
      'sessionCreated',
      (payload: { sessionId: string; title: string; channelType: string }) => {
        eventBus.emit('session:created', payload)
      },
    )

    conn.on('sessionApproved', (payload: { sessionId: string; title: string }) => {
      eventBus.emit('session:approved', payload)
    })

    conn.on('sessionDisabled', (payload: { sessionId: string; title: string }) => {
      eventBus.emit('session:disabled', payload)
    })

    conn.on('cronJobExecuted', (payload: { jobId: string; jobName: string; success: boolean; message?: string }) => {
      eventBus.emit('cron:jobExecuted', payload)
    })

    conn.on(
      'agentStatus',
      (payload: { sessionId: string; agentId: string; status: 'running' | 'completed' | 'failed' }) => {
        eventBus.emit('agent:statusChanged', payload)
      },
    )

    conn.start().catch(() => {
      // silent - auto-reconnect handles this
    })

    this.connection = conn
  }

  stop() {
    this.connection?.stop()
    this.connection = null
  }
}

export const signalRService = new SignalRService()
