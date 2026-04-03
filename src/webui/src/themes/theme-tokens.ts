export interface ThemeTokens {
  id: string
  label: string

  // 品牌色
  primary: string
  primarySoft: string
  secondary: string
  secondarySoft: string
  accent: string
  accentSoft: string

  // 语义色
  success: string
  successSoft: string
  warning: string
  warningSoft: string
  danger: string
  dangerSoft: string
  info: string
  infoSoft: string

  // 界面表面色
  bg: string
  card: string
  cardHover: string
  input: string

  // 文字色
  text: string
  textLight: string
  textMuted: string

  // 边框色
  border: string
  borderLight: string

  // 头像与图标色
  avatarBg: string
  avatarColor: string

  // 聊天消息气泡色
  userBubble: string
  userBubbleText: string
  aiBubbleBg: string
  aiBubbleBorder: string

  // 终端/控制台色
  terminalBg: string
  terminalGreen: string
  terminalBlue: string
  terminalYellow: string
  terminalPink: string
  terminalDim: string

  // 侧边栏色
  sidebarActive: string
  sidebarActiveBorder: string

  // 交互态色
  inputFocusBorder: string
  inputFocusShadow: string
  sendButtonBg: string
  sendButtonColor: string

  // 占位符与问候语
  placeholder: string
  greeting: string

  // 扩展 Token — AI 思考块
  thinkingBg: string
  thinkingBorder: string
  thinkingLabel: string

  // 扩展 Token — 工具调用块与子 Agent 块
  toolCallBg: string
  toolCallBorder: string
  subAgentBg: string
  subAgentBorder: string

  // 扩展 Token — 工作流节点色
  nodeStart: string
  nodeEnd: string
  nodeAgent: string
  nodeTool: string
  nodeRouter: string
  nodeFunction: string
  nodeSwitchModel: string

  // 扩展 Token — 图表色
  chartLine: string
  chartBar: string
  chartArea: string

  // Markdown 渲染色
  codeBg: string
  codeText: string
  inlineCodeBg: string
  blockquoteBorder: string
  blockquoteText: string
  linkColor: string
  tableBorder: string

  // 通用 Surface 色（替代 gray.50/_dark gray.800）
  surfaceMuted: string
  surfaceMutedHover: string

  // 选中态
  selectedBg: string
  selectedHoverBg: string

  // 布局 Token — 圆角
  cardRadius: string
  badgeRadius: string
  buttonRadius: string
  inputRadius: string
  dialogRadius: string

  // 布局 Token — 卡片样式
  cardShadow: string
  cardBorderWidth: string
  cardPadding: string

  // 字体
  fontFamily: string
}