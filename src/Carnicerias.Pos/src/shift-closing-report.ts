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
      !/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(report.shiftId as string) ||
      ['openedAtUtc', 'closedAtUtc'].some((key) => !Number.isFinite(Date.parse(report[key] as string))) ||
      amounts.some((key) => typeof report[key] !== 'number' || !Number.isFinite(report[key])) ||
      (report.terminalName !== null && typeof report.terminalName !== 'string') ||
      ['countedCash', 'handedCash', 'cashDifference'].some((key) =>
        report[key] !== null && (typeof report[key] !== 'number' || !Number.isFinite(report[key]))) ||
      ([report.countedCash, report.handedCash, report.cashDifference].some((amount) => amount === null) &&
        [report.countedCash, report.handedCash, report.cashDifference].some((amount) => amount !== null)) ||
      !Array.isArray(report.paymentBreakdown) || report.paymentBreakdown.some((item: unknown) =>
        !item || typeof item !== 'object' || typeof (item as { method?: unknown }).method !== 'string' ||
        typeof (item as { amount?: unknown }).amount !== 'number' ||
        !Number.isFinite((item as { amount: number }).amount)))
    throw new Error('SHIFT_REPORT_INVALID');
  return value as ShiftClosingReport;
}

const money = (value: number): string => value.toLocaleString('es-AR', {
  minimumFractionDigits: 2, maximumFractionDigits: 2,
});
const date = (value: string): string => new Date(value).toLocaleString('es-AR', {
  day: '2-digit', month: '2-digit', year: 'numeric', hour: '2-digit', minute: '2-digit',
  timeZone: 'America/Argentina/Buenos_Aires',
});
const clean = (value: string): string => value.replace(/[\u0000-\u001f\u007f-\u009f\p{Cf}]/gu, ' ')
  .replace(/\s+/g, ' ').trim();
const escapeHtml = (value: string): string => value.replace(/[&<>"']/g, (character) =>
  ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[character]!);

const paymentMethodName = (method: string): string => ({
  cash: 'Efectivo', debit: 'Débito', credit: 'Crédito', transfer: 'Transferencia',
  mercadoPago: 'Mercado Pago', cheque: 'Cheque',
} as Record<string, string>)[method] ?? clean(method);

function reportRows(report: ShiftClosingReport): { label: string; amount: number }[] {
  return [
    { label: 'Fondo inicial', amount: report.openingCash },
    { label: 'Ventas totales', amount: report.salesTotal },
    { label: 'Ventas en efectivo', amount: report.cashSales },
    { label: 'Ventas por otros medios', amount: report.nonCashSales },
    { label: 'Ventas cuenta corriente', amount: report.accountSales },
    { label: 'Saldo a favor aplicado', amount: report.creditApplied },
    { label: 'Cobranzas en efectivo', amount: report.cashCollections },
    { label: 'Cobranzas por otros medios', amount: report.nonCashCollections },
    ...report.paymentBreakdown.map((payment) => ({
      label: `Cobros ${paymentMethodName(payment.method)}`, amount: payment.amount,
    })),
    { label: 'Retiros de efectivo', amount: report.cashWithdrawals },
    { label: 'Efectivo esperado', amount: report.expectedCash },
    ...(report.countedCash === null ? [] : [
      { label: 'Efectivo contado', amount: report.countedCash },
      { label: 'Efectivo entregado', amount: report.handedCash! },
      { label: 'Diferencia', amount: report.cashDifference! },
    ]),
  ];
}

export function shiftClosingLines(report: ShiftClosingReport): string[] {
  return [
    'RENDICION DE TURNO - NO FISCAL', clean(report.companyName),
    clean(report.branchName), clean(report.terminalName ?? 'Caja historica'),
    `Cajero: ${clean(report.cashierName)}`, `Turno: ${report.shiftId}`,
    `Apertura: ${date(report.openedAtUtc)}`, `Cierre: ${date(report.closedAtUtc)}`,
    ...reportRows(report).map(({ label, amount }) => `${label}: $ ${money(amount)}`),
    ...(report.countedCash === null ? ['Sin arqueo declarado'] : []),
  ];
}

export function shiftClosingHtml(report: ShiftClosingReport): string {
  const metadataRows: [string, string][] = [
    ['Sucursal', report.branchName], ['Caja', report.terminalName ?? 'Caja histórica'],
    ['Cajero', report.cashierName], ['Turno', report.shiftId],
    ['Apertura', date(report.openedAtUtc)], ['Cierre', date(report.closedAtUtc)],
  ];
  const metadata = metadataRows.map(([label, value]) =>
    `<div><span>${escapeHtml(label)}</span><strong>${escapeHtml(clean(value))}</strong></div>`).join('');
  const rows = reportRows(report).map(({ label, amount }) =>
    `<tr><th scope="row">${escapeHtml(label)}</th><td>$ ${money(amount)}</td></tr>`).join('');
  return `<!doctype html><html lang="es"><head><meta charset="utf-8"><style>
    @page{size:A4;margin:16mm}body{font:12px Arial,sans-serif;color:#202020;margin:0}
    h1{font-size:20px;margin:0 0 4px}h2{font-size:14px;margin:0 0 16px;font-weight:normal}
    .meta{display:grid;grid-template-columns:1fr 1fr;gap:8px 24px;margin-bottom:20px}
    .meta div{display:flex;gap:8px}.meta span{color:#555;min-width:56px}.meta strong{overflow-wrap:anywhere}
    table{border-collapse:collapse;width:100%}th,td{border-bottom:1px solid #ddd;padding:7px 4px}
    th{text-align:left;font-weight:normal}td{text-align:right;font-variant-numeric:tabular-nums;white-space:nowrap}
    tr:last-child th,tr:last-child td{border-top:2px solid #222;font-weight:bold}
    footer{margin-top:18px;color:#555}
  </style></head><body><h1>Rendición de turno</h1><h2>${escapeHtml(clean(report.companyName))}</h2>
  <section class="meta">${metadata}</section><table><tbody>${rows}</tbody></table>
  ${report.countedCash === null ? '<p>Sin arqueo declarado</p>' : ''}
  <footer>Documento interno no fiscal · Generado a partir del cierre registrado.</footer></body></html>`;
}
