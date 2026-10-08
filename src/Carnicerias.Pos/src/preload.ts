import { contextBridge, ipcRenderer } from 'electron';
import type { CarniceriasNativeApi } from './native-api';
import { diagnosticChannel, saveReceiptPdfChannel } from './native-api';
import type { ReceiptRequest } from './receipt-pdf';

const nativeApi: CarniceriasNativeApi = Object.freeze({
  getDiagnostic: () => ipcRenderer.invoke(diagnosticChannel),
  saveReceiptPdf: (request: ReceiptRequest) => ipcRenderer.invoke(saveReceiptPdfChannel, request),
});

contextBridge.exposeInMainWorld('carnicerias', nativeApi);
