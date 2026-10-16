import path from 'node:path';
import { createHash } from 'node:crypto';
import { mkdir, readFile, unlink, writeFile } from 'node:fs/promises';
import { buildHasarSaleDocument, type HasarSaleDocument } from './hasar-sale';
import type { ReceiptRequest } from './receipt-pdf';

export interface HasarSalePrintResult {
  readonly kind: 'fiscal' | 'nonFiscal';
  readonly number: number | null;
  readonly trainingMode: boolean;
  readonly total: number;
}

async function confirmed(file: string): Promise<HasarSalePrintResult | null> {
  try {
    const parsed: unknown = JSON.parse(await readFile(file, 'utf8'));
    if (typeof parsed !== 'object' || parsed === null || !('kind' in parsed) ||
        !('number' in parsed) || !('trainingMode' in parsed) || !('total' in parsed) ||
        !['fiscal', 'nonFiscal'].includes(String(parsed.kind)) ||
        (parsed.number !== null && (!Number.isSafeInteger(parsed.number) || Number(parsed.number) < 1)) ||
        typeof parsed.trainingMode !== 'boolean' || typeof parsed.total !== 'number')
      throw new Error('HASAR_LEDGER_INVALID');
    return parsed as HasarSalePrintResult;
  } catch (error) {
    if (typeof error === 'object' && error !== null && 'code' in error && error.code === 'ENOENT')
      return null;
    throw error;
  }
}

export async function printHasarSaleWithLedger(receipt: ReceiptRequest, directory: string,
  issue: (document: HasarSaleDocument) => Promise<HasarSalePrintResult>): Promise<HasarSalePrintResult> {
  const document = buildHasarSaleDocument(receipt); // Validate before reserving or contacting the device.
  const fingerprint = createHash('sha256').update(JSON.stringify(document)).digest('hex');
  await mkdir(directory, { recursive: true });
  const pendingFile = path.join(directory, `${receipt.saleId}.pending.json`);
  const confirmedFile = path.join(directory, `${receipt.saleId}.confirmed.json`);
  async function priorConfirmed(): Promise<HasarSalePrintResult | null> {
    const prior = await confirmed(confirmedFile);
    if (!prior) return null;
    const marker: unknown = JSON.parse(await readFile(pendingFile, 'utf8'));
    if (typeof marker !== 'object' || marker === null || !('fingerprint' in marker) ||
        marker.fingerprint !== fingerprint)
      throw new Error('HASAR_SALE_PAYLOAD_CHANGED');
    return prior;
  }
  const prior = await priorConfirmed();
  if (prior) return prior;
  try {
    await writeFile(pendingFile, JSON.stringify({ saleId: receipt.saleId,
      kind: document.kind, total: receipt.total, fingerprint,
      startedAtUtc: new Date().toISOString() }),
    { flag: 'wx' });
  } catch (error) {
    if (typeof error === 'object' && error !== null && 'code' in error && error.code === 'EEXIST') {
      const completed = await priorConfirmed();
      if (completed) return completed;
      throw new Error('HASAR_SALE_RESULT_PENDING');
    }
    throw error;
  }
  let result: HasarSalePrintResult;
  try {
    result = await issue(document);
  } catch (error) {
    // These failures are guaranteed to occur before any document-opening command.
    if (error instanceof Error && (error.message.startsWith('HASAR_DEVICE_NOT_READY_') ||
      ['HASAR_DOCUMENT_ALREADY_OPEN',
      'HASAR_FISCALIZED_OPT_IN_REQUIRED', 'HASAR_TICKET_ITEMS_INVALID',
      'HASAR_TICKET_AMOUNT_INVALID', 'HASAR_TICKET_TEXT_INVALID',
      'HASAR_TICKET_VAT_INVALID', 'HASAR_TICKET_TOTAL_MISMATCH',
      'HASAR_TENDERS_INVALID', 'HASAR_TENDERS_TOTAL_MISMATCH',
      'HASAR_FISCAL_TEXT_INVALID', 'HASAR_NONFISCAL_LINES_INVALID'].includes(error.message)))
      await unlink(pendingFile);
    throw error;
  }
  await writeFile(confirmedFile, JSON.stringify(result), { flag: 'wx' });
  return result;
}
