import { describe, expect, it } from 'vitest';
import { buildHasarSaleDocument } from '../src/hasar-sale';
import type { ReceiptRequest } from '../src/receipt-pdf';

const sale: ReceiptRequest = {
  saleId: '11111111-1111-4111-8111-111111111111', documentType: 'nonFiscalTicket',
  branch: 'La Rústica', terminal: 'Caja A', cashier: 'cajero1',
  confirmedAtUtc: '2026-10-14T12:00:00Z', total: 1100, changeAmount: 0,
  accountChargeAmount: 0, creditAppliedAmount: 0, customerCode: null, customerName: null,
  lines: [{ code: '2546', name: 'CORTITO C/FALDA EXP', unit: 'kg', quantity: 1,
    unitPrice: 1100, lineTotal: 1100, pieceIdentifier: '250661', taxRatePercent: 21 }],
  payments: [{ method: 'cash', tenderedAmount: 1100, appliedAmount: 1100 }],
};
const authorized: NonNullable<ReceiptRequest['fiscal']> = {
  saleDocumentType: 'FiscalTicket', issuerCuit: '20123456789', issuerName: 'Laboratorio',
  issuerAddress: 'Córdoba', issuerIibb: null, issuerActivityStartDate: null,
  pointOfSale: 99, voucherType: 6, number: 123, issueDate: '2026-10-14',
  receiverName: null, receiverAddress: null, receiverTaxStatus: 'finalConsumer',
  receiverDocumentType: 99, receiverDocumentNumber: 0,
  vatBreakdown: [{ ratePercent: 21, taxableBase: 826.45, taxAmount: 173.55 }],
  exemptAmount: 0, notTaxedAmount: 0, cae: '12345678901234', caeExpiry: '2026-10-24',
};

describe('Hasar confirmed sale mapping', () => {
  it('prints actual sale lines, piece and payment in a non-fiscal document', () => {
    const document = buildHasarSaleDocument(sale);
    expect(document.kind).toBe('nonFiscal');
    if (document.kind !== 'nonFiscal') return;
    expect(document.lines.join('\n')).toContain('CORTITO C/FALDA EXP #250661');
    expect(document.lines.join('\n')).toContain('TOTAL: $1100.00');
    expect(document.lines.join('\n')).toContain('EFECTIVO: $1100.00');
    expect(document.lines.every((line) => line.length <= 45 && /^[\x20-\x7e]+$/.test(line)))
      .toBe(true);
  });

  it('maps an ARCA-authorized B fiscal ticket with discount and cash change', () => {
    const document = buildHasarSaleDocument({ ...sale, documentType: 'fiscalTicket',
      total: 1000, discountAmount: 100, discountReason: 'Promoción', changeAmount: 100,
      payments: [{ method: 'cash', tenderedAmount: 1100, appliedAmount: 1000 }],
      fiscal: authorized,
    });
    expect(document.kind).toBe('fiscal');
    if (document.kind !== 'fiscal') return;
    expect(document.ticket.items).toEqual([{
      name: 'CORTITO C/FALDA #250661', quantity: 1, unitPrice: 1100, vatRate: 21,
    }]);
    expect(document.ticket.discount).toEqual({ name: 'PROMOCION', amount: 100 });
    expect(document.ticket.tenders).toEqual([{ name: 'EFECTIVO', amount: 1100 }]);
    expect(document.ticket.fiscalText).toContain('CAE HOMO 12345678901234');
  });

  it('rejects missing VAT, voucher A and a mismatched total before any device command', () => {
    expect(() => buildHasarSaleDocument({ ...sale, documentType: 'fiscalTicket' }))
      .toThrow('HASAR_FISCAL_TICKET_B_ONLY');
    expect(() => buildHasarSaleDocument({ ...sale, documentType: 'fiscalTicket',
      fiscal: { ...authorized, voucherType: 1 } })).toThrow('HASAR_FISCAL_TICKET_B_ONLY');
    expect(() => buildHasarSaleDocument({ ...sale, documentType: 'fiscalTicket',
      fiscal: authorized,
      lines: [{ ...sale.lines[0]!, taxRatePercent: null }] }))
      .toThrow('HASAR_SALE_VAT_MISSING');
    expect(() => buildHasarSaleDocument({ ...sale, total: 1200, documentType: 'fiscalTicket',
      fiscal: authorized })).toThrow('HASAR_SALE_TOTAL_MISMATCH');
  });
});
