import { afterEach, describe, expect, it } from 'vitest';
import { SerialPortMock } from 'serialport';
import { createFiscalQrUrl } from '../src/fiscal-invoice';
import { formatEscPosReceipt, sendEscPosReceipt } from '../src/escpos-receipt';
import type { ReceiptRequest } from '../src/receipt-pdf';

const receipt: ReceiptRequest = {
  saleId: 'SALE-1', documentType: 'electronicInvoice', branch: 'Centro',
  terminal: 'Caja A', cashier: 'cajero1', confirmedAtUtc: '2026-10-30T12:00:00Z',
  total: 2400, changeAmount: 0, accountChargeAmount: 0, creditAppliedAmount: 0,
  customerCode: null, customerName: null,
  lines: [{ code: '8001', name: 'Pan rallado', unit: 'unidad', quantity: 1,
    unitPrice: 2400, lineTotal: 2400 }],
  payments: [{ method: 'cash', tenderedAmount: 2400, appliedAmount: 2400 }],
  fiscal: {
    saleDocumentType: 'ElectronicInvoice', issuerCuit: '30710106513',
    issuerName: 'Empresa de prueba', issuerAddress: 'Domicilio de prueba',
    issuerIibb: '123456789', issuerActivityStartDate: '2020-01-15',
    pointOfSale: 99, voucherType: 6, number: 3, issueDate: '2026-10-30',
    receiverName: null, receiverAddress: null, receiverTaxStatus: 'finalConsumer',
    receiverDocumentType: 99, receiverDocumentNumber: 0,
    vatBreakdown: [{ ratePercent: 21, taxableBase: 1983.47, taxAmount: 416.53 }],
    exemptAmount: 0, notTaxedAmount: 0, cae: '86410975393203', caeExpiry: '2026-11-09',
  },
};

afterEach(() => SerialPortMock.binding.reset());

describe('ESC/POS invoice receipt', () => {
  it('formats an authorized invoice with the same ARCA QR data used by A4', () => {
    const bytes = formatEscPosReceipt({ ...receipt,
      fiscalCustomerCode: 'CLI-100', purchaseOrderNumber: 'OC-22',
      recipientLocality: 'Cordoba', recipientProvince: 'Cordoba' }, 58);
    expect(bytes.subarray(0, 2)).toEqual(Buffer.from([0x1b, 0x40]));
    expect(bytes.toString('ascii')).toContain('FACTURA B');
    expect(bytes.toString('ascii')).toContain('00099-00000003');
    expect(bytes.toString('ascii')).toContain('CAE 86410975393203');
    expect(bytes.toString('ascii')).toContain('OC-22');
    expect(bytes.toString('ascii')).toContain('CLI-100');
    expect(bytes.toString('ascii')).toContain('2.400,00');
    const qrUrl = createFiscalQrUrl(receipt.fiscal!, receipt.total);
    expect(bytes.includes(Buffer.from(qrUrl, 'ascii'))).toBe(true);
    expect(bytes.includes(Buffer.from([0x1d, 0x28, 0x6b, 0x03, 0x00, 0x31, 0x51, 0x30])))
      .toBe(true);
    expect(bytes.includes(Buffer.from([0x1d, 0x28, 0x6b, 0x03, 0x00, 0x31, 0x43, 0x03])))
      .toBe(true);
    expect(formatEscPosReceipt(receipt, 80).includes(
      Buffer.from([0x1d, 0x28, 0x6b, 0x03, 0x00, 0x31, 0x43, 0x04]))).toBe(true);
  });

  it('requires authorization for an electronic invoice and strips control bytes', () => {
    expect(() => formatEscPosReceipt({ ...receipt, fiscal: undefined }, 80)).toThrow();
    const bytes = formatEscPosReceipt({ ...receipt,
      lines: [{ ...receipt.lines[0], name: '\x1b[2JPan\nRallado' }] }, 80);
    expect(bytes.toString('ascii')).toContain('Pan Rallado');
    expect(bytes.includes(Buffer.from('\x1b[2J'))).toBe(false);
  });

  it('prints physical pieces and weight as different values', () => {
    const bytes = formatEscPosReceipt({ ...receipt,
      lines: [{ code: '2546', name: 'Cortito', unit: 'kg', quantity: 1.5,
        pieceCount: 2, unitPrice: 1600, lineTotal: 2400 }] }, 80);
    expect(bytes.toString('ascii')).toContain('Piezas fisicas 2');
    expect(bytes.toString('ascii')).toContain('1,500 kg');
  });

  it('prints each B invoice line rate and a net, VAT and total summary per rate', () => {
    const mixed: ReceiptRequest = { ...receipt, total: 2315,
      lines: [
        { code: '1', name: 'Corte 10', unit: 'kg', quantity: 1,
          unitPrice: 1105, lineTotal: 1105, netAfterDiscount: 1105,
          taxTreatment: 'taxed', taxableBase: 1000, taxAmount: 105, taxRatePercent: 10.5 },
        { code: '2', name: 'Corte 21', unit: 'kg', quantity: 1,
          unitPrice: 1210, lineTotal: 1210, netAfterDiscount: 1210,
          taxTreatment: 'taxed', taxableBase: 1000, taxAmount: 210, taxRatePercent: 21 },
      ],
      fiscal: { ...receipt.fiscal!, vatBreakdown: [
        { ratePercent: 10.5, taxableBase: 1000, taxAmount: 105 },
        { ratePercent: 21, taxableBase: 1000, taxAmount: 210 },
      ] },
    };
    const printed = formatEscPosReceipt(mixed, 58).toString('ascii');
    expect(printed).toContain('IVA 10,5%');
    expect(printed).toContain('IVA 21%');
    expect(printed).toContain('Neto 10,5%');
    expect(printed).toContain('Total 10,5%');
    expect(printed).toContain('Total 21%');
    expect(printed).toContain('IVA contenido');
  });

  it('writes the thermal bytes to the assigned COM port', async () => {
    SerialPortMock.binding.createPort('COM3', { record: true });
    let port: SerialPortMock | undefined;
    const result = await sendEscPosReceipt(receipt, { port: 'COM3', baudRate: 19200,
      paperWidthMm: 80 }, (options) => {
        port = new SerialPortMock(options);
        return port;
      });
    expect(result.port).toBe('COM3');
    expect(result.confirmation).toBe('drained');
    expect(port?.port?.recording.includes(Buffer.from('CAE 86410975393203'))).toBe(true);
    expect(port?.isOpen).toBe(false);
  });
});
