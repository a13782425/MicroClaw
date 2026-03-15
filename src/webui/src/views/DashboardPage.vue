<template>
  <section class="card">
    <h2>Gateway 状态</h2>
    <p v-if="loading">正在检查网关...</p>
    <p v-else-if="error" class="error">{{ error }}</p>
    <div v-else-if="health">
      <p><strong>状态：</strong>{{ health.status }}</p>
      <p><strong>服务：</strong>{{ health.service }}</p>
      <p><strong>版本：</strong>{{ health.version }}</p>
      <p><strong>时间：</strong>{{ health.utcNow }}</p>
    </div>
    <button @click="load">刷新</button>
  </section>
</template>

<script setup lang="ts">
import { onMounted, ref } from "vue";
import { getGatewayHealth, type GatewayHealth } from "@/services/gatewayApi";

const loading = ref(false);
const error = ref("");
const health = ref<GatewayHealth | null>(null);

async function load() {
  loading.value = true;
  error.value = "";

  try {
    health.value = await getGatewayHealth();
  } catch {
    error.value = "无法连接到 Gateway，请确认后端已启动。";
  } finally {
    loading.value = false;
  }
}

onMounted(load);
</script>