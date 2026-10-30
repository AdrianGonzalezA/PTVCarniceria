import path from 'node:path';
import { randomBytes, randomUUID } from 'node:crypto';
import { mkdirSync, readFileSync } from 'node:fs';
import { mkdir, writeFile } from 'node:fs/promises';
import { pathToFileURL } from 'node:url';
import {
  app,
  BrowserWindow,
  dialog,
  ipcMain,
  net,
  protocol,
  safeStorage,
  session,
  type IpcMainInvokeEvent,
} from 'electron';
import { resolveAppAsset, toBackendRequest, toBackendUrl } from './app-protocol';
import { createDiagnostic } from './diagnostic';
import { parseDeploymentConfig, resolveConfigPath } from './deployment-config';
import { createFiscalInvoiceHtml } from './fiscal-invoice';
import { HasarSerialSession } from './hasar-serial';
import { printHasarSaleWithLedger } from './hasar-sale-ledger';
import { issueHasarFiscalTicket, issueHasarNonFiscalTicket } from './hasar-ticket';
import { NativeChannel } from './native-api';
import { initialUrlForProfile, resolvePosProfile } from './pos-profile';
import { createReceiptHtml, validateReceiptRequest } from './receipt-pdf';
import { sendSerialReceipt } from './serial-printer';
import { sendEscPosReceipt } from './escpos-receipt';
import { fetchAssignedDevices, noDevices, TerminalDevices, type AssignedDevices } from './terminal-devices';
import { createSecureWebPreferences, isAllowedNavigation } from './security-policy';
import { loadTerminalCredential } from './terminal-credential';

function createContentSecurityPolicy(nonce?: string): string {
  const nonceSource = nonce ? ` 'nonce-${nonce}'` : '';
  return [
    "default-src 'self'",
    "base-uri 'self'",
    "connect-src 'self'",
    "font-src 'self'",
    "frame-ancestors 'none'",
    "img-src 'self' data:",
    "object-src 'none'",
    `script-src 'self'${nonceSource}`,
    `style-src 'self'${nonceSource}`,
  ].join('; ');
}

protocol.registerSchemesAsPrivileged([
  { scheme: 'app', privileges: { corsEnabled: true, secure: true, standard: true, supportFetchAPI: true } },
]);

const profile = resolvePosProfile(process.argv, app.getPath('userData'));
const initialTerminalCredential = process.env.CARNICERIAS_POS_TERMINAL_TOKEN;
delete process.env.CARNICERIAS_POS_TERMINAL_TOKEN;
if (initialTerminalCredential && !profile.name) {
  throw new Error('A named POS profile is required for terminal provisioning');
}
if (profile.name) {
  mkdirSync(profile.userDataPath, { recursive: true });
  app.setPath('userData', profile.userDataPath);
  app.setPath('sessionData', profile.userDataPath);
}

function validateIpcSender(event: IpcMainInvokeEvent): void {
  if (!event.senderFrame || !isAllowedNavigation(event.senderFrame.url)) {
    throw new Error('Rejected IPC sender');
  }
}

function registerNativeApi(devices: TerminalDevices,
  synchronizeDevices: () => Promise<AssignedDevices>): void {
  let hasarBusy = false;
  async function withHasar<T>(use: (session: HasarSerialSession, port: string) => Promise<T>): Promise<T> {
    const printer = devices.hasarPrinter();
    if (hasarBusy) throw new Error('HASAR_PORT_BUSY');
    hasarBusy = true;
    let session: HasarSerialSession | undefined;
    try {
      session = new HasarSerialSession(printer);
      await session.open();
      return await use(session, printer.port);
    } finally {
      try { await session?.close(); }
      finally { hasarBusy = false; }
    }
  }
  ipcMain.handle(NativeChannel.Diagnostic, (event) => {
    validateIpcSender(event);
    return createDiagnostic(process.platform, process.versions.electron);
  });
  ipcMain.handle(NativeChannel.SaveReceiptPdf, async (event, payload: unknown) => {
    validateIpcSender(event);
    const receipt = validateReceiptRequest(payload);
    const receiptWindow = new BrowserWindow({
      show: false,
      skipTaskbar: true,
      webPreferences: { contextIsolation: true, nodeIntegration: false, sandbox: true, webSecurity: true },
    });
    receiptWindow.webContents.on('will-navigate', (navigationEvent) => navigationEvent.preventDefault());
    receiptWindow.webContents.setWindowOpenHandler(() => ({ action: 'deny' }));
    try {
      // Electron's loadURL promise completes after load; printToPDF returns the PDF bytes.
      // https://www.electronjs.org/docs/latest/api/browser-window#winloadurlurl-options
      // https://www.electronjs.org/docs/latest/api/web-contents#contentsprinttopdfoptions
      const electronicInvoice = receipt.fiscal?.saleDocumentType === 'ElectronicInvoice';
      const html = electronicInvoice ? await createFiscalInvoiceHtml(receipt) : createReceiptHtml(receipt);
      await receiptWindow.loadURL(`data:text/html;charset=utf-8,${encodeURIComponent(html)}`);
      const pdf = await receiptWindow.webContents.printToPDF({
        printBackground: true,
        pageSize: electronicInvoice ? 'A4' : { width: 3.15, height: 11.7 },
        margins: electronicInvoice
          ? { top: 0, bottom: 0, left: 0, right: 0 }
          : { top: 0.12, bottom: 0.12, left: 0.12, right: 0.12 },
      });
      const directory = app.getPath('downloads');
      await mkdir(directory, { recursive: true });
      const fiscalName = receipt.fiscal
        ? `${receipt.fiscal.saleDocumentType === 'ElectronicInvoice' ? 'factura' : 'ticket-fiscal'}-homo-${receipt.fiscal.voucherType === 1 ? 'a' : 'b'}-${receipt.fiscal.pointOfSale.toString().padStart(5, '0')}-${receipt.fiscal.number.toString().padStart(8, '0')}`
        : 'ticket-no-fiscal';
      const filePath = path.join(directory, `${fiscalName}-${randomUUID()}.pdf`);
      await writeFile(filePath, pdf, { flag: 'wx' });
      return { path: filePath };
    } finally {
      receiptWindow.destroy();
    }
  });
  ipcMain.handle(NativeChannel.DeviceAvailability, async (event) => {
    validateIpcSender(event);
    return synchronizeDevices();
  });
  ipcMain.handle(NativeChannel.ReadSerialScale, async (event) => {
    validateIpcSender(event);
    await synchronizeDevices();
    return devices.readScale();
  });
  ipcMain.handle(NativeChannel.PrintSerialReceipt, async (event, payload: unknown) => {
    validateIpcSender(event);
    await synchronizeDevices();
    const receipt = validateReceiptRequest(payload);
    return sendSerialReceipt(receipt, undefined, devices.textPrinter());
  });
  ipcMain.handle(NativeChannel.PrintThermalReceipt, async (event, payload: unknown) => {
    validateIpcSender(event);
    await synchronizeDevices();
    const receipt = validateReceiptRequest(payload);
    return sendEscPosReceipt(receipt, devices.escPosPrinter());
  });
  ipcMain.handle(NativeChannel.HasarSaleAvailability, async (event) => {
    validateIpcSender(event);
    const assigned = await synchronizeDevices();
    return { port: assigned.printer?.kind === 'hasar' ? assigned.printer.port : null };
  });
  ipcMain.handle(NativeChannel.PrintHasarSaleReceipt, async (event, payload: unknown) => {
    validateIpcSender(event);
    await synchronizeDevices();
    const receipt = validateReceiptRequest(payload);
    return withHasar(async (session, port) => {
      const result = await printHasarSaleWithLedger(receipt,
        path.join(app.getPath('userData'), 'hasar-sale-prints'), async (document) => {
          if (document.kind === 'fiscal') {
            const printed = await issueHasarFiscalTicket(session, document.ticket, { allowFiscalized: true });
            return { kind: 'fiscal' as const, number: printed.number,
              trainingMode: printed.trainingMode, total: receipt.total };
          }
          const printed = await issueHasarNonFiscalTicket(session, document.lines);
          return { kind: 'nonFiscal' as const, number: null,
            trainingMode: printed.trainingMode, total: receipt.total };
        });
      return { ...result, port };
    });
  });
}

function registerApplicationProtocol(apiOrigin: string, terminalCredential?: string): void {
  const webRoot = path.join(__dirname, '..', 'web');

  protocol.handle('app', async (request) => {
    const backendUrl = toBackendUrl(request.url, apiOrigin);
    if (backendUrl) {
      return net.fetch(toBackendRequest(request, { terminalCredential, backendOrigin: apiOrigin }));
    }

    if (request.method !== 'GET') {
      return new Response(null, { status: 405 });
    }

    try {
      const asset = resolveAppAsset(webRoot, request.url);
      const response = await net.fetch(pathToFileURL(asset).toString());
      const headers = new Headers(response.headers);
      const isApplicationDocument = path.basename(asset) === 'index.html';
      const nonce = isApplicationDocument ? randomBytes(32).toString('base64url') : undefined;
      headers.set('Content-Security-Policy', createContentSecurityPolicy(nonce));

      if (nonce) {
        const html = (await response.text()).replace(
          '<app-root',
          `<app-root ngCspNonce="${nonce}"`,
        );
        return new Response(html, { headers, status: response.status });
      }

      return new Response(response.body, { headers, status: response.status });
    } catch {
      return new Response(null, { status: 400 });
    }
  });
}

function createWindow(): BrowserWindow {
  const window = new BrowserWindow({
    height: 800,
    show: false,
    webPreferences: createSecureWebPreferences(path.join(__dirname, 'preload.js')),
    width: 1280,
  });

  window.webContents.on('will-navigate', (event, url) => {
    if (!isAllowedNavigation(url)) event.preventDefault();
  });
  window.webContents.setWindowOpenHandler(() => ({ action: 'deny' }));
  window.once('ready-to-show', () => {
    window.maximize();
    window.show();
  });
  void window.loadURL(initialUrlForProfile(profile));
  return window;
}

app.whenReady().then(async () => {
  const configPath = resolveConfigPath(process.argv);
  const deploymentConfig = parseDeploymentConfig(
    configPath ? JSON.parse(readFileSync(configPath, 'utf8')) as unknown : undefined,
    app.isPackaged,
  );
  const terminalCredential = await loadTerminalCredential(
    app.getPath('userData'),
    initialTerminalCredential,
    safeStorage,
  );
  session.defaultSession.setPermissionCheckHandler(() => false);
  session.defaultSession.setPermissionRequestHandler((_webContents, _permission, callback) => callback(false));
  registerApplicationProtocol(deploymentConfig.apiOrigin, terminalCredential);
  const devices = new TerminalDevices();
  let synchronization: Promise<AssignedDevices> | undefined;
  function synchronizeDevices(): Promise<AssignedDevices> {
    if (!synchronization) {
      synchronization = (async () => {
        try {
          const assigned = await fetchAssignedDevices(deploymentConfig.apiOrigin,
            terminalCredential, (request) => net.fetch(request));
          await devices.apply(assigned);
          return assigned;
        } catch (error) {
          await devices.apply(noDevices);
          throw error;
        } finally {
          synchronization = undefined;
        }
      })();
    }
    return synchronization;
  }
  registerNativeApi(devices, synchronizeDevices);
  app.on('before-quit', () => { void devices.stop(); });
  createWindow();

  app.on('activate', () => {
    if (BrowserWindow.getAllWindows().length === 0) createWindow();
  });
}).catch((error: unknown) => {
  const message = error instanceof Error ? error.message : 'POS startup failed';
  console.error(message);
  dialog.showErrorBox('No se pudo iniciar Carnicerías POS', message);
  app.quit();
});

app.on('window-all-closed', () => {
  if (process.platform !== 'darwin') app.quit();
});
