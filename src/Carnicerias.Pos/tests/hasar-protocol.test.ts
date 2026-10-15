import { describe, expect, it } from 'vitest';
import { encodeHasarCommand, parseHasarResponse, parseHasarStatus } from '../src/hasar-protocol';

describe('Hasar SMH/P-441F protocol', () => {
  it('frames a read-only status command with sequence and four-digit BCC', () => {
    expect(encodeHasarCommand(0x20, 0x2a).toString('hex'))
      .toBe('02201b2a0330303641');
  });

  it('encodes FS-separated arguments and rejects control characters', () => {
    const frame = encodeHasarCommand(0x22, 0x40, ['T', 'T']);
    expect(frame.subarray(0, -5).toString('hex')).toBe('02221b401c541c54');
    expect(() => encodeHasarCommand(0x20, 0x40, ['A\x1cB'])).toThrow('HASAR_FIELD_INVALID');
    expect(() => encodeHasarCommand(0x21, 0x2a)).toThrow('HASAR_SEQUENCE_INVALID');
  });

  it('verifies response framing, sequence, command and checksum', () => {
    const frame = encodeHasarCommand(0x20, 0x2a,
      ['C080', '0600', '1234', '8002', '0', '0000', '0', '0']);
    const response = parseHasarResponse(frame, 0x20, 0x2a);
    expect(response.fields.slice(0, 4)).toEqual(['C080', '0600', '1234', '8002']);
    const lowercaseBcc = Buffer.from(frame);
    lowercaseBcc.write(frame.subarray(-4).toString('ascii').toLowerCase(), frame.length - 4, 'ascii');
    expect(parseHasarResponse(lowercaseBcc, 0x20, 0x2a).fields[0]).toBe('C080');
    expect(() => parseHasarResponse(frame, 0x22, 0x2a)).toThrow('HASAR_SEQUENCE_MISMATCH');
    expect(() => parseHasarResponse(frame, 0x20, 0x40)).toThrow('HASAR_COMMAND_MISMATCH');
    const corrupt = Buffer.from(frame);
    corrupt[7] ^= 1;
    expect(() => parseHasarResponse(corrupt, 0x20, 0x2a)).toThrow('HASAR_BCC_INVALID');
  });

  it('extracts mode, document state and fault bits from StatusRequest', () => {
    const status = parseHasarStatus(['C080', '0600', '1234', '8002', '0', '0000', '0', '0']);
    expect(status.trainingMode).toBe(true);
    expect(status.documentOpen).toBe(false);
    expect(status.lastTicketNumber).toBe(1234);
    expect(status.paperOut).toBe(false);
    expect(parseHasarStatus(['C0A0', '3600', '1234', '8003', '0', '0000', '0', '0']).paperOut)
      .toBe(true);
    const disconnected = parseHasarStatus(['8004', '0600', '1234', '0002', '0', '0000', '0', '0']);
    expect(disconnected.printerFault).toBe(true);
    expect(disconnected.documentOpen).toBe(false);
  });
});
