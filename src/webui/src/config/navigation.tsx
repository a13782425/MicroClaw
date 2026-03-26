import {
  MessageSquare, Timer, BarChart2,
  Bot, Zap, Server, Database, Wrench,
  CpuIcon, Radio, ListTree, Settings, GitBranch, Bug,
} from 'lucide-react'

export interface MenuItem {
  path: string
  label: string
  icon: React.ReactNode
}

export interface MenuGroup {
  title: string
  items: MenuItem[]
}

export const MENU_GROUPS: MenuGroup[] = [
  {
    title: '功能',
    items: [
      { path: '/sessions', label: '对话', icon: <MessageSquare size={18} /> },
      { path: '/workflows', label: '工作流', icon: <GitBranch size={18} /> },
      { path: '/cron', label: '计划任务', icon: <Timer size={18} /> },
      { path: '/usage', label: '用量统计', icon: <BarChart2 size={18} /> },
      { path: '/dev', label: 'DevUI', icon: <Bug size={18} /> },
    ],
  },
  {
    title: '智能体',
    items: [
      { path: '/agents', label: 'Agent', icon: <Bot size={18} /> },
      { path: '/skills', label: '技能', icon: <Zap size={18} /> },
      { path: '/mcp', label: 'MCP 管理', icon: <Server size={18} /> },
      { path: '/rag', label: 'RAG 知识库', icon: <Database size={18} /> },
      { path: '/tools', label: '工具', icon: <Wrench size={18} /> },
    ],
  },
  {
    title: '系统',
    items: [
      { path: '/models', label: '模型', icon: <CpuIcon size={18} /> },
      { path: '/channels', label: '渠道', icon: <Radio size={18} /> },
      { path: '/session-manage', label: '会话管理', icon: <ListTree size={18} /> },
      { path: '/config', label: '系统配置', icon: <Settings size={18} /> },
    ],
  },
]
