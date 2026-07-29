import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// The built UI is served by the ASP.NET app, so it is emitted straight into that project's
// wwwroot. In development, run `dotnet run` alongside `npm run dev` and the proxy paths below
// reach the API without any CORS setup.
export default defineConfig({
  plugins: [react()],
  build: {
    outDir: '../src/StaatAppProxy/wwwroot',
    emptyOutDir: true,
  },
  server: {
    port: 5173,
    proxy: {
      '/admin': 'http://localhost:5000',
      '/echo': 'http://localhost:5000',
      '/healthz': 'http://localhost:5000',
    },
  },
})
