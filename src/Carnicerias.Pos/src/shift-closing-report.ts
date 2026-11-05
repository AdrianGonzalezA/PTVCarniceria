export interface ShiftClosingReport {
  readonly version: number;
  readonly shiftId: string;
  readonly companyName: string;
  readonly branchName: string;
  readonly terminalName: string | null;
  readonly cashierName: string;
  readonly openedAtUtc: string;
  readonly closedAtUtc: string;
  readonly openingCash: number;
  readonly cashSales: number;
  readonly nonCashSales: number;
  readonly accountSales: number;
  readonly salesTotal: number;
  readonly cashCollections: number;
  readonly nonCashCollections: number;
  readonly creditApplied: number;
  readonly cashWithdrawals: number;
  readonly expectedCash: number;
  readonly countedCash: number | null;
  readonly handedCash: number | null;
  readonly cashDifference: number | null;
  readonly paymentBreakdown: readonly { readonly method: string; readonly amount: number }[];
}

export function validateShiftClosingReport(value: unknown): ShiftClosingReport {
  if (!value || typeof value !== 'object') throw new Error('SHIFT_REPORT_INVALID');
  const report = value as Record<string, unknown>;
  const strings = ['shiftId', 'companyName', 'branchName', 'cashierName', 'openedAtUtc', 'closedAtUtc'];
  const amounts = ['openingCash', 'cashSales', 'nonCashSales', 'accountSales', 'salesTotal',
    'cashCollections', 'nonCashCollections', 'creditApplied', 'cashWithdrawals', 'expectedCash'];
  if (report.version !== 1 || strings.some((key) => typeof report[key] !== 'string') ||
      amounts.some((key) => typeof report[key] !== 'number' || !Number.isFinite(report[key])) ||
      (report.terminalName !== null && typeof report.terminalName !== 'string') ||
      ['countedCash', 'handedCash', 'cashDifference'].some((key) =>
        report[key] !== null && (typeof report[key] !== 'number' || !Number.isFinite(report[key]))) ||
      !Array.isArray(report.paymentBreakdown) || report.paymentBreakdown.some((item: unknown) =>
        !item || typeof item !== 'object' || typeof (item as { method?: unknown }).method !== 'string' ||
        typeof (item as { amount?: unknown }).amount !== 'number'))
    throw new Error('SHIFT_REPORT_INVALID');
  return value as ShiftClosingReport;
}

const money = (value: number): string => value.toLocaleString('es-AR', {
  minimumFractionDigits: 2, maximumFractionDigits: 2,
});
const date = (value: string): string => new Date(value).toLocaleString('es-AR', {
  day: '2-digit', month: '2-digit', year: 'numeric', hour: '2-digit', minute: '2-digit',
});
const clean = (value: string): string => value.replace(/[\u0000-\u001f\u007f-\u009f\p{Cf}]/gu, ' ')
  .replace(/\s+/g, ' ').trim();
const escapeHtml = (value: string): string => value.replace(/[&<>"']/g, (character) =>
  ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[character]!);

export function shiftClosingLines(report: ShiftClosingReport): string[] {
  const row = (label: string, amount: number): string => `${label}: $ ${money(amount)}`;
  return [
    'RENDICION DE TURNO - NO FISCAL', clean(report.companyName),
    clean(report.branchName), clean(report.terminalName ?? 'Caja historica'),
    `Cajero: ${clean(report.cashierName)}`, `Turno: ${report.shiftId}`,
    `Apertura: ${date(report.openedAtUtc)}`, `Cierre: ${date(report.closedAtUtc)}`,
    row('Fondo inicial', report.openingCash), row('Ventas totales', report.salesTotal),
    row('Ventas efectivo', report.cashSales), row('Ventas otros medios', report.nonCashSales),
    row('Ventas cta cte', report.accountSales), row('Credito aplicado', report.creditApplied),
    row('Cobranzas efectivo', report.cashCollections),
    row('Cobranzas otros medios', report.nonCashCollections),
    ...report.paymentBreakdown.map((payment) => row(`Cobros ${clean(payment.method)}`, payment.amount)),
    row('Retiros', report.cashWithdrawals), row('Efectivo esperado', report.expectedCash),
    ...(report.countedCash === null ? ['Sin arqueo declarado'] : [
      row('Efectivo contado', report.countedCash), row('Efectivo entregado', report.handedCash!),
      row('Diferencia', report.cashDifference!),
    ]),
  ];
}

export function shiftClosingHtml(report: ShiftClosingReport): string {
  const lines = shiftClosingLines(report).map((line) => `<p>${escapeHtml(line)}</p>`).join('');
  return `<!doctype html><html lang="es"><head><meta charset="utf-8"><style>body{font:12px Arial,sans-serif;padding:16px;color:#222}p{margin:4px 0;overflow-wrap:anywhere}</style></head><body>${lines}</body></html>`;
}
