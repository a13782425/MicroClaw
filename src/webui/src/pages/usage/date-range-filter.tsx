import { Text, Button, HStack } from '@chakra-ui/react'
import { RefreshCw } from 'lucide-react'
import { DateInput } from '@/components/ui/date-input'

interface DateRangeFilterProps {
  startDate: string
  endDate: string
  loading: boolean
  onStartDateChange: (value: string) => void
  onEndDateChange: (value: string) => void
  onSearch: () => void
}

export function DateRangeFilter({
  startDate,
  endDate,
  loading,
  onStartDateChange,
  onEndDateChange,
  onSearch,
}: DateRangeFilterProps) {
  return (
    <HStack>
      <DateInput
        ariaLabel="开始日期"
        value={startDate}
        onChange={onStartDateChange}
        placeholder="开始日期"
      />
      <Text fontSize="sm" color="var(--mc-text-muted)">—</Text>
      <DateInput
        ariaLabel="结束日期"
        value={endDate}
        onChange={onEndDateChange}
        placeholder="结束日期"
      />
      <Button size="sm" variant="outline" data-mc-refresh="true" loading={loading} onClick={onSearch}>
        <RefreshCw size={14} />查询
      </Button>
    </HStack>
  )
}
