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
  },
})
