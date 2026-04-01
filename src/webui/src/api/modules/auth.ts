import request from '../request'

export async function login(username: string, password: string) {
  const { data } = await request.post('/api/auth/login', { username, password })
  return data as {
    token: string
    username: string
    role: string
    expiresAtUtc: string
  }
}