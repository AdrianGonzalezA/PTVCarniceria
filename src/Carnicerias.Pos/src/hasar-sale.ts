import type { ReceiptRequest } from './receipt-pdf';
import type { HasarFiscalTicket } from './hasar-ticket';

export type HasarSaleDocument =
  | { readonly kind: 'fiscal'; readonly ticket: HasarFiscalTicket }
  | { readonly kind: 'nonFiscal'; readonly lines: readonly string[] };

const paymentNames: Record<string, string> = {
  cash: 'EFECTIVO', debit: 'DEBITO', credit: 'CREDITO', transfer: 'TRANSFERENCIA',
  mercadoPago: 'MERCADO PAGO', cheque: 'CHEQUE',
};

function ascii(value: string, maximum: number): string {
  const normalized = value.normalize('NFD').replace(/[\u0300-\u036f]/g, '')
    .toUpperCase().replace(/[^\x20-\x7e]/g, ' ').replace(/\s+/g, ' ').trim();
  return (normalized || 'SIN DESCRIPCION').slice(0, maximum);
}

function cents(value: number): number {
  if (!Number.isFinite(value) || value < 0 ||
      Math.abs(value * 100 - Math.round(value * 100)) > 0.000001)
    throw new Error('HASAR_SALE_AMOUNT_INVALID');
  return Math.round(value * 100);
}

function money(value: number): string { return value.toFixed(2); }

function lineDescription(line: ReceiptRequest['lines'][number], maximum: number): string {
  const piece = line.pieceIdentifier ? ` #${ascii(line.pieceIdentifier, 12)}` : '';
  return `${ascii(line.name, Math.max(1, maximum - piece.length))}${piece}`.slice(0, maximum);
}

function fiscalTicket(receipt: ReceiptRequest): HasarFiscalTicket {
  if (!receipt.fiscal || receipt.fiscal.saleDocumentType !== 'FiscalTicket' ||
      receipt.fiscal.voucherType !== 6 || receipt.fiscal.receiverTaxStatus !== 'finalConsumer')
    throw new Error('HASAR_FISCAL_TICKET_B_ONLY');
  const items = receipt.lines.map((line) => {
    if (line.taxRatePercent == null || ![0, 10.5, 21, 27].includes(line.taxRatePercent))
      throw new Error('HASAR_SALE_VAT_MISSING');
    if (cents(line.lineTotal) !== Math.round(line.quantity * line.unitPrice * 100 + 1e-7))
      throw new Error('HASAR_SALE_LINE_TOTAL_MISMATCH');
    return { name: lineDescription(line, 23), quantity: line.quantity,
      unitPrice: line.unitPrice, vatRate: line.taxRatePercent };
  });
  const gross = receipt.lines.reduce((sum, line) => sum + cents(line.lineTotal), 0);
  const discount = cents(receipt.discountAmount ?? 0);
  if (gross - discount !== cents(receipt.total))
    throw new Error('HASAR_SALE_TOTAL_MISMATCH');
  const tenders = receipt.payments.map((payment) => ({
    name: paymentNames[payment.method] ?? 'OTRO',
    amount: payment.method === 'cash' ? payment.tenderedAmount : payment.appliedAmount,
  }));
  if (receipt.accountChargeAmount > 0)
    tenders.push({ name: 'CUENTA CORRIENTE', amount: receipt.accountChargeAmount });
  if (receipt.creditAppliedAmount > 0)
    tenders.push({ name: 'SALDO A FAVOR', amount: receipt.creditAppliedAmount });
  if (tenders.length > 5) throw new Error('HASAR_TENDERS_INVALID');
  const paid = tenders.reduce((sum, tender) => sum + cents(tender.amount), 0);
  if (paid !== cents(receipt.total) + cents(receipt.changeAmount))
    throw new Error('HASAR_TENDERS_TOTAL_MISMATCH');
  return { items, total: receipt.total, tenders, fiscalText: [
    'ARCA HOMO SIN VALIDEZ',
    `CAE HOMO ${receipt.fiscal.cae}`,
    `PV ${String(receipt.fiscal.pointOfSale).padStart(5, '0')} N ${String(receipt.fiscal.number).padStart(8, '0')}`,
  ],
    ...(discount > 0 ? { discount: { name: ascii(receipt.discountReason ?? 'DESCUENTO', 28),
      amount: discount / 100 } } : {}) };
}

function nonFiscalLines(receipt: ReceiptRequest): readonly string[] {
  const date = new Date(receipt.confirmedAtUtc).toLocaleString('es-AR', {
    timeZone: 'America/Argentina/Buenos_Aires', hour12: false,
  });
  const lines = [
    `VENTA ${receipt.saleId.slice(0, 8).toUpperCase()} - ${date}`,
    `${ascii(receipt.branch, 20)} / ${ascii(receipt.terminal, 20)}`,
    `CAJERO: ${ascii(receipt.cashier, 35)}`,
  ];
  for (const item of receipt.lines) {
    lines.push(lineDescription(item, 45));
    lines.push(`${item.quantity.toFixed(item.unit === 'kg' ? 3 : 0)} ${ascii(item.unit, 5)} X $${money(item.unitPrice)}`);
    lines.push(`IMPORTE: $${money(item.lineTotal)}`);
  }
  if ((receipt.discountAmount ?? 0) > 0)
    lines.push(`DESCUENTO: -$${money(receipt.discountAmount ?? 0)}`);
  lines.push(`TOTAL: $${money(receipt.total)}`);
  for (const payment of receipt.payments)
    lines.push(`${paymentNames[payment.method] ?? 'OTRO'}: $${money(payment.appliedAmount)}`);
  if (receipt.accountChargeAmount > 0)
    lines.push(`CUENTA CORRIENTE: $${money(receipt.accountChargeAmount)}`);
  if (receipt.creditAppliedAmount > 0)
    lines.push(`SALDO A FAVOR: $${money(receipt.creditAppliedAmount)}`);
  if (receipt.changeAmount > 0) lines.push(`VUELTO: $${money(receipt.changeAmount)}`);
  return lines.map((line) => ascii(line, 45));
}

export function buildHasarSaleDocument(receipt: ReceiptRequest): HasarSaleDocument {
  if (!/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(receipt.saleId))
    throw new Error('HASAR_SALE_ID_INVALID');
  if (receipt.documentType === 'fiscalTicket')
    return { kind: 'fiscal', ticket: fiscalTicket(receipt) };
  if (receipt.documentType === 'nonFiscalTicket')
    return { kind: 'nonFiscal', lines: nonFiscalLines(receipt) };
  throw new Error('HASAR_DOCUMENT_TYPE_UNSUPPORTED');
}
