import { afterEach, describe, expect, it, vi } from 'vitest';
import { mkdtemp, rm } from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import { printHasarSaleWithLedger } from '../src/hasar-sale-ledger';
import type { ReceiptRequest } from '../src/receipt-pdf';

const folders: string[] = [];
const sale: ReceiptRequest = {
  saleId: '22222222-2222-4222-8222-222222222222', documentType: 'nonFiscalTicket',
  branch: 'Sucursal', terminal: 'Caja A', cashier: 'cajero1',
  confirmedAtUtc: '2026-10-14T12:00:00Z', total: 1, changeAmount: 0,
  accountChargeAmount: 0, creditAppliedAmount: 0, customerCode: null, customerName: null,
  lines: [{ code: '1', name: 'Asado', unit: 'kg', quantity: 1,
    unitPrice: 1, lineTotal: 1 }],
  payments: [{ method: 'cash', tenderedAmount: 1, appliedAmount: 1 }],
};

afterEach(async () => {
  for (const folder of folders.splice(0)) {
    if (!folder.startsWith(path.join(os.tmpdir(), 'hasar-ledger-test-')))
      throw new Error('Unsafe test cleanup path');
    await rm(folder, { recursive: true });
  }
});

describe('Hasar sale print ledger', () => {
  it('prints one confirmed sale only once even when invoked a second time', async () => {
    const folder = await mkdtemp(path.join(os.tmpdir(), 'hasar-ledger-test-'));
    folders.push(folder);
    const issue = vi.fn().mockResolvedValue({ kind: 'nonFiscal', number: null,
      trainingMode: false, total: 1 });
    const first = await printHasarSaleWithLedger(sale, folder, issue);
    const second = await printHasarSaleWithLedger(sale, folder, issue);
    expect(second).toEqual(first);
    await expect(printHasarSaleWithLedger({ ...sale, terminal: 'Otra caja' }, folder, issue))
      .rejects.toThrow('HASAR_SALE_PAYLOAD_CHANGED');
    expect(issue).toHaveBeenCalledTimes(1);
  });

  it('never automatically retries after an uncertain result', async () => {
    const folder = await mkdtemp(path.join(os.tmpdir(), 'hasar-ledger-test-'));
    folders.push(folder);
    const issue = vi.fn().mockRejectedValue(new Error('HASAR_RESPONSE_TIMEOUT'));
    await expect(printHasarSaleWithLedger(sale, folder, issue))
      .rejects.toThrow('HASAR_RESPONSE_TIMEOUT');
    await expect(printHasarSaleWithLedger(sale, folder, issue))
      .rejects.toThrow('HASAR_SALE_RESULT_PENDING');
    expect(issue).toHaveBeenCalledTimes(1);
  });

  it('allows a retry when the controller rejects the job before opening a document', async () => {
    const folder = await mkdtemp(path.join(os.tmpdir(), 'hasar-ledger-test-'));
    folders.push(folder);
    const issue = vi.fn().mockRejectedValueOnce(new Error('HASAR_DEVICE_NOT_READY_8004_0600'))
      .mockResolvedValueOnce({ kind: 'nonFiscal', number: null, trainingMode: false, total: 1 });
    await expect(printHasarSaleWithLedger(sale, folder, issue))
      .rejects.toThrow('HASAR_DEVICE_NOT_READY');
    await expect(printHasarSaleWithLedger(sale, folder, issue))
      .resolves.toMatchObject({ kind: 'nonFiscal', total: 1 });
    expect(issue).toHaveBeenCalledTimes(2);
  });
});
