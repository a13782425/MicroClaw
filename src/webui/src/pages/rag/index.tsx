import { Box, Text, Tabs } from '@chakra-ui/react'
import { Database, FileText, BarChart2, Settings } from 'lucide-react'
import { GlobalKnowledgeTab } from './global-knowledge-tab'
import { SessionKnowledgeTab } from './session-knowledge-tab'
import { RagStatsTab } from './rag-stats-tab'
import { RagSettingsTab } from './rag-settings-tab'

export default function RagPage() {
  return (
    <Box p="6">
      <Box mb="6">
        <Text fontSize="xl" fontWeight="bold">RAG 知识库</Text>
        <Text color="gray.500" fontSize="sm" mt="1">
          管理全局文档、会话索引、检索统计和自动遗忘配置
        </Text>
      </Box>

      <Tabs.Root defaultValue="global">
        <Tabs.List mb="4" flexWrap="wrap">
          <Tabs.Trigger value="global"><FileText size={14} /> 全局知识库</Tabs.Trigger>
          <Tabs.Trigger value="session"><Database size={14} /> 会话知识库</Tabs.Trigger>
          <Tabs.Trigger value="stats"><BarChart2 size={14} /> 检索统计</Tabs.Trigger>
          <Tabs.Trigger value="settings"><Settings size={14} /> 配置</Tabs.Trigger>
        </Tabs.List>

        <Tabs.Content value="global">
          <GlobalKnowledgeTab />
        </Tabs.Content>
        <Tabs.Content value="session">
          <SessionKnowledgeTab />
        </Tabs.Content>
        <Tabs.Content value="stats">
          <RagStatsTab />
        </Tabs.Content>
        <Tabs.Content value="settings">
          <RagSettingsTab />
        </Tabs.Content>
      </Tabs.Root>
    </Box>
  )
}
