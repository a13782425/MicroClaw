<template>
  <div class="login-bg">
    <div class="login-wrapper">
      <div class="login-brand">
        <el-icon class="login-logo"><Cpu /></el-icon>
        <h1 class="login-title">MicroClaw</h1>
        <p class="login-subtitle">AI Agent 控制面板</p>
      </div>

      <el-card class="login-card" shadow="always">
        <template #header>
          <span class="card-title">管理员登录</span>
        </template>

        <el-form
          ref="formRef"
          :model="form"
          :rules="rules"
          label-position="top"
          size="large"
          @submit.prevent="submit"
        >
          <el-form-item label="用户名" prop="username">
            <el-input
              v-model.trim="form.username"
              placeholder="请输入用户名"
              autocomplete="username"
              :prefix-icon="User"
            />
          </el-form-item>

          <el-form-item label="密码" prop="password">
            <el-input
              v-model="form.password"
              type="password"
              placeholder="请输入密码"
              show-password
              autocomplete="current-password"
              :prefix-icon="Lock"
              @keyup.enter="submit"
            />
          </el-form-item>

          <el-form-item>
            <el-button
              type="primary"
              native-type="submit"
              :loading="loading"
              style="width: 100%"
              @click="submit"
            >
              {{ loading ? '登录中...' : '登 录' }}
            </el-button>
          </el-form-item>
        </el-form>
      </el-card>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, reactive } from 'vue'
import { useRouter } from 'vue-router'
import type { FormInstance, FormRules } from 'element-plus'
import { ElMessage } from 'element-plus'
import { User, Lock } from '@element-plus/icons-vue'
import { login } from '@/services/gatewayApi'
import { useAuthStore } from '@/stores/auth'

const router = useRouter()
const auth = useAuthStore()

const formRef = ref<FormInstance>()
const loading = ref(false)

const form = reactive({
  username: '',
  password: ''
})

const rules: FormRules = {
  username: [{ required: true, message: '请输入用户名', trigger: 'blur' }],
  password: [{ required: true, message: '请输入密码', trigger: 'blur' }]
}

async function submit() {
  if (!formRef.value) return
  const valid = await formRef.value.validate().catch(() => false)
  if (!valid) return

  loading.value = true
  try {
    const data = await login(form.username, form.password)
    auth.setAuth(data.token, data.username)
    ElMessage.success('登录成功')
    router.push({ name: 'sessions' })
  } catch (err: any) {
    const msg = err.response?.status === 401 ? '用户名或密码错误' : '登录失败，请稍后重试'
    ElMessage.error(msg)
  } finally {
    loading.value = false
  }
}
</script>

<style scoped>
.login-bg {
  min-height: 100vh;
  display: flex;
  align-items: center;
  justify-content: center;
  background: linear-gradient(135deg, #0f172a 0%, #1d2b45 50%, #0f2744 100%);
}

.login-wrapper {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 24px;
  width: 100%;
  max-width: 420px;
  padding: 0 16px;
}

.login-brand {
  text-align: center;
  color: #fff;
}

.login-logo {
  font-size: 52px;
  color: #60a5fa;
  margin-bottom: 8px;
}

.login-title {
  margin: 8px 0 4px;
  font-size: 28px;
  font-weight: 700;
  letter-spacing: 1px;
}

.login-subtitle {
  margin: 0;
  font-size: 14px;
  color: rgba(255, 255, 255, 0.55);
  letter-spacing: 0.5px;
}

.login-card {
  width: 100%;
  border-radius: 12px;
  border: none;
}

.card-title {
  font-size: 16px;
  font-weight: 600;
  color: #1f2937;
}
</style>
