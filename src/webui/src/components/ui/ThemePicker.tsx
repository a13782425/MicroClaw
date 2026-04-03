import { Box, Flex, Text, Tooltip, Popover, Portal } from '@chakra-ui/react'
import { themeList } from '@/themes/presets'
import { useTheme } from '@/themes/ThemeContext'

export function ThemePicker() {
  const { themeId, setTheme } = useTheme()

  return (
    <Popover.Root positioning={{ placement: 'bottom-end' }}>
      <Popover.Trigger asChild>
        <Box
          as="button"
          p="1"
          borderRadius="md"
          cursor="pointer"
          title={`当前主题：${themeId}`}
          _hover={{ bg: 'var(--mc-card-hover, var(--mc-card))' }}
          aria-label="切换主题"
        >
          {/* 调色板图标（inline SVG，避免引入额外依赖） */}
          <svg
            width="16"
            height="16"
            viewBox="0 0 24 24"
            fill="none"
            stroke="currentColor"
            strokeWidth="2"
            strokeLinecap="round"
            strokeLinejoin="round"
          >
            <circle cx="13.5" cy="6.5" r=".5" fill="currentColor" />
            <circle cx="17.5" cy="10.5" r=".5" fill="currentColor" />
            <circle cx="8.5" cy="7.5" r=".5" fill="currentColor" />
            <circle cx="6.5" cy="12.5" r=".5" fill="currentColor" />
            <path d="M12 2C6.5 2 2 6.5 2 12s4.5 10 10 10c.926 0 1.648-.746 1.648-1.688 0-.437-.18-.835-.437-1.125-.29-.289-.438-.652-.438-1.125a1.64 1.64 0 0 1 1.668-1.668h1.996c3.051 0 5.555-2.503 5.555-5.554C21.965 6.012 17.461 2 12 2z" />
          </svg>
        </Box>
      </Popover.Trigger>

      <Portal>
        <Popover.Positioner>
          <Popover.Content
            bg="var(--mc-card)"
            borderColor="var(--mc-border)"
            borderRadius="var(--mc-card-radius)"
            boxShadow="var(--mc-card-shadow)"
            p="3"
            minW="0"
            w="auto"
          >
            <Popover.Arrow>
              <Popover.ArrowTip />
            </Popover.Arrow>
            <Text fontSize="xs" fontWeight="semibold" color="var(--mc-text-muted)" mb="2">
              界面主题
            </Text>
            <Flex gap="2" wrap="wrap" maxW="200px">
              {themeList.map((t) => {
                const isActive = t.id === themeId
                return (
                  <Tooltip.Root key={t.id} positioning={{ placement: 'top' }}>
                    <Tooltip.Trigger asChild>
                      <Box
                        as="button"
                        w="28px"
                        h="28px"
                        borderRadius="full"
                        bg={t.primary}
                        cursor="pointer"
                        outline={isActive ? `2px solid ${t.primary}` : '2px solid transparent'}
                        outlineOffset="2px"
                        border="2px solid"
                        borderColor={isActive ? 'var(--mc-card)' : 'transparent'}
                        transition="outline 0.15s, transform 0.15s"
                        _hover={{ transform: 'scale(1.15)' }}
                        onClick={() => setTheme(t.id)}
                        aria-label={t.label}
                        aria-pressed={isActive}
                      />
                    </Tooltip.Trigger>
                    <Tooltip.Content>{t.label}</Tooltip.Content>
                  </Tooltip.Root>
                )
              })}
            </Flex>
          </Popover.Content>
        </Popover.Positioner>
      </Portal>
    </Popover.Root>
  )
}
