import { describe, expect, it } from 'vitest'
import { rutaCatalogo, rutaDetalle, rutaGoleadores, rutaPartidos, rutaPosiciones, rutaValla } from '../../src/api/rutas'

const slug = 'liga ñ'
const codigo = 'HUMO-T1_a'

describe('T-17: los seis constructores de ruta codifican cada valor (R-36)', () => {
  it('construyen exactamente las seis rutas de la API, con un código con - y _ y un slug con caracteres que se codifican', () => {
    expect(rutaCatalogo(slug)).toBe('/liga%20%C3%B1/torneos')
    expect(rutaPartidos(slug, codigo, 1)).toBe('/liga%20%C3%B1/torneos/HUMO-T1_a/fases/1/partidos')
    expect(rutaDetalle(slug, codigo, 1, 'HUMO-T1-E1', 'HUMO-T1_E2')).toBe(
      '/liga%20%C3%B1/torneos/HUMO-T1_a/fases/1/partidos/HUMO-T1-E1/HUMO-T1_E2',
    )
    expect(rutaPosiciones(slug, codigo, 2)).toBe('/liga%20%C3%B1/torneos/HUMO-T1_a/fases/2/posiciones')
    expect(rutaGoleadores(slug, codigo)).toBe('/liga%20%C3%B1/torneos/HUMO-T1_a/goleadores')
    expect(rutaValla(slug, codigo)).toBe('/liga%20%C3%B1/torneos/HUMO-T1_a/valla')
  })

  it('un valor con separadores de ruta, consulta o fragmento se codifica en cada posición y nunca cambia la estructura', () => {
    const raro = 'a/b?c#d'
    const codificado = 'a%2Fb%3Fc%23d'
    expect(rutaCatalogo(raro)).toBe(`/${codificado}/torneos`)
    expect(rutaPartidos(raro, raro, 1)).toBe(`/${codificado}/torneos/${codificado}/fases/1/partidos`)
    expect(rutaDetalle(raro, raro, 1, raro, raro)).toBe(
      `/${codificado}/torneos/${codificado}/fases/1/partidos/${codificado}/${codificado}`,
    )
    expect(rutaPosiciones(raro, raro, 1)).toBe(`/${codificado}/torneos/${codificado}/fases/1/posiciones`)
    expect(rutaGoleadores(raro, raro)).toBe(`/${codificado}/torneos/${codificado}/goleadores`)
    expect(rutaValla(raro, raro)).toBe(`/${codificado}/torneos/${codificado}/valla`)
  })
})
