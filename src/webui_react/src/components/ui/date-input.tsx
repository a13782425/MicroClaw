import { DatePicker, Portal, parseDate } from '@chakra-ui/react'
import { Calendar } from 'lucide-react'

interface DateInputProps {
  value: string
  onChange: (value: string) => void
  placeholder?: string
  ariaLabel: string
  width?: string
  size?: 'xs' | 'sm' | 'md' | 'lg'
  disabled?: boolean
}

function toDatePickerValue(value: string) {
  return value ? [parseDate(value)] : []
}

export function DateInput({
  value,
  onChange,
  placeholder = '选择日期',
  ariaLabel,
  width = '160px',
  size = 'sm',
  disabled = false,
}: DateInputProps) {
  return (
    <DatePicker.Root
      closeOnSelect
      disabled={disabled}
      locale="zh-CN"
      size={size}
      value={toDatePickerValue(value)}
      onValueChange={(event) => onChange(event.value[0]?.toString() ?? '')}
      width={width}
    >
      <DatePicker.Control>
        <DatePicker.Input aria-label={ariaLabel} placeholder={placeholder} />
        <DatePicker.IndicatorGroup>
          <DatePicker.Trigger aria-label={`${ariaLabel}日历选择`}>
            <Calendar size={16} />
          </DatePicker.Trigger>
        </DatePicker.IndicatorGroup>
      </DatePicker.Control>
      <Portal>
        <DatePicker.Positioner>
          <DatePicker.Content>
            <DatePicker.View view="day">
              <DatePicker.Header />
              <DatePicker.DayTable />
            </DatePicker.View>
            <DatePicker.View view="month">
              <DatePicker.Header />
              <DatePicker.MonthTable />
            </DatePicker.View>
            <DatePicker.View view="year">
              <DatePicker.Header />
              <DatePicker.YearTable />
            </DatePicker.View>
          </DatePicker.Content>
        </DatePicker.Positioner>
      </Portal>
    </DatePicker.Root>
  )
}