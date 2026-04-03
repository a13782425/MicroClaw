import { Box, type BoxProps } from '@chakra-ui/react'
import type { ReactNode } from 'react'

interface ThemedSurfaceProps extends BoxProps {
  children: ReactNode
}

export function ThemedSurface({ children, ...rest }: ThemedSurfaceProps) {
  return (
    <Box
      bg="var(--mc-bg)"
      borderWidth="1px"
      borderColor="var(--mc-border)"
      borderRadius="var(--mc-card-radius)"
      {...rest}
    >
      {children}
    </Box>
  )
}
