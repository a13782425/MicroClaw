import { Box, Text, HStack, Card } from '@chakra-ui/react'

export function UsageStatCard({ label, value, icon: Icon, color }: { label: string; value: string; icon: React.ElementType; color: string }) {
  return (
    <Card.Root>
      <Card.Body>
        <HStack justify="space-between">
          <Box>
            <Text fontSize="xs" color="var(--mc-text-muted)" mb="1">{label}</Text>
            <Text fontSize="xl" fontWeight="bold">{value}</Text>
          </Box>
          <Box color={color} opacity={0.8}><Icon size={28} /></Box>
        </HStack>
      </Card.Body>
    </Card.Root>
  )
}
