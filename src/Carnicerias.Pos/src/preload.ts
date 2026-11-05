import { contextBridge, ipcRenderer } from 'electron';
import type { CarniceriasNativeApi } from './native-api';
import { NativeChannel } from './native-api';
import type { ReceiptRequest } from './receipt-pdf';
import type { ShiftClosingReport } from './shift-closing-report';

const nativeApi: CarniceriasNativeApi = Object.freeze({
  getDiagnostic: () => ipcRenderer.invoke(NativeChannel.Diagnostic),
  saveReceiptPdf: (request: ReceiptRequest) => ipcRenderer.invoke(NativeChannel.SaveReceiptPdf, request),
  saveShiftReportPdf: (report: ShiftClosingReport) =>
    ipcRenderer.invoke(NativeChannel.SaveShiftReportPdf, report),
  printShiftReport: (report: ShiftClosingReport) =>
    ipcRenderer.invoke(NativeChannel.PrintShiftReport, report),
  readSerialScale: () => ipcRenderer.invoke(NativeChannel.ReadSerialScale),
  getDeviceAvailability: () => ipcRenderer.invoke(NativeChannel.DeviceAvailability),
  printSerialReceipt: (request: ReceiptRequest) => ipcRenderer.invoke(NativeChannel.PrintSerialReceipt, request),
  printThermalReceipt: (request: ReceiptRequest) => ipcRenderer.invoke(NativeChannel.PrintThermalReceipt, request),
  getHasarSaleAvailability: () => ipcRenderer.invoke(NativeChannel.HasarSaleAvailability),
  printHasarSaleReceipt: (request: ReceiptRequest) =>
    ipcRenderer.invoke(NativeChannel.PrintHasarSaleReceipt, request),
});

contextBridge.exposeInMainWorld('carnicerias', nativeApi);
