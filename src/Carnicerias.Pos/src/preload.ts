import { contextBridge, ipcRenderer } from 'electron';
import type { CarniceriasNativeApi } from './native-api';
import { NativeChannel } from './native-api';
import type { ReceiptRequest } from './receipt-pdf';

const nativeApi: CarniceriasNativeApi = Object.freeze({
  getDiagnostic: () => ipcRenderer.invoke(NativeChannel.Diagnostic),
  saveReceiptPdf: (request: ReceiptRequest) => ipcRenderer.invoke(NativeChannel.SaveReceiptPdf, request),
  readVirtualScale: () => ipcRenderer.invoke(NativeChannel.ReadVirtualScale),
  printVirtualReceipt: (request: ReceiptRequest) => ipcRenderer.invoke(NativeChannel.PrintVirtualReceipt, request),
});

contextBridge.exposeInMainWorld('carnicerias', nativeApi);
