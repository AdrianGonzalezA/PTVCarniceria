import type { HasarResponse, HasarStatus } from './hasar-protocol';

interface HasarTicketSession {
  queryStatus(): Promise<HasarStatus>;
  command(command: number, fields?: readonly string[]): Promise<HasarResponse>;
}

export interface HasarFiscalTicket {
  readonly items: readonly { readonly name: string; readonly quantity: number;
    readonly unitPrice: number; readonly vatRate: number }[];
  readonly total: number;
  readonly paymentName?: string;
  readonly tenders?: readonly { readonly name: string; readonly amount: number }[];
  readonly discount?: { readonly name: string; readonly amount: number };
  readonly fiscalText?: readonly string[];
}

export interface HasarTicketResult {
  readonly number: number;
  readonly trainingMode: boolean;
  readonly total: number;
}

export interface HasarNonFiscalResult {
  readonly trainingMode: boolean;
}

function decimal(value: number, places: number, maximum: number): string {
  if (!Number.isFinite(value) || value <= 0 || value > maximum ||
      Math.abs(value - Number(value.toFixed(places))) > 0.0000001)
    throw new Error('HASAR_TICKET_AMOUNT_INVALID');
  return value.toFixed(places);
}

function text(value: string, maximum: number): string {
  if (typeof value !== 'string' || value.length < 1 || value.length > maximum ||
      !/^[\x20-\x7e]+$/.test(value))
    throw new Error('HASAR_TICKET_TEXT_INVALID');
  return value;
}

function itemFields(item: HasarFiscalTicket['items'][number]): readonly string[] {
  if (![0, 10.5, 21, 27].includes(item.vatRate)) throw new Error('HASAR_TICKET_VAT_INVALID');
  return [text(item.name, 23), decimal(item.quantity, 3, 99999999),
    decimal(item.unitPrice, 4, 99999999), item.vatRate.toFixed(2), 'M', '0.0', '0', 'T'];
}

function accepted(response: HasarResponse): void {
  const [printer, fiscal] = response.fields;
  if (!printer || !fiscal || !/^[0-9A-Fa-f]{4}$/.test(printer) ||
      !/^[0-9A-Fa-f]{4}$/.test(fiscal)) throw new Error('HASAR_REPLY_STATUS_INVALID');
  const printerBits = parseInt(printer, 16);
  const fiscalBits = parseInt(fiscal, 16);
  if ((printerBits & 0x017c) !== 0 || (fiscalBits & 0x08ff) !== 0)
    throw new Error(`HASAR_DEVICE_REJECTED_${printer.toUpperCase()}_${fiscal.toUpperCase()}`);
}

function ticketNumber(response: HasarResponse): number {
  const value = response.fields[2];
  if (!value || !/^\d{1,8}$/.test(value) || Number(value) < 1)
    throw new Error('HASAR_TICKET_NUMBER_INVALID');
  return Number(value);
}

function ensureReady(status: HasarStatus): void {
  if (status.documentOpen) throw new Error('HASAR_DOCUMENT_ALREADY_OPEN');
  if (status.paperOut || status.printerFault || status.fiscalFault)
    throw new Error(`HASAR_DEVICE_NOT_READY_${status.printerStatus}_${status.fiscalStatus}`);
}

export async function issueHasarFiscalTicket(session: HasarTicketSession, ticket: HasarFiscalTicket,
  options: { readonly allowFiscalized?: boolean } = {}): Promise<HasarTicketResult> {
  if (!ticket || !Array.isArray(ticket.items) || ticket.items.length < 1 ||
      ticket.items.length > 50) throw new Error('HASAR_TICKET_ITEMS_INVALID');
  const items = ticket.items.map(itemFields);
  if (ticket.fiscalText && ticket.fiscalText.length > 4)
    throw new Error('HASAR_FISCAL_TEXT_INVALID');
  const fiscalText = ticket.fiscalText?.map((line) => text(line, 28)) ?? [];
  const total = Number(decimal(ticket.total, 2, 999999999));
  const calculated = ticket.items.reduce((sum, item) =>
    sum + Math.round(item.quantity * item.unitPrice * 100 + 1e-7), 0) / 100;
  const discount = ticket.discount ? {
    name: text(ticket.discount.name, 28),
    amount: Number(decimal(ticket.discount.amount, 2, 999999999)),
  } : null;
  if (Math.abs(calculated - (total + (discount?.amount ?? 0))) > 0.001)
    throw new Error('HASAR_TICKET_TOTAL_MISMATCH');
  const tenders = ticket.tenders ?? (ticket.paymentName ? [{ name: ticket.paymentName, amount: total }] : []);
  if (tenders.length < 1 || tenders.length > 5) throw new Error('HASAR_TENDERS_INVALID');
  const validatedTenders = tenders.map((tender) => ({ name: text(tender.name, 28),
    amount: Number(decimal(tender.amount, 2, 999999999)) }));
  if (validatedTenders.reduce((sum, tender) => sum + tender.amount, 0) < total - 0.001)
    throw new Error('HASAR_TENDERS_TOTAL_MISMATCH');

  const before = await session.queryStatus();
  ensureReady(before);
  if (before.fiscalized && options.allowFiscalized !== true)
    throw new Error('HASAR_FISCALIZED_OPT_IN_REQUIRED');

  let started = false;
  let closed = false;
  try {
    started = true;
    const opened = await session.command(0x40, ['T', 'T']);
    accepted(opened);
    const openedNumber = ticketNumber(opened);
    for (const line of fiscalText) accepted(await session.command(0x41, [line, '0']));
    for (const fields of items) accepted(await session.command(0x42, fields));
    if (discount) accepted(await session.command(0x54,
      [discount.name, discount.amount.toFixed(2), 'm', '0', 'T']));
    for (const tender of validatedTenders)
      accepted(await session.command(0x44, [tender.name, tender.amount.toFixed(2), 'T', '0']));
    const closure = await session.command(0x45);
    accepted(closure);
    const closedNumber = ticketNumber(closure);
    if (closedNumber !== openedNumber || closedNumber !== before.lastTicketNumber + 1)
      throw new Error('HASAR_TICKET_NUMBER_MISMATCH');
    const after = await session.queryStatus();
    if (after.documentOpen || after.lastTicketNumber !== closedNumber)
      throw new Error('HASAR_CLOSURE_NOT_CONFIRMED');
    closed = true;
    return { number: closedNumber, trainingMode: before.trainingMode, total };
  } catch (error) {
    if (started && !closed) {
      try {
        const status = await session.queryStatus();
        if (status.documentOpen) {
          accepted(await session.command(0x98)); // Cancel only this document; never resend a sale.
          throw new Error('HASAR_FISCAL_DOCUMENT_CANCELED', { cause: error });
        }
      } catch (reconcileError) {
        if (reconcileError instanceof Error &&
            reconcileError.message === 'HASAR_FISCAL_DOCUMENT_CANCELED') throw reconcileError;
      }
      throw new Error('HASAR_RESULT_UNCERTAIN_CHECK_DEVICE', { cause: error });
    }
    throw error;
  }
}

export async function issueHasarNonFiscalTicket(session: HasarTicketSession,
  lines: readonly string[]): Promise<HasarNonFiscalResult> {
  if (!Array.isArray(lines) || lines.length < 1 || lines.length > 250)
    throw new Error('HASAR_NONFISCAL_LINES_INVALID');
  const checked = lines.map((line) => text(line, 45));
  const before = await session.queryStatus();
  ensureReady(before);
  let started = false;
  try {
    started = true;
    accepted(await session.command(0x48));
    for (const line of checked) accepted(await session.command(0x49, [line, '0']));
    accepted(await session.command(0x4a, ['0']));
    const after = await session.queryStatus();
    if (after.documentOpen) throw new Error('HASAR_CLOSURE_NOT_CONFIRMED');
    return { trainingMode: before.trainingMode };
  } catch (error) {
    if (started) {
      try {
        const status = await session.queryStatus();
        if (status.documentOpen) {
          accepted(await session.command(0x98));
          throw new Error('HASAR_NONFISCAL_DOCUMENT_CANCELED', { cause: error });
        }
      } catch (reconcileError) {
        if (reconcileError instanceof Error &&
            reconcileError.message === 'HASAR_NONFISCAL_DOCUMENT_CANCELED') throw reconcileError;
      }
      throw new Error('HASAR_RESULT_UNCERTAIN_CHECK_DEVICE', { cause: error });
    }
    throw error;
  }
}
