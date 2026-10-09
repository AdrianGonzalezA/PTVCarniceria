import { describe, expect, it } from 'vitest';
import { createReceiptHtml, validateReceiptRequest } from '../src/receipt-pdf';
import { createFiscalInvoiceHtml, createFiscalQrUrl } from '../src/fiscal-invoice';

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

  it('renders only an authorized homologation invoice with a bounded CAE', async () => {
    const fiscal = {
      saleId: 'sale-123', status: 'Authorized', total: 2400,
      saleDocumentType: 'ElectronicInvoice', issuerCuit: '30710106513',
      issuerName: 'Empresa de prueba', issuerAddress: 'Domicilio de prueba',
      issuerIibb: '123456789', issuerActivityStartDate: '2020-01-15',
      pointOfSale: 99, voucherType: 6, number: 3, issueDate: '2026-10-09',
      receiverName: null, receiverAddress: null, receiverTaxStatus: 'finalConsumer',
      receiverDocumentType: 99, receiverDocumentNumber: 0,
      vatBreakdown: [{ ratePercent: 21, taxableBase: 1983.47, taxAmount: 416.53 }],
      exemptAmount: 0, notTaxedAmount: 0,
      cae: '86410975393203', caeExpiry: '2026-10-19',
    };
    const receipt = validateReceiptRequest({ ...validRequest, fiscal });
    const html = await createFiscalInvoiceHtml(receipt);
    expect(html).toContain('FACTURA B');
    expect(html).toContain('Código Nº 006');
    expect(html).toContain('00099-00000003');
    expect(html).toContain('IIBB: 123456789 | Inicio: 15/01/2020');
    expect(html).toContain('CAE: 86410975393203');
    expect(html).toContain('IVA&nbsp;contenido');
    expect(html).toContain('data:image/png;base64,');
    expect(html).toContain('@page { size: A4;');
    expect(html).toContain('SIN VALIDEZ FISCAL');
    expect(html).not.toContain('DOCUMENTO DE PRUEBA<br>NO FISCAL');
    expect(html).not.toContain('QR pendiente');

    const qrUrl = createFiscalQrUrl(receipt.fiscal!, receipt.total);
    const qrData = JSON.parse(Buffer.from(new URL(qrUrl).searchParams.get('p')!, 'base64').toString('utf8'));
    expect(qrData).toEqual({ ver: 1, fecha: '2026-10-09', cuit: 30710106513,
      ptoVta: 99, tipoCmp: 6, nroCmp: 3, importe: 2400, moneda: 'PES', ctz: 1,
      tipoCodAut: 'E', codAut: 86410975393203 });
    expect(qrUrl).toMatch(/^https:\/\/www\.arca\.gob\.ar\/fe\/qr\/\?p=/);
    const previousApiReceipt = validateReceiptRequest({ ...validRequest,
      fiscal: { ...fiscal, receiverTaxStatus: 'FinalConsumer' } });
    expect((await createFiscalInvoiceHtml(previousApiReceipt))).toContain('A CONSUMIDOR FINAL');
    await expect(createFiscalInvoiceHtml({ ...receipt,
      fiscal: { ...receipt.fiscal!, receiverTaxStatus: 'registered' } })).rejects.toThrow('class');
    expect(() => validateReceiptRequest({ ...validRequest,
      fiscal: { ...fiscal, status: 'NeedsReconciliation' } })).toThrow();
    expect(() => validateReceiptRequest({ ...validRequest,
      fiscal: { ...fiscal, cae: '123' } })).toThrow();
    expect(() => validateReceiptRequest({ ...validRequest,
      fiscal: { ...fiscal, pointOfSale: 100000 } })).toThrow();
    expect(() => validateReceiptRequest({ ...validRequest,
      fiscal: { ...fiscal, number: 100000000 } })).toThrow();
    expect(() => validateReceiptRequest({ ...validRequest,
      fiscal: { ...fiscal, issuerActivityStartDate: '<script>' } })).toThrow();
  });

  it('shows net line prices and separated VAT on an A invoice', async () => {
    const receipt = validateReceiptRequest({ ...validRequest,
      lines: [{ ...validRequest.lines[0], netAfterDiscount: 2400,
        taxableBase: 1983.47, taxAmount: 416.53 }],
      fiscal: { saleId: 'sale-123', status: 'Authorized', total: 2400,
        saleDocumentType: 'ElectronicInvoice', issuerCuit: '30710106513',
        issuerName: 'Empresa de prueba', issuerAddress: 'Domicilio de prueba',
        pointOfSale: 99, voucherType: 1, number: 4, issueDate: '2026-10-09',
        receiverName: 'Comercio receptor', receiverAddress: 'Dirección receptor',
        receiverTaxStatus: 'registered', receiverDocumentType: 80, receiverDocumentNumber: 30710000001,
        vatBreakdown: [{ ratePercent: 21, taxableBase: 1983.47, taxAmount: 416.53 }],
        exemptAmount: 0, notTaxedAmount: 0, cae: '86410975393204', caeExpiry: '2026-10-19' },
    });
    const html = await createFiscalInvoiceHtml(receipt);
    expect(html).toContain('FACTURA A');
    expect(html).toContain('Código Nº 001');
    expect(html).toContain('1.983,47');
    expect(html).toContain('416,53');
    expect(html).toContain('Precios netos sin IVA');
    expect(html).not.toContain('IVA contenido');
    const qr = JSON.parse(Buffer.from(new URL(createFiscalQrUrl(receipt.fiscal!, receipt.total))
      .searchParams.get('p')!, 'base64').toString('utf8'));
    expect(qr.tipoDocRec).toBe(80);
    expect(qr.nroDocRec).toBe(30710000001);
    const monoHtml = await createFiscalInvoiceHtml({ ...receipt,
      fiscal: { ...receipt.fiscal!, receiverTaxStatus: 'smallTaxpayer' } });
    expect(monoHtml).toContain('Ley Nº 27.618');
    await expect(createFiscalInvoiceHtml({ ...receipt,
      fiscal: { ...receipt.fiscal!, receiverDocumentType: 99,
        receiverDocumentNumber: 0 } })).rejects.toThrow('CUIT');
  });

  it('keeps exempt lines in an A invoice without treating them as taxable', async () => {
    const receipt = validateReceiptRequest({ ...validRequest, total: 1605,
      payments: [{ method: 'cash', tenderedAmount: 1605, appliedAmount: 1605 }],
      lines: [
        { code: '1', name: 'Corte gravado', unit: 'kg', quantity: 1, unitPrice: 1105,
          lineTotal: 1105, netAfterDiscount: 1105, taxableBase: 1000, taxAmount: 105 },
        { code: '2', name: 'Artículo exento', unit: 'unidad', quantity: 1, unitPrice: 500,
          lineTotal: 500, netAfterDiscount: 500, taxableBase: 0, taxAmount: 0 },
      ],
      fiscal: { saleId: 'sale-123', status: 'Authorized', total: 1605,
        saleDocumentType: 'ElectronicInvoice', issuerCuit: '30710106513',
        issuerName: 'Emisor', issuerAddress: 'Domicilio', pointOfSale: 99,
        voucherType: 1, number: 5, issueDate: '2026-10-09',
        receiverName: 'Cliente', receiverAddress: 'Domicilio cliente', receiverTaxStatus: 'registered',
        receiverDocumentType: 80, receiverDocumentNumber: 30710000001,
        vatBreakdown: [{ ratePercent: 10.5, taxableBase: 1000, taxAmount: 105 }],
        exemptAmount: 500, notTaxedAmount: 0, cae: '12345678901234', caeExpiry: '2026-10-19' },
    });
    const html = await createFiscalInvoiceHtml(receipt);
    expect(html).toContain('Artículo exento');
    expect(html).toContain('Exento');
    expect(html).toContain('1.605,00');
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
