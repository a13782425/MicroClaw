# 江湖世界静态 HTML 原型

ShadUI 风格静态页面原型，用于 WPF 产物前期界面设计参考。

## 特点

- 不使用 React
- 不需要构建
- 一个页签一个 HTML
- `index.html` 只作为入口页
- 页面间使用普通 `<a href="*.html">` 跳转
- 样式集中在 `styles.css`
- 聚贤庄不提供人工直接招募按钮，只展示 AI 招募策略与提案
- 长约已并入 `bounty-board.html`，作为悬赏榜筛选类型展示
- 悬赏榜增加“待确认信息”，用于展示执行前需要主公 / 发起人补充的信息

## 文件清单

| 文件 | 页面 |
|---|---|
| `index.html` | 入口页 |
| `overview.html` | 江湖总览 |
| `bounty-board.html` | 悬赏与长约 |
| `bid-adjudication.html` | 竞价裁决 |
| `sects.html` | 门派 |
| `people.html` | 弟子 / 侠客 |
| `recruitment-hall.html` | 聚贤庄 |
| `economy.html` | 义仓经济 |
| `market.html` | 三阁市集 |
| `log-station.html` | 江湖录与驿站 |
| `styles.css` | 全局 ShadUI 风格样式 |

## 使用

直接打开：

```text
index.html
```

或直接打开任意单页，例如：

```text
recruitment-hall.html
```

## 转 WPF 建议

- 每个 HTML 文件可映射为一个 WPF Page / UserControl。
- `.card` 可映射为统一 Card 控件样式。
- `.table` 可映射为 DataGrid。
- `.split` 可映射为左列表 + 右详情布局。
- `.metrics` 可映射为横向指标卡。
- `.badge`、`.button`、`.tabs` 可沉淀为基础控件样式。
