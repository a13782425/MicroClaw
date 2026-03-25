import { Button, Text } from '@chakra-ui/react'
import { AppDialog } from '@/components/ui/app-dialog'

interface ConfirmDialogProps {
  open: boolean
  onClose: () => void
  onConfirm: () => void
  title?: string
  description: string
  confirmText?: string
  cancelText?: string
  colorPalette?: string
  loading?: boolean
}

export function ConfirmDialog({
  open,
  onClose,
  onConfirm,
  title = '确认操作',
  description,
  confirmText = '确认',
  cancelText = '取消',
  colorPalette = 'red',
  loading = false,
}: ConfirmDialogProps) {
  return (
    <AppDialog
      open={open}
      onClose={onClose}
      title={title}
      contentProps={{ maxW: '400px' }}
      footer={(
        <>
          <Button variant="ghost" onClick={onClose} disabled={loading}>
            {cancelText}
          </Button>
          <Button colorPalette={colorPalette} onClick={onConfirm} loading={loading}>
            {confirmText}
          </Button>
        </>
      )}
    >
      <Text>{description}</Text>
    </AppDialog>
  )
}
