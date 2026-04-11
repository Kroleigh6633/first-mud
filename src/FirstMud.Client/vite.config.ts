import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// https://vite.dev/config/
export default defineConfig({
  plugins: [react()],
  server: {
    // Docker volume mounts on Windows don't emit inotify events.
    // Force polling so file edits on the host trigger HMR in the container.
    watch: {
      usePolling: true,
      interval: 500,
    },
    host: '0.0.0.0',
    // Allow Docker compose hostnames so the e2e container can reach this
    // dev server via `http://client:5173`. `localhost` is whitelisted by
    // default.
    allowedHosts: ['client', 'localhost', '127.0.0.1', 'host.docker.internal'],
    // Proxy API + SignalR hub to the gameserver so the browser can use
    // same-origin paths. The upstream target is resolved by the container's
    // own DNS — `gameserver` on the compose network, or the GAMESERVER_URL
    // env var when overridden.
    proxy: {
      '/api': {
        target: process.env.GAMESERVER_URL ?? 'http://gameserver:5000',
        changeOrigin: true,
      },
      '/gamehub': {
        target: process.env.GAMESERVER_URL ?? 'http://gameserver:5000',
        changeOrigin: true,
        ws: true,
      },
    },
  },
})
