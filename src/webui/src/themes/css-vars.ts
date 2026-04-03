import type { ThemeTokens } from './theme-tokens'

/**
 * Token 到 CSS 变量名的映射规则：
 * camelCase → kebab-case，前缀统一为 --mc-
 * 例如：primary → --mc-primary，cardRadius → --mc-card-radius
 */
function toKebabCase(str: string): string {
  return str.replace(/([A-Z])/g, (m) => `-${m.toLowerCase()}`)
}

/**
 * 将 ThemeTokens 中的所有 token 写入 document.documentElement 的 CSS 变量
 * 非颜色/非字符串类属性（id、label）跳过
 */
export function applyThemeVars(tokens: ThemeTokens): void {
  const root = document.documentElement
  const skip = new Set<keyof ThemeTokens>(['id', 'label'])

  for (const key in tokens) {
    if (skip.has(key as keyof ThemeTokens)) continue
    const cssVarName = `--mc-${toKebabCase(key)}`
    root.style.setProperty(cssVarName, tokens[key as keyof ThemeTokens] as string)
  }

  // 同步 body color/background，使 Chakra UI 默认 Text（无显式 color prop）
  // 能正确继承当前主题的文字色与背景色，而不是 Chakra 内置的 gray.800
  document.body.style.setProperty('color', 'var(--mc-text)')
  document.body.style.setProperty('background-color', 'var(--mc-bg)')
}

/**
 * 获取当前主题的某个 CSS 变量值（运行时读取，调试用）
 */
export function getThemeVar(token: keyof Omit<ThemeTokens, 'id' | 'label'>): string {
  return document.documentElement.style.getPropertyValue(`--mc-${toKebabCase(token)}`)
}
