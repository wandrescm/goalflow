// @vitest-environment node
import { existsSync } from 'node:fs'
import { join } from 'node:path'
import { fileURLToPath } from 'node:url'
import { describe, expect, it } from 'vitest'
import { leer } from '../ayudantes/escaner'

const ruta = join(fileURLToPath(new URL('../..', import.meta.url)), 'wrangler.jsonc')

describe('T-26: el Worker es solo de assets, con rutas de aplicación de una sola página (R-49)', () => {
  it('wrangler.jsonc existe y es JSON sin comentarios', () => {
    expect(existsSync(ruta)).toBe(true)
    expect(() => JSON.parse(leer(ruta))).not.toThrow()
  })

  it('no tiene main, tiene un nombre fijo y sirve ./dist con single-page-application', () => {
    const configuracion = JSON.parse(leer(ruta)) as Record<string, unknown> & { assets?: Record<string, unknown> }
    expect('main' in configuracion).toBe(false)
    expect(configuracion.name).toBe('goalflow-web')
    expect(typeof configuracion.compatibility_date).toBe('string')
    expect(configuracion.assets?.directory).toBe('./dist')
    expect(configuracion.assets?.not_found_handling).toBe('single-page-application')
  })
})
