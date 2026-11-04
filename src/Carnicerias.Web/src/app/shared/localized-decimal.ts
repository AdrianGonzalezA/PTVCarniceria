/** Entry never accepts grouping: one comma or point is always decimal. */
export function parseLocalizedDecimal(value: string, fractionDigits: number): number | null {
  if (!Number.isInteger(fractionDigits) || fractionDigits < 0 || fractionDigits > 3) return null;
  const trimmed = value.trim();
  const pattern = fractionDigits === 0
    ? /^[+-]?\d+$/
    : new RegExp(`^[+-]?(?:\\d+(?:[.,]\\d{1,${fractionDigits}})?|[.,]\\d{1,${fractionDigits}})$`);
  if (!pattern.test(trimmed)) return null;
  const parsed = Number(trimmed.replace(',', '.'));
  return Number.isFinite(parsed) ? parsed : null;
}

export function formatLocalizedDecimal(value: number, fractionDigits: number): string {
  if (!Number.isFinite(value)) return '';
  return value.toLocaleString('es-AR', {
    minimumFractionDigits: fractionDigits,
    maximumFractionDigits: fractionDigits,
    useGrouping: true,
  });
}
