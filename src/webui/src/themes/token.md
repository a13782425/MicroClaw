# Theme Tokens 说明

本文档说明 `src/webui/src/themes/theme-tokens.ts` 中每个 token 的职责与推荐使用场景。

## 基础信息

- `id`: 主题唯一标识（程序内部使用）。
- `label`: 主题显示名称（UI 下拉框/主题选择器展示）。

## 品牌色

- `primary`: 主品牌色，核心 CTA、主强调元素。
- `primarySoft`: 主品牌浅色背景，用于轻提示/弱强调区块。
- `secondary`: 次品牌色，次级强调、补充视觉层级。
- `secondarySoft`: 次品牌浅色背景。
- `accent`: 点缀色，用于少量视觉亮点（标签、图标强调）。
- `accentSoft`: 点缀浅色背景。

## 语义色

- `success`: 成功态主色（成功提示、状态点）。
- `successSoft`: 成功态浅背景。
- `warning`: 警告态主色。
- `warningSoft`: 警告态浅背景。
- `danger`: 错误/危险态主色（删除、失败）。
- `dangerSoft`: 错误/危险态浅背景。
- `info`: 信息态主色。
- `infoSoft`: 信息态浅背景。

## 界面表面色

- `bg`: 页面主背景色。
- `card`: 卡片/面板默认背景色。
- `cardHover`: 卡片/可悬停项 hover 背景色。
- `input`: 输入框、分段头部、弱对比容器背景。

## 文字色

- `text`: 默认正文文字色。
- `textLight`: 浅色文字（用于深色底或次级信息）。
- `textMuted`: 弱化说明文字色（副标题、元信息）。

## 边框色

- `border`: 默认边框色。
- `borderLight`: 更轻的边框色（分割线、弱边框）。

## 头像与图标色

- `avatarBg`: 头像背景色。
- `avatarColor`: 头像前景色（字母/图标颜色）。

## 聊天气泡

- `userBubble`: 用户消息气泡背景。
- `userBubbleText`: 用户消息文字色。
- `aiBubbleBg`: AI 消息气泡背景。
- `aiBubbleBorder`: AI 消息气泡边框色。

## 终端/控制台色

- `terminalBg`: 终端背景色。
- `terminalGreen`: 终端绿色文本。
- `terminalBlue`: 终端蓝色文本。
- `terminalYellow`: 终端黄色文本。
- `terminalPink`: 终端粉色文本。
- `terminalDim`: 终端弱化文本色。

## 侧边栏

- `sidebarActive`: 侧边栏当前项背景色。
- `sidebarActiveBorder`: 侧边栏当前项边框/指示条颜色。

## 交互态

- `inputFocusBorder`: 输入框 focus 边框色。
- `inputFocusShadow`: 输入框 focus 阴影色。
- `sendButtonBg`: 发送按钮背景色。
- `sendButtonColor`: 发送按钮文字/图标色。

## 占位与问候

- `placeholder`: 占位符文字色。
- `greeting`: 欢迎语/引导语文字色。

## AI 思考块

- `thinkingBg`: 思考块背景色。
- `thinkingBorder`: 思考块边框色。
- `thinkingLabel`: 思考块标题/标签文字色。

## 工具调用与子 Agent 块

- `toolCallBg`: 工具调用块头部/背景色。
- `toolCallBorder`: 工具调用块边框色。
- `subAgentBg`: 子 Agent 块头部/背景色。
- `subAgentBorder`: 子 Agent 块边框色。

## 工作流节点色

- `nodeStart`: 开始节点颜色。
- `nodeEnd`: 结束节点颜色。
- `nodeAgent`: Agent 节点颜色。
- `nodeTool`: 工具节点颜色。
- `nodeRouter`: 路由节点颜色。
- `nodeFunction`: 函数节点颜色。
- `nodeSwitchModel`: 切换模型节点颜色。

## 图表色

- `chartLine`: 折线图主线颜色。
- `chartBar`: 柱状图主色。
- `chartArea`: 面积图填充主色。

## Markdown 渲染色

- `codeBg`: 代码块背景色。
- `codeText`: 代码块文字色。
- `inlineCodeBg`: 行内代码背景色。
- `blockquoteBorder`: 引用块左边框色。
- `blockquoteText`: 引用块文字色。
- `linkColor`: Markdown 链接色。
- `tableBorder`: Markdown 表格边框色。

## 通用 Surface（替代硬编码 gray）

- `surfaceMuted`: 次级表面色（弱背景容器）。
- `surfaceMutedHover`: 次级表面 hover 色。

## 选中态

- `selectedBg`: 选中项背景色。
- `selectedHoverBg`: 选中项 hover 背景色。

## 布局 Token：圆角

- `cardRadius`: 卡片圆角。
- `badgeRadius`: Badge 圆角。
- `buttonRadius`: 按钮圆角。
- `inputRadius`: 输入框圆角。
- `dialogRadius`: 弹窗圆角。

## 布局 Token：卡片

- `cardShadow`: 卡片阴影。
- `cardBorderWidth`: 卡片边框宽度。
- `cardPadding`: 卡片内边距。

## 字体

- `fontFamily`: 全局字体族。

## 使用建议

- 新页面优先使用 `--mc-*` 变量，不直接写 `blue.500`、`gray.100` 等硬编码颜色。
- 交互控件（刷新、切换、列表选中）优先使用 `selectedBg`、`selectedHoverBg`、`cardHover`。
- 状态反馈统一使用语义色：成功用 `success`，警告用 `warning`，错误用 `danger`。
