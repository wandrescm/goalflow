import vue from '@vitejs/plugin-vue'
import { loadEnv } from 'vite'
import { defineConfig } from 'vitest/config'

export default defineConfig(({ command, mode }) => {
  // R-47: un build de producción sin VITE_API_BASE_URL, o con una URL que no empieza con https://, falla.
  if (command === 'build') {
    const url = loadEnv(mode, process.cwd(), 'VITE_').VITE_API_BASE_URL ?? ''
    if (!url.startsWith('https://')) {
      throw new Error('VITE_API_BASE_URL debe existir y empezar con https:// en un build de producción (Spec 011 R-47).')
    }
  }

  // T-22: la suite corre en una zona distinta de Bogotá, para que el formato de fechas nunca dependa de la del equipo.
  if (mode === 'test') process.env.TZ = 'Asia/Tokyo'

  return {
    plugins: [vue()],
    test: {
      environment: 'jsdom',
      include: ['test/**/*.test.ts'],
      env: { VITE_API_BASE_URL: 'https://api.goalflow.test' },
    },
  }
})
