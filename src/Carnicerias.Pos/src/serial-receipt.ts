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
  const fiscal = receipt.fiscal;
  const lines = [
    '========================================',
    fiscal ? (fiscal.saleDocumentType === 'FiscalTicket'
      ? 'TICKET FISCAL DE PRUEBA' : 'FACTURA ELECTRONICA DE PRUEBA') : 'COMPROBANTE DE PRUEBA',
    fiscal ? 'HOMOLOGACION - SIN VALIDEZ FISCAL' : 'NO FISCAL',
    '========================================',
    ...(fiscal ? [
      printable(fiscal.issuerName),
      `CUIT ${fiscal.issuerCuit}`,
      printable(fiscal.issuerAddress),
      `FACTURA ${fiscal.voucherType === 1 ? 'A' : 'B'} ${fiscal.pointOfSale.toString().padStart(5, '0')}-${fiscal.number.toString().padStart(8, '0')}`,
      `Fecha ${fiscal.issueDate}`,
      `Receptor ${printable(fiscal.receiverName ?? 'Consumidor final')}`,
      `Documento ${fiscal.receiverDocumentType} ${fiscal.receiverDocumentNumber}`,
      ...(fiscal.receiverAddress ? [`Domicilio ${printable(fiscal.receiverAddress)}`] : []),
    ] : []),
    printable(receipt.branch),
    `${printable(receipt.terminal)} - ${printable(receipt.cashier)}`,
    `Venta ${printable(receipt.saleId)}`,
    date,
    '----------------------------------------',
    'PRODUCTOS',
  ];
  if (receipt.customerName && receipt.customerCode)
    lines.push(`Cliente ${printable(receipt.customerName)} (${printable(receipt.customerCode)})`);
  for (const line of receipt.lines) {
    const quantity = line.quantity.toLocaleString('es-AR', {
      minimumFractionDigits: line.unit === 'kg' ? 3 : 0, maximumFractionDigits: 3,
    });
    lines.push(`${printable(line.name)} (${printable(line.code)})`);
    if (line.pieceIdentifier) lines.push(`  Pieza ${printable(line.pieceIdentifier)}`);
    lines.push(`  ${quantity} ${printable(line.unit)} x $ ${money(line.unitPrice)}`);
    lines.push(`  Importe $ ${money(line.lineTotal)}`);
  }
  if ((receipt.discountAmount ?? 0) > 0) {
    lines.push(`Descuento global - $ ${money(receipt.discountAmount ?? 0)}`);
    lines.push(`  ${printable(receipt.discountReason ?? '')}`);
  }
  if (fiscal) {
    lines.push('----------------------------------------', 'DESGLOSE IMPOSITIVO DE PRUEBA');
    for (const item of fiscal.vatBreakdown)
      lines.push(`Neto ${item.ratePercent}% $ ${money(item.taxableBase)}`,
        `IVA ${item.ratePercent}% incluido $ ${money(item.taxAmount)}`);
    if (fiscal.exemptAmount > 0) lines.push(`Exento $ ${money(fiscal.exemptAmount)}`);
    if (fiscal.notTaxedAmount > 0) lines.push(`No gravado $ ${money(fiscal.notTaxedAmount)}`);
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
  if (fiscal) {
    lines.push(`CAE ${fiscal.cae}`, `Vencimiento CAE ${fiscal.caeExpiry}`,
      'QR PENDIENTE PARA PRODUCCION', 'HOMOLOGACION - SIN VALIDEZ FISCAL', '');
  } else lines.push('NO ES FACTURA NI COMPROBANTE FISCAL', '');
  return lines.join('\r\n') + '\r\n\r\n';
}
