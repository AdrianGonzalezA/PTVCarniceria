import { EventEmitter } from 'node:events';
import { describe, expect, it } from 'vitest';
import { encodeHasarCommand } from '../src/hasar-protocol';
import { HasarSerialSession } from '../src/hasar-serial';

class FakePort extends EventEmitter {
  isOpen = false;
  readonly writes: Buffer[] = [];
  readonly options: unknown;
  reply?: (bytes: Buffer) => void;

  constructor(options: unknown) { super(); this.options = options; }
  open(callback: (error?: Error | null) => void): void { this.isOpen = true; callback(); }
  write(bytes: Buffer, callback: (error?: Error | null) => void): void {
    this.writes.push(Buffer.from(bytes));
    callback();
    this.reply?.(bytes);
  }
  close(callback: (error?: Error | null) => void): void { this.isOpen = false; callback(); }
}

describe('Hasar serial transaction', () => {
  it('sends framed StatusRequest at 9600 8N1, ACKs the response and closes cleanly', async () => {
    let port!: FakePort;
    const session = new HasarSerialSession({ port: 'COM8', baudRate: 9600 }, (options) => {
      port = new FakePort(options);
      port.reply = (bytes) => {
        if (bytes.length === 1) return;
        port.emit('data', Buffer.concat([Buffer.from([0x06, 0x12]),
          encodeHasarCommand(bytes[1]!, bytes[3]!,
            ['C080', '0600', '1234', '8002', '0', '0000', '0', '0'])]));
      };
      return port;
    });
    try {
      await session.open();
      const status = await session.queryStatus();
      expect(status.trainingMode).toBe(true);
      expect(status.lastTicketNumber).toBe(1234);
      expect(port.writes.map((bytes) => bytes.toString('hex')))
        .toEqual(['02201b2a0330303641', '06']);
      expect(port.options).toMatchObject({ path: 'COM8', baudRate: 9600,
        dataBits: 8, stopBits: 1, parity: 'none' });
    } finally { await session.close(); }
    expect(port.isOpen).toBe(false);
  });

  it('does not retry a command after NAK or an uncertain execution', async () => {
    let port!: FakePort;
    const session = new HasarSerialSession({ port: 'COM8', baudRate: 9600 }, (options) => {
      port = new FakePort(options);
      port.reply = (bytes) => { if (bytes.length > 1) port.emit('data', Buffer.from([0x15])); };
      return port;
    });
    await session.open();
    try {
      await expect(session.command(0x40, ['T', 'T'])).rejects.toThrow('HASAR_NAK');
      expect(port.writes).toHaveLength(1);
    } finally { await session.close(); }
  });
});
