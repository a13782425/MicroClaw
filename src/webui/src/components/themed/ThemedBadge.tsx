import { Box, type BoxProps } from '@chakra-ui/react'
import type { ReactNode } from 'react'

export type ThemedBadgeSemantic = 'success' | 'warning' | 'danger' | 'info'

interface ThemedBadgeProps extends Omit<BoxProps, 'color'> {
  semantic?: ThemedBadgeSemantic
  children: ReactNode
}

const semanticVars: Record<ThemedBadgeSemantic, { bg: string; color: string }> = {
  success: { bg: 'var(--mc-success-soft)', color: 'var(--mc-success)' },
  warning: { bg: 'var(--mc-warning-soft)', color: 'var(--mc-warning)' },
  danger:  { bg: 'var(--mc-danger-soft)',  color: 'var(--mc-danger)' },
  info:    { bg: 'var(--mc-info-soft)',     color: 'var(--mc-info)' },
}

export function ThemedBadge({
  semantic = 'info',
  children,
  ...rest
}: ThemedBadgeProps) {
  const { bg, color } = semanticVars[semantic]

  return (
    <Box
      as="span"
      display="inline-flex"
      alignItems="center"
      px="2"
      py="0.5"
      fontSize="xs"
      fontWeight="medium"
      borderRadius="var(--mc-badge-radius)"
      bg={bg}
      color={color}
      {...rest}
    >
      {children}
    </Box>
  )
}
