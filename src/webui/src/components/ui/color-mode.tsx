'use client'

import { useTheme } from 'next-themes'
import type { ThemeProviderProps } from 'next-themes'
import { ThemeProvider } from 'next-themes'

export type ColorModeProviderProps = ThemeProviderProps

export function ColorModeProvider({ children, ...props }: ColorModeProviderProps) {
  return (
    // MicroClaw 使用自定义 --mc-* 主题系统管理亮/暗色，禁止 OS 偏好影响 Chakra 内置 token
    <ThemeProvider attribute="class" disableTransitionOnChange defaultTheme="light" enableSystem={false} {...props}>
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
