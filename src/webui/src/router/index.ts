import { createRouter, createWebHistory } from 'vue-router'
import { useAuthStore } from '@/stores/auth'

const LoginPage     = () => import('@/views/LoginPage.vue')
const SessionsPage  = () => import('@/views/SessionsPage.vue')
const CronPage      = () => import('@/views/CronPage.vue')
const ModelsPage    = () => import('@/views/ModelsPage.vue')
const ChannelsPage  = () => import('@/views/ChannelsPage.vue')
const SessionManagePage = () => import('@/views/SessionManagePage.vue')
const AgentsPage        = () => import('@/views/AgentsPage.vue')
const SkillsPage    = () => import('@/views/SkillsPage.vue')
const UsagePage     = () => import('@/views/UsagePage.vue')
const ConfigPage    = () => import('@/views/ConfigPage.vue')
const DnaPage       = () => import('@/views/DnaPage.vue')
const McpPage       = () => import('@/views/McpPage.vue')
const RagPage       = () => import('@/views/RagPage.vue')
const ToolsPage     = () => import('@/views/ToolsPage.vue')

export const router = createRouter({
  history: createWebHistory(),
  routes: [
    { path: '/', redirect: '/sessions' },
    { path: '/login', name: 'login', component: LoginPage },
    { path: '/sessions', name: 'sessions', component: SessionsPage },
    { path: '/cron', name: 'cron', component: CronPage },
    { path: '/usage', name: 'usage', component: UsagePage },
    { path: '/models', name: 'models', component: ModelsPage },
    { path: '/channels', name: 'channels', component: ChannelsPage },
    { path: '/session-manage', name: 'session-manage', component: SessionManagePage },
    { path: '/agents', name: 'agents', component: AgentsPage },
    { path: '/skills', name: 'skills', component: SkillsPage },
    { path: '/config', name: 'config', component: ConfigPage },
    { path: '/dna', name: 'dna', component: DnaPage },
    { path: '/mcp', name: 'mcp', component: McpPage },
    { path: '/rag', name: 'rag', component: RagPage },
    { path: '/tools', name: 'tools', component: ToolsPage },
  ]
})

router.beforeEach((to) => {
  const auth = useAuthStore()
  if (to.name !== 'login' && !auth.isLoggedIn) return { name: 'login' }
  if (to.name === 'login' && auth.isLoggedIn) return { name: 'sessions' }
})
