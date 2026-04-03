import { create } from 'zustand'
import { persist } from 'zustand/middleware'

interface UIState {
  sidebarCollapsed: boolean
  setSidebarCollapsed: (collapsed: boolean) => void
  toggleSidebar: () => void
  collapsedGroups: string[]
  toggleGroup: (title: string) => void
  themeId: string
  setThemeId: (id: string) => void
}

export const useUIStore = create<UIState>()(
  persist(
    (set) => ({
      sidebarCollapsed: false,
      setSidebarCollapsed: (collapsed) => set({ sidebarCollapsed: collapsed }),
      toggleSidebar: () => set((s) => ({ sidebarCollapsed: !s.sidebarCollapsed })),
      collapsedGroups: [],
      toggleGroup: (title) =>
        set((s) => ({
          collapsedGroups: s.collapsedGroups.includes(title)
            ? s.collapsedGroups.filter((g) => g !== title)
            : [...s.collapsedGroups, title],
        })),
      themeId: 'tailwind',
      setThemeId: (id) => set({ themeId: id }),
    }),
    { name: 'mc-ui' },
  ),
)
