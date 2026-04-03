import { Box, type BoxProps } from '@chakra-ui/react'
import type { ReactNode } from 'react'

export type ThemedCardVariant = 'default' | 'status' | 'stat'

interface ThemedCardProps extends Omit<BoxProps, 'variant'> {
  variant?: ThemedCardVariant
  /** status variant：左边框颜色，如 'var(--mc-success)' */
  statusColor?: string
  children: ReactNode
}

export function ThemedCard({
  variant = 'default',
  statusColor,
  children,
  ...rest
}: ThemedCardProps) {
  const base: BoxProps = {
    bg: 'var(--mc-card)',
    borderWidth: 'var(--mc-card-border-width)',
    borderColor: 'var(--mc-border)',
    borderRadius: 'var(--mc-card-radius)',
    boxShadow: 'var(--mc-card-shadow)',
    p: 'var(--mc-card-padding)',
  }

  if (variant === 'status') {
    return (
      <Box
        {...base}
        borderLeftWidth="4px"
        borderLeftColor={statusColor ?? 'var(--mc-primary)'}
        {...rest}
      >
        {children}
      </Box>
    )
  }

  if (variant === 'stat') {
    return (
      <Box
        {...base}
        textAlign="center"
        {...rest}
      >
        {children}
      </Box>
    )
  }

  return (
    <Box {...base} {...rest}>
      {children}
    </Box>
  )
}
