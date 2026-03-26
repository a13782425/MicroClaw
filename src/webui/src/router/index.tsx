import { Suspense, lazy } from 'react'
import { createBrowserRouter, Navigate } from 'react-router-dom'
import { useAuthStore } from '@/store/authStore'

const AppLayout = lazy(() => import('@/components/layout/AppLayout'))
const LoginPage = lazy(() => import('@/pages/login'))
const SessionsPage = lazy(() => import('@/pages/sessions'))
const AgentsPage = lazy(() => import('@/pages/agents'))
const SkillsPage = lazy(() => import('@/pages/skills'))
const McpPage = lazy(() => import('@/pages/mcp'))
const RagPage = lazy(() => import('@/pages/rag'))
const ToolsPage = lazy(() => import('@/pages/tools'))
const ModelsPage = lazy(() => import('@/pages/models'))
const ChannelsPage = lazy(() => import('@/pages/channels'))
const SessionManagePage = lazy(() => import('@/pages/session-manage'))
const CronPage = lazy(() => import('@/pages/cron'))
const UsagePage = lazy(() => import('@/pages/usage'))
const ConfigPage = lazy(() => import('@/pages/config'))

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
