import path from 'node:path';
import { pathToFileURL } from 'node:url';
import { app, BrowserWindow, clipboard, ipcMain, type IpcMainInvokeEvent } from 'electron';
import QRCode from 'qrcode';
import { EmulatorChannel, type EmulatorState, type KretzQrPreview } from './emulator-api';
import { makeKretzConsolidatedQr, makeRandomKretzCuts, nextSimulatedWeight } from './kretz-emulator';
import { loadLabScaleCatalog, selectLabScaleCompany } from './lab-scale-catalog';
import { createVirtualDeviceServer, virtualDevicePipe, type ScaleReading,
  type VirtualDeviceRequest, type VirtualDeviceResponse } from './virtual-device-protocol';

app.setPath('userData', path.join(app.getPath('userData'), 'device-emulator'));

let reading: ScaleReading = { weightKg: 0, stable: false, observedAtUtc: new Date().toISOString() };
let consoleWindow: BrowserWindow;

function verifyConsoleSender(event: IpcMainInvokeEvent): void {
  if (!consoleWindow || event.sender !== consoleWindow.webContents ||
      event.senderFrame?.url !== pathToFileURL(path.join(__dirname, '..', 'devices', 'emulator.html')).href)
    throw new Error('Rejected emulator sender');
}

async function handleDeviceRequest(_request: VirtualDeviceRequest): Promise<VirtualDeviceResponse> {
  return { ok: true, reading: { ...reading, observedAtUtc: new Date().toISOString() } };
}

app.whenReady().then(async () => {
  const server = createVirtualDeviceServer(handleDeviceRequest);
  await new Promise<void>((resolve, reject) => {
    server.once('error', reject);
    server.listen(virtualDevicePipe, resolve);
  });
  app.once('will-quit', () => server.close());

  consoleWindow = new BrowserWindow({
    width: 780, height: 850, minWidth: 560, minHeight: 600,
    title: 'Balanza virtual · Carnicerías',
    webPreferences: {
      preload: path.join(__dirname, 'emulator-preload.js'),
      contextIsolation: true, nodeIntegration: false, sandbox: true, webSecurity: true,
    },
  });
  consoleWindow.webContents.on('will-navigate', (event) => event.preventDefault());
  consoleWindow.webContents.setWindowOpenHandler(() => ({ action: 'deny' }));
  ipcMain.handle(EmulatorChannel.State, (event): EmulatorState => {
    verifyConsoleSender(event);
    return { weightKg: reading.weightKg, stable: reading.stable };
  });
  ipcMain.handle(EmulatorChannel.SetWeight, (event, weightKg: unknown, stable: unknown) => {
    verifyConsoleSender(event);
    if (typeof weightKg !== 'number' || !Number.isFinite(weightKg) || weightKg < 0 || weightKg > 10000 ||
        Math.round(weightKg * 1000) !== weightKg * 1000 || typeof stable !== 'boolean')
      throw new Error('INVALID_WEIGHT');
    reading = { weightKg, stable, observedAtUtc: new Date().toISOString() };
    return { weightKg: reading.weightKg, stable: reading.stable };
  });
  ipcMain.handle(EmulatorChannel.SimulateWeight, (event) => {
    verifyConsoleSender(event);
    reading = { weightKg: nextSimulatedWeight(), stable: true,
      observedAtUtc: new Date().toISOString() };
    return { weightKg: reading.weightKg, stable: true };
  });
  ipcMain.handle(EmulatorChannel.RandomKretzQr, async (event,
    ticketNumber: unknown, scaleNumber: unknown): Promise<KretzQrPreview> => {
    verifyConsoleSender(event);
    if (typeof ticketNumber !== 'number' || typeof scaleNumber !== 'number')
      throw new Error('INVALID_KRETZ_TICKET');
    const company = selectLabScaleCompany(await loadLabScaleCatalog(),
      process.env.CARNICERIAS_EMULATOR_COMPANY_ID);
    const items = makeRandomKretzCuts(company.products.map(product => product.plu));
    const payload = makeKretzConsolidatedQr(items, { now: new Date(),
      ticketNumber, seller: ' A', scaleNumber });
    return { payload, imageUrl: await QRCode.toDataURL(payload, { width: 360, margin: 2 }),
      companyName: company.companyName, catalogCount: company.products.length,
      items: items.map(item => ({ ...item,
        productName: company.products.find(product => product.plu === item.plu)!.name })) };
  });
  ipcMain.handle(EmulatorChannel.CopyQr, (event, payload: unknown) => {
    verifyConsoleSender(event);
    if (typeof payload !== 'string' || payload.length > 1500 || !payload.includes('\n'))
      throw new Error('INVALID_KRETZ_TICKET');
    clipboard.writeText(payload);
  });
  await consoleWindow.loadFile(path.join(__dirname, '..', 'devices', 'emulator.html'));
}).catch((error: unknown) => {
  console.error(error instanceof Error ? error.message : 'Device emulator failed');
  app.quit();
});

app.on('window-all-closed', () => app.quit());
