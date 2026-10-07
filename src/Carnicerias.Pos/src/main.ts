import path from 'node:path';
import { randomBytes } from 'node:crypto';
import { pathToFileURL } from 'node:url';
import {
  app,
  BrowserWindow,
  ipcMain,
  net,
  protocol,
  session,
  type IpcMainInvokeEvent,
} from 'electron';
import { resolveAppAsset, toBackendUrl } from './app-protocol';
import { createDiagnostic } from './diagnostic';
import { diagnosticChannel } from './native-api';
import { createSecureWebPreferences, isAllowedNavigation } from './security-policy';

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

function validateIpcSender(event: IpcMainInvokeEvent): void {
  if (!event.senderFrame || !isAllowedNavigation(event.senderFrame.url)) {
    throw new Error('Rejected IPC sender');
  }
}

function registerNativeApi(): void {
  ipcMain.handle(diagnosticChannel, (event) => {
    validateIpcSender(event);
    return createDiagnostic(process.platform, process.versions.electron);
  });
}

function registerApplicationProtocol(): void {
  const webRoot = path.join(__dirname, '..', 'web');

  protocol.handle('app', async (request) => {
    const backendUrl = toBackendUrl(request.url);
    if (backendUrl) {
      return net.fetch(new Request(backendUrl, request));
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

app.whenReady().then(() => {
  session.defaultSession.setPermissionCheckHandler(() => false);
  session.defaultSession.setPermissionRequestHandler((_webContents, _permission, callback) => callback(false));
  registerApplicationProtocol();
  registerNativeApi();
  createWindow();

  app.on('activate', () => {
    if (BrowserWindow.getAllWindows().length === 0) createWindow();
  });
});

app.on('window-all-closed', () => {
  if (process.platform !== 'darwin') app.quit();
});
