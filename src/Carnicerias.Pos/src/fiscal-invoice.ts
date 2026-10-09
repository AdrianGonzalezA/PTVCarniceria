import QRCode from 'qrcode';
import { type FiscalReceipt, type ReceiptRequest } from './receipt-pdf';

const money = (value: number): string => value.toLocaleString('es-AR', {
  minimumFractionDigits: 2, maximumFractionDigits: 2,
});

const escapeHtml = (value: string): string => value.replace(/[&<>"']/g, (character) => ({
  '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;',
})[character] ?? character);

const date = (iso: string): string => `${iso.slice(8, 10)}/${iso.slice(5, 7)}/${iso.slice(0, 4)}`;
const equalCents = (left: number, right: number): boolean => Math.abs(left - right) <= 0.011;

export function createFiscalQrUrl(fiscal: FiscalReceipt, total: number): string {
  const document = fiscal.receiverDocumentType === 99 && fiscal.receiverDocumentNumber === 0
    ? {} : { tipoDocRec: fiscal.receiverDocumentType, nroDocRec: fiscal.receiverDocumentNumber };
  const payload = {
    ver: 1,
    fecha: fiscal.issueDate,
    cuit: Number(fiscal.issuerCuit),
    ptoVta: fiscal.pointOfSale,
    tipoCmp: fiscal.voucherType,
    nroCmp: fiscal.number,
    importe: total,
    moneda: 'PES',
    ctz: 1,
    ...document,
    tipoCodAut: 'E',
    codAut: Number(fiscal.cae),
  };
  return `https://www.arca.gob.ar/fe/qr/?p=${encodeURIComponent(Buffer.from(JSON.stringify(payload)).toString('base64'))}`;
}

function lineAmount(receipt: ReceiptRequest, line: ReceiptRequest['lines'][number]): number {
  const final = line.netAfterDiscount ?? line.lineTotal;
  if (receipt.discountAmount && line.netAfterDiscount == null)
    throw new Error('Invoice discount allocation is missing');
  if (receipt.fiscal?.voucherType === 6) return final;
  if (line.netAfterDiscount == null || line.taxableBase == null || line.taxAmount == null)
    throw new Error('Invoice tax snapshot is missing');
  if (line.taxableBase === 0 && line.taxAmount === 0) return final;
  if (!equalCents(line.taxableBase + line.taxAmount, final))
    throw new Error('Invoice tax snapshot does not reconcile');
  return line.taxableBase;
}

export async function createFiscalInvoiceHtml(receipt: ReceiptRequest): Promise<string> {
  const fiscal = receipt.fiscal;
  if (!fiscal || fiscal.saleDocumentType !== 'ElectronicInvoice')
    throw new Error('An authorized electronic invoice is required');

  const isA = fiscal.voucherType === 1;
  const aReceiver = fiscal.receiverTaxStatus === 'registered' || fiscal.receiverTaxStatus === 'smallTaxpayer';
  if (isA !== aReceiver)
    throw new Error('Invoice class does not match the receiver tax status');
  if (isA && (fiscal.receiverDocumentType !== 80 || fiscal.receiverDocumentNumber === 0))
    throw new Error('Invoice A requires receiver CUIT');
  const amounts = receipt.lines.map((line) => lineAmount(receipt, line));
  const taxable = fiscal.vatBreakdown.reduce((sum, group) => sum + group.taxableBase, 0);
  const vat = fiscal.vatBreakdown.reduce((sum, group) => sum + group.taxAmount, 0);
  const expectedLines = isA ? taxable + fiscal.exemptAmount + fiscal.notTaxedAmount : receipt.total;
  if (!equalCents(amounts.reduce((sum, value) => sum + value, 0), expectedLines))
    throw new Error('Invoice lines do not reconcile with the fiscal authorization');

  const qrImage = await QRCode.toDataURL(createFiscalQrUrl(fiscal, receipt.total), {
    errorCorrectionLevel: 'M', margin: 1, width: 220,
    color: { dark: '#172925', light: '#FFFFFFFF' },
  });
  const receiver = fiscal.receiverTaxStatus === 'finalConsumer'
    ? 'A CONSUMIDOR FINAL' : (fiscal.receiverName ?? 'No informado');
  const taxStatus: Record<FiscalReceipt['receiverTaxStatus'], string> = {
    finalConsumer: 'Consumidor Final', registered: 'Responsable Inscripto',
    smallTaxpayer: 'Responsable Monotributo', exempt: 'IVA Exento',
  };
  const receiverDocument = fiscal.receiverDocumentType === 99 && fiscal.receiverDocumentNumber === 0
    ? 'Documento: NR'
    : `${fiscal.receiverDocumentType === 80 ? 'CUIT' : 'DNI'}: ${fiscal.receiverDocumentNumber}`;
  const paymentMethods: Record<string, string> = {
    cash: 'Efectivo', debit: 'Débito', credit: 'Crédito', transfer: 'Transferencia',
    mercadoPago: 'Mercado Pago', cheque: 'Cheque',
  };
  const saleCondition = [
    ...receipt.payments.map((payment) => paymentMethods[payment.method]),
    ...(receipt.accountChargeAmount ? ['Cuenta corriente'] : []),
    ...(receipt.creditAppliedAmount ? ['Saldo a favor'] : []),
  ].join(' / ');
  const onlyKg = receipt.lines.every((line) => line.unit === 'kg');
  const rows = receipt.lines.map((line, index) => `<tr>
    <td>${escapeHtml(line.code)}</td>
    <td>${escapeHtml(line.name)}${line.pieceIdentifier ? `<small>Pieza ${escapeHtml(line.pieceIdentifier)}</small>` : ''}</td>
    <td class="number">${line.quantity.toLocaleString('es-AR', {
      minimumFractionDigits: line.unit === 'kg' ? 3 : 0, maximumFractionDigits: 3,
    })} ${escapeHtml(line.unit)}</td>
    <td class="number">${money(amounts[index]! / line.quantity)}</td>
    <td class="number">${money(amounts[index]!)}</td>
  </tr>`).join('');
  const taxLines = isA
    ? `<div class="summary-row"><span>Neto gravado</span><span>${money(taxable)}</span></div>
      ${fiscal.vatBreakdown.map((group) => `<div class="summary-row"><span>IVA ${group.ratePercent}%</span><span>${money(group.taxAmount)}</span></div>`).join('')}
      ${fiscal.exemptAmount ? `<div class="summary-row"><span>Exento</span><span>${money(fiscal.exemptAmount)}</span></div>` : ''}
      ${fiscal.notTaxedAmount ? `<div class="summary-row"><span>No gravado</span><span>${money(fiscal.notTaxedAmount)}</span></div>` : ''}`
    : `<div class="summary-row"><span>Subtotal</span><span>${money(receipt.total)}</span></div>`;
  const transparency = isA ? '' : `<section class="transparency">
    <strong>Régimen de Transparencia Fiscal al Consumidor</strong>
    <span>(Ley 27.743)</span>
    <p>IVA&nbsp;contenido: $ ${money(vat)}</p>
    <p>Otros Impuestos Nacionales Indirectos: $ 0,00</p>
  </section>`;
  const smallTaxpayerLegend = isA && fiscal.receiverTaxStatus === 'smallTaxpayer'
    ? '<p class="legal-note">El crédito fiscal discriminado en el presente comprobante, sólo podrá ser computado a efectos del Régimen de Sostenimiento e Inclusión Fiscal para Pequeños Contribuyentes de la Ley Nº 27.618.</p>'
    : '';
  const number = `${fiscal.pointOfSale.toString().padStart(5, '0')}-${fiscal.number.toString().padStart(8, '0')}`;
  return `<!doctype html><html lang="es"><head><meta charset="utf-8">
    <meta http-equiv="Content-Security-Policy" content="default-src 'none'; img-src data:; style-src 'unsafe-inline'">
    <title>Factura ${isA ? 'A' : 'B'} ${number}</title><style>
    @page { size: A4; margin: 12mm; }
    * { box-sizing: border-box; }
    body { margin: 0; font: 10pt Arial, sans-serif; color: #192a28; overflow-wrap: anywhere; }
    .invoice { min-height: 271mm; display: flex; flex-direction: column; }
    .environment { font-size: 8pt; font-weight: bold; color: #275c4b; text-align: right; margin-bottom: 5mm; }
    .heading { border: 1px solid #afc2ba; display: grid; grid-template-columns: 1fr 1fr; }
    .seller, .document { padding: 6mm; min-height: 47mm; }
    .seller { border-right: 1px solid #afc2ba; }
    .issuer { color: #285e4c; font-size: 20pt; font-weight: 800; line-height: 1.05; margin-bottom: 2mm; }
    .subtitle { font-size: 11pt; font-weight: bold; margin-bottom: 5mm; }
    .seller p, .document p { margin: 0 0 2.5mm; }
    .document { position: relative; padding-left: 9mm; }
    .badge { position: absolute; top: 0; left: -6mm; background: white; border: 1px solid #afc2ba;
      width: 12mm; height: 12mm; text-align: center; line-height: 11mm; font-size: 20pt; font-weight: bold; }
    .document h1 { font-size: 15pt; margin: 0 0 2mm; }
    .document .serial { font-size: 13pt; font-weight: bold; margin: 3mm 0; }
    .customer { background: #eaf0ed; border: 1px solid #afc2ba; padding: 4mm 6mm; margin-top: 3mm; }
    .customer strong { display: block; margin-bottom: 2mm; }
    .customer p { margin: 1mm 0; }
    .customer .split { display: flex; justify-content: space-between; gap: 4mm; }
    .condition { margin: 4mm 0 2mm; }
    table { border-collapse: collapse; width: 100%; }
    thead { display: table-header-group; }
    th { background: #285e4c; color: white; text-align: left; padding: 2.3mm 2.5mm; }
    td { padding: 3mm 2.5mm; border: 1px solid #afc2ba; border-left: 0; border-right: 0; vertical-align: top; }
    th.number, td.number { text-align: right; white-space: nowrap; }
    small { display: block; color: #526861; margin-top: 1mm; }
    .notes { color: #526861; margin-top: 4mm; font-size: 9pt; }
    .notes p { margin: 1mm 0; }
    .legal-note { font-size: 9pt; margin: 5mm 0 0; line-height: 1.4; }
    .lower { display: grid; grid-template-columns: 1fr 1fr; gap: 8mm; margin-top: auto; padding-top: 8mm; break-inside: avoid; }
    .transparency { align-self: end; font-size: 9pt; }
    .transparency strong, .transparency span { display: block; }
    .transparency p { margin: 2mm 0; }
    .summary { grid-column: 2; }
    .summary-row { display: flex; justify-content: space-between; padding: 1.5mm 2mm; }
    .total { background: #285e4c; color: white; font-size: 16pt; font-weight: bold;
      display: flex; justify-content: space-between; padding: 4mm; margin-top: 6mm; }
    .authorization { display: flex; align-items: center; gap: 5mm; margin-top: 11mm; break-inside: avoid; }
    .authorization img { width: 27mm; height: 27mm; image-rendering: pixelated; }
    .authorization p { margin: 0 0 2mm; }
    .authorization strong { display: block; }
    .disclaimer { color: #285e4c; font-weight: bold; }
    .footer { border-top: 1px solid #afc2ba; color: #526861; font-size: 8pt; margin-top: 7mm; padding-top: 3mm; }
    </style></head><body><main class="invoice">
    <div class="environment">HOMOLOGACIÓN ARCA · SIN VALIDEZ FISCAL</div>
    <header class="heading"><section class="seller">
      <div class="issuer">${escapeHtml(fiscal.issuerName)}</div>
      <div class="subtitle">${escapeHtml(receipt.branch)}</div>
      <p>${escapeHtml(fiscal.issuerAddress)}</p>
      <p>IVA: Responsable Inscripto</p>
      <p>CUIT: ${escapeHtml(fiscal.issuerCuit)}</p>
      <p>IIBB: ${escapeHtml(fiscal.issuerIibb ?? 'NR')} | Inicio: ${fiscal.issuerActivityStartDate ? date(fiscal.issuerActivityStartDate) : 'NR'}</p>
    </section><section class="document"><span class="badge">${isA ? 'A' : 'B'}</span>
      <h1>FACTURA ${isA ? 'A' : 'B'}</h1>
      <p>Código Nº ${isA ? '001' : '006'}</p>
      <p class="serial">Nº ${number}</p>
      <p>Fecha: ${date(fiscal.issueDate)}</p>
      <p>ORIGINAL | Moneda: ARS</p>
    </section></header>
    <section class="customer"><strong>Cliente: ${escapeHtml(receiver)}</strong>
      <p>Domicilio: ${escapeHtml(fiscal.receiverAddress ?? 'NR')}</p>
      <div class="split"><span>Condición IVA: ${taxStatus[fiscal.receiverTaxStatus]}</span><span>${receiverDocument}</span></div>
    </section>
    <p class="condition">Condición de venta: ${escapeHtml(saleCondition)}</p>
    <table><thead><tr><th>Cód.</th><th>Descripción</th><th class="number">${onlyKg ? 'Kg netos' : 'Cantidad'}</th>
      <th class="number">${onlyKg ? 'Precio/kg' : 'Precio unit.'}</th><th class="number">Importe</th></tr></thead>
      <tbody>${rows}</tbody></table>
    <div class="notes"><p>${isA ? 'Precios netos sin IVA' : 'Precios finales con IVA incluido'}</p>
      ${receipt.discountAmount ? `<p>Descuento global incluido: $ ${money(receipt.discountAmount)}${receipt.discountReason ? ` · ${escapeHtml(receipt.discountReason)}` : ''}</p>` : ''}
      <p>${escapeHtml(receipt.terminal)} | Cajero: ${escapeHtml(receipt.cashier)} | Operación interna: ${escapeHtml(receipt.saleId)}</p></div>
    ${smallTaxpayerLegend}
    <div class="lower">${transparency}<section class="summary">${taxLines}
      <div class="summary-row"><span>Otros tributos</span><span>0,00</span></div>
      <div class="total"><span>TOTAL ARS</span><span>${money(receipt.total)}</span></div></section></div>
    <section class="authorization"><img src="${qrImage}" alt="QR de factura electrónica ARCA">
      <div><strong>CAE: ${escapeHtml(fiscal.cae)}</strong>
      <p>Vencimiento CAE: ${date(fiscal.caeExpiry)}</p>
      <p class="disclaimer">HOMOLOGACIÓN · SIN VALIDEZ FISCAL</p></div></section>
    <footer class="footer">Comprobante electrónico de prueba autorizado en homologación. No es válido para operaciones comerciales reales.</footer>
    </main></body></html>`;
}
