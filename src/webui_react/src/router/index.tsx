import { Suspense, lazy } from 'react'
import { createBrowserRouter, Navigate } from 'react-router-dom'
import { useAuthStore } from '@/store/authStore'

const AppLayout = lazy(() => import('@/components/layout/AppLayout'))
const LoginPage = lazy(() => import('@/pages/Login'))
const SessionsPage = lazy(() => import('@/pages/Sessions'))
const AgentsPage = lazy(() => import('@/pages/Agents'))
const SkillsPage = lazy(() => import('@/pages/Skills'))
const McpPage = lazy(() => import('@/pages/Mcp'))
const RagPage = lazy(() => import('@/pages/Rag'))
const ToolsPage = lazy(() => import('@/pages/Tools'))
const ModelsPage = lazy(() => import('@/pages/Models'))
const ChannelsPage = lazy(() => import('@/pages/Channels'))
const SessionManagePage = lazy(() => import('@/pages/SessionManage'))
const CronPage = lazy(() => import('@/pages/Cron'))
const UsagePage = lazy(() => import('@/pages/Usage'))
const ConfigPage = lazy(() => import('@/pages/Config'))

function RouteFallback() {
  return (
    <div
      style={{
        display: 'flex',
        minHeight: '40vh',
        alignItems: 'center',
        justifyContent: 'center',
        color: '#64748b',
        fontSize: '14px',
      }}
    >
      页面加载中...
    </div>
  )
}

function withSuspense(element: React.ReactNode) {
  return <Suspense fallback={<RouteFallback />}>{element}</Suspense>
}

function RequireAuth({ children }: { children: React.ReactNode }) {
  const isLoggedIn = useAuthStore((s) => s.isLoggedIn)
  if (!isLoggedIn) return <Navigate to="/login" replace />
  return <>{children}</>
}

function RedirectIfAuthed({ children }: { children: React.ReactNode }) {
  const isLoggedIn = useAuthStore((s) => s.isLoggedIn)
  if (isLoggedIn) return <Navigate to="/sessions" replace />
  return <>{children}</>
}

export const router = createBrowserRouter([
  {
    path: '/login',
    element: (
      <RedirectIfAuthed>
        {withSuspense(<LoginPage />)}
      </RedirectIfAuthed>
    ),
  },
  {
    path: '/',
    element: (
      <RequireAuth>
        {withSuspense(<AppLayout />)}
      </RequireAuth>
    ),
    children: [
      { index: true, element: <Navigate to="/sessions" replace /> },
      { path: 'sessions',       element: withSuspense(<SessionsPage />) },
      { path: 'agents',         element: withSuspense(<AgentsPage />) },
      { path: 'skills',         element: withSuspense(<SkillsPage />) },
      { path: 'mcp',            element: withSuspense(<McpPage />) },
      { path: 'rag',            element: withSuspense(<RagPage />) },
      { path: 'tools',          element: withSuspense(<ToolsPage />) },
      { path: 'models',         element: withSuspense(<ModelsPage />) },
      { path: 'channels',       element: withSuspense(<ChannelsPage />) },
      { path: 'session-manage', element: withSuspense(<SessionManagePage />) },
      { path: 'cron',           element: withSuspense(<CronPage />) },
      { path: 'usage',          element: withSuspense(<UsagePage />) },
      { path: 'config',         element: withSuspense(<ConfigPage />) },
      { path: 'dna',            element: <Navigate to="/session-manage" replace /> },
    ],
  },
])
