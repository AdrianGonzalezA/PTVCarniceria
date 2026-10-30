import type { SerialDeviceConfig } from './deployment-config';
import { SerialScale, type ScaleReading } from './serial-scale';

export interface AssignedPrinter extends SerialDeviceConfig {
  readonly kind: 'text' | 'hasar' | 'escpos';
  readonly paperWidthMm?: 58 | 80 | null;
}

export interface AssignedDevices {
  readonly scale: SerialDeviceConfig | null;
  readonly printer: AssignedPrinter | null;
}

export const noDevices: AssignedDevices = { scale: null, printer: null };
const baudRates = new Set([1200, 2400, 4800, 9600, 19200, 38400, 57600, 115200]);

function record(value: unknown): Record<string, unknown> {
  if (typeof value !== 'object' || value === null || Array.isArray(value))
    throw new Error('POS_DEVICE_CONFIG_INVALID');
  return value as Record<string, unknown>;
}

function serial(value: unknown): SerialDeviceConfig {
  const input = record(value);
  if (typeof input.port !== 'string' || !/^COM[1-9][0-9]{0,2}$/iu.test(input.port) ||
      typeof input.baudRate !== 'number' || !baudRates.has(input.baudRate))
    throw new Error('POS_DEVICE_CONFIG_INVALID');
  return { port: input.port.toUpperCase(), baudRate: input.baudRate };
}

export function parseAssignedDevices(value: unknown): AssignedDevices {
  const devices = record(record(value).devices);
  if (!Object.hasOwn(devices, 'scale') || !Object.hasOwn(devices, 'printer'))
    throw new Error('POS_DEVICE_CONFIG_INVALID');
  const scale = devices.scale === null ? null : serial(devices.scale);
  let printer: AssignedPrinter | null = null;
  if (devices.printer !== null) {
    const input = record(devices.printer);
    const setting = serial(input);
    if ((input.kind !== 'text' && input.kind !== 'hasar' && input.kind !== 'escpos') ||
        (input.kind === 'hasar' && setting.baudRate !== 9600) ||
        (input.kind === 'escpos' ? ![58, 80].includes(Number(input.paperWidthMm))
          : input.paperWidthMm != null))
      throw new Error('POS_DEVICE_CONFIG_INVALID');
    printer = { ...setting, kind: input.kind,
      ...(input.kind === 'escpos' ? { paperWidthMm: input.paperWidthMm as 58 | 80 } : {}) };
  }
  if (scale && printer && scale.port === printer.port)
    throw new Error('POS_DEVICE_CONFIG_INVALID');
  return { scale, printer };
}

export async function fetchAssignedDevices(apiOrigin: string, credential: string | undefined,
  send: (request: Request) => Promise<Response>): Promise<AssignedDevices> {
  if (!credential) return noDevices;
  const request = new Request(new URL('/api/pos-terminals/current', apiOrigin), {
    headers: { 'X-Pos-Terminal-Credential': credential },
    cache: 'no-store', signal: AbortSignal.timeout(5000),
  });
  const response = await send(request);
  if (!response.ok) throw new Error('POS_DEVICE_CONFIG_UNAVAILABLE');
  return parseAssignedDevices(await response.json() as unknown);
}

interface ScaleReader {
  start(): void;
  stop(): Promise<void>;
  read(): ScaleReading;
}

export class TerminalDevices {
  private scale: ScaleReader | undefined;
  private settings: AssignedDevices = noDevices;

  constructor(private readonly createScale: (config: SerialDeviceConfig) => ScaleReader =
    (config) => new SerialScale(undefined, config)) {}

  availability(): AssignedDevices { return this.settings; }

  async apply(settings: AssignedDevices): Promise<void> {
    const previous = this.settings.scale;
    if (previous?.port !== settings.scale?.port ||
        previous?.baudRate !== settings.scale?.baudRate) {
      await this.scale?.stop();
      this.scale = settings.scale ? this.createScale(settings.scale) : undefined;
      this.scale?.start();
    }
    this.settings = settings;
  }

  readScale(): ScaleReading {
    if (!this.scale) throw new Error('SERIAL_SCALE_NOT_CONFIGURED');
    return this.scale.read();
  }

  textPrinter(): SerialDeviceConfig {
    if (this.settings.printer?.kind !== 'text') throw new Error('PRINTER_NOT_CONFIGURED');
    return this.settings.printer;
  }

  hasarPrinter(): SerialDeviceConfig {
    if (this.settings.printer?.kind !== 'hasar') throw new Error('HASAR_PRINTER_NOT_CONFIGURED');
    return this.settings.printer;
  }

  escPosPrinter(): AssignedPrinter & { readonly paperWidthMm: 58 | 80 } {
    if (this.settings.printer?.kind !== 'escpos') throw new Error('THERMAL_PRINTER_NOT_CONFIGURED');
    return this.settings.printer as AssignedPrinter & { readonly paperWidthMm: 58 | 80 };
  }

  async stop(): Promise<void> { await this.apply(noDevices); }
}
