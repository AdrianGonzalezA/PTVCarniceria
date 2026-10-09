import type { ReceiptRequest } from './receipt-pdf';

const paymentNames: Record<string, string> = {
  cash: 'Efectivo', debit: 'Débito', credit: 'Crédito', transfer: 'Transferencia',
  mercadoPago: 'Mercado Pago', cheque: 'Cheque',
};

const money = (value: number): string => value.toLocaleString('es-AR', {
  minimumFractionDigits: 2, maximumFractionDigits: 2,
});

function printable(value: string): string {
  return value
    .replace(/\u001b(?:\[[0-?]*[ -/]*[@-~]|\][^\u0007]*(?:\u0007|\u001b\\))?/g, '')
    .replace(/[\u0000-\u001f\u007f-\u009f\p{Cf}]/gu, ' ')
    .replace(/\s+/g, ' ').trim();
}

export function formatSerialReceipt(receipt: ReceiptRequest): string {
  const date = new Date(receipt.confirmedAtUtc).toLocaleString('es-AR', {
    timeZone: 'America/Argentina/Buenos_Aires',
  });
  const lines = [
    '========================================',
    'COMPROBANTE DE PRUEBA',
    'NO FISCAL',
    '========================================',
    printable(receipt.branch),
    `${printable(receipt.terminal)} - ${printable(receipt.cashier)}`,
    `Venta ${printable(receipt.saleId)}`,
    date,
    '----------------------------------------',
    'PRODUCTOS',
  ];
  if (receipt.customerName && receipt.customerCode)
    lines.splice(8, 0, `Cliente ${printable(receipt.customerName)} (${printable(receipt.customerCode)})`);
  for (const line of receipt.lines) {
    const quantity = line.quantity.toLocaleString('es-AR', {
      minimumFractionDigits: line.unit === 'kg' ? 3 : 0, maximumFractionDigits: 3,
    });
    lines.push(`${printable(line.name)} (${printable(line.code)})`);
    if (line.pieceIdentifier) lines.push(`  Pieza ${printable(line.pieceIdentifier)}`);
    lines.push(`  ${quantity} ${printable(line.unit)} x $ ${money(line.unitPrice)}`);
    lines.push(`  Importe $ ${money(line.lineTotal)}`);
  }
  lines.push('----------------------------------------', 'PAGOS');
  for (const payment of receipt.payments)
    lines.push(`${paymentNames[payment.method] ?? printable(payment.method)} $ ${money(payment.appliedAmount)}`);
  if (receipt.accountChargeAmount > 0)
    lines.push(`Cuenta corriente $ ${money(receipt.accountChargeAmount)}`);
  if (receipt.creditAppliedAmount > 0)
    lines.push(`Saldo a favor $ ${money(receipt.creditAppliedAmount)}`);
  lines.push('----------------------------------------', `TOTAL $ ${money(receipt.total)}`);
  if (receipt.changeAmount > 0) lines.push(`Vuelto $ ${money(receipt.changeAmount)}`);
  lines.push('NO ES FACTURA NI COMPROBANTE FISCAL', '');
  return lines.join('\r\n') + '\r\n\r\n';
}
