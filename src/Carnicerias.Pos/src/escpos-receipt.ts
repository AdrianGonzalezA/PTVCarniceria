import type { ReceiptRequest } from './receipt-pdf';
import { createFiscalQrUrl, fiscalInvoiceLineAmount } from './fiscal-invoice';
import { sendSerialBytes } from './serial-printer';

export interface EscPosDevice {
  readonly port: string;
  readonly baudRate: number;
  readonly paperWidthMm: 58 | 80;
}

const money = (value: number): string => value.toLocaleString('es-AR', {
  minimumFractionDigits: 2, maximumFractionDigits: 2,
});

function clean(value: string): string {
  return value.replace(/\u001b\[[0-?]*[ -/]*[@-~]/g, '')
    .replace(/[\u0000-\u001f\u007f-\u009f\p{Cf}]/gu, ' ')
    .normalize('NFD').replace(/\p{Mn}/gu, '')
    .replace(/[^\x20-\x7e]/g, '?').replace(/\s+/g, ' ').trim();
}

function wrap(value: string, columns: number): string[] {
  const words = clean(value).split(' ');
  const result: string[] = [];
  let current = '';
  for (const word of words) {
    if (!word) continue;
    if (current && current.length + word.length + 1 > columns) {
      result.push(current);
      current = '';
    }
    if (word.length > columns) {
      if (current) { result.push(current); current = ''; }
      for (let offset = 0; offset < word.length; offset += columns)
        result.push(word.slice(offset, offset + columns));
    } else current = current ? `${current} ${word}` : word;
  }
  if (current) result.push(current);
  return result.length ? result : [''];
}

function rightPair(label: string, value: string, columns: number): string[] {
  const left = clean(label);
  const right = clean(value);
  if (left.length + right.length + 1 > columns)
    return [...wrap(left, columns), right.padStart(columns)];
  return [left + ' '.repeat(columns - left.length - right.length) + right];
}

function qrCommand(url: string, paperWidthMm: 58 | 80): Buffer {
  const data = Buffer.from(url, 'ascii');
  const size = data.length + 3;
  return Buffer.concat([
    Buffer.from([0x1d, 0x28, 0x6b, 0x04, 0x00, 0x31, 0x41, 0x32, 0x00]),
    Buffer.from([0x1d, 0x28, 0x6b, 0x03, 0x00, 0x31, 0x43,
      paperWidthMm === 58 ? 0x03 : 0x04]),
    Buffer.from([0x1d, 0x28, 0x6b, 0x03, 0x00, 0x31, 0x45, 0x31]),
    Buffer.from([0x1d, 0x28, 0x6b, size & 0xff, size >> 8, 0x31, 0x50, 0x30]),
    data,
    Buffer.from([0x1d, 0x28, 0x6b, 0x03, 0x00, 0x31, 0x51, 0x30]),
  ]);
}

export function formatEscPosReceipt(receipt: ReceiptRequest, paperWidthMm: 58 | 80): Buffer {
  if (paperWidthMm !== 58 && paperWidthMm !== 80)
    throw new Error('THERMAL_PAPER_WIDTH_INVALID');
  const fiscal = receipt.fiscal;
  if (receipt.documentType === 'electronicInvoice' &&
      (!fiscal || fiscal.saleDocumentType !== 'ElectronicInvoice'))
    throw new Error('AUTHORIZED_INVOICE_REQUIRED');
  const columns = paperWidthMm === 58 ? 32 : 48;
  const chunks: Buffer[] = [Buffer.from([0x1b, 0x40])];
  const write = (value: string): void => { chunks.push(Buffer.from(`${value}\n`, 'ascii')); };
  const lines = (value: string): void => { for (const row of wrap(value, columns)) write(row); };
  const pair = (label: string, value: string): void => {
    for (const row of rightPair(label, value, columns)) write(row);
  };
  const rule = (): void => write('-'.repeat(columns));
  const center = (value: string): void => {
    chunks.push(Buffer.from([0x1b, 0x61, 0x01]));
    for (const row of wrap(value, columns)) write(row);
    chunks.push(Buffer.from([0x1b, 0x61, 0x00]));
  };
  const invoice = fiscal?.saleDocumentType === 'ElectronicInvoice';
  const title = invoice ? `FACTURA ${fiscal.voucherType === 1 ? 'A' : 'B'}`
    : fiscal ? 'TICKET FISCAL' : 'TICKET NO FISCAL';
  center(fiscal ? 'HOMOLOGACION - SIN VALIDEZ FISCAL' : 'NO FISCAL');
  chunks.push(Buffer.from([0x1d, 0x21, 0x11]));
  center(title);
  chunks.push(Buffer.from([0x1d, 0x21, 0x00]));
  rule();
  if (fiscal) {
    lines(fiscal.issuerName);
    lines(`CUIT ${fiscal.issuerCuit}`);
    lines(fiscal.issuerAddress);
    if (fiscal.issuerIibb) lines(`IIBB ${fiscal.issuerIibb}`);
    if (fiscal.issuerActivityStartDate)
      lines(`Inicio actividad ${fiscal.issuerActivityStartDate}`);
    lines(`${fiscal.pointOfSale.toString().padStart(5, '0')}-${fiscal.number.toString().padStart(8, '0')}`);
    lines(`Fecha emision ${fiscal.issueDate}`);
    rule();
    lines(`Cliente ${fiscal.receiverName ?? 'Consumidor final'}`);
    if (fiscal.receiverAddress) lines(`Domicilio ${fiscal.receiverAddress}`);
    if (receipt.recipientLocality || receipt.recipientProvince)
      lines(`Localidad / provincia ${[receipt.recipientLocality, receipt.recipientProvince]
        .filter(Boolean).join(' / ')}`);
    if (receipt.fiscalCustomerCode) lines(`Cliente Nro. ${receipt.fiscalCustomerCode}`);
    lines(`Documento ${fiscal.receiverDocumentType} ${fiscal.receiverDocumentNumber}`);
  } else lines(receipt.branch);
  rule();
  lines(`${receipt.branch} / ${receipt.terminal} / ${receipt.cashier}`);
  lines(`Venta ${receipt.saleId}`);
  if (receipt.purchaseOrderNumber) lines(`Pedido / OC ${receipt.purchaseOrderNumber}`);
  rule();
  for (const line of receipt.lines) {
    lines(`${line.code} ${line.name}`);
    if (line.pieceIdentifier) lines(`Pieza ${line.pieceIdentifier}`);
    if (line.pieceCount != null) lines(`Piezas fisicas ${line.pieceCount}`);
    const quantity = line.quantity.toLocaleString('es-AR', {
      minimumFractionDigits: line.unit === 'kg' ? 3 : 0, maximumFractionDigits: 3,
    });
    const value = invoice ? fiscalInvoiceLineAmount(receipt, line)
      : line.netAfterDiscount ?? line.lineTotal;
    lines(`${quantity} ${line.unit} x $ ${money(value / line.quantity)}`);
    pair('Importe', `$ ${money(value)}`);
  }
  if (receipt.discountAmount) lines(`Descuento incluido $ ${money(receipt.discountAmount)}`);
  rule();
  if (fiscal) {
    if (fiscal.voucherType === 1) {
      for (const item of fiscal.vatBreakdown) {
        pair(`Neto ${item.ratePercent}%`, `$ ${money(item.taxableBase)}`);
        pair(`IVA ${item.ratePercent}%`, `$ ${money(item.taxAmount)}`);
      }
      if (fiscal.exemptAmount) pair('Exento', `$ ${money(fiscal.exemptAmount)}`);
      if (fiscal.notTaxedAmount) pair('No gravado', `$ ${money(fiscal.notTaxedAmount)}`);
    } else {
      pair('IVA contenido', `$ ${money(fiscal.vatBreakdown.reduce(
        (sum, item) => sum + item.taxAmount, 0))}`);
    }
  }
  pair('TOTAL ARS', `$ ${money(receipt.total)}`);
  rule();
  for (const payment of receipt.payments)
    pair(payment.method, `$ ${money(payment.appliedAmount)}`);
  if (receipt.accountChargeAmount) pair('Cuenta corriente', `$ ${money(receipt.accountChargeAmount)}`);
  if (receipt.creditAppliedAmount) pair('Saldo a favor', `$ ${money(receipt.creditAppliedAmount)}`);
  if (receipt.changeAmount) pair('Vuelto', `$ ${money(receipt.changeAmount)}`);
  if (fiscal) {
    rule();
    lines(`CAE ${fiscal.cae}`);
    lines(`Vencimiento CAE ${fiscal.caeExpiry}`);
    if (invoice) {
      chunks.push(Buffer.from([0x1b, 0x61, 0x01]));
      chunks.push(qrCommand(createFiscalQrUrl(fiscal, receipt.total), paperWidthMm));
      chunks.push(Buffer.from([0x1b, 0x61, 0x00]));
    }
    center('HOMOLOGACION - SIN VALIDEZ FISCAL');
  }
  chunks.push(Buffer.from('\n\n\n', 'ascii'));
  return Buffer.concat(chunks);
}

export function sendEscPosReceipt(receipt: ReceiptRequest, device: EscPosDevice,
  createPort?: Parameters<typeof sendSerialBytes>[1]) {
  return sendSerialBytes(formatEscPosReceipt(receipt, device.paperWidthMm), createPort, device);
}
