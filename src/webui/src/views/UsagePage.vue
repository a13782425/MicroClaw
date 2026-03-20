<template>
  <div class="page-container">
    <!-- 页面标题 -->
    <div class="page-header">
      <div class="header-left">
        <h2 class="page-title">用量统计</h2>
        <p class="page-desc">查看 Token 消耗趋势及按模型、来源的分布情况，费用基于 Provider 配置的单价估算</p>
      </div>
      <div class="header-actions">
        <el-date-picker
          v-model="dateRange"
          type="daterange"
          format="YYYY-MM-DD"
          value-format="YYYY-MM-DD"
          range-separator="至"
          start-placeholder="开始日期"
          end-placeholder="结束日期"
          :disabled-date="disableDate"
          :shortcuts="shortcuts"
          style="width: 280px"
        />
        <el-button type="primary" :icon="Refresh" :loading="loading" @click="fetchData">刷新</el-button>
      </div>
    </div>

    <!-- 汇总卡片 -->
    <div class="summary-cards">
      <div class="stat-card">
        <div class="stat-label">输入 Token</div>
        <div class="stat-value blue">{{ formatTokens(summary.totalInputTokens) }}</div>
      </div>
      <div class="stat-card">
        <div class="stat-label">输出 Token</div>
        <div class="stat-value green">{{ formatTokens(summary.totalOutputTokens) }}</div>
      </div>
      <div class="stat-card">
        <div class="stat-label">总 Token</div>
        <div class="stat-value purple">{{ formatTokens(summary.totalInputTokens + summary.totalOutputTokens) }}</div>
      </div>
      <div class="stat-card">
        <div class="stat-label">估算费用（USD）</div>
        <div class="stat-value orange">
          {{ summary.totalCostUsd > 0 ? '$' + summary.totalCostUsd.toFixed(4) : '—' }}
        </div>
      </div>
    </div>

    <!-- 图表区域 -->
    <div class="charts-row">
      <!-- 折线图：按日期趋势 -->
      <div class="chart-card">
        <div class="chart-title">Token 趋势（按日）</div>
        <div ref="lineChartEl" class="chart-box" />
      </div>
      <!-- 条形图：按 Provider 对比 -->
      <div class="chart-card">
        <div class="chart-title">按模型分布</div>
        <div ref="barChartEl" class="chart-box" />
      </div>
    </div>

    <!-- 来源分布 -->
    <div class="source-row" v-if="bySource.length > 0">
      <div class="chart-card source-card">
        <div class="chart-title">按来源分布</div>
        <div ref="pieChartEl" class="chart-box chart-box-sm" />
      </div>
      <div class="chart-card source-table-card">
        <div class="chart-title">来源明细</div>
        <el-table :data="bySource" stripe border style="width:100%">
          <el-table-column label="来源" prop="source" width="120">
            <template #default="{ row }">
              <el-tag :type="sourceTagType(row.source)" size="small">{{ sourceLabel(row.source) }}</el-tag>
            </template>
          </el-table-column>
          <el-table-column label="输入 Token" align="right">
            <template #default="{ row }">{{ formatTokens(row.inputTokens) }}</template>
          </el-table-column>
          <el-table-column label="输出 Token" align="right">
            <template #default="{ row }">{{ formatTokens(row.outputTokens) }}</template>
          </el-table-column>
          <el-table-column label="总计" align="right">
            <template #default="{ row }">{{ formatTokens(row.inputTokens + row.outputTokens) }}</template>
          </el-table-column>
        </el-table>
      </div>
    </div>

    <!-- 每日明细表格 -->
    <div class="detail-card">
      <div class="chart-title">每日明细</div>
      <el-table :data="daily" stripe border style="width:100%" v-loading="loading">
        <el-table-column label="日期" prop="date" width="130" />
        <el-table-column label="输入 Token" align="right">
          <template #default="{ row }">{{ formatTokens(row.inputTokens) }}</template>
        </el-table-column>
        <el-table-column label="输出 Token" align="right">
          <template #default="{ row }">{{ formatTokens(row.outputTokens) }}</template>
        </el-table-column>
        <el-table-column label="总 Token" align="right">
          <template #default="{ row }">{{ formatTokens(row.inputTokens + row.outputTokens) }}</template>
        </el-table-column>
        <el-table-column label="估算费用（USD）" align="right" width="160">
          <template #default="{ row }">
            <span v-if="row.estimatedCostUsd > 0">${{ row.estimatedCostUsd.toFixed(6) }}</span>
            <span v-else class="text-muted">—</span>
          </template>
        </el-table-column>
      </el-table>
      <el-empty v-if="!loading && daily.length === 0" description="该时间范围内暂无数据" :image-size="80" />
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted, watch, nextTick } from 'vue'
import * as echarts from 'echarts'
import { Refresh } from '@element-plus/icons-vue'
import { ElMessage } from 'element-plus'
import {
  fetchUsageStats,
  type DailyUsage,
  type ProviderUsage,
  type SourceUsage,
  type UsageSummary,
} from '@/services/gatewayApi'

// ── 状态 ─────────────────────────────────────────────────────────────────────

const loading = ref(false)

// 默认最近 7 天
const today = new Date()
const sevenDaysAgo = new Date(today)
sevenDaysAgo.setDate(today.getDate() - 6)
const fmt = (d: Date) => d.toISOString().slice(0, 10)

const dateRange = ref<[string, string]>([fmt(sevenDaysAgo), fmt(today)])

const daily = ref<DailyUsage[]>([])
const byProvider = ref<ProviderUsage[]>([])
const bySource = ref<SourceUsage[]>([])
const summary = ref<UsageSummary>({ totalInputTokens: 0, totalOutputTokens: 0, totalCostUsd: 0 })

// ECharts 实例和 DOM refs
const lineChartEl = ref<HTMLDivElement>()
const barChartEl = ref<HTMLDivElement>()
const pieChartEl = ref<HTMLDivElement>()
let lineChart: echarts.ECharts | null = null
let barChart: echarts.ECharts | null = null
let pieChart: echarts.ECharts | null = null

// ── 日期选择器配置 ────────────────────────────────────────────────────────────

const shortcuts = [
  { text: '今天', value: () => { const d = fmt(new Date()); return [d, d] } },
  { text: '最近 7 天', value: () => { const e = new Date(); const s = new Date(); s.setDate(e.getDate() - 6); return [fmt(s), fmt(e)] } },
  { text: '最近 14 天', value: () => { const e = new Date(); const s = new Date(); s.setDate(e.getDate() - 13); return [fmt(s), fmt(e)] } },
  { text: '最近 30 天', value: () => { const e = new Date(); const s = new Date(); s.setDate(e.getDate() - 30); return [fmt(s), fmt(e)] } },
]

function disableDate(date: Date): boolean {
  if (!dateRange.value?.[0]) return false
  const start = new Date(dateRange.value[0])
  const diff = Math.abs((date.getTime() - start.getTime()) / 86400000)
  return diff > 31
}

// ── 数据获取 ──────────────────────────────────────────────────────────────────

async function fetchData() {
  if (!dateRange.value?.[0] || !dateRange.value?.[1]) {
    ElMessage.warning('请选择日期范围')
    return
  }
  const [startDate, endDate] = dateRange.value
  loading.value = true
  try {
    const result = await fetchUsageStats(startDate, endDate)
    daily.value = result.daily
    byProvider.value = result.byProvider
    bySource.value = result.bySource
    summary.value = result.summary
    await nextTick()
    renderCharts()
  } catch (e: unknown) {
    const msg = e instanceof Error ? e.message : '请求失败'
    ElMessage.error('获取用量数据失败：' + msg)
  } finally {
    loading.value = false
  }
}

// ── 图表渲染 ──────────────────────────────────────────────────────────────────

function renderCharts() {
  renderLineChart()
  renderBarChart()
  renderPieChart()
}

function renderLineChart() {
  if (!lineChartEl.value) return
  if (!lineChart) lineChart = echarts.init(lineChartEl.value, undefined, { renderer: 'canvas' })

  const dates = daily.value.map(d => d.date)
  const inputs = daily.value.map(d => d.inputTokens)
  const outputs = daily.value.map(d => d.outputTokens)

  lineChart.setOption({
    tooltip: { trigger: 'axis' },
    legend: { data: ['输入 Token', '输出 Token'], top: 4 },
    grid: { top: 36, bottom: 28, left: 60, right: 16 },
    xAxis: { type: 'category', data: dates, axisLabel: { rotate: 30, fontSize: 11 } },
    yAxis: { type: 'value', axisLabel: { formatter: formatTokensShort } },
    series: [
      { name: '输入 Token', type: 'line', data: inputs, smooth: true, areaStyle: { opacity: 0.15 }, lineStyle: { width: 2 }, itemStyle: { color: '#409eff' } },
      { name: '输出 Token', type: 'line', data: outputs, smooth: true, areaStyle: { opacity: 0.15 }, lineStyle: { width: 2 }, itemStyle: { color: '#67c23a' } },
    ],
  }, true)
}

function renderBarChart() {
  if (!barChartEl.value) return
  if (!barChart) barChart = echarts.init(barChartEl.value, undefined, { renderer: 'canvas' })

  const providers = byProvider.value.map(p => p.providerName)
  const inputs = byProvider.value.map(p => p.inputTokens)
  const outputs = byProvider.value.map(p => p.outputTokens)

  barChart.setOption({
    tooltip: { trigger: 'axis', axisPointer: { type: 'shadow' } },
    legend: { data: ['输入 Token', '输出 Token'], top: 4 },
    grid: { top: 36, bottom: 28, left: 65, right: 16 },
    xAxis: { type: 'category', data: providers, axisLabel: { overflow: 'truncate', width: 80 } },
    yAxis: { type: 'value', axisLabel: { formatter: formatTokensShort } },
    series: [
      { name: '输入 Token', type: 'bar', stack: 'total', data: inputs, itemStyle: { color: '#409eff' } },
      { name: '输出 Token', type: 'bar', stack: 'total', data: outputs, itemStyle: { color: '#67c23a' } },
    ],
  }, true)
}

function renderPieChart() {
  if (!pieChartEl.value || bySource.value.length === 0) return
  if (!pieChart) pieChart = echarts.init(pieChartEl.value, undefined, { renderer: 'canvas' })

  const pieData = bySource.value.map(s => ({
    name: sourceLabel(s.source),
    value: s.inputTokens + s.outputTokens,
  }))

  pieChart.setOption({
    tooltip: { trigger: 'item', formatter: '{b}: {c} ({d}%)' },
    legend: { orient: 'vertical', right: 10, top: 'center' },
    series: [{
      type: 'pie',
      radius: ['40%', '65%'],
      center: ['40%', '50%'],
      data: pieData,
      label: { show: false },
      emphasis: { label: { show: true, fontSize: 13, fontWeight: 'bold' } },
    }],
  }, true)
}

// ── 工具函数 ──────────────────────────────────────────────────────────────────

function formatTokens(n: number): string {
  if (n >= 1_000_000) return (n / 1_000_000).toFixed(2) + 'M'
  if (n >= 1_000) return (n / 1_000).toFixed(1) + 'K'
  return String(n)
}

function formatTokensShort(n: number): string {
  if (n >= 1_000_000) return (n / 1_000_000).toFixed(1) + 'M'
  if (n >= 1_000) return (n / 1_000).toFixed(0) + 'K'
  return String(n)
}

const sourceMap: Record<string, string> = {
  chat: '对话',
  cron: '定时任务',
  channel: '渠道消息',
  subagent: '子代理',
}
type TagType = 'primary' | 'success' | 'warning' | 'danger' | 'info'

function sourceLabel(s: string): string {
  return sourceMap[s] ?? s
}

function sourceTagType(s: string): TagType {
  const map: Record<string, TagType> = { chat: 'primary', cron: 'warning', channel: 'success', subagent: 'info' }
  return map[s] ?? 'info'
}

// ── 生命周期 ──────────────────────────────────────────────────────────────────

onMounted(fetchData)

// 窗口 resize 时重绘图表
window.addEventListener('resize', () => {
  lineChart?.resize()
  barChart?.resize()
  pieChart?.resize()
})
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
  gap: 16px;
  margin-bottom: 24px;
  flex-wrap: wrap;
}

.header-left {
  flex: 1;
  min-width: 200px;
}

.header-actions {
  display: flex;
  align-items: center;
  gap: 12px;
  flex-shrink: 0;
}

.page-title {
  margin: 0 0 4px;
  font-size: 22px;
  font-weight: 700;
  color: #1f2937;
}

.page-desc {
  margin: 0;
  font-size: 13px;
  color: #6b7280;
}

/* 汇总卡片 */
.summary-cards {
  display: grid;
  grid-template-columns: repeat(4, 1fr);
  gap: 16px;
  margin-bottom: 24px;
}

.stat-card {
  background: #fff;
  border: 1px solid #e5e7eb;
  border-radius: 10px;
  padding: 20px 24px;
  text-align: center;
}

.stat-label {
  font-size: 13px;
  color: #6b7280;
  margin-bottom: 8px;
}

.stat-value {
  font-size: 28px;
  font-weight: 700;
  line-height: 1.2;
}

.stat-value.blue   { color: #409eff; }
.stat-value.green  { color: #67c23a; }
.stat-value.purple { color: #9b59b6; }
.stat-value.orange { color: #e6a23c; }

/* 图表区域 */
.charts-row {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 16px;
  margin-bottom: 16px;
}

.source-row {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 16px;
  margin-bottom: 16px;
}

.chart-card,
.detail-card {
  background: #fff;
  border: 1px solid #e5e7eb;
  border-radius: 10px;
  padding: 16px 20px 20px;
}

.source-card {
  display: flex;
  flex-direction: column;
}

.source-table-card {
  overflow: auto;
}

.chart-title {
  font-size: 14px;
  font-weight: 600;
  color: #374151;
  margin-bottom: 12px;
}

.chart-box {
  width: 100%;
  height: 240px;
}

.chart-box-sm {
  height: 200px;
}

.detail-card {
  margin-bottom: 0;
}

.text-muted {
  color: #9ca3af;
}

/* 响应式：小屏折叠 */
@media (max-width: 900px) {
  .summary-cards { grid-template-columns: repeat(2, 1fr); }
  .charts-row, .source-row { grid-template-columns: 1fr; }
}
</style>
