import { describe, expect, it } from 'vitest';
import { decodeKretzWeightEan, parseKretzConsolidatedQr,
  scaleQrTicketKey } from './kretz-label';

describe('Kretz Report NX weight profile for the lab', () => {
  it('decodes a checked 2-5-5 EAN into PLU and kilograms', () => {
    expect(decodeKretzWeightEan('2000123007502')).toEqual({
      barcode: '2000123007502', plu: '00123', weightKg: 0.75,
    });
  });

  it('rejects bad checksums, amount labels, zero weights and unknown prefixes', () => {
    expect(decodeKretzWeightEan('2000123007504')).toBeNull();
    expect(decodeKretzWeightEan('2100123007504')).toBeNull();
    expect(decodeKretzWeightEan('2000123000008')).toBeNull();
  });

  it('parses all entries from the documented Kretz QR header and validates its count', () => {
    const code = '08/09/2010:390003 A0102\n2000123007502\n2000045005006';
    expect(parseKretzConsolidatedQr(code)).toEqual({
      header: '08/09/2010:390003 A0102',
      ticketKey: '08/09/2010:390003 A01', scaleNumber: '01', ticketNumber: '0003',
      labels: [
        { barcode: '2000123007502', plu: '00123', weightKg: 0.75 },
        { barcode: '2000045005006', plu: '00045', weightKg: 0.5 },
      ],
    });
    expect(parseKretzConsolidatedQr(code.replace('0102\n', '0103\n'))).toBeNull();
    expect(parseKretzConsolidatedQr(code.replace('2000045005006', '2000045005003'))).toBeNull();
    expect(parseKretzConsolidatedQr(code.replaceAll('\n', ';'))?.labels).toHaveLength(2);
  });

  it('identifies every cut from one QR as the same single-use ticket', () => {
    const first = 'QR:17/11/2613:390003 A0102:01:2000123007502';
    const second = 'QR:17/11/2613:390003 A0102:02:2000045005006';
    expect(scaleQrTicketKey(first)).toBe('17/11/2613:390003 A01');
    expect(scaleQrTicketKey(second)).toBe(scaleQrTicketKey(first));
    expect(scaleQrTicketKey('EAN:2000123007502')).toBeNull();
  });
});
