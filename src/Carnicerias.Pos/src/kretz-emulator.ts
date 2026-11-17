export interface KretzEmulatorItem {
  readonly plu: string;
  readonly weightKg: number;
}

export interface KretzEmulatorTicket {
  readonly now: Date;
  readonly ticketNumber: number;
  readonly seller: string;
  readonly scaleNumber: number;
}

/** Sólo perfil de laboratorio: EAN13 20 + PLU(5) + peso en gramos(5) + control. */
export function makeKretzWeightEan(plu: string, weightKg: number): string {
  if (!/^\d{1,5}$/.test(plu) || !Number.isFinite(weightKg) || weightKg <= 0 ||
      weightKg > 99.999 || Math.round(weightKg * 1000) !== weightKg * 1000)
    throw new Error('INVALID_KRETZ_ITEM');
  const grams = Math.round(weightKg * 1000);
  const data = `20${plu.padStart(5, '0')}${String(grams).padStart(5, '0')}`;
  let sum = 0;
  for (let index = 0; index < data.length; index++)
    sum += Number(data[index]) * (index % 2 === 0 ? 1 : 3);
  return `${data}${(10 - sum % 10) % 10}`;
}

export function makeKretzConsolidatedQr(items: readonly KretzEmulatorItem[], ticket: KretzEmulatorTicket): string {
  if (items.length < 1 || items.length > 99 || !Number.isInteger(ticket.ticketNumber) ||
      ticket.ticketNumber < 0 || ticket.ticketNumber > 9999 ||
      !Number.isInteger(ticket.scaleNumber) || ticket.scaleNumber < 0 || ticket.scaleNumber > 99 ||
      ticket.seller.length !== 2 || !Number.isFinite(ticket.now.getTime()))
    throw new Error('INVALID_KRETZ_TICKET');
  const date = ticket.now;
  const two = (value: number) => String(value).padStart(2, '0');
  const header = `${two(date.getDate())}/${two(date.getMonth() + 1)}/${two(date.getFullYear() % 100)}` +
    `${two(date.getHours())}:${two(date.getMinutes())}${String(ticket.ticketNumber).padStart(4, '0')}` +
    `${ticket.seller}${two(ticket.scaleNumber)}${two(items.length)}`;
  return [header, ...items.map(item => makeKretzWeightEan(item.plu, item.weightKg))].join('\n');
}
