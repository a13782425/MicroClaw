import request from '../request'

export type GatewayHealth = {
  status: string
  service: string
  utcNow: string
  version: string
}

export async function getGatewayHealth(): Promise<GatewayHealth> {
  const { data } = await request.get<GatewayHealth>('/api/health')
  return data
}