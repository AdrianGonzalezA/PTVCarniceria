import { contextBridge, ipcRenderer } from 'electron';
import { EmulatorChannel } from './emulator-api';

contextBridge.exposeInMainWorld('carniceriasEmulator', Object.freeze({
  getState: () => ipcRenderer.invoke(EmulatorChannel.State),
  setWeight: (weightKg: number, stable: boolean) =>
    ipcRenderer.invoke(EmulatorChannel.SetWeight, weightKg, stable),
  makeRandomKretzQr: (ticketNumber: number, scaleNumber: number) =>
    ipcRenderer.invoke(EmulatorChannel.RandomKretzQr, ticketNumber, scaleNumber),
  simulateWeight: () => ipcRenderer.invoke(EmulatorChannel.SimulateWeight),
  copyKretzQr: (payload: string) => ipcRenderer.invoke(EmulatorChannel.CopyQr, payload),
}));
