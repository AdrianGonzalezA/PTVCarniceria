import { describe, expect, it } from 'vitest';
import { formatLocalizedDecimal, parseLocalizedDecimal } from './localized-decimal';

describe('localized decimal', () => {
  it('treats a single comma or point as a decimal separator, never as thousands', () => {
    expect(parseLocalizedDecimal('1.234', 3)).toBe(1.234);
    expect(parseLocalizedDecimal('1,234', 3)).toBe(1.234);
    expect(parseLocalizedDecimal('1.234', 2)).toBeNull();
    expect(parseLocalizedDecimal('1,234', 2)).toBeNull();
    expect(parseLocalizedDecimal('-.5', 3)).toBe(-0.5);
  });

  it('rejects ambiguous or invalid entry rather than inferring thousands', () => {
    expect(parseLocalizedDecimal('1.234,56', 2)).toBeNull();
    expect(parseLocalizedDecimal('1,234.56', 2)).toBeNull();
    expect(parseLocalizedDecimal('1.234.567', 3)).toBeNull();
    expect(parseLocalizedDecimal('12a', 2)).toBeNull();
    expect(parseLocalizedDecimal('', 2)).toBeNull();
    expect(parseLocalizedDecimal('1,2', 0)).toBeNull();
  });

  it('displays grouping with point and decimals with comma', () => {
    expect(formatLocalizedDecimal(1234567.5, 2)).toBe('1.234.567,50');
    expect(formatLocalizedDecimal(1234.567, 3)).toBe('1.234,567');
    expect(formatLocalizedDecimal(1234, 0)).toBe('1.234');
  });
});
