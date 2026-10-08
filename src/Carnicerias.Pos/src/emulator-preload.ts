import { contextBridge, ipcRenderer } from 'electron';
import { EmulatorChannel } from './emulator-api';

contextBridge.exposeInMainWorld('carniceriasEmulator', Object.freeze({
  getState: () => ipcRenderer.invoke(EmulatorChannel.State),
  setWeight: (weightKg: number, stable: boolean) =>
    ipcRenderer.invoke(EmulatorChannel.SetWeight, weightKg, stable),
}));
