import { SerialPort } from 'serialport';
import type { SerialDeviceConfig } from './deployment-config';
import { encodeHasarCommand, parseHasarResponse, parseHasarStatus,
  type HasarResponse, type HasarStatus } from './hasar-protocol';

interface HasarPort {
  readonly isOpen: boolean;
  on(event: 'data', listener: (data: Buffer) => void): this;
  on(event: 'error', listener: (error: Error) => void): this;
  off(event: 'data', listener: (data: Buffer) => void): this;
  off(event: 'error', listener: (error: Error) => void): this;
  open(callback: (error?: Error | null) => void): void;
  write(bytes: Buffer, callback: (error?: Error | null) => void): void;
  close(callback: (error?: Error | null) => void): void;
}

const portOptions = {
  dataBits: 8, stopBits: 1, parity: 'none', rtscts: false,
  xon: false, xoff: false, autoOpen: false,
} as const;
type PortOptions = typeof portOptions & { path: string; baudRate: number };

export class HasarSerialSession {
  private port: HasarPort;
  private bytes: number[] = [];
  private waiting?: { resolve: (byte: number) => void; reject: (error: Error) => void;
    timer: NodeJS.Timeout };
  private failure?: Error;
  private sequence = 0x20;
  private inCommand = false;

  constructor(device: SerialDeviceConfig,
    createPort: (options: PortOptions) => HasarPort = (options) => new SerialPort(options)) {
    this.port = createPort({ ...portOptions, path: device.port, baudRate: device.baudRate });
    this.port.on('data', this.onData);
    this.port.on('error', this.onError);
  }

  private onData = (chunk: Buffer): void => {
    for (const byte of chunk) {
      const waiting = this.waiting;
      if (waiting) {
        this.waiting = undefined;
        clearTimeout(waiting.timer);
        waiting.resolve(byte);
      } else this.bytes.push(byte);
    }
  };

  private onError = (error: Error): void => {
    this.failure = error;
    const waiting = this.waiting;
    if (waiting) {
      this.waiting = undefined;
      clearTimeout(waiting.timer);
      waiting.reject(error);
    }
  };

  async open(): Promise<void> {
    if (this.port.isOpen) throw new Error('HASAR_ALREADY_OPEN');
    await new Promise<void>((resolve, reject) => this.port.open((error) => error ? reject(error) : resolve()));
  }

  private async write(bytes: Buffer): Promise<void> {
    await new Promise<void>((resolve, reject) => this.port.write(bytes,
      (error) => error ? reject(error) : resolve()));
  }

  private async readByte(deadline: number): Promise<number> {
    if (this.failure) throw this.failure;
    const queued = this.bytes.shift();
    if (queued !== undefined) return queued;
    const remaining = deadline - Date.now();
    if (remaining <= 0) throw new Error('HASAR_RESPONSE_TIMEOUT');
    return new Promise<number>((resolve, reject) => {
      const timer = setTimeout(() => {
        this.waiting = undefined;
        reject(new Error('HASAR_RESPONSE_TIMEOUT'));
      }, remaining);
      this.waiting = { resolve, reject, timer };
    });
  }

  private async readFrame(deadline: number): Promise<Buffer> {
    let byte: number;
    do {
      byte = await this.readByte(deadline);
      // DC2 means processing; DC4 means paper error. In both cases a framed reply follows.
    } while (byte === 0x12 || byte === 0x14 || byte === 0x06);
    if (byte !== 0x02) throw new Error(`HASAR_UNEXPECTED_BYTE_${byte.toString(16)}`);
    const frame = [byte];
    while (frame.length <= 4096) {
      byte = await this.readByte(deadline);
      frame.push(byte);
      if (byte === 0x03) {
        for (let index = 0; index < 4; index++) frame.push(await this.readByte(deadline));
        return Buffer.from(frame);
      }
    }
    throw new Error('HASAR_FRAME_TOO_LONG');
  }

  async command(command: number, fields: readonly string[] = [], timeoutMs = 60000): Promise<HasarResponse> {
    if (!this.port.isOpen) throw new Error('HASAR_PORT_CLOSED');
    if (this.inCommand) throw new Error('HASAR_COMMAND_IN_PROGRESS');
    this.inCommand = true;
    const sequence = this.sequence;
    this.sequence = sequence === 0x7e ? 0x20 : sequence + 2;
    try {
      const frame = encodeHasarCommand(sequence, command, fields);
      const deadline = Date.now() + timeoutMs;
      await this.write(frame);
      const acknowledgement = await this.readByte(deadline);
      if (acknowledgement === 0x15) throw new Error('HASAR_NAK');
      if (acknowledgement !== 0x06)
        throw new Error(`HASAR_ACK_INVALID_${acknowledgement.toString(16)}`);
      for (let attempt = 0; attempt < 2; attempt++) {
        const responseFrame = await this.readFrame(deadline);
        let response: HasarResponse;
        try { response = parseHasarResponse(responseFrame, sequence, command); }
        catch (error) {
          if (error instanceof Error && error.message === 'HASAR_BCC_INVALID' && attempt === 0) {
            await this.write(Buffer.from([0x15]));
            continue;
          }
          // A valid but unexpected frame must be ACKed so the controller does not block.
          if (!(error instanceof Error) || error.message !== 'HASAR_BCC_INVALID')
            await this.write(Buffer.from([0x06]));
          throw error;
        }
        await this.write(Buffer.from([0x06]));
        return response;
      }
      throw new Error('HASAR_RESPONSE_RETRY_EXHAUSTED');
    } finally { this.inCommand = false; }
  }

  async queryStatus(): Promise<HasarStatus> {
    const response = await this.command(0x2a, [], 10000);
    return parseHasarStatus(response.fields);
  }

  async close(): Promise<void> {
    try {
      if (this.port.isOpen)
        await new Promise<void>((resolve, reject) => this.port.close(
          (error) => error ? reject(error) : resolve()));
    } finally {
      this.port.off('data', this.onData);
      this.port.off('error', this.onError);
    }
  }
}
