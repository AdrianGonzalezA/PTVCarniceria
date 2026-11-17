import { describe, expect, it } from 'vitest';
import { makeKretzWeightEan, makeKretzConsolidatedQr,
  makeRandomKretzCuts, nextSimulatedWeight } from '../src/kretz-emulator';

describe('Kretz Report NX laboratory emulator', () => {
  it('encodes grams with PLU and a valid EAN13 check digit', () => {
    expect(makeKretzWeightEan('00123', 0.75)).toBe('2000123007502');
    expect(makeKretzWeightEan('00045', 0.5)).toBe('2000045005006');
    expect(() => makeKretzWeightEan('123456', 0.75)).toThrow();
    expect(() => makeKretzWeightEan('00123', 0.7505)).toThrow();
  });

  it('emits the documented header and one EAN13 per weighed cut', () => {
    expect(makeKretzConsolidatedQr([
      { plu: '00123', weightKg: 0.75 }, { plu: '00045', weightKg: 0.5 },
    ], { now: new Date(2026, 10, 17, 13, 39), ticketNumber: 3, seller: ' A', scaleNumber: 1 })).toBe(
      '17/11/2613:390003 A0102\n2000123007502\n2000045005006');
  });

  it('generates complete random tickets without manual item entry', () => {
    const cuts = makeRandomKretzCuts(['00123', '00045', '00046', '00047', '00048'], () => 0.5);
    expect(cuts).toHaveLength(5);
    expect(new Set(cuts.map(cut => cut.plu)).size).toBe(5);
    expect(cuts.every(cut => cut.weightKg === 1.45)).toBe(true);
    expect(makeKretzConsolidatedQr(cuts, {
      now: new Date(2026, 10, 17, 13, 39), ticketNumber: 3, seller: ' A', scaleNumber: 1,
    }).split('\n')).toHaveLength(6);
    expect(() => makeRandomKretzCuts(['00123', '00045'])).toThrow('INVALID_KRETZ_PLU_LIST');
  });

  it('keeps each QR between three and six distinct articles', () => {
    const plus = ['00001', '00002', '00003', '00004', '00005', '00006', '00007'];
    for (const random of [() => 0, () => 0.5, () => 0.999]) {
      const cuts = makeRandomKretzCuts(plus, random);
      expect(cuts.length).toBeGreaterThanOrEqual(3);
      expect(cuts.length).toBeLessThanOrEqual(6);
      expect(new Set(cuts.map(cut => cut.plu)).size).toBe(cuts.length);
    }
  });

  it('creates stable readings in grams for the virtual scale', () => {
    expect(nextSimulatedWeight(() => 0)).toBe(0.2);
    expect(nextSimulatedWeight(() => 0.5)).toBe(1.85);
  });
});
