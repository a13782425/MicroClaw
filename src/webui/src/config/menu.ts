import type { Component } from 'vue'
import {
  Grid,
  Tools,
  ChatDotRound,
  Timer,
  Cpu,
  Connection,
  DocumentChecked,
  Promotion,
  MagicStick,
  TrendCharts,
  Setting,
  Collection,
  Share,
  DataBoard,
} from '@element-plus/icons-vue'

export interface MenuItem {
  label: string
  icon: Component
  route: string
}

export interface MenuGroup {
  id: string
  label: string
  icon: Component
  items: MenuItem[]
}

export const menuGroups: MenuGroup[] = [
  {
    id: 'group-main',
    label: '功能',
    icon: Grid,
    items: [
      { label: '会话', icon: ChatDotRound, route: '/sessions' },
      { label: '计时任务', icon: Timer, route: '/cron' },
      { label: '用量统计', icon: TrendCharts, route: '/usage' },
    ],
  },
  {
    id: 'group-agent',
    label: '智能体',
    icon: Promotion,
    items: [
       { label: 'Agent', icon: Promotion, route: '/agents' },
       { label: '技能', icon: MagicStick, route: '/skills' },
       { label: 'MCP 管理', icon: Share, route: '/mcp' },
       { label: 'RAG 知识库', icon: DataBoard, route: '/rag' },
       { label: '工具', icon: Tools, route: '/tools' },
    ],
  },
  {
    id: 'group-system',
    label: '系统',
    icon: Tools,
    items: [
      { label: '模型', icon: Cpu, route: '/models' },
      { label: '渠道', icon: Connection, route: '/channels' },
      { label: '会话管理', icon: DocumentChecked, route: '/session-manage' },
      { label: '系统配置', icon: Setting, route: '/config' },
    ],
  },
]
