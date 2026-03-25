import { Box, Text, Badge, Tabs } from '@chakra-ui/react'

/**
 * TODO: RAG 知识库页面
 *
 * Tabs：
 *   - Tab 1「全局知识库」
 *     - 文档列表（全局共享）
 *     - 上传文档 / 删除文档
 *     - 文档状态（indexing/ready/error）
 *   - Tab 2「会话知识库」
 *     - 选择 Session → 显示该 Session 的知识库文档
 *     - 上传 / 删除
 *
 * 注：RAG 功能在后端尚未完全实现（见 MicroClaw.Agent/Rag/）
 * 可先做 UI 框架，API 接口待后端就绪后接入
 *
 * 关键 API（参考 src/webui/src/services/gatewayApi.ts 中 rag 相关）：
 *   待后端实现后再接入
 */
export default function RagPage() {
  return (
    <Box p="6">
      <Badge colorPalette="orange" mb="4">TODO</Badge>
      <Text fontSize="lg" fontWeight="semibold">RAG 知识库</Text>
      <Text color="gray.500" mt="2">
        该功能尚在规划中，请参照上方 TODO 注释实现 UI 框架。
      </Text>
      <Tabs.Root mt="4" defaultValue="global">
        <Tabs.List>
          <Tabs.Trigger value="global">全局知识库</Tabs.Trigger>
          <Tabs.Trigger value="session">会话知识库</Tabs.Trigger>
        </Tabs.List>
        <Tabs.Content value="global">
          <Box p="4" color="gray.400">（待实现）</Box>
        </Tabs.Content>
        <Tabs.Content value="session">
          <Box p="4" color="gray.400">（待实现）</Box>
        </Tabs.Content>
      </Tabs.Root>
    </Box>
  )
}
