import { describe, expect, it } from 'vitest';
import type { ReceiptRequest } from '../src/receipt-pdf';
import { formatSerialReceipt } from '../src/serial-receipt';

const receipt: ReceiptRequest = {
  saleId: 'VENTA-123', branch: 'Sucursal Visual', terminal: 'Caja 1', cashier: 'cajero1',
  confirmedAtUtc: '2026-10-08T15:00:00Z', total: 10200, changeAmount: 150,
  accountChargeAmount: 0, creditAppliedAmount: 0,
  customerCode: null, customerName: null,
  lines: [
    { code: '2546', name: 'Cortito c/falda', unit: 'kg', quantity: 0.5,
      unitPrice: 10000, lineTotal: 5000, pieceIdentifier: '250661' },
    { code: '2546', name: 'Cortito c/falda', unit: 'kg', quantity: 0.52,
      unitPrice: 10000, lineTotal: 5200, pieceIdentifier: '250662' },
  ],
  payments: [{ method: 'cash', tenderedAmount: 10350, appliedAmount: 10200 }],
};

describe('serial receipt', () => {
  it('prints each traceable piece on its own line with totals and CRLF for PuTTY', () => {
    const text = formatSerialReceipt(receipt);
    expect(text).toContain('NO FISCAL\r\n');
    expect(text).toContain('COMPROBANTE DE PRUEBA');
    expect(text).toContain('Pieza 250661');
    expect(text).toContain('Pieza 250662');
    expect(text).toContain('0,500 kg');
    expect(text).toContain('0,520 kg');
    expect(text).toContain('TOTAL $ 10.200,00');
    expect(text).toContain('Vuelto $ 150,00');
    expect(text.endsWith('\r\n\r\n\r\n')).toBe(true);
  });

  it('does not pass control codes or embedded newlines from product names to the terminal', () => {
    const text = formatSerialReceipt({ ...receipt, lines: [{ ...receipt.lines[0],
      name: 'Asado\x1b[31m\r\nTOTAL FALSO' }] });
    expect(text).not.toContain('\x1b');
    expect(text).toContain('Asado TOTAL FALSO');
    expect(text.match(/TOTAL \$/g)).toHaveLength(1);
  });

  it('prints the account charge separately from money received', () => {
    const text = formatSerialReceipt({ ...receipt, accountChargeAmount: 5200,
      customerCode: 'CLI-001', customerName: 'Cliente prueba',
      payments: [{ method: 'cash', tenderedAmount: 5000, appliedAmount: 5000 }], changeAmount: 0 });

    expect(text).toContain('Efectivo $ 5.000,00');
    expect(text).toContain('Cuenta corriente $ 5.200,00');
    expect(text).toContain('Cliente Cliente prueba (CLI-001)');
    expect(text).toContain('TOTAL $ 10.200,00');
  });
});
