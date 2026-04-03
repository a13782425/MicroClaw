'use client'

import { ChakraProvider, createSystem, defaultConfig, defineConfig } from '@chakra-ui/react'
import type { ReactNode } from 'react'
import { ColorModeProvider } from './color-mode'
import { ThemeProvider } from '../../themes/ThemeContext'

const customConfig = defineConfig({
  theme: {
    semanticTokens: {
      colors: {
        border: {
          value: '{colors.gray.300}',
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
