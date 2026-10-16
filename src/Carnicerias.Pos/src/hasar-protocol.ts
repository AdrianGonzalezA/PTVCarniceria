// Hasar SMH/P-441F first-generation serial framing: STX SN ESC command [FS fields] ETX BCC.
// DSB-MCMD-2013AR025-EFH/00, sections 1.1, 2.1 and 3.2.1.
const STX = 0x02;
const ETX = 0x03;
const ESC = 0x1b;
const FS = 0x1c;

export interface HasarResponse {
  readonly sequence: number;
  readonly command: number;
  readonly fields: readonly string[];
}

export interface HasarStatus {
  readonly printerStatus: string;
  readonly fiscalStatus: string;
  readonly auxiliaryStatus: string;
  readonly documentStatus: string;
  readonly lastTicketNumber: number;
  readonly trainingMode: boolean;
  readonly fiscalized: boolean;
  readonly documentOpen: boolean;
  readonly paperOut: boolean;
  readonly printerFault: boolean;
  readonly fiscalFault: boolean;
}

function checksum(frameThroughEtx: Buffer): string {
  let sum = 0;
  for (const byte of frameThroughEtx) sum = (sum + byte) & 0xffff;
  return sum.toString(16).toUpperCase().padStart(4, '0');
}

export function encodeHasarCommand(sequence: number, command: number,
  fields: readonly string[] = []): Buffer {
  if (!Number.isInteger(sequence) || sequence < 0x20 || sequence > 0x7e || sequence % 2 !== 0)
    throw new Error('HASAR_SEQUENCE_INVALID');
  if (!Number.isInteger(command) || command < 0x20 || command > 0xff)
    throw new Error('HASAR_COMMAND_INVALID');
  for (const field of fields) {
    if (typeof field !== 'string' || field.length > 255 ||
        [...field].some((char) => char.codePointAt(0)! < 0x20 || char.codePointAt(0)! > 0xaf))
      throw new Error('HASAR_FIELD_INVALID');
  }
  const payload = Buffer.concat([
    Buffer.from([STX, sequence, ESC, command]),
    ...fields.map((field) => Buffer.concat([Buffer.from([FS]), Buffer.from(field, 'latin1')])),
    Buffer.from([ETX]),
  ]);
  return Buffer.concat([payload, Buffer.from(checksum(payload), 'ascii')]);
}

export function parseHasarResponse(frame: Buffer, sequence: number, command: number): HasarResponse {
  if (frame.length < 9 || frame.length > 4096 || frame[0] !== STX || frame[2] !== ESC ||
      frame[frame.length - 5] !== ETX)
    throw new Error('HASAR_FRAME_INVALID');
  const bcc = frame.subarray(-4).toString('ascii');
  if (!/^[0-9A-Fa-f]{4}$/.test(bcc) || bcc.toUpperCase() !== checksum(frame.subarray(0, -4)))
    throw new Error('HASAR_BCC_INVALID');
  if (frame[1] !== sequence) throw new Error('HASAR_SEQUENCE_MISMATCH');
  if (frame[3] !== command) throw new Error('HASAR_COMMAND_MISMATCH');
  if (frame[4] !== FS) throw new Error('HASAR_FRAME_INVALID');
  const payload = frame.subarray(5, -5);
  return { sequence, command, fields: payload.toString('latin1').split(String.fromCharCode(FS)) };
}

export function parseHasarStatus(fields: readonly string[]): HasarStatus {
  const [printerStatus, fiscalStatus, lastTicket, auxiliaryStatus, , documentStatus] = fields;
  if (!printerStatus || !fiscalStatus || !auxiliaryStatus || !documentStatus ||
      !/^[0-9A-Fa-f]{4}$/.test(printerStatus) || !/^[0-9A-Fa-f]{4}$/.test(fiscalStatus) ||
      !/^[0-9A-Fa-f]{4}$/.test(auxiliaryStatus) || !/^[0-9A-Fa-f]{4}$/.test(documentStatus) ||
      !lastTicket || !/^\d{1,8}$/.test(lastTicket))
    throw new Error('HASAR_STATUS_INVALID');
  const printerBits = parseInt(printerStatus, 16);
  const fiscalBits = parseInt(fiscalStatus, 16);
  const auxiliaryBits = parseInt(auxiliaryStatus, 16);
  return {
    printerStatus: printerStatus.toUpperCase(), fiscalStatus: fiscalStatus.toUpperCase(),
    auxiliaryStatus: auxiliaryStatus.toUpperCase(), documentStatus: documentStatus.toUpperCase(),
    lastTicketNumber: Number(lastTicket),
    trainingMode: (auxiliaryBits & 0x8000) !== 0,
    fiscalized: (fiscalBits & 0x0400) !== 0,
    documentOpen: (fiscalBits & 0x2000) !== 0,
    paperOut: (printerBits & 0x0030) !== 0,
    printerFault: (printerBits & 0x017c) !== 0,
    fiscalFault: (fiscalBits & 0x08ff) !== 0,
  };
}
