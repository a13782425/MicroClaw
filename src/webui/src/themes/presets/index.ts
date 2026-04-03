import type { ThemeTokens } from '../theme-tokens'
import { apple } from './apple'
import { cyberpunk } from './cyberpunk'
import { macaron } from './macaron'
import { morandi } from './morandi'
import { saas } from './saas'
import { tailwind } from './tailwind'

export const themes: Record<string, ThemeTokens> = {
  tailwind,
  apple,
  saas,
  morandi,
  macaron,
  cyberpunk,
}

export const themeList: ThemeTokens[] = Object.values(themes)

export { apple, cyberpunk, macaron, morandi, saas, tailwind }
