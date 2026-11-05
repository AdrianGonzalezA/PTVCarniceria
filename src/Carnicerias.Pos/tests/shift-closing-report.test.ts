import { describe, expect, it } from 'vitest';
import { shiftClosingHtml, shiftClosingLines, validateShiftClosingReport } from '../src/shift-closing-report';

const report = {
  version: 1, shiftId: '11111111-1111-1111-1111-111111111111',
  companyName: 'Carnes & Más', branchName: '<Centro>', terminalName: 'Caja A',
  cashierName: 'cajero1', openedAtUtc: '2026-11-05T10:00:00Z',
  closedAtUtc: '2026-11-05T18:00:00Z', openingCash: 100,
  cashSales: 400, nonCashSales: 200, accountSales: 50, salesTotal: 650,
  cashCollections: 80, nonCashCollections: 25, creditApplied: 10,
  cashWithdrawals: 20, expectedCash: 560, countedCash: 550,
  handedCash: 500, cashDifference: -10,
  paymentBreakdown: [{ method: 'cash', amount: 480 }],
};

describe('shift closing report', () => {
  it('formats immutable totals for a nonfiscal receipt and escapes HTML', () => {
    const valid = validateShiftClosingReport(report);
    const lines = shiftClosingLines(valid);
    expect(lines).toContain('Efectivo esperado: $ 560,00');
    expect(lines).toContain('Diferencia: $ -10,00');
    expect(lines).toContain('Cobros cash: $ 480,00');
    const html = shiftClosingHtml(valid);
    expect(html).toContain('Carnes &amp; Más');
    expect(html).toContain('&lt;Centro&gt;');
    expect(html).not.toContain('<Centro>');
  });

  it('rejects malformed and nonfinite reports before native output', () => {
    expect(() => validateShiftClosingReport({ ...report, expectedCash: Infinity })).toThrow('SHIFT_REPORT_INVALID');
    expect(() => validateShiftClosingReport({ ...report, paymentBreakdown: [{ amount: 1 }] })).toThrow('SHIFT_REPORT_INVALID');
  });
});
