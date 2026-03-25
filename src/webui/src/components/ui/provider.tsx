'use client'

import { ChakraProvider, createSystem, defaultConfig, defineConfig } from '@chakra-ui/react'
import type { ReactNode } from 'react'
import { ColorModeProvider } from './color-mode'

const customConfig = defineConfig({
  theme: {
    semanticTokens: {
      colors: {
        border: {
          value: { _light: '{colors.gray.300}', _dark: '{colors.gray.600}' },
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
        {children}
      </ColorModeProvider>
    </ChakraProvider>
  )
}
