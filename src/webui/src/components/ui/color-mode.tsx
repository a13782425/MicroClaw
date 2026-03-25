'use client'

import { useTheme } from 'next-themes'
import type { ThemeProviderProps } from 'next-themes'
import { ThemeProvider } from 'next-themes'

export type ColorModeProviderProps = ThemeProviderProps

export function ColorModeProvider({ children, ...props }: ColorModeProviderProps) {
  return (
    <ThemeProvider attribute="class" disableTransitionOnChange {...props}>
      {children}
    </ThemeProvider>
  )
}

export type ColorMode = 'light' | 'dark'

export function useColorMode() {
  const { resolvedTheme, setTheme } = useTheme()
  const colorMode = (resolvedTheme ?? 'light') as ColorMode
  return {
    colorMode,
    setColorMode: (mode: ColorMode) => setTheme(mode),
    toggleColorMode: () => setTheme(resolvedTheme === 'dark' ? 'light' : 'dark'),
  }
}

export function useColorModeValue<T>(light: T, dark: T): T {
  const { colorMode } = useColorMode()
  return colorMode === 'dark' ? dark : light
}
