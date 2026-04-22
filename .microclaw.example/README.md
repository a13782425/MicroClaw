# .microclaw.example

此目录是 MicroClaw 工作目录（`.microclaw/`）的示例模板，提交到仓库供参考。

## 快速开始

### 方式一：使用 `init` 命令自动初始化（推荐）

```bash
dotnet run --project src/gateway/MicroClaw -- init
```

这会在当前目录下创建 `.microclaw/`、目录骨架、主配置文件和 `.env` 示例文件。

缺失的 `config/*.yaml` 会在首次读取对应配置时按需自动生成；例如首次启动 `serve` 会生成 `auth.yaml`。

### 方式二：手动从 example 复制

```bash
cp -r .microclaw.example .microclaw
cp .microclaw/.env.example .microclaw/.env
```

然后编辑 `.microclaw/config/auth.yaml` 修改密码和 JWT Secret。

## 目录结构

```
.microclaw/
├── microclaw.yaml          # 主配置文件（通过 $imports 导入 config/*.yaml）
├── .env                    # 环境变量（不提交到 Git）
├── config/                 # 配置文件目录（可从 example 复制，或在首次使用时按需自动生成）
│   ├── auth.yaml           # 认证配置（用户名、密码、JWT Secret）
│   ├── channels.yaml       # 功能开关（启用的 Provider 和 Channel）
│   ├── logging.yaml        # 日志配置（Serilog）
│   └── providers.yaml      # Model Provider 配置（含 API Key，不提交到 Git）
├── logs/                   # 日志文件（自动创建）
└── workspace/
    ├── sessions/           # 会话数据（自动创建）
    └── skills/             # 技能数据（自动创建）
```

## 安全注意事项

- `.microclaw/` 已加入 `.gitignore`，请勿手动提交
- `config/auth.yaml` 中的 `jwt_secret` 请修改为随机强密钥（≥32 字符）
- `config/providers.yaml` 中的 API Key 属于敏感信息，请通过环境变量或 `.env` 覆盖
