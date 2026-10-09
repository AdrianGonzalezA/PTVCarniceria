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

  it('shows the persisted discount and escapes its reason', () => {
    const html = createReceiptHtml(validateReceiptRequest({ ...validRequest,
      total: 2200, discountAmount: 200, discountReason: '<promoción>',
      payments: [{ method: 'cash', tenderedAmount: 2200, appliedAmount: 2200 }],
    }));
    expect(html).toContain('Descuento global');
    expect(html).toContain('&lt;promoción&gt;');
    expect(html).toContain('2.200,00');
    expect(() => validateReceiptRequest({ ...validRequest, discountAmount: 200 })).toThrow();
  });

  it('renders a full account charge without inventing a cash payment', () => {
    const html = createReceiptHtml(validateReceiptRequest({
      ...validRequest, payments: [], accountChargeAmount: 2400,
      customerCode: 'CLI-001', customerName: 'Cliente cuenta',
    }));

    expect(html).toContain('Cuenta corriente');
    expect(html).toContain('2.400,00');
    expect(html).not.toContain('<td>Efectivo</td>');
    expect(html).toContain('Cliente cuenta');
    expect(html).toContain('CLI-001');
  });

  it('requires a customer snapshot for account charges and escapes its name', () => {
    expect(() => validateReceiptRequest({ ...validRequest, payments: [], accountChargeAmount: 2400 })).toThrow();
    const html = createReceiptHtml(validateReceiptRequest({ ...validRequest, payments: [],
      accountChargeAmount: 2400, customerCode: 'CLI-001', customerName: '<script>alert(1)</script>' }));
    expect(html).toContain('&lt;script&gt;alert(1)&lt;/script&gt;');
    expect(html).not.toContain('<script>');
  });

  it('shows customer credit as settlement without a new cash payment', () => {
    const html = createReceiptHtml(validateReceiptRequest({ ...validRequest, payments: [],
      creditAppliedAmount: 2400, customerCode: 'CLI-001', customerName: 'Cliente cuenta' }));
    expect(html).toContain('Saldo a favor aplicado');
    expect(html).not.toContain('<td>Efectivo</td>');
  });

  it('prints each traceable piece with its own weight and identifier', () => {
    const html = createReceiptHtml(validateReceiptRequest({ ...validRequest,
      lines: [
        { code: '1002', name: 'Asado', unit: 'kg', quantity: 0.5, unitPrice: 11500,
          lineTotal: 5750, pieceIdentifier: '999001' },
        { code: '1002', name: 'Asado', unit: 'kg', quantity: 0.75, unitPrice: 11500,
          lineTotal: 8625, pieceIdentifier: '999002' },
      ],
    }));
    expect(html).toContain('Pieza 999001 · 0,500 kg');
    expect(html).toContain('Pieza 999002 · 0,750 kg');
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
