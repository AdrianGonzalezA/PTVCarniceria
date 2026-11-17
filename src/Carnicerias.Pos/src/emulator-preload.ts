import { contextBridge, ipcRenderer } from 'electron';
import { EmulatorChannel } from './emulator-api';

contextBridge.exposeInMainWorld('carniceriasEmulator', Object.freeze({
  getState: () => ipcRenderer.invoke(EmulatorChannel.State),
  setWeight: (weightKg: number, stable: boolean) =>
    ipcRenderer.invoke(EmulatorChannel.SetWeight, weightKg, stable),
  makeKretzQr: (items: readonly { plu: string; weightKg: number }[],
    ticketNumber: number, scaleNumber: number) =>
    ipcRenderer.invoke(EmulatorChannel.KretzQr, items, ticketNumber, scaleNumber),
  copyKretzQr: (payload: string) => ipcRenderer.invoke(EmulatorChannel.CopyQr, payload),
}));
