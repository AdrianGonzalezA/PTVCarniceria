import { describe, expect, it } from 'vitest';
import { createReceiptHtml, validateReceiptRequest } from '../src/receipt-pdf';

const validRequest = {
  saleId: 'sale-123', branch: 'Sucursal Centro', terminal: 'Caja 1', cashier: 'cajero1',
  confirmedAtUtc: '2026-10-08T13:00:00Z', total: 2400, changeAmount: 0,
  lines: [{ code: '8001', name: 'Pan rallado', unit: 'unidad', quantity: 1, unitPrice: 2400, lineTotal: 2400 }],
  payments: [{ method: 'cash', tenderedAmount: 2400, appliedAmount: 2400 }],
};

describe('virtual receipt PDF content', () => {
  it('renders a confirmed sale with a prominent non-fiscal notice', () => {
    const html = createReceiptHtml(validateReceiptRequest(validRequest));
    expect(html).toContain('DOCUMENTO DE PRUEBA');
    expect(html).toContain('NO FISCAL');
    expect(html).toContain('Pan rallado');
    expect(html).toContain('sale-123');
    expect(html).toContain('2.400,00');
  });

  it('escapes untrusted sale text and does not load remote assets', () => {
    const html = createReceiptHtml(validateReceiptRequest({
      ...validRequest, lines: [{ ...validRequest.lines[0], name: '<img src=x onerror=alert(1)>' }],
    }));
    expect(html).toContain('&lt;img src=x onerror=alert(1)&gt;');
    expect(html).not.toContain('<img');
    expect(html).toContain("default-src 'none'");
  });

  it.each([
    { total: -1 },
    { total: Number.POSITIVE_INFINITY },
    { lines: [] },
    { lines: [{ ...validRequest.lines[0], quantity: -2 }] },
    { payments: [{ ...validRequest.payments[0], method: 'unknown' }] },
    { cashier: 'x'.repeat(121) },
  ])('rejects malformed or excessive renderer input', (change) => {
    expect(() => validateReceiptRequest({ ...validRequest, ...change })).toThrow();
  });
});
