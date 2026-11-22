// @vitest-environment node
import { join } from 'node:path'
import { fileURLToPath } from 'node:url'
import { ESLint } from 'eslint'
import { describe, expect, it } from 'vitest'
import { leer } from '../ayudantes/escaner'

const raiz = fileURLToPath(new URL('../..', import.meta.url))

/** Pasa el archivo por la configuración real de ESLint, sin respetar los ignores (los canarios están excluidos del lint normal). */
async function mensajesDe(archivo: string) {
  const eslint = new ESLint({ cwd: raiz, ignore: false })
  const ruta = join(raiz, archivo)
  const [resultado] = await eslint.lintText(leer(ruta), { filePath: ruta })
  return resultado?.messages ?? []
}

const comoError = (ruleId: string) => (m: { ruleId: string | null; severity: number }) => m.ruleId === ruleId && m.severity === 2

// La primera instancia de ESLint carga la configuración y el analizador de TypeScript: en frío tarda más que los 5 s por defecto.
describe('T-23 (lint): los sumideros HTML y el almacenamiento están prohibidos como error (R-46, R-47)', { timeout: 60_000 }, () => {
  it('el canario con v-html reporta vue/no-v-html como error', async () => {
    expect((await mensajesDe('test/canarios/ConVHtml.vue')).some(comoError('vue/no-v-html'))).toBe(true)
  })

  it('el canario con innerHTML reporta no-restricted-syntax como error', async () => {
    expect((await mensajesDe('test/canarios/ConInnerHtml.ts')).some(comoError('no-restricted-syntax'))).toBe(true)
  })

  it('el canario con almacenamiento y cookies reporta no-restricted-globals y no-restricted-properties como error', async () => {
    const mensajes = await mensajesDe('test/canarios/ConAlmacenamiento.ts')
    expect(mensajes.some(comoError('no-restricted-globals'))).toBe(true)
    expect(mensajes.some(comoError('no-restricted-properties'))).toBe(true)
  })

  it('control positivo: un archivo limpio de la aplicación no reporta ningún error', async () => {
    const errores = (await mensajesDe('src/AplicacionGoalFlow.vue')).filter((m) => m.severity === 2)
    expect(errores).toEqual([])
  })
})
