import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';
import tailwindcss from '@tailwindcss/vite';

export default defineConfig({
  plugins: [react(), tailwindcss()],
  server: {
    port: 5173,
    // The TenderAI API (backend/TenderAI.Api)
    proxy: { '/api': 'http://localhost:5240' }
  }
});
