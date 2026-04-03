import { Input, type InputProps } from '@chakra-ui/react'

type ThemedInputProps = InputProps

export function ThemedInput(props: ThemedInputProps) {
  return (
    <Input
      bg="var(--mc-input)"
      borderColor="var(--mc-border)"
      borderRadius="var(--mc-input-radius)"
      color="var(--mc-text)"
      _placeholder={{ color: 'var(--mc-placeholder)' }}
      _focus={{
        borderColor: 'var(--mc-input-focus-border)',
        boxShadow: 'var(--mc-input-focus-shadow)',
        outline: 'none',
      }}
      {...props}
    />
  )
}
