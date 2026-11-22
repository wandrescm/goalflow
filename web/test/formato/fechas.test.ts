import { describe, expect, it } from 'vitest'
import { fechaHoraEnBogota, horaEnBogota, partesEnBogota } from '../../src/formato/fechas'

describe('T-22: las fechas se muestran en America/Bogota, no en la zona del equipo (R-45)', () => {
  it('el proceso de pruebas está en otra zona (Asia/Tokyo), como pide la Spec', () => {
    expect(Intl.DateTimeFormat().resolvedOptions().timeZone).toBe('Asia/Tokyo')
    // Control: con la zona del equipo, el mismo instante sería el 21 a las 10.
    const local = new Date('2026-11-21T01:00:00Z')
    expect([local.getDate(), local.getHours()]).toEqual([21, 10])
  })

  it('2026-11-21T01:00:00Z es el 20 de noviembre a las 20 en Bogotá (partes del día y de la hora, no un texto de la cultura)', () => {
    const partes = partesEnBogota('2026-11-21T01:00:00Z')

    expect(partes.dia).toBe('20')
    expect(partes.hora).toBe('20')
    expect([partes.mes, partes.anio, partes.minuto]).toEqual(['11', '2026', '00'])
  })

  it('la hora de medianoche es 00 y no 24', () => {
    expect(partesEnBogota('2026-11-21T05:00:00Z').hora).toBe('00')
  })

  it('fechaHoraEnBogota arma «dd/MM/aaaa HH:mm» con las partes de Bogotá', () => {
    expect(fechaHoraEnBogota('2026-11-21T01:00:00Z')).toBe('20/11/2026 20:00')
    expect(fechaHoraEnBogota(new Date('2026-01-05T13:05:00Z'))).toBe('05/01/2026 08:05')
  })

  it('horaEnBogota muestra solo la hora de carga con minutos', () => {
    expect(horaEnBogota(new Date('2026-11-21T15:30:00Z'))).toBe('10:30')
  })
})
