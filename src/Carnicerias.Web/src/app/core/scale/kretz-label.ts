/** Perfil de laboratorio: configurar Report NX para EAN13 20 + PLU(5) + gramos(5) + control. */
export const kretzLabWeightProfile = Object.freeze({
  prefix: '20', pluDigits: 5, weightDigits: 5, weightDecimals: 3,
});

export interface KretzWeightLabel {
  readonly barcode: string;
  readonly plu: string;
  readonly weightKg: number;
}

export interface KretzConsolidatedQr {
  readonly header: string;
  readonly ticketKey: string;
  readonly scaleNumber: string;
  readonly ticketNumber: string;
  readonly labels: readonly KretzWeightLabel[];
}

export function decodeKretzWeightEan(value: string): KretzWeightLabel | null {
  const code = value.trim();
  if (!/^\d{13}$/.test(code) || !code.startsWith(kretzLabWeightProfile.prefix)) return null;
  const data = code.slice(0, 12);
  let sum = 0;
  for (let index = 0; index < data.length; index++)
    sum += Number(data[index]) * (index % 2 === 0 ? 1 : 3);
  if ((10 - sum % 10) % 10 !== Number(code[12])) return null;

  const pluStart = kretzLabWeightProfile.prefix.length;
  const weightStart = pluStart + kretzLabWeightProfile.pluDigits;
  const weight = Number(data.slice(weightStart, weightStart + kretzLabWeightProfile.weightDigits)) /
    10 ** kretzLabWeightProfile.weightDecimals;
  if (weight <= 0) return null;
  return { barcode: code, plu: data.slice(pluStart, weightStart), weightKg: weight };
}

/** Encabezado estándar AAAAAAAAHHHHHTTTTVVGGQQ y un EAN13 por renglón. */
export function parseKretzConsolidatedQr(value: string): KretzConsolidatedQr | null {
  const lines = value.trim().split(/\r?\n|;/).map(line => line.trimEnd());
  if (lines.length < 2 || lines.length > 100) return null;
  const header = /^(\d{2}\/\d{2}\/\d{2})(\d{2}:\d{2})(\d{4})(.{2})(\d{2})(\d{2})$/.exec(lines[0]);
  if (!header) return null;
  const count = Number(header[6]);
  if (count === 0 || count !== lines.length - 1) return null;
  const labels = lines.slice(1).map(line => decodeKretzWeightEan(line));
  if (labels.some(label => label === null)) return null;
  return {
    header: lines[0],
    ticketKey: lines[0].slice(0, -2), scaleNumber: header[5], ticketNumber: header[3],
    labels: labels as KretzWeightLabel[],
  };
}

export function kretzQrExpectedCount(header: string): number | null {
  const match = /^(\d{2}\/\d{2}\/\d{2})(\d{2}:\d{2})(\d{4})(.{2})(\d{2})(\d{2})$/.exec(header);
  const count = match ? Number(match[6]) : 0;
  return count >= 1 && count <= 99 ? count : null;
}
