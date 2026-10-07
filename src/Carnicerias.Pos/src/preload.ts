import { contextBridge, ipcRenderer } from 'electron';
import type { CarniceriasNativeApi } from './native-api';
import { diagnosticChannel } from './native-api';

const nativeApi: CarniceriasNativeApi = Object.freeze({
  getDiagnostic: () => ipcRenderer.invoke(diagnosticChannel),
});

contextBridge.exposeInMainWorld('carnicerias', nativeApi);
