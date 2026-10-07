import { describe, expect, it } from 'vitest';
import { createDiagnostic } from '../src/diagnostic';

describe('native diagnostic', () => {
  it('returns only the safe, typed runtime summary', () => {
    expect(createDiagnostic('win32', '44.5.0')).toEqual({
      electronVersion: '44.5.0',
      platform: 'win32',
      status: 'ready',
    });
  });
});
