import { useState, useEffect, useCallback } from 'react'
import {
  Box, Text, Badge, HStack, VStack, Spinner, Button, Table, Input, Tabs, Flex,
} from '@chakra-ui/react'
import {
  Puzzle, RefreshCw, Download, Trash2, Power, PowerOff, ArrowUpCircle,
  Store, Plus, Search, ExternalLink, ChevronDown,
} from 'lucide-react'
import {
  getPlugins, installPlugin, enablePlugin, disablePlugin,
  updatePlugin, uninstallPlugin, reloadPlugins,
  getMarketplaces, addMarketplace, removeMarketplace, updateMarketplace,
  getMarketplacePlugins, installMarketplacePlugin,
  type PluginSummary, type MarketplaceInfo, type MarketplacePluginEntry,
  type InstallPluginResult,
} from '@/api/gateway'
import { toaster } from '@/components/ui/toaster'

// ─── Installed Tab ────────────────────────────────────────────────────────────

function InstalledTab({ onMarketplaceDetected }: { onMarketplaceDetected: () => void }) {
  const [plugins, setPlugins] = useState<PluginSummary[]>([])
  const [loading, setLoading] = useState(false)
  const [installUrl, setInstallUrl] = useState('')
  const [installing, setInstalling] = useState(false)

  const load = useCallback(async () => {
    setLoading(true)
    try {
      setPlugins(await getPlugins())
    } catch {
      toaster.create({ type: 'error', title: '加载插件列表失败' })
    } finally {
      setLoading(false)
    }
  }, [])

  useEffect(() => { load() }, [load])

  const handleInstall = async () => {
    if (!installUrl.trim()) return
    setInstalling(true)
    try {
      const result: InstallPluginResult = await installPlugin(installUrl.trim())
      if (result.type === 'marketplace') {
        toaster.create({ type: 'success', title: `检测到插件市场 "${result.marketplace.name}"，已自动注册` })
        setInstallUrl('')
        onMarketplaceDetected()
      } else {
        toaster.create({ type: 'success', title: '插件安装成功' })
        setInstallUrl('')
        await load()
      }
    } catch {
      toaster.create({ type: 'error', title: '插件安装失败' })
    } finally {
      setInstalling(false)
    }
  }

  const handleToggle = async (name: string, enabled: boolean) => {
    try {
      if (enabled) await disablePlugin(name); else await enablePlugin(name)
      await load()
    } catch {
      toaster.create({ type: 'error', title: '操作失败' })
    }
  }

  const handleUpdate = async (name: string) => {
    try {
      await updatePlugin(name)
      toaster.create({ type: 'success', title: '插件已更新' })
      await load()
    } catch {
      toaster.create({ type: 'error', title: '更新失败' })
    }
  }

  const handleUninstall = async (name: string) => {
    try {
      await uninstallPlugin(name)
      toaster.create({ type: 'success', title: '插件已卸载' })
      await load()
    } catch {
      toaster.create({ type: 'error', title: '卸载失败' })
    }
  }

  const handleReload = async () => {
    try {
      await reloadPlugins()
      toaster.create({ type: 'success', title: '插件已重新加载' })
      await load()
    } catch {
      toaster.create({ type: 'error', title: '重新加载失败' })
    }
  }

  return (
    <Box>
      <HStack mb="4" justify="flex-end">
        <Button size="sm" variant="outline" onClick={handleReload}>
          <RefreshCw size={14} /> 重新加载
        </Button>
      </HStack>

      <HStack mb="4" gap="2">
        <Input
          flex="1" size="sm"
          placeholder="输入 Git 仓库 URL 安装插件..."
          value={installUrl}
          onChange={(e) => setInstallUrl(e.target.value)}
          onKeyDown={(e) => e.key === 'Enter' && handleInstall()}
        />
        <Button size="sm" colorPalette="blue" onClick={handleInstall} disabled={installing || !installUrl.trim()}>
          {installing ? <Spinner size="xs" /> : <Download size={14} />} 安装
        </Button>
      </HStack>

      {loading ? (
        <VStack py="12"><Spinner /><Text>加载中...</Text></VStack>
      ) : plugins.length === 0 ? (
        <Text color="gray.400" textAlign="center" py="12">暂无已安装的插件</Text>
      ) : (
        <Table.Root size="sm">
          <Table.Header>
            <Table.Row>
              <Table.ColumnHeader>名称</Table.ColumnHeader>
              <Table.ColumnHeader>状态</Table.ColumnHeader>
              <Table.ColumnHeader>版本</Table.ColumnHeader>
              <Table.ColumnHeader>来源</Table.ColumnHeader>
              <Table.ColumnHeader>资源</Table.ColumnHeader>
              <Table.ColumnHeader>操作</Table.ColumnHeader>
            </Table.Row>
          </Table.Header>
          <Table.Body>
            {plugins.map((p) => (
              <Table.Row key={p.name}>
                <Table.Cell>
                  <VStack align="start" gap="0">
                    <Text fontWeight="medium" fontSize="sm">{p.name}</Text>
                    {p.description && <Text fontSize="xs" color="gray.500" lineClamp={1}>{p.description}</Text>}
                  </VStack>
                </Table.Cell>
                <Table.Cell>
                  <Badge colorPalette={p.isEnabled ? 'green' : 'gray'} size="sm">{p.isEnabled ? '启用' : '禁用'}</Badge>
                </Table.Cell>
                <Table.Cell><Text fontSize="xs">{p.version ?? '-'}</Text></Table.Cell>
                <Table.Cell>
                  <Badge colorPalette={p.source.type === 'git' ? 'blue' : 'orange'} size="sm">{p.source.type}</Badge>
                </Table.Cell>
                <Table.Cell>
                  <HStack gap="1" flexWrap="wrap">
                    {p.skillCount > 0 && <Badge size="sm" variant="outline">{p.skillCount} 技能</Badge>}
                    {p.agentCount > 0 && <Badge size="sm" variant="outline">{p.agentCount} Agent</Badge>}
                    {p.hookCount > 0 && <Badge size="sm" variant="outline">{p.hookCount} Hook</Badge>}
                    {p.hasMcpConfig && <Badge size="sm" variant="outline">MCP</Badge>}
                  </HStack>
                </Table.Cell>
                <Table.Cell>
                  <HStack gap="1">
                    <Button size="xs" variant="ghost" onClick={() => handleToggle(p.name, p.isEnabled)} title={p.isEnabled ? '禁用' : '启用'}>
                      {p.isEnabled ? <PowerOff size={14} /> : <Power size={14} />}
                    </Button>
                    {p.source.type === 'git' && (
                      <Button size="xs" variant="ghost" onClick={() => handleUpdate(p.name)} title="更新">
                        <ArrowUpCircle size={14} />
                      </Button>
                    )}
                    <Button size="xs" variant="ghost" colorPalette="red" onClick={() => handleUninstall(p.name)} title="卸载">
                      <Trash2 size={14} />
                    </Button>
                  </HStack>
                </Table.Cell>
              </Table.Row>
            ))}
          </Table.Body>
        </Table.Root>
      )}
    </Box>
  )
}

// ─── Marketplace Tab ──────────────────────────────────────────────────────────

function MarketplaceTab({ onInstalled }: { onInstalled: () => void }) {
  // Marketplace sources
  const [marketplaces, setMarketplaces] = useState<MarketplaceInfo[]>([])
  const [loadingMp, setLoadingMp] = useState(false)
  const [addUrl, setAddUrl] = useState('')
  const [adding, setAdding] = useState(false)
  const [selectedMp, setSelectedMp] = useState('')

  // Plugins from selected marketplace
  const [mpPlugins, setMpPlugins] = useState<MarketplacePluginEntry[]>([])
  const [loadingPlugins, setLoadingPlugins] = useState(false)
  const [keyword, setKeyword] = useState('')
  const [searchInput, setSearchInput] = useState('')
  const [installingPlugin, setInstallingPlugin] = useState<string | null>(null)

  const loadMarketplaces = useCallback(async () => {
    setLoadingMp(true)
    try {
      const list = await getMarketplaces()
      setMarketplaces(list)
      if (list.length > 0 && !list.find(m => m.name === selectedMp)) {
        setSelectedMp(list[0].name)
      }
    } catch {
      toaster.create({ type: 'error', title: '加载市场列表失败' })
    } finally {
      setLoadingMp(false)
    }
  }, [selectedMp])

  const loadPlugins = useCallback(async () => {
    if (!selectedMp) { setMpPlugins([]); return }
    setLoadingPlugins(true)
    try {
      setMpPlugins(await getMarketplacePlugins(selectedMp, keyword || undefined))
    } catch {
      toaster.create({ type: 'error', title: '加载插件列表失败' })
    } finally {
      setLoadingPlugins(false)
    }
  }, [selectedMp, keyword])

  useEffect(() => { loadMarketplaces() }, []) // eslint-disable-line react-hooks/exhaustive-deps
  useEffect(() => { loadPlugins() }, [selectedMp, keyword]) // eslint-disable-line react-hooks/exhaustive-deps

  const handleAdd = async () => {
    if (!addUrl.trim()) return
    setAdding(true)
    try {
      const info = await addMarketplace(addUrl.trim())
      toaster.create({ type: 'success', title: `市场 "${info.name}" 已注册` })
      setAddUrl('')
      setSelectedMp(info.name)
      await loadMarketplaces()
    } catch {
      toaster.create({ type: 'error', title: '注册市场失败' })
    } finally {
      setAdding(false)
    }
  }

  const handleRemove = async () => {
    if (!selectedMp) return
    try {
      await removeMarketplace(selectedMp)
      toaster.create({ type: 'success', title: '市场已移除' })
      setSelectedMp('')
      setMpPlugins([])
      await loadMarketplaces()
    } catch {
      toaster.create({ type: 'error', title: '移除失败' })
    }
  }

  const handleUpdate = async () => {
    if (!selectedMp) return
    try {
      await updateMarketplace(selectedMp)
      toaster.create({ type: 'success', title: '市场索引已更新' })
      await loadPlugins()
    } catch {
      toaster.create({ type: 'error', title: '更新失败' })
    }
  }

  const handleInstallPlugin = async (pluginName: string) => {
    if (!selectedMp) return
    setInstallingPlugin(pluginName)
    try {
      await installMarketplacePlugin(selectedMp, pluginName)
      toaster.create({ type: 'success', title: `插件 "${pluginName}" 安装成功` })
      onInstalled()
    } catch {
      toaster.create({ type: 'error', title: `安装 "${pluginName}" 失败` })
    } finally {
      setInstallingPlugin(null)
    }
  }

  const handleSearch = () => {
    setKeyword(searchInput.trim())
  }

  const currentMp = marketplaces.find(m => m.name === selectedMp)

  return (
    <Box>
      {/* Add marketplace source */}
      <HStack mb="4" gap="2">
        <Input
          flex="1" size="sm"
          placeholder="输入插件市场 Git 仓库 URL 注册..."
          value={addUrl}
          onChange={(e) => setAddUrl(e.target.value)}
          onKeyDown={(e) => e.key === 'Enter' && handleAdd()}
        />
        <Button size="sm" colorPalette="blue" onClick={handleAdd} disabled={adding || !addUrl.trim()}>
          {adding ? <Spinner size="xs" /> : <Plus size={14} />} 注册市场
        </Button>
      </HStack>

      {/* Marketplace selector + actions */}
      {marketplaces.length > 0 && (
        <HStack mb="4" gap="2" flexWrap="wrap">
          <HStack gap="1">
            <Store size={16} />
            <Text fontSize="sm" fontWeight="medium">市场源：</Text>
          </HStack>
          <select
            value={selectedMp}
            onChange={(e) => setSelectedMp(e.target.value)}
            style={{
              padding: '2px 8px',
              borderRadius: '6px',
              fontSize: '14px',
              cursor: 'pointer',
              border: '1px solid var(--chakra-colors-border)',
              background: 'transparent',
              color: 'inherit',
            }}
          >
            {marketplaces.map(m => (
              <option key={m.name} value={m.name}>{m.name}</option>
            ))}
          </select>
          {currentMp && (
            <Badge colorPalette="teal" size="sm">{currentMp.marketplaceType}</Badge>
          )}
          <Button size="xs" variant="ghost" onClick={handleUpdate} title="更新市场索引">
            <RefreshCw size={14} />
          </Button>
          <Button size="xs" variant="ghost" colorPalette="red" onClick={handleRemove} title="移除市场">
            <Trash2 size={14} />
          </Button>
        </HStack>
      )}

      {/* Search */}
      {selectedMp && (
        <HStack mb="4" gap="2">
          <Input
            flex="1" size="sm"
            placeholder="搜索插件名称或描述..."
            value={searchInput}
            onChange={(e) => setSearchInput(e.target.value)}
            onKeyDown={(e) => e.key === 'Enter' && handleSearch()}
          />
          <Button size="sm" variant="outline" onClick={handleSearch}>
            <Search size={14} /> 搜索
          </Button>
          {keyword && (
            <Button size="sm" variant="ghost" onClick={() => { setKeyword(''); setSearchInput('') }}>
              清除
            </Button>
          )}
        </HStack>
      )}

      {/* Plugin list */}
      {loadingMp ? (
        <VStack py="12"><Spinner /><Text>加载市场列表...</Text></VStack>
      ) : marketplaces.length === 0 ? (
        <VStack py="12" gap="2">
          <Store size={32} color="gray" />
          <Text color="gray.400" textAlign="center">暂无注册的插件市场</Text>
          <Text color="gray.400" fontSize="sm" textAlign="center">输入 Git 仓库 URL 注册一个插件市场开始使用</Text>
        </VStack>
      ) : loadingPlugins ? (
        <VStack py="12"><Spinner /><Text>加载插件列表...</Text></VStack>
      ) : mpPlugins.length === 0 ? (
        <Text color="gray.400" textAlign="center" py="12">
          {keyword ? '没有匹配的插件' : '该市场中暂无插件'}
        </Text>
      ) : (
        <>
          <Text fontSize="xs" color="gray.500" mb="2">
            共 {mpPlugins.length} 个插件{keyword && `（搜索: "${keyword}"）`}
          </Text>
          <Table.Root size="sm">
            <Table.Header>
              <Table.Row>
                <Table.ColumnHeader w="40%">名称</Table.ColumnHeader>
                <Table.ColumnHeader>分类</Table.ColumnHeader>
                <Table.ColumnHeader>来源</Table.ColumnHeader>
                <Table.ColumnHeader>主页</Table.ColumnHeader>
                <Table.ColumnHeader>操作</Table.ColumnHeader>
              </Table.Row>
            </Table.Header>
            <Table.Body>
              {mpPlugins.map((p) => (
                <Table.Row key={p.name}>
                  <Table.Cell>
                    <VStack align="start" gap="0">
                      <HStack gap="1">
                        <Text fontWeight="medium" fontSize="sm">{p.name}</Text>
                        {p.version && <Badge size="sm" variant="outline">{p.version}</Badge>}
                      </HStack>
                      {p.description && (
                        <Text fontSize="xs" color="gray.500" lineClamp={2}>{p.description}</Text>
                      )}
                      {p.tags && p.tags.length > 0 && (
                        <HStack gap="1" mt="1">
                          {p.tags.map(t => <Badge key={t} size="sm" variant="subtle" colorPalette="orange">{t}</Badge>)}
                        </HStack>
                      )}
                    </VStack>
                  </Table.Cell>
                  <Table.Cell>
                    {p.category && <Badge size="sm" variant="subtle" colorPalette="cyan">{p.category}</Badge>}
                  </Table.Cell>
                  <Table.Cell>
                    <Badge size="sm" colorPalette={sourceColor(p.source.sourceType)}>{p.source.sourceType}</Badge>
                  </Table.Cell>
                  <Table.Cell>
                    {p.homepage && (
                      <a href={p.homepage} target="_blank" rel="noopener noreferrer" title={p.homepage}>
                        <ExternalLink size={14} />
                      </a>
                    )}
                  </Table.Cell>
                  <Table.Cell>
                    <Button
                      size="xs" colorPalette="blue"
                      onClick={() => handleInstallPlugin(p.name)}
                      disabled={installingPlugin !== null}
                    >
                      {installingPlugin === p.name ? <Spinner size="xs" /> : <Download size={14} />}
                      安装
                    </Button>
                  </Table.Cell>
                </Table.Row>
              ))}
            </Table.Body>
          </Table.Root>
        </>
      )}
    </Box>
  )
}

function sourceColor(type: string): string {
  switch (type) {
    case 'Local': return 'green'
    case 'Url': return 'blue'
    case 'GitSubdir': return 'purple'
    case 'GitHub': return 'gray'
    default: return 'gray'
  }
}

// ─── Main Page ────────────────────────────────────────────────────────────────

export default function PluginsPage() {
  const [activeTab, setActiveTab] = useState('installed')
  const [refreshKey, setRefreshKey] = useState(0)
  const [mpRefreshKey, setMpRefreshKey] = useState(0)

  const handleMarketplaceInstalled = () => {
    setActiveTab('installed')
    setRefreshKey(k => k + 1)
  }

  const handleMarketplaceDetected = () => {
    setActiveTab('marketplace')
    setMpRefreshKey(k => k + 1)
  }

  return (
    <Box p="6">
      <HStack mb="4" gap="2">
        <Puzzle size={20} />
        <Text fontSize="xl" fontWeight="bold">插件管理</Text>
      </HStack>

      <Tabs.Root value={activeTab} onValueChange={(d) => setActiveTab(d.value)}>
        <Tabs.List mb="4">
          <Tabs.Trigger value="installed">
            已安装
          </Tabs.Trigger>
          <Tabs.Trigger value="marketplace">
            <Store size={14} /> 插件市场
          </Tabs.Trigger>
        </Tabs.List>

        <Tabs.Content value="installed" pt="2">
          <InstalledTab key={refreshKey} onMarketplaceDetected={handleMarketplaceDetected} />
        </Tabs.Content>

        <Tabs.Content value="marketplace" pt="2">
          <MarketplaceTab key={mpRefreshKey} onInstalled={handleMarketplaceInstalled} />
        </Tabs.Content>
      </Tabs.Root>
    </Box>
  )
}
