import type { Component } from 'vue'
import {
  Grid,
  Tools,
  ChatDotRound,
  Timer,
  Cpu,
  Setting,
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
    ],
  },
  {
    id: 'group-system',
    label: '系统',
    icon: Tools,
    items: [
      { label: '模型', icon: Cpu, route: '/models' },
      { label: '配置', icon: Setting, route: '/config' },
    ],
  },
]
