import { useEffect, useRef, useState } from 'react'
import { Box, Text, Flex } from '@chakra-ui/react'
import { ChevronDown, ChevronRight } from 'lucide-react'
import type { SessionMessage } from '@/api/gateway'
import ToolCallBlock from './ToolCallBlock'
import SubAgentBlock from './SubAgentBlock'

let mermaidCounter = 0

function escapeHtml(content: string): string {
  return content
    .replaceAll('&', '&amp;')
    .replaceAll('<', '&lt;')
    .replaceAll('>', '&gt;')
    .replaceAll('"', '&quot;')
    .replaceAll("'", '&#39;')
}

function createFallbackHtml(content: string): string {
  return escapeHtml(content).replaceAll('\n', '<br />')
}

interface MarkdownRuntime {
  renderHtml: (content: string) => string
}

let markdownRuntimePromise: Promise<MarkdownRuntime> | null = null
let mermaidRuntimePromise: Promise<((container: HTMLElement) => Promise<void>)> | null = null

function getMarkdownRuntime(): Promise<MarkdownRuntime> {
  if (markdownRuntimePromise) {
    return markdownRuntimePromise
  }

  markdownRuntimePromise = Promise.all([
    import('marked'),
    import('highlight.js/lib/common'),
    import('dompurify'),
  ]).then(([markedModule, highlightModule, domPurifyModule]) => {
    const { marked } = markedModule
    const highlight = highlightModule.default
    const DOMPurify = domPurifyModule.default

    marked.use({
      async: false,
      renderer: {
        code({ text, lang }) {
          if (lang === 'mermaid') {
            return `<div class="mermaid-block" data-graph="${encodeURIComponent(text)}"></div>`
          }

          const validLang = lang && highlight.getLanguage(lang) ? lang : 'plaintext'
          const highlighted = highlight.highlight(text, { language: validLang }).value
          return `<pre><code class="hljs language-${validLang}">${highlighted}</code></pre>`
        },
      },
    })

    return {
      renderHtml(content: string) {
        return DOMPurify.sanitize(marked.parse(content) as string)
      },
    }
  }).catch((error) => {
    markdownRuntimePromise = null
    throw error
  })

  return markdownRuntimePromise
}

function getMermaidRuntime(): Promise<(container: HTMLElement) => Promise<void>> {
  if (mermaidRuntimePromise) {
    return mermaidRuntimePromise
  }

  mermaidRuntimePromise = Promise.all([
    import('dompurify'),
    import('mermaid/dist/mermaid.core.mjs'),
  ]).then(([domPurifyModule, mermaidModule]) => {
    const DOMPurify = domPurifyModule.default
    const mermaid = mermaidModule.default

    mermaid.initialize({ startOnLoad: false, theme: 'default', securityLevel: 'loose' })

    return async (container: HTMLElement) => {
      const blocks = container.querySelectorAll<HTMLElement>('.mermaid-block')

      for (const block of blocks) {
        const graph = decodeURIComponent(block.getAttribute('data-graph') ?? '')
        if (!graph) {
          continue
        }

        try {
          const id = `mermaid-${++mermaidCounter}`
          const { svg } = await mermaid.render(id, graph)
          block.innerHTML = DOMPurify.sanitize(svg, { FORCE_BODY: true })
        } catch {
          block.innerHTML = '<pre style="color:red">Mermaid 渲染失败</pre>'
        }
      }
    }
  }).catch((error) => {
    mermaidRuntimePromise = null
    throw error
  })

  return mermaidRuntimePromise
}

interface ChatMessageProps {
  message: SessionMessage
  isStreaming?: boolean
  /** 配对的结果消息（tool_result / sub_agent_result） */
  resultMessage?: SessionMessage | null
  /** 子代理进度步骤 */
  progressSteps?: string[]
}

function ThinkBlock({ content }: { content: string }) {
  const [open, setOpen] = useState(false)
  return (
    <Box
      borderWidth="1px"
      borderRadius="md"
      borderColor="purple.200"
      _dark={{ borderColor: 'purple.700' }}
      mb="2"
      overflow="hidden"
    >
      <Flex
        align="center"
        gap="1"
        px="3"
        py="1"
        bg="purple.50"
        _dark={{ bg: 'purple.900' }}
        cursor="pointer"
        onClick={() => setOpen(!open)}
        userSelect="none"
      >
        {open ? <ChevronDown size={14} /> : <ChevronRight size={14} />}
        <Text fontSize="xs" color="purple.600" _dark={{ color: 'purple.300' }}>
          思考过程
        </Text>
      </Flex>
      {open && (
        <Box
          px="3"
          py="2"
          fontSize="xs"
          color="gray.600"
          _dark={{ color: 'gray.400', bg: 'purple.950' }}
          whiteSpace="pre-wrap"
          fontFamily="mono"
          bg="purple.50"
        >
          {content}
        </Box>
      )}
    </Box>
  )
}

function MarkdownContent({ content }: { content: string }) {
  const ref = useRef<HTMLDivElement>(null)
  const [html, setHtml] = useState(() => createFallbackHtml(content))
  const [runtime, setRuntime] = useState<MarkdownRuntime | null>(null)

  useEffect(() => {
    let cancelled = false

    setHtml(createFallbackHtml(content))

    void getMarkdownRuntime()
      .then((nextRuntime) => {
        if (cancelled) {
          return
        }

        setRuntime(nextRuntime)
        setHtml(nextRuntime.renderHtml(content))
      })
      .catch(() => {
        if (!cancelled) {
          setRuntime(null)
          setHtml(createFallbackHtml(content))
        }
      })

    return () => {
      cancelled = true
    }
  }, [content])

  useEffect(() => {
    if (!ref.current || !runtime) {
      return
    }

    const hasMermaidBlocks = ref.current.querySelector('.mermaid-block')
    if (!hasMermaidBlocks) {
      return
    }

    let cancelled = false

    void getMermaidRuntime()
      .then(async (renderMermaidBlocks) => {
        if (cancelled || !ref.current) {
          return
        }

        await renderMermaidBlocks(ref.current)
      })
      .catch(() => {
        if (!cancelled && ref.current) {
          const blocks = ref.current.querySelectorAll<HTMLElement>('.mermaid-block')
          for (const block of blocks) {
            block.innerHTML = '<pre style="color:red">Mermaid 加载失败</pre>'
          }
        }
      })

    return () => {
      cancelled = true
    }
  }, [html, runtime])

  return (
    <Box
      ref={ref}
      className="md-content"
      fontSize="sm"
      lineHeight="1.7"
      dangerouslySetInnerHTML={{ __html: html }}
      css={{
        '& p': { marginBottom: '0.5em' },
        '& p:last-child': { marginBottom: 0 },
        '& pre': { borderRadius: '6px', overflow: 'auto', padding: '12px', background: '#1e1e1e', color: '#d4d4d4', fontSize: '0.85em', marginBottom: '0.5em' },
        '& code:not(pre > code)': { background: 'rgba(0,0,0,0.08)', borderRadius: '3px', padding: '1px 4px', fontSize: '0.85em', fontFamily: 'monospace' },
        '& ul, & ol': { paddingLeft: '1.5em', marginBottom: '0.5em' },
        '& blockquote': { borderLeft: '3px solid', borderColor: 'gray.300', paddingLeft: '0.75em', color: 'gray.500', marginBottom: '0.5em' },
        '& table': { borderCollapse: 'collapse', width: '100%', marginBottom: '0.5em' },
        '& th, & td': { border: '1px solid', borderColor: 'gray.200', padding: '4px 8px' },
        '& a': { color: 'blue.400', textDecoration: 'underline' },
        '& h1, & h2, & h3': { fontWeight: 'bold', marginBottom: '0.3em' },
      }}
    />
  )
}

const fadeInUp = {
  animation: 'fadeInUp 0.25s ease-out',
  '@keyframes fadeInUp': {
    from: { opacity: 0, transform: 'translateY(8px)' },
    to: { opacity: 1, transform: 'translateY(0)' },
  },
}

export default function ChatMessage({ message, isStreaming, resultMessage, progressSteps }: ChatMessageProps) {
  const isUser = message.role === 'user'
  const contentRef = useRef<HTMLDivElement>(null)

  // tool_result / sub_agent_result 由配对的 start 消息渲染，不单独显示
  if (message.messageType === 'tool_result' || message.messageType === 'sub_agent_result') {
    return null
  }

  // 工具调用
  if (message.messageType === 'tool_call') {
    return (
      <Flex justify="flex-start" mb="3" px="2" css={fadeInUp}>
        <ToolCallBlock message={message} resultMessage={resultMessage} />
      </Flex>
    )
  }

  // 子代理
  if (message.messageType === 'sub_agent_start') {
    return (
      <Flex justify="flex-start" mb="3" px="2" css={fadeInUp}>
        <SubAgentBlock message={message} resultMessage={resultMessage} progressSteps={progressSteps} />
      </Flex>
    )
  }

  return (
    <Flex
      justify={isUser ? 'flex-end' : 'flex-start'}
      mb="3"
      px="2"
      css={fadeInUp}
    >
      <Box
        maxW="80%"
        bg={isUser ? 'blue.500' : 'white'}
        color={isUser ? 'white' : 'gray.800'}
        _dark={{
          bg: isUser ? 'blue.600' : 'gray.700',
          color: isUser ? 'white' : 'gray.100',
        }}
        borderRadius="lg"
        px="4"
        py="3"
        boxShadow="sm"
      >
        {/* Think block */}
        {message.thinkContent && (
          <ThinkBlock content={message.thinkContent} />
        )}

        {/* Main content */}
        <Box ref={contentRef}>
          {isUser ? (
            <Text fontSize="sm" whiteSpace="pre-wrap" wordBreak="break-word">
              {message.content}
            </Text>
          ) : (
            <MarkdownContent content={message.content} />
          )}
        </Box>

        {/* Streaming cursor */}
        {isStreaming && (
          <Box
            display="inline-block"
            w="2px"
            h="14px"
            bg="current"
            ml="1"
            animation="blink 1s step-end infinite"
            verticalAlign="text-bottom"
          />
        )}

        {/* Attachments */}
        {message.attachments && message.attachments.length > 0 && (
          <Flex mt="2" gap="2" flexWrap="wrap">
            {message.attachments.map((att, i) => (
              att.mimeType?.startsWith('image/') ? (
                <Box key={i} borderRadius="md" overflow="hidden" maxW="200px">
                  <img
                    src={`data:${att.mimeType};base64,${att.base64Data}`}
                    alt={att.fileName}
                    style={{ maxWidth: '100%', display: 'block' }}
                  />
                </Box>
              ) : (
                <Flex
                  key={i}
                  align="center"
                  gap="1"
                  px="2"
                  py="1"
                  borderRadius="md"
                  bg="blackAlpha.100"
                  fontSize="xs"
                >
                  📎 {att.fileName}
                </Flex>
              )
            ))}
          </Flex>
        )}
      </Box>
    </Flex>
  )
}
