import { Box, type BoxProps } from '@chakra-ui/react'

export type ThemedStatusDotStatus = 'online' | 'warning' | 'offline' | 'error'

interface ThemedStatusDotProps extends Omit<BoxProps, 'color'> {
  status: ThemedStatusDotStatus
}

const statusVars: Record<ThemedStatusDotStatus, string> = {
  online:  'var(--mc-success)',
  warning: 'var(--mc-warning)',
  offline: 'var(--mc-text-muted)',
  error:   'var(--mc-danger)',
}

export function ThemedStatusDot({ status, ...rest }: ThemedStatusDotProps) {
  return (
    <Box
      w="7px"
      h="7px"
      borderRadius="full"
      bg={statusVars[status]}
      flexShrink={0}
      {...rest}
    />
  )
}
