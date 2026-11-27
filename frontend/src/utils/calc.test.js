import { describe, expect, it } from 'vitest'
import { distribute, fillRight, occLevel, parseClipboard, parseHours, pct, sumMonths } from './calc.js'
import { compareVersions } from './format.js'

describe('distribute', () => {
  it('fördelar jämnt i heltal och lägger resten på tidigaste månaderna', () => {
    const out = distribute(100, [0, 1, 2])
    expect(out.slice(0, 3)).toEqual([34, 33, 33])
    expect(out.reduce((a, b) => a + b)).toBe(100)
  })
  it('viktar efter kapacitet så att juli får mindre', () => {
    const cap = [144, 144, 144, 144, 144, 130, 36, 130, 144, 144, 144, 122]
    const out = distribute(600, [5, 6, 7], cap)
    expect(out[6]).toBeLessThan(out[5])
    expect(out[5] + out[6] + out[7]).toBe(600)
  })
  it('hanterar nollvikter och tomma intervall', () => {
    expect(distribute(10, [], null).every((x) => x === 0)).toBe(true)
    expect(distribute(10, [0, 1], [0, 0])).toEqual([5, 5, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0])
  })
})

describe('occLevel', () => {
  const t = { thresholdLow: 80, thresholdHigh: 100, thresholdCritical: 110 }
  it('klassar beläggning enligt trösklarna', () => {
    expect(occLevel(0, 144, t)).toBe('empty')
    expect(occLevel(50, 144, t)).toBe('low')
    expect(occLevel(130, 144, t)).toBe('ok')
    expect(occLevel(144, 144, t)).toBe('ok')
    expect(occLevel(150, 144, t)).toBe('near')
    expect(occLevel(170, 144, t)).toBe('over')
    expect(occLevel(10, 0, t)).toBe('over')
  })
  it('pct hanterar noll kapacitet', () => {
    expect(pct(0, 0)).toBe(0)
    expect(pct(5, 0)).toBe(Infinity)
  })
})

describe('Excel-hjälpare', () => {
  it('tolkar inklistrad TSV', () => {
    expect(parseClipboard('10\t20\n30\t\n')).toEqual([
      [10, 20],
      [30, null],
    ])
    expect(parseClipboard('1,5\tabc')[0][1]).toBeNaN()
  })
  it('parseHours', () => {
    expect(parseHours('')).toBe(0)
    expect(parseHours(' 12 ')).toBe(12)
    expect(parseHours('7,5')).toBe(8)
    expect(parseHours('-1')).toBeNaN()
    expect(parseHours('x')).toBeNaN()
  })
  it('fyll höger', () => {
    expect(fillRight([1, 2, 3, 4], 1)).toEqual([1, 2, 2, 2])
  })
  it('summerar månader', () => {
    expect(sumMonths([[1, 2], [3, 4]]).slice(0, 2)).toEqual([4, 6])
  })
})

describe('compareVersions', () => {
  it('jämför semver numeriskt', () => {
    expect(compareVersions('1.2.10', '1.2.9')).toBe(1)
    expect(compareVersions('1.0.0', '1.0.0')).toBe(0)
    expect(compareVersions('0.9', '1.0')).toBe(-1)
  })
})
