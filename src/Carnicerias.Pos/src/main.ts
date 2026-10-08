import path from 'node:path';
import { randomBytes, randomUUID } from 'node:crypto';
import { mkdirSync } from 'node:fs';
import { mkdir, writeFile } from 'node:fs/promises';
import { pathToFileURL } from 'node:url';
import {
  app,
  BrowserWindow,
  ipcMain,
  net,
  protocol,
  safeStorage,
  session,
  type IpcMainInvokeEvent,
} from 'electron';
import { resolveAppAsset, toBackendRequest, toBackendUrl } from './app-protocol';
import { createDiagnostic } from './diagnostic';
import { NativeChannel } from './native-api';
import { resolvePosProfile } from './pos-profile';
import { createReceiptHtml, validateReceiptRequest } from './receipt-pdf';
import { requestVirtualDevice, virtualDevicePipe } from './virtual-device-protocol';
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

function registerNativeApi(): void {
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
      await receiptWindow.loadURL(`data:text/html;charset=utf-8,${encodeURIComponent(createReceiptHtml(receipt))}`);
      const pdf = await receiptWindow.webContents.printToPDF({
        printBackground: true,
        pageSize: { width: 3.15, height: 11.7 },
        margins: { top: 0.12, bottom: 0.12, left: 0.12, right: 0.12 },
      });
      const directory = path.join(app.getPath('userData'), 'tickets');
      await mkdir(directory, { recursive: true });
      const filePath = path.join(directory, `ticket-${randomUUID()}.pdf`);
      await writeFile(filePath, pdf, { flag: 'wx' });
      return { path: filePath };
    } finally {
      receiptWindow.destroy();
    }
  });
  ipcMain.handle(NativeChannel.ReadVirtualScale, async (event) => {
    validateIpcSender(event);
    const response = await requestVirtualDevice(virtualDevicePipe, { action: 'read-scale' });
    if (!response.ok || !('reading' in response)) throw new Error('VIRTUAL_SCALE_UNAVAILABLE');
    const { weightKg, stable, observedAtUtc } = response.reading;
    if (typeof weightKg !== 'number' || !Number.isFinite(weightKg) ||
        typeof stable !== 'boolean' || typeof observedAtUtc !== 'string' ||
        Number.isNaN(Date.parse(observedAtUtc))) throw new Error('VIRTUAL_SCALE_INVALID_READING');
    return response.reading;
  });
  ipcMain.handle(NativeChannel.PrintVirtualReceipt, async (event, payload: unknown) => {
    validateIpcSender(event);
    const receipt = validateReceiptRequest(payload);
    const response = await requestVirtualDevice(virtualDevicePipe, { action: 'print-receipt', receipt });
    if (!response.ok || !('path' in response) || typeof response.path !== 'string' ||
        response.path.length > 4096) throw new Error('VIRTUAL_PRINTER_UNAVAILABLE');
    return { path: response.path };
  });
}

function registerApplicationProtocol(terminalCredential?: string): void {
  const webRoot = path.join(__dirname, '..', 'web');

  protocol.handle('app', async (request) => {
    const backendUrl = toBackendUrl(request.url);
    if (backendUrl) {
      return net.fetch(toBackendRequest(request, terminalCredential));
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
  void window.loadURL('app://bundle/index.html');
  return window;
}

app.whenReady().then(async () => {
  const terminalCredential = await loadTerminalCredential(
    app.getPath('userData'),
    initialTerminalCredential,
    safeStorage,
  );
  session.defaultSession.setPermissionCheckHandler(() => false);
  session.defaultSession.setPermissionRequestHandler((_webContents, _permission, callback) => callback(false));
  registerApplicationProtocol(terminalCredential);
  registerNativeApi();
  createWindow();

  app.on('activate', () => {
    if (BrowserWindow.getAllWindows().length === 0) createWindow();
  });
}).catch((error: unknown) => {
  console.error(error instanceof Error ? error.message : 'POS startup failed');
  app.quit();
});

app.on('window-all-closed', () => {
  if (process.platform !== 'darwin') app.quit();
});
