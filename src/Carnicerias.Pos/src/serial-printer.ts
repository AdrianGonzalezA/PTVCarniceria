import { SerialPort } from 'serialport';
import type { ReceiptRequest } from './receipt-pdf';
import { formatSerialReceipt } from './serial-receipt';

const printerOptions = {
  path: 'COM1', baudRate: 9600, dataBits: 8, stopBits: 1, parity: 'none',
  rtscts: false, xon: false, xoff: false, autoOpen: false,
} as const;

type SerialWritable = Pick<SerialPort, 'open' | 'write' | 'drain' | 'close' | 'on' | 'off' | 'isOpen'>;

export async function sendSerialReceipt(
  receipt: ReceiptRequest,
  createPort: (options: typeof printerOptions) => SerialWritable = (options) => new SerialPort(options),
): Promise<{ readonly port: string; readonly bytesWritten: number;
  readonly confirmation: 'drained' | 'write-only' }> {
  const bytes = Buffer.from(formatSerialReceipt(receipt), 'utf8');
  const transmissionMs = Math.ceil(bytes.length * 10 / printerOptions.baudRate * 1000) + 150;
  const port = createPort(printerOptions);
  let confirmation: 'drained' | 'write-only' = 'drained';
  let rejectActive: (error: Error) => void = () => {};
  const onError = (error: Error) => rejectActive(error);
  port.on('error', onError);

  let timeout: NodeJS.Timeout | undefined;
  try {
    await new Promise<void>((resolve, reject) => {
      let settled = false;
      const fail = (error: Error) => {
        if (settled) return;
        settled = true;
        reject(error);
      };
      rejectActive = fail;
      timeout = setTimeout(() => fail(new Error('SERIAL_PRINTER_TIMEOUT')),
        Math.max(15000, transmissionMs + 2000));
      port.open((openError) => {
        if (openError) { fail(openError); return; }
        port.write(bytes, (writeError) => {
          if (writeError) { fail(writeError); return; }
          port.drain((drainError) => {
            if (drainError) {
              // SerialPort drains with FlushFileBuffers on Windows:
              // https://serialport.io/docs/api-stream/#drain
              // The local HHD virtual driver accepts writes but rejects that call.
              // Wait at least one wire-time before closing; this is not a device acknowledgment.
              if (process.platform !== 'win32' ||
                  !/^Draining connection \(FlushFileBuffers\): Unknown error code 1$/.test(drainError.message)) {
                fail(drainError);
                return;
              }
              confirmation = 'write-only';
              setTimeout(() => {
                if (settled) return;
                settled = true;
                resolve();
              }, transmissionMs);
              return;
            }
            if (settled) return;
            settled = true;
            resolve();
          });
        });
      });
    });
    return { port: printerOptions.path, bytesWritten: bytes.length, confirmation };
  } finally {
    clearTimeout(timeout);
    try {
      if (port.isOpen)
        await new Promise<void>((resolve, reject) => port.close((error) => error ? reject(error) : resolve()));
    } finally {
      port.off('error', onError);
    }
  }
}
