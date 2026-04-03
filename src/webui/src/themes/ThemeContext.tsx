import { createContext, useContext, useEffect, type ReactNode } from 'react'
import { applyThemeVars } from './css-vars'
import { themes } from './presets'
import type { ThemeTokens } from './theme-tokens'
import { useUIStore } from '../store/uiStore'

interface ThemeContextValue {
  theme: ThemeTokens
  themeId: string
  setTheme: (id: string) => void
}

const ThemeContext = createContext<ThemeContextValue>({
  theme: themes.tailwind,
  themeId: 'tailwind',
  setTheme: () => {},
})

export function ThemeProvider({ children }: { children: ReactNode }) {
  const themeId = useUIStore((s) => s.themeId)
  const setThemeId = useUIStore((s) => s.setThemeId)

  const theme = themes[themeId] ?? themes.tailwind

  useEffect(() => {
    applyThemeVars(theme)
  }, [theme])

  function setTheme(id: string) {
    if (themes[id]) {
      setThemeId(id)
    }
  }

  return (
    <ThemeContext.Provider value={{ theme, themeId, setTheme }}>
      {children}
    </ThemeContext.Provider>
  )
}

export function useTheme(): ThemeContextValue {
  return useContext(ThemeContext)
}
