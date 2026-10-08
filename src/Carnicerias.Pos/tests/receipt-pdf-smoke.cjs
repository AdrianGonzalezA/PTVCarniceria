const { app, BrowserWindow } = require('electron');
const { createReceiptHtml, validateReceiptRequest } = require('../dist/receipt-pdf');

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
    process.stdout.write(`Electron PDF smoke passed (${pdf.length} bytes)\n`);
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
