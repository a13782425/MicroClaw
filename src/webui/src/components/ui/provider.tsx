'use client'

import { ChakraProvider, createSystem, defaultConfig, defineConfig } from '@chakra-ui/react'
import type { ReactNode } from 'react'
import { ColorModeProvider } from './color-mode'
import { ThemeProvider } from '../../themes/ThemeContext'

const customConfig = defineConfig({
  // 全局 CSS：重置 Chakra Table 行背景，使其继承父容器的 --mc-* 颜色
  globalCss: {
    '::selection': {
      background: 'var(--mc-selected-bg)',
      color: 'var(--mc-text)',
    },
    'table': { color: 'var(--mc-text) !important' },
    'table thead tr': { background: 'transparent !important' },
    'table tbody tr': { background: 'transparent !important' },
    'table tbody tr:hover': { background: 'var(--mc-card-hover) !important' },
    'table th': {
      borderColor: 'var(--mc-border) !important',
      color: 'var(--mc-text-muted) !important',
    },
    'table td': {
      borderColor: 'var(--mc-border) !important',
      color: 'var(--mc-text) !important',
    },
    '[data-mc-refresh="true"]': {
      color: 'var(--mc-text) !important',
      borderColor: 'var(--mc-border) !important',
      background: 'transparent !important',
    },
    '[data-mc-refresh="true"]:hover': {
      background: 'var(--mc-card-hover) !important',
    },
  },
  theme: {
    semanticTokens: {
      colors: {
        // 边框 token
        border: {
          value: { base: 'var(--mc-border)', _dark: 'var(--mc-border)' },
        },
        // 背景 token
        bg: {
          subtle: {
            value: { base: 'var(--mc-input)', _dark: 'var(--mc-input)' },
          },
          muted: {
            value: { base: 'var(--mc-card-hover)', _dark: 'var(--mc-card-hover)' },
          },
        },
        // 前景文字 token
        fg: {
          muted: {
            value: { base: 'var(--mc-text-muted)', _dark: 'var(--mc-text-muted)' },
          },
        },
      },
    },
  },
})

const system = createSystem(defaultConfig, customConfig)

export interface ProviderProps {
  children?: ReactNode
}

export function Provider({ children }: ProviderProps) {
  return (
    <ChakraProvider value={system}>
      <ColorModeProvider>
        <ThemeProvider>
          {children}
        </ThemeProvider>
      </ColorModeProvider>
    </ChakraProvider>
  )
}
