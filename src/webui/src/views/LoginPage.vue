<template>
  <section class="card">
    <h2>登录</h2>
    <form class="form" @submit.prevent="submit">
      <label>
        用户名
        <input v-model.trim="username" required />
      </label>

      <label>
        密码
        <input v-model="password" type="password" required />
      </label>

      <button type="submit" :disabled="loading">{{ loading ? "登录中..." : "登录" }}</button>
      <p v-if="message">{{ message }}</p>
    </form>
  </section>
</template>

<script setup lang="ts">
import { ref } from "vue";
import { login } from "@/services/gatewayApi";

const username = ref("admin");
const password = ref("admin");
const message = ref("");
const loading = ref(false);

async function submit() {
  loading.value = true;
  message.value = "";

  try {
    const result = await login(username.value, password.value);
    message.value = `登录成功：${result.username} (${result.role})`;
  } catch {
    message.value = "登录失败，请检查输入。";
  } finally {
    loading.value = false;
  }
}
</script>