import React from 'react'
import ReactDOM from 'react-dom/client'
import { Provider } from '@/components/ui/provider'
import { Toaster } from '@/components/ui/toaster'
import App from './App'

ReactDOM.createRoot(document.getElementById('root')!).render(
  <React.StrictMode>
    <Provider>
      <App />
      <Toaster />
    </Provider>
  </React.StrictMode>,
)
