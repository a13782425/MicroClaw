<template>
  <div class="layout">
    <header class="header">
      <h1>MicroClaw 控制台</h1>
      <nav>
        <RouterLink to="/">首页</RouterLink>
        <RouterLink to="/login">登录</RouterLink>
      </nav>
    </header>

    <main class="container">
      <RouterView />
    </main>
  </div>
</template>

<script setup lang="ts">
import { RouterLink, RouterView } from "vue-router";
</script>