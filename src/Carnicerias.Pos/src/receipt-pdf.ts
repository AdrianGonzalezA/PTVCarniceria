export interface FiscalReceipt {
  readonly saleDocumentType: 'FiscalTicket' | 'ElectronicInvoice';
  readonly issuerCuit: string;
  readonly issuerName: string;
  readonly issuerAddress: string;
  readonly issuerIibb: string | null;
  readonly issuerActivityStartDate: string | null;
  readonly pointOfSale: number;
  readonly voucherType: 1 | 6;
  readonly number: number;
  readonly issueDate: string;
  readonly receiverName: string | null;
  readonly receiverAddress: string | null;
  readonly receiverTaxStatus: 'finalConsumer' | 'registered' | 'smallTaxpayer' | 'exempt';
  readonly receiverDocumentType: number;
  readonly receiverDocumentNumber: number;
  readonly vatBreakdown: readonly { readonly ratePercent: number;
    readonly taxableBase: number; readonly taxAmount: number }[];
  readonly exemptAmount: number;
  readonly notTaxedAmount: number;
  readonly cae: string;
  readonly caeExpiry: string;
}

export interface ReceiptRequest {
  readonly saleId: string;
  readonly branch: string;
  readonly terminal: string;
  readonly cashier: string;
  readonly confirmedAtUtc: string;
  readonly total: number;
  readonly changeAmount: number;
  readonly accountChargeAmount: number;
  readonly creditAppliedAmount: number;
  readonly discountAmount?: number;
  readonly discountReason?: string | null;
  readonly customerCode: string | null;
  readonly customerName: string | null;
  readonly lines: readonly {
    readonly code: string; readonly name: string; readonly unit: string;
    readonly quantity: number; readonly unitPrice: number; readonly lineTotal: number;
    readonly pieceIdentifier?: string | null;
    readonly netAfterDiscount?: number | null;
    readonly taxableBase?: number | null;
    readonly taxAmount?: number | null;
  }[];
  readonly payments: readonly {
    readonly method: string; readonly tenderedAmount: number; readonly appliedAmount: number;
  }[];
  readonly fiscal?: FiscalReceipt;
}

const methods: Record<string, string> = {
  cash: 'Efectivo', debit: 'Débito', credit: 'Crédito', transfer: 'Transferencia',
  mercadoPago: 'Mercado Pago', cheque: 'Cheque',
};

function object(value: unknown): Record<string, unknown> {
  if (typeof value !== 'object' || value === null || Array.isArray(value)) throw new Error('Invalid receipt data');
  return value as Record<string, unknown>;
}

function label(value: unknown, maximum = 120): string {
  if (typeof value !== 'string' || value.trim().length === 0 || value.length > maximum) {
    throw new Error('Invalid receipt text');
  }
  return value.trim();
}

function amount(value: unknown, positive = false): number {
  if (typeof value !== 'number' || !Number.isFinite(value) || value < 0 || value > 1_000_000_000 ||
      (positive && value === 0)) throw new Error('Invalid receipt amount');
  return value;
}

export function validateReceiptRequest(value: unknown): ReceiptRequest {
  const data = object(value);
  const lines = data['lines'];
  const payments = data['payments'];
  if (!Array.isArray(lines) || lines.length < 1 || lines.length > 100 ||
      !Array.isArray(payments) || payments.length > 6) {
    throw new Error('Invalid receipt detail');
  }
  const total = amount(data['total'], true);
  const accountChargeAmount = amount(data['accountChargeAmount'] ?? 0);
  const creditAppliedAmount = amount(data['creditAppliedAmount'] ?? 0);
  const discountAmount = amount(data['discountAmount'] ?? 0);
  const discountReason = discountAmount > 0 ? label(data['discountReason'], 200) : null;
  if (accountChargeAmount + creditAppliedAmount > total ||
      (payments.length === 0 && accountChargeAmount + creditAppliedAmount !== total))
    throw new Error('Invalid receipt settlement');
  const customerCode = accountChargeAmount + creditAppliedAmount > 0 ? label(data['customerCode'], 80) : null;
  const customerName = accountChargeAmount + creditAppliedAmount > 0 ? label(data['customerName']) : null;
  const confirmedAtUtc = label(data['confirmedAtUtc'], 40);
  if (Number.isNaN(Date.parse(confirmedAtUtc))) throw new Error('Invalid receipt date');
  const result: ReceiptRequest = {
    saleId: label(data['saleId'], 80),
    branch: label(data['branch']),
    terminal: label(data['terminal']),
    cashier: label(data['cashier']),
    confirmedAtUtc,
    total,
    changeAmount: amount(data['changeAmount']),
    accountChargeAmount,
    creditAppliedAmount,
    discountAmount,
    discountReason,
    customerCode,
    customerName,
    lines: lines.map((input: unknown) => {
      const line = object(input);
      const quantity = amount(line['quantity'], true);
      if (quantity > 10_000) throw new Error('Invalid receipt quantity');
      return {
        code: label(line['code']), name: label(line['name']), unit: label(line['unit'], 30),
        quantity, unitPrice: amount(line['unitPrice']), lineTotal: amount(line['lineTotal']),
        pieceIdentifier: line['pieceIdentifier'] == null ? null : label(line['pieceIdentifier'], 80),
        netAfterDiscount: line['netAfterDiscount'] == null ? null : amount(line['netAfterDiscount']),
        taxableBase: line['taxableBase'] == null ? null : amount(line['taxableBase']),
        taxAmount: line['taxAmount'] == null ? null : amount(line['taxAmount']),
      };
    }),
    payments: payments.map((input: unknown) => {
      const payment = object(input);
      const method = label(payment['method'], 30);
      if (!Object.hasOwn(methods, method)) throw new Error('Invalid receipt payment method');
      return {
        method, tenderedAmount: amount(payment['tenderedAmount'], true),
        appliedAmount: amount(payment['appliedAmount'], true),
      };
    }),
    ...(data['fiscal'] == null ? {} : { fiscal: validateFiscal(data['fiscal'], data['saleId'], total) }),
  };
  const settled = result.payments.reduce((sum, payment) => sum + payment.appliedAmount, 0) +
    accountChargeAmount + creditAppliedAmount;
  if (Math.abs(settled - total) > 0.001) throw new Error('Invalid receipt settlement');
  return result;
}

function validateFiscal(value: unknown, saleId: unknown, total: number): FiscalReceipt {
  const fiscal = object(value);
  const taxStatus = String(fiscal['receiverTaxStatus']);
  const receiverTaxStatus = `${taxStatus.slice(0, 1).toLowerCase()}${taxStatus.slice(1)}`;
  const rawVat = fiscal['vatBreakdown'];
  if (!Array.isArray(rawVat) || rawVat.length > 20) throw new Error('Invalid fiscal taxes');
  const vatBreakdown = rawVat.map((entry: unknown) => {
    const row = object(entry);
    const ratePercent = Number(row['ratePercent']);
    if (![0, 10.5, 21, 27].includes(ratePercent)) throw new Error('Invalid fiscal tax rate');
    return { ratePercent, taxableBase: amount(row['taxableBase'], true), taxAmount: amount(row['taxAmount']) };
  });
  const exemptAmount = amount(fiscal['exemptAmount']);
  const notTaxedAmount = amount(fiscal['notTaxedAmount']);
  const taxTotal = vatBreakdown.reduce((sum, row) => sum + row.taxableBase + row.taxAmount, 0) +
    exemptAmount + notTaxedAmount;
  if (fiscal['status'] !== 'Authorized' || fiscal['saleId'] !== saleId || fiscal['total'] !== total ||
      Math.abs(taxTotal - total) > 0.011 ||
      !/^\d{11}$/.test(String(fiscal['issuerCuit'])) || !/^\d{14}$/.test(String(fiscal['cae'])) ||
      !/^\d{4}-\d{2}-\d{2}$/.test(String(fiscal['issueDate'])) ||
      !/^\d{4}-\d{2}-\d{2}$/.test(String(fiscal['caeExpiry'])) ||
      !Number.isSafeInteger(fiscal['pointOfSale']) || Number(fiscal['pointOfSale']) < 1 ||
      Number(fiscal['pointOfSale']) > 99999 ||
      !Number.isSafeInteger(fiscal['number']) || Number(fiscal['number']) < 1 ||
      Number(fiscal['number']) > 99999999 ||
      ![1, 6].includes(Number(fiscal['voucherType'])) ||
      ![80, 96, 99].includes(Number(fiscal['receiverDocumentType'])) ||
      !['finalConsumer', 'registered', 'smallTaxpayer', 'exempt'].includes(receiverTaxStatus) ||
      !Number.isSafeInteger(fiscal['receiverDocumentNumber']) ||
      Number(fiscal['receiverDocumentNumber']) < 0 ||
      !['FiscalTicket', 'ElectronicInvoice'].includes(String(fiscal['saleDocumentType'])))
    throw new Error('Invalid fiscal authorization');
  const issuerActivityStartDate = fiscal['issuerActivityStartDate'];
  if (issuerActivityStartDate != null &&
      (typeof issuerActivityStartDate !== 'string' || !/^\d{4}-\d{2}-\d{2}$/.test(issuerActivityStartDate)))
    throw new Error('Invalid issuer activity date');
  return {
    saleDocumentType: fiscal['saleDocumentType'] as FiscalReceipt['saleDocumentType'],
    issuerCuit: String(fiscal['issuerCuit']),
    issuerName: label(fiscal['issuerName'], 200),
    issuerAddress: label(fiscal['issuerAddress'], 200),
    issuerIibb: fiscal['issuerIibb'] == null ? null : label(fiscal['issuerIibb'], 40),
    issuerActivityStartDate: issuerActivityStartDate ?? null,
    pointOfSale: Number(fiscal['pointOfSale']),
    voucherType: Number(fiscal['voucherType']) as 1 | 6,
    number: Number(fiscal['number']),
    issueDate: String(fiscal['issueDate']),
    receiverName: fiscal['receiverName'] == null ? null : label(fiscal['receiverName'], 200),
    receiverAddress: fiscal['receiverAddress'] == null ? null : label(fiscal['receiverAddress'], 200),
    receiverTaxStatus: receiverTaxStatus as FiscalReceipt['receiverTaxStatus'],
    receiverDocumentType: Number(fiscal['receiverDocumentType']),
    receiverDocumentNumber: Number(fiscal['receiverDocumentNumber']),
    vatBreakdown,
    exemptAmount,
    notTaxedAmount,
    cae: String(fiscal['cae']),
    caeExpiry: String(fiscal['caeExpiry']),
  };
}

function escapeHtml(value: string): string {
  return value.replace(/[&<>"']/g, (character) => ({
    '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;',
  })[character] ?? character);
}

const money = (value: number): string => value.toLocaleString('es-AR', {
  minimumFractionDigits: 2, maximumFractionDigits: 2,
});

export function createReceiptHtml(receipt: ReceiptRequest): string {
  const rows = receipt.lines.map((line) => `<tr><td>${escapeHtml(line.name)}<small>${escapeHtml(line.code)}${line.pieceIdentifier ? ` · Pieza ${escapeHtml(line.pieceIdentifier)}` : ''} · ${line.quantity.toLocaleString('es-AR', { minimumFractionDigits: line.unit === 'kg' ? 3 : 0, maximumFractionDigits: 3 })} ${escapeHtml(line.unit)} × $ ${money(line.unitPrice)}</small></td><td>$ ${money(line.lineTotal)}</td></tr>`).join('');
  const payments = receipt.payments.map((payment) => `<tr><td>${methods[payment.method]}</td><td>$ ${money(payment.appliedAmount)}</td></tr>`).join('');
  const accountCharge = receipt.accountChargeAmount > 0
    ? `<tr><td>Cuenta corriente</td><td>$ ${money(receipt.accountChargeAmount)}</td></tr>` : '';
  const creditApplied = receipt.creditAppliedAmount > 0
    ? `<tr><td>Saldo a favor aplicado</td><td>$ ${money(receipt.creditAppliedAmount)}</td></tr>` : '';
  const discount = (receipt.discountAmount ?? 0) > 0
    ? `<tr><td>Descuento global<small>${escapeHtml(receipt.discountReason ?? '')}</small></td><td>− $ ${money(receipt.discountAmount ?? 0)}</td></tr>` : '';
  const customer = receipt.customerName && receipt.customerCode
    ? `<p class="meta">Cliente: ${escapeHtml(receipt.customerName)} (${escapeHtml(receipt.customerCode)})</p>` : '';
  const date = new Date(receipt.confirmedAtUtc).toLocaleString('es-AR', { timeZone: 'America/Argentina/Buenos_Aires' });
  const fiscal = receipt.fiscal;
  const fiscalHeader = fiscal ? `<div class="warning">HOMOLOGACIÓN ARCA<br>SIN VALIDEZ FISCAL</div>
    <h1>${fiscal.saleDocumentType === 'FiscalTicket' ? 'TICKET FISCAL DE PRUEBA' : 'FACTURA ELECTRÓNICA DE PRUEBA'} ${fiscal.voucherType === 1 ? 'A' : 'B'}</h1>
    <p class="meta">${escapeHtml(fiscal.issuerName)} · CUIT ${escapeHtml(fiscal.issuerCuit)}</p>
    <p class="meta">${escapeHtml(fiscal.issuerAddress)}</p>
    <p class="meta">Punto de venta ${fiscal.pointOfSale.toString().padStart(5, '0')} · N.º ${fiscal.number.toString().padStart(8, '0')} · ${escapeHtml(fiscal.issueDate)}</p>
    <p class="meta">Receptor: ${escapeHtml(fiscal.receiverName ?? 'Consumidor final')} · Documento ${fiscal.receiverDocumentType}: ${fiscal.receiverDocumentNumber}</p>
    ${fiscal.receiverAddress ? `<p class="meta">Domicilio: ${escapeHtml(fiscal.receiverAddress)}</p>` : ''}` :
    '<div class="warning">DOCUMENTO DE PRUEBA<br>NO FISCAL</div>';
  const fiscalFooter = fiscal ? `CAE ${escapeHtml(fiscal.cae)} · Vencimiento ${escapeHtml(fiscal.caeExpiry)}<br>
    Homologación: no usar como comprobante válido. El QR corresponde solo a factura electrónica.` :
    'Simulación de impresión. No es factura ni comprobante fiscal válido.';
  const fiscalTaxes = fiscal ? `<h2>Desglose impositivo de prueba</h2><table>
    ${fiscal.vatBreakdown.map((item) => `<tr><td>Neto gravado ${item.ratePercent}%</td><td>$ ${money(item.taxableBase)}</td></tr>
      <tr><td>IVA ${item.ratePercent}% incluido</td><td>$ ${money(item.taxAmount)}</td></tr>`).join('')}
    ${fiscal.exemptAmount > 0 ? `<tr><td>Exento</td><td>$ ${money(fiscal.exemptAmount)}</td></tr>` : ''}
    ${fiscal.notTaxedAmount > 0 ? `<tr><td>No gravado</td><td>$ ${money(fiscal.notTaxedAmount)}</td></tr>` : ''}</table>` : '';
  return `<!doctype html><html lang="es"><head><meta charset="utf-8"><meta http-equiv="Content-Security-Policy" content="default-src 'none'; style-src 'unsafe-inline'"><title>Ticket de prueba</title><style>
    @page { margin: 3mm; } body { font: 10pt Arial, sans-serif; color: #171717; margin: 0; overflow-wrap: anywhere; }
    h1 { font-size: 15pt; margin: 0 0 4mm; } h2 { font-size: 11pt; margin: 5mm 0 2mm; }
    .warning { border: 2px solid #171717; padding: 2mm; margin: 0 0 5mm; text-align: center; font-weight: bold; font-size: 12pt; }
    .meta { margin: 1mm 0; } table { border-collapse: collapse; width: 100%; } td { padding: 2mm 0; border-bottom: 1px dashed #aaa; vertical-align: top; }
    td:last-child { text-align: right; white-space: nowrap; } small { display: block; font-size: 8pt; color: #555; margin-top: 1mm; }
    .total { font-size: 13pt; font-weight: bold; } .footer { border-top: 1px solid #555; margin-top: 5mm; padding-top: 3mm; font-size: 9pt; }
  </style></head><body>${fiscalHeader}
    <h1>${escapeHtml(receipt.branch)}</h1><p class="meta">${escapeHtml(receipt.terminal)} · ${escapeHtml(receipt.cashier)}</p>
    <p class="meta">Venta ${escapeHtml(receipt.saleId)}</p><p class="meta">${escapeHtml(date)}</p>${customer}
    <h2>Productos</h2><table>${rows}${discount}</table>${fiscalTaxes}<h2>Pagos y saldo a cuenta</h2><table>${payments}${accountCharge}${creditApplied}
    <tr class="total"><td>Total</td><td>$ ${money(receipt.total)}</td></tr>
    ${receipt.changeAmount > 0 ? `<tr><td>Vuelto</td><td>$ ${money(receipt.changeAmount)}</td></tr>` : ''}</table>
    <p class="footer">${fiscalFooter}</p></body></html>`;
}
