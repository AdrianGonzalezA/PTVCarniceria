const { app, BrowserWindow } = require('electron');
const { mkdirSync, writeFileSync } = require('node:fs');
const path = require('node:path');
const { createReceiptHtml, validateReceiptRequest } = require('../dist/receipt-pdf');
const { createFiscalInvoiceHtml } = require('../dist/fiscal-invoice');
const { shiftClosingHtml, validateShiftClosingReport } = require('../dist/shift-closing-report');

app.whenReady().then(async () => {
  const window = new BrowserWindow({ show: false, webPreferences: {
    contextIsolation: true, nodeIntegration: false, sandbox: true, webSecurity: true,
  } });
  try {
    const receipt = validateReceiptRequest({
      saleId: 'SMOKE-TEST', branch: 'Sucursal de prueba', terminal: 'Caja 1', cashier: 'prueba',
      confirmedAtUtc: '2026-10-08T13:00:00Z', total: 2450, changeAmount: 0,
      lines: [{ code: '1001', name: 'Asado', unit: 'kg', quantity: 0.5, unitPrice: 4900, lineTotal: 2450 }],
      payments: [{ method: 'cash', tenderedAmount: 2450, appliedAmount: 2450 }],
    });
    await window.loadURL(`data:text/html;charset=utf-8,${encodeURIComponent(createReceiptHtml(receipt))}`);
    const pdf = await window.webContents.printToPDF({
      printBackground: true, pageSize: { width: 3.15, height: 11.7 },
      margins: { top: 0.12, bottom: 0.12, left: 0.12, right: 0.12 },
    });
    if (pdf.subarray(0, 5).toString() !== '%PDF-' || pdf.length < 1000) {
      throw new Error('Electron did not produce a valid PDF buffer');
    }
    const invoice = validateReceiptRequest({
      saleId: 'SMOKE-FACTURA', branch: 'Sucursal de prueba', terminal: 'Caja 1', cashier: 'prueba',
      confirmedAtUtc: '2026-10-09T13:00:00Z', total: 49835.50, changeAmount: 0,
      lines: [
        { code: '001', name: 'Asado bovino fresco', unit: 'kg', quantity: 1.250,
          unitPrice: 13260, lineTotal: 16575, netAfterDiscount: 16575, taxableBase: 15000,
          taxAmount: 1575, taxRatePercent: 10.5, taxTreatment: 'taxed' },
        { code: '002', name: 'Vacío bovino fresco', unit: 'kg', quantity: 0.850,
          unitPrice: 17680, lineTotal: 15028, netAfterDiscount: 15028, taxableBase: 13600,
          taxAmount: 1428, taxRatePercent: 10.5, taxTreatment: 'taxed' },
        { code: '003', name: 'Nalga bovina fresca', unit: 'kg', quantity: 1.100,
          unitPrice: 16575, lineTotal: 18232.50, netAfterDiscount: 18232.50,
          taxableBase: 15068.18, taxAmount: 3164.32, taxRatePercent: 21, taxTreatment: 'taxed' },
      ],
      payments: [{ method: 'cash', tenderedAmount: 49835.50, appliedAmount: 49835.50 }],
      fiscal: { saleId: 'SMOKE-FACTURA', status: 'Authorized', total: 49835.50,
        saleDocumentType: 'ElectronicInvoice', issuerCuit: '30710106513',
        issuerName: 'La Estancia', issuerAddress: 'Domicilio de prueba - Córdoba',
        issuerIibb: '123456789', issuerActivityStartDate: '2020-01-15',
        pointOfSale: 99, voucherType: 6, number: 128, issueDate: '2026-10-09',
        receiverName: null, receiverAddress: null, receiverTaxStatus: 'finalConsumer',
        receiverDocumentType: 99, receiverDocumentNumber: 0,
        vatBreakdown: [
          { ratePercent: 10.5, taxableBase: 28600, taxAmount: 3003 },
          { ratePercent: 21, taxableBase: 15068.18, taxAmount: 3164.32 },
        ],
        exemptAmount: 0, notTaxedAmount: 0, cae: '12345678901234', caeExpiry: '2026-10-19' },
    });
    await window.loadURL(`data:text/html;charset=utf-8,${encodeURIComponent(await createFiscalInvoiceHtml(invoice))}`);
    const invoicePdf = await window.webContents.printToPDF({
      printBackground: true, pageSize: 'A4',
      margins: { top: 0, bottom: 0, left: 0, right: 0 },
    });
    if (invoicePdf.subarray(0, 5).toString() !== '%PDF-' || invoicePdf.length < 3000) {
      throw new Error('Electron did not produce a valid invoice PDF buffer');
    }
    if (process.env.CARNICERIAS_PDF_SMOKE_OUTPUT) {
      const output = path.resolve(process.env.CARNICERIAS_PDF_SMOKE_OUTPUT);
      mkdirSync(path.dirname(output), { recursive: true });
      writeFileSync(output, invoicePdf);
    }
    const invoiceA = { ...invoice, fiscal: { ...invoice.fiscal, voucherType: 1,
      receiverName: 'Comercio receptor', receiverAddress: 'Domicilio cliente',
      receiverTaxStatus: 'registered', receiverDocumentType: 80, receiverDocumentNumber: 30710000001 } };
    await window.loadURL(`data:text/html;charset=utf-8,${encodeURIComponent(await createFiscalInvoiceHtml(invoiceA))}`);
    const invoicePdfA = await window.webContents.printToPDF({
      printBackground: true, pageSize: 'A4',
      margins: { top: 0, bottom: 0, left: 0, right: 0 },
    });
    if (invoicePdfA.subarray(0, 5).toString() !== '%PDF-' || invoicePdfA.length < 3000)
      throw new Error('Electron did not produce a valid A invoice PDF buffer');
    if (process.env.CARNICERIAS_PDF_SMOKE_OUTPUT_A) {
      const output = path.resolve(process.env.CARNICERIAS_PDF_SMOKE_OUTPUT_A);
      mkdirSync(path.dirname(output), { recursive: true });
      writeFileSync(output, invoicePdfA);
    }
    const shift = validateShiftClosingReport({
      version: 1, shiftId: '11111111-1111-1111-1111-111111111111',
      companyName: 'Carnicería de prueba', branchName: 'Centro', terminalName: 'Caja A',
      cashierName: 'cajero1', openedAtUtc: '2026-11-05T10:00:00Z',
      closedAtUtc: '2026-11-05T18:00:00Z', openingCash: 100,
      cashSales: 400, nonCashSales: 200, accountSales: 50, salesTotal: 650,
      cashCollections: 80, nonCashCollections: 25, creditApplied: 10,
      cashWithdrawals: 20, expectedCash: 560, countedCash: 550,
      handedCash: 500, cashDifference: -10,
      paymentBreakdown: [{ method: 'cash', amount: 480 }],
    });
    await window.loadURL(`data:text/html;charset=utf-8,${encodeURIComponent(shiftClosingHtml(shift))}`);
    const shiftPdf = await window.webContents.printToPDF({ printBackground: true, pageSize: 'A4' });
    if (shiftPdf.subarray(0, 5).toString() !== '%PDF-' || shiftPdf.length < 2000)
      throw new Error('Electron did not produce a valid shift closing PDF buffer');
    if (process.env.CARNICERIAS_SHIFT_PDF_SMOKE_OUTPUT) {
      const output = path.resolve(process.env.CARNICERIAS_SHIFT_PDF_SMOKE_OUTPUT);
      mkdirSync(path.dirname(output), { recursive: true });
      writeFileSync(output, shiftPdf);
    }
    const preview = validateShiftClosingReport({ ...shift, isComplete: false,
      pendingApprovedCash: 75, pendingApprovedNonCash: 25 });
    await window.loadURL(`data:text/html;charset=utf-8,${encodeURIComponent(shiftClosingHtml(preview))}`);
    const previewPdf = await window.webContents.printToPDF({ printBackground: true, pageSize: 'A4' });
    if (previewPdf.subarray(0, 5).toString() !== '%PDF-' || previewPdf.length < 2000)
      throw new Error('Electron did not produce a valid shift preview PDF buffer');
    if (process.env.CARNICERIAS_SHIFT_PREVIEW_PDF_SMOKE_OUTPUT) {
      const output = path.resolve(process.env.CARNICERIAS_SHIFT_PREVIEW_PDF_SMOKE_OUTPUT);
      mkdirSync(path.dirname(output), { recursive: true });
      writeFileSync(output, previewPdf);
    }
    process.stdout.write(`Electron PDF smoke passed (ticket ${pdf.length}; B ${invoicePdf.length}; A ${invoicePdfA.length}; shift ${shiftPdf.length}; preview ${previewPdf.length} bytes)\n`);
  } catch (error) {
    process.exitCode = 1;
    process.stderr.write(`${error instanceof Error ? error.message : String(error)}\n`);
  } finally {
    window.destroy();
    app.quit();
  }
}).catch((error) => {
  process.exitCode = 1;
  process.stderr.write(`${error instanceof Error ? error.message : String(error)}\n`);
  app.quit();
});
