import React from 'react'
import ReactDOM from 'react-dom/client'
import { Provider } from '@/components/ui/provider'
import { Toaster } from '@/components/ui/toaster'
import App from './App'
// @ts-expect-error -- CSS side-effect import, no type declarations needed
import '@xyflow/react/dist/base.css'

ReactDOM.createRoot(document.getElementById('root')!).render(
  <React.StrictMode>
    <Provider>
      <App />
      <Toaster />
    </Provider>
  </React.StrictMode>,
)
