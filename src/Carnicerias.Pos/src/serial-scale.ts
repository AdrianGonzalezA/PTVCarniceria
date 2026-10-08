import { SerialPort } from 'serialport';

const scaleOptions = {
  path: 'COM6', baudRate: 9600, dataBits: 8, stopBits: 1, parity: 'none',
  rtscts: false, xon: false, xoff: false, autoOpen: false,
} as const;
const maximumFrameBytes = 64;
const maximumReadingAgeMs = 30000;

export interface ScaleReading {
  readonly weightKg: number;
  readonly stable: boolean;
  readonly observedAtUtc: string;
}

type ScalePort = Pick<SerialPort, 'on' | 'open' | 'close' | 'isOpen'>;

export function parseScaleFrame(frame: string): Pick<ScaleReading, 'weightKg' | 'stable'> | null {
  const match = /^(ST|US),([0-9]{1,5}(?:\.[0-9]{1,3})?),kg$/.exec(frame);
  if (!match) return null;
  const weightKg = Number(match[2]);
  if (!Number.isFinite(weightKg) || weightKg <= 0 || weightKg > 10000) return null;
  return { weightKg, stable: match[1] === 'ST' };
}

export class SerialScale {
  private port: ScalePort | undefined;
  private reading: ScaleReading | undefined;
  private state: 'starting' | 'ready' | 'unavailable' | 'invalid' = 'starting';
  private frame = '';
  private discardingFrame = false;
  private retry: NodeJS.Timeout | undefined;
  private opening: Promise<void> = Promise.resolve();
  private stopped = false;

  constructor(private readonly createPort: (options: typeof scaleOptions) => ScalePort =
    (options) => new SerialPort(options)) {}

  start(): void {
    if (this.port || this.stopped) return;
    this.open();
  }

  whenOpen(): Promise<void> { return this.opening; }

  read(now = Date.now()): ScaleReading {
    if (this.state === 'unavailable') throw new Error('SERIAL_SCALE_UNAVAILABLE');
    if (this.state === 'invalid') throw new Error('SERIAL_SCALE_INVALID_READING');
    if (!this.reading) throw new Error('SERIAL_SCALE_NO_READING');
    if (now - Date.parse(this.reading.observedAtUtc) > maximumReadingAgeMs)
      throw new Error('SERIAL_SCALE_STALE_READING');
    return this.reading;
  }

  async stop(): Promise<void> {
    this.stopped = true;
    clearTimeout(this.retry);
    await this.opening;
    const port = this.port;
    this.port = undefined;
    if (port?.isOpen)
      await new Promise<void>((resolve) => port.close(() => resolve()));
  }

  private open(): void {
    this.state = 'starting';
    this.opening = new Promise<void>((resolve) => {
      let port: ScalePort;
      try { port = this.createPort(scaleOptions); }
      catch { this.state = 'unavailable'; this.scheduleRetry(); resolve(); return; }
      this.port = port;
      port.on('data', (chunk: Buffer) => this.accept(chunk));
      port.on('error', () => { this.state = 'unavailable'; this.reading = undefined; });
      port.on('close', () => {
        this.state = 'unavailable';
        this.reading = undefined;
        this.port = undefined;
        this.scheduleRetry();
      });
      port.open((error) => {
        if (error) {
          this.state = 'unavailable';
          this.port = undefined;
          this.scheduleRetry();
        } else this.state = 'ready';
        resolve();
      });
    });
  }

  private scheduleRetry(): void {
    if (this.stopped || this.retry) return;
    this.retry = setTimeout(() => { this.retry = undefined; this.open(); }, 3000);
    this.retry.unref();
  }

  private accept(chunk: Buffer): void {
    for (const byte of chunk) {
      if (byte === 13 || byte === 10) {
        if (this.discardingFrame) {
          this.discardingFrame = false;
          this.frame = '';
          continue;
        }
        if (this.frame) {
          const parsed = parseScaleFrame(this.frame);
          this.frame = '';
          if (parsed) {
            this.reading = { ...parsed, observedAtUtc: new Date().toISOString() };
            this.state = 'ready';
          } else {
            this.reading = undefined;
            this.state = 'invalid';
          }
        }
      } else if (this.discardingFrame) {
        continue;
      } else if (byte >= 32 && byte <= 126 && this.frame.length < maximumFrameBytes) {
        this.frame += String.fromCharCode(byte);
      } else {
        this.frame = '';
        this.discardingFrame = true;
        this.reading = undefined;
        this.state = 'invalid';
      }
    }
  }
}
