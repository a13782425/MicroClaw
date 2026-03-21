<template>
  <div class="page-container">
    <div class="page-header">
      <div>
        <h2 class="page-title">系统配置</h2>
        <p class="page-desc">配置参考手册——所有设置通过 <code>microclaw.yaml</code> 及 <code>config/*.yaml</code> 文件管理</p>
      </div>
    </div>

    <!-- 快速导航 -->
    <p class="section-label">快速导航</p>
    <div class="nav-grid">
      <router-link v-for="nav in navItems" :key="nav.route" :to="nav.route" class="nav-card">
        <el-icon class="nav-icon" :style="{ color: nav.color }">
          <component :is="nav.icon" />
        </el-icon>
        <div class="nav-body">
          <div class="nav-title">{{ nav.title }}</div>
          <div class="nav-desc">{{ nav.desc }}</div>
        </div>
        <el-icon class="nav-arrow"><ArrowRight /></el-icon>
      </router-link>
    </div>

    <!-- 配置项说明 -->
    <p class="section-label" style="margin-top: 28px">配置项说明</p>
    <div class="config-grid">
      <!-- 认证 -->
      <div class="config-card">
        <div class="config-card-header" style="background: #eff6ff; color: #2563eb">
          <el-icon><Lock /></el-icon>
          <span>认证（auth）</span>
        </div>
        <table class="config-table">
          <tbody>
            <tr>
              <td class="key-cell"><code>auth:jwt_secret</code></td>
              <td class="val-cell">JWT 签名密钥，<strong>建议 ≥ 32 字符</strong>的随机字符串，长度不足时系统启动将输出安全警告</td>
            </tr>
            <tr>
              <td class="key-cell"><code>auth:username</code></td>
              <td class="val-cell">管理员账号（默认 <code>admin</code>）</td>
            </tr>
            <tr>
              <td class="key-cell"><code>auth:password</code></td>
              <td class="val-cell">管理员登录密码，生产环境必须设置强密码</td>
            </tr>
          </tbody>
        </table>
      </div>

      <!-- 数据库 -->
      <div class="config-card">
        <div class="config-card-header" style="background: #f0fdf4; color: #16a34a">
          <el-icon><Coin /></el-icon>
          <span>数据库（database）</span>
        </div>
        <table class="config-table">
          <tbody>
            <tr>
              <td class="key-cell"><code>database:path</code></td>
              <td class="val-cell">SQLite 数据库文件路径，默认 <code>./data/microclaw.db</code></td>
            </tr>
          </tbody>
        </table>
      </div>

      <!-- 配置文件机制 -->
      <div class="config-card config-card-wide">
        <div class="config-card-header" style="background: #fefce8; color: #ca8a04">
          <el-icon><Document /></el-icon>
          <span>配置文件导入机制（$imports）</span>
        </div>
        <div class="config-note">
          <p>主配置文件 <code>microclaw.yaml</code> 通过 <code>$imports</code> 字段导入子配置：</p>
          <pre class="code-snippet">$imports:
  - ./config/*.yaml</pre>
          <ul class="tip-list">
            <li>主配置作为默认值层，子配置可覆盖同名键</li>
            <li>多个子配置文件之间<strong>不允许出现相同的键</strong>，冲突时启动报错</li>
            <li>支持通配符（如 <code>./config/*.yaml</code>）和具体路径混用</li>
          </ul>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ArrowRight, Lock, Coin, Document, Cpu, Connection, Promotion, MagicStick } from '@element-plus/icons-vue'

const navItems = [
  {
    route: '/models',
    icon: Cpu,
    color: '#6366f1',
    title: '模型提供方',
    desc: '管理 OpenAI / Anthropic 等 AI 模型接入',
  },
  {
    route: '/channels',
    icon: Connection,
    color: '#0ea5e9',
    title: '消息渠道',
    desc: '配置飞书、企业微信、微信等接入渠道',
  },
  {
    route: '/agents',
    icon: Promotion,
    color: '#f59e0b',
    title: 'Agent',
    desc: '管理 AI 智能体及 DNA 系统配置',
  },
  {
    route: '/skills',
    icon: MagicStick,
    color: '#10b981',
    title: '技能',
    desc: '管理 AI 调用的外部技能与 MCP 工具',
  },
]
</script>

<style scoped>
.page-container {
  max-width: 1200px;
  height: 100%;
  padding: 24px;
  overflow-y: auto;
  box-sizing: border-box;
}

.page-header {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  margin-bottom: 28px;
}

.page-title {
  margin: 0 0 4px;
  font-size: 22px;
  font-weight: 700;
  color: #1f2937;
}

.page-desc {
  margin: 0;
  font-size: 14px;
  color: #6b7280;
}

.page-desc code {
  background: #f3f4f6;
  padding: 1px 5px;
  border-radius: 3px;
  font-size: 12px;
  color: #374151;
}

.section-label {
  font-size: 13px;
  font-weight: 600;
  color: #6b7280;
  text-transform: uppercase;
  letter-spacing: 0.05em;
  margin: 0 0 12px;
}

/* ---- 快速导航 ---- */
.nav-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(260px, 1fr));
  gap: 12px;
}

.nav-card {
  display: flex;
  align-items: center;
  gap: 14px;
  padding: 16px 18px;
  background: #fff;
  border: 1px solid #e5e7eb;
  border-radius: 10px;
  text-decoration: none;
  color: inherit;
  transition: box-shadow 0.2s, border-color 0.2s;
}

.nav-card:hover {
  box-shadow: 0 4px 12px rgba(0, 0, 0, 0.08);
  border-color: #d1d5db;
}

.nav-icon {
  font-size: 26px;
  flex-shrink: 0;
}

.nav-body {
  flex: 1;
  min-width: 0;
}

.nav-title {
  font-size: 14px;
  font-weight: 600;
  color: #111827;
  margin-bottom: 2px;
}

.nav-desc {
  font-size: 12px;
  color: #6b7280;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.nav-arrow {
  font-size: 14px;
  color: #9ca3af;
  flex-shrink: 0;
}

/* ---- 配置项说明 ---- */
.config-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(340px, 1fr));
  gap: 16px;
}

.config-card {
  background: #fff;
  border: 1px solid #e5e7eb;
  border-radius: 10px;
  overflow: hidden;
}

.config-card-wide {
  grid-column: 1 / -1;
}

.config-card-header {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 10px 16px;
  font-size: 13px;
  font-weight: 600;
}

.config-table {
  width: 100%;
  border-collapse: collapse;
  font-size: 13px;
}

.config-table tr {
  border-top: 1px solid #f3f4f6;
}

.config-table tr:first-child {
  border-top: none;
}

.key-cell {
  padding: 10px 14px;
  vertical-align: top;
  white-space: nowrap;
  width: 1%;
  color: #374151;
}

.key-cell code {
  background: #f3f4f6;
  padding: 2px 6px;
  border-radius: 3px;
  font-size: 12px;
  color: #dc2626;
}

.val-cell {
  padding: 10px 14px 10px 0;
  color: #6b7280;
  line-height: 1.5;
}

.val-cell code {
  background: #f3f4f6;
  padding: 1px 4px;
  border-radius: 3px;
  font-size: 12px;
  color: #374151;
}

/* ---- 配置说明 note ---- */
.config-note {
  padding: 14px 18px;
  font-size: 13px;
  color: #374151;
}

.config-note p {
  margin: 0 0 10px;
}

.config-note p code {
  background: #f3f4f6;
  padding: 1px 5px;
  border-radius: 3px;
  font-size: 12px;
  color: #374151;
}

.code-snippet {
  background: #1e293b;
  color: #e2e8f0;
  padding: 12px 16px;
  border-radius: 6px;
  font-size: 12px;
  line-height: 1.6;
  margin: 0 0 12px;
  overflow-x: auto;
  font-family: 'SF Mono', 'Fira Code', monospace;
}

.tip-list {
  margin: 0;
  padding-left: 18px;
  color: #6b7280;
  line-height: 1.8;
}

.tip-list li strong {
  color: #374151;
}

.tip-list li code {
  background: #f3f4f6;
  padding: 1px 4px;
  border-radius: 3px;
  font-size: 12px;
  color: #374151;
}
</style>

