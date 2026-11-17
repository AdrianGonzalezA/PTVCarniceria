import { describe, expect, it } from 'vitest';
import { makeKretzWeightEan, makeKretzConsolidatedQr } from '../src/kretz-emulator';

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
});
