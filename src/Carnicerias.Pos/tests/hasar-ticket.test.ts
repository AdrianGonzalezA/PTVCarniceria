import { describe, expect, it, vi } from 'vitest';
import { issueHasarFiscalTicket, issueHasarNonFiscalTicket } from '../src/hasar-ticket';
import type { HasarResponse, HasarStatus } from '../src/hasar-protocol';

const ticket = { items: [{ name: 'ASADO PRUEBA', quantity: 1, unitPrice: 1, vatRate: 21 }],
  total: 1, paymentName: 'Efectivo' };
const ready: HasarStatus = { printerStatus: 'C080', fiscalStatus: '0600',
  auxiliaryStatus: '0002', documentStatus: '0000', lastTicketNumber: 5159,
  trainingMode: false, fiscalized: true, documentOpen: false, paperOut: false,
  printerFault: false, fiscalFault: false };
const reply = (command: number, fields: string[]): HasarResponse =>
  ({ sequence: 0x20, command, fields });

describe('Hasar lab fiscal ticket', () => {
  it('requires explicit opt-in before using an already fiscalized unit', async () => {
    const session = { queryStatus: vi.fn().mockResolvedValue(ready), command: vi.fn() };
    await expect(issueHasarFiscalTicket(session, ticket)).rejects.toThrow('HASAR_FISCALIZED_OPT_IN_REQUIRED');
    expect(session.command).not.toHaveBeenCalled();
  });

  it('opens, prints the 21% VAT item, pays, closes and confirms the new number', async () => {
    const session = {
      queryStatus: vi.fn().mockResolvedValueOnce(ready).mockResolvedValueOnce({
        ...ready, lastTicketNumber: 5160 }),
      command: vi.fn().mockImplementation(async (command: number) =>
        reply(command, ['C080', command === 0x45 ? '0600' : '3600',
          ...(command === 0x40 || command === 0x45 ? ['5160'] : [])])),
    };
    const result = await issueHasarFiscalTicket(session, ticket, { allowFiscalized: true });
    expect(result).toEqual({ number: 5160, trainingMode: false, total: 1 });
    expect(session.command.mock.calls).toEqual([
      [0x40, ['T', 'T']],
      [0x42, ['ASADO PRUEBA', '1.000', '1.0000', '21.00', 'M', '0.0', '0', 'T']],
      [0x44, ['Efectivo', '1.00', 'T', '0']],
      [0x45],
    ]);
  });

  it('validates totals and item fields before opening a document', async () => {
    const session = { queryStatus: vi.fn(), command: vi.fn() };
    await expect(issueHasarFiscalTicket(session, { ...ticket, total: 2 },
      { allowFiscalized: true })).rejects.toThrow('HASAR_TICKET_TOTAL_MISMATCH');
    expect(session.queryStatus).not.toHaveBeenCalled();
  });

  it('cancels its own open lab document after an item error without retrying the item', async () => {
    const session = {
      queryStatus: vi.fn().mockResolvedValueOnce(ready).mockResolvedValueOnce({
        ...ready, documentOpen: true, fiscalStatus: '3600' }),
      command: vi.fn().mockImplementation(async (command: number) => {
        if (command === 0x42) throw new Error('HASAR_RESPONSE_TIMEOUT');
        return reply(command, ['C080', command === 0x98 ? '0600' : '3600', '5160']);
      }),
    };
    await expect(issueHasarFiscalTicket(session, ticket, { allowFiscalized: true }))
      .rejects.toThrow('HASAR_FISCAL_DOCUMENT_CANCELED');
    expect(session.command.mock.calls.map(([command]) => command)).toEqual([0x40, 0x42, 0x98]);
  });

  it('leaves a lost close response uncertain instead of issuing another ticket', async () => {
    const session = {
      queryStatus: vi.fn().mockResolvedValueOnce(ready).mockResolvedValueOnce({
        ...ready, lastTicketNumber: 5160 }),
      command: vi.fn().mockImplementation(async (command: number) => {
        if (command === 0x45) throw new Error('HASAR_RESPONSE_TIMEOUT');
        return reply(command, ['C080', '3600', '5160']);
      }),
    };
    await expect(issueHasarFiscalTicket(session, ticket, { allowFiscalized: true }))
      .rejects.toThrow('HASAR_RESULT_UNCERTAIN_CHECK_DEVICE');
    expect(session.command.mock.calls.map(([command]) => command)).toEqual([0x40, 0x42, 0x44, 0x45]);
  });

  it('prints multiple payments and a global discount from a sale', async () => {
    const session = {
      queryStatus: vi.fn().mockResolvedValueOnce(ready).mockResolvedValueOnce({
        ...ready, lastTicketNumber: 5160 }),
      command: vi.fn().mockImplementation(async (command: number) =>
        reply(command, ['C080', command === 0x45 ? '0600' : '3600',
          ...(command === 0x40 || command === 0x45 ? ['5160'] : [])])),
    };
    await issueHasarFiscalTicket(session, { items: ticket.items, total: 0.9,
      fiscalText: ['ARCA HOMO SIN VALIDEZ', 'CAE HOMO 12345678901234'],
      discount: { name: 'PROMO', amount: 0.1 },
      tenders: [{ name: 'EFECTIVO', amount: 0.5 }, { name: 'DEBITO', amount: 0.4 }] },
    { allowFiscalized: true });
    expect(session.command.mock.calls.map(([command, fields]) => [command, fields])).toEqual([
      [0x40, ['T', 'T']],
      [0x41, ['ARCA HOMO SIN VALIDEZ', '0']],
      [0x41, ['CAE HOMO 12345678901234', '0']],
      [0x42, ['ASADO PRUEBA', '1.000', '1.0000', '21.00', 'M', '0.0', '0', 'T']],
      [0x54, ['PROMO', '0.10', 'm', '0', 'T']],
      [0x44, ['EFECTIVO', '0.50', 'T', '0']],
      [0x44, ['DEBITO', '0.40', 'T', '0']],
      [0x45, undefined],
    ]);
  });
});

describe('Hasar non-fiscal sale ticket', () => {
  it('opens H, prints every sale line with I, closes J and checks the closed state', async () => {
    const session = {
      queryStatus: vi.fn().mockResolvedValue(ready),
      command: vi.fn().mockImplementation(async (command: number) =>
        reply(command, ['C080', '0600'])),
    };
    const result = await issueHasarNonFiscalTicket(session, ['VENTA 123', 'TOTAL: $1100.00']);
    expect(result).toEqual({ trainingMode: false });
    expect(session.command.mock.calls).toEqual([
      [0x48], [0x49, ['VENTA 123', '0']], [0x49, ['TOTAL: $1100.00', '0']], [0x4a, ['0']],
    ]);
  });

  it('does not open a document if a line exceeds the 441F printable width', async () => {
    const session = { queryStatus: vi.fn(), command: vi.fn() };
    await expect(issueHasarNonFiscalTicket(session, ['X'.repeat(46)]))
      .rejects.toThrow('HASAR_TICKET_TEXT_INVALID');
    expect(session.command).not.toHaveBeenCalled();
  });
});
