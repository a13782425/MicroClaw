import { createRouter, createWebHistory } from "vue-router";
import DashboardPage from "@/views/DashboardPage.vue";
import LoginPage from "@/views/LoginPage.vue";

export const router = createRouter({
  history: createWebHistory(),
  routes: [
    { path: "/", name: "dashboard", component: DashboardPage },
    { path: "/login", name: "login", component: LoginPage }
  ]
});