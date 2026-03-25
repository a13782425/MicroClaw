import type { ComponentProps, ReactNode } from 'react'
import { Dialog, Portal } from '@chakra-ui/react'

interface AppDialogProps {
  open: boolean
  onClose: () => void
  title: ReactNode
  children: ReactNode
  footer?: ReactNode
  contentProps?: Omit<ComponentProps<typeof Dialog.Content>, 'children'>
  bodyProps?: Omit<ComponentProps<typeof Dialog.Body>, 'children'>
}

export function AppDialog({
  open,
  onClose,
  title,
  children,
  footer,
  contentProps,
  bodyProps,
}: AppDialogProps) {
  return (
    <Dialog.Root open={open} onOpenChange={(event) => { if (!event.open) onClose() }}>
      <Portal>
        <Dialog.Backdrop />
        <Dialog.Positioner>
          <Dialog.Content maxW="480px" maxH="86vh" display="flex" flexDirection="column" {...contentProps}>
            <Dialog.Header flexShrink={0}>
              <Dialog.Title>{title}</Dialog.Title>
            </Dialog.Header>
            <Dialog.Body flex="1" overflowY="auto" {...bodyProps}>
              {children}
            </Dialog.Body>
            {footer && <Dialog.Footer gap="2" flexShrink={0}>{footer}</Dialog.Footer>}
          </Dialog.Content>
        </Dialog.Positioner>
      </Portal>
    </Dialog.Root>
  )
}