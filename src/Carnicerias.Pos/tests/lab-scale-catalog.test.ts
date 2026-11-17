import { describe, expect, it } from 'vitest';
import { parseLabScaleCatalog, selectLabScaleCompany } from '../src/lab-scale-catalog';

const catalog = JSON.stringify([
  { companyId: 'visual', companyName: 'Empresa Visual', products: [
    { plu: '10001', name: 'Asado' }, { plu: '10002', name: 'Nalga' },
    { plu: '10003', name: 'Bondiola' },
  ] },
  { companyId: 'rustica', companyName: 'La Rústica', products: [
    { plu: '00123', name: 'Tapa de asado' }, { plu: '10001', name: 'Aguja' },
    { plu: '10002', name: 'Vacío' }, { plu: '10003', name: 'Bife' },
  ] },
]);

describe('laboratory scale catalog', () => {
  it('uses live PLUs from the company with the largest weight catalog by default', () => {
    const companies = parseLabScaleCatalog(catalog);
    expect(selectLabScaleCompany(companies)).toEqual(companies[1]);
    expect(companies[1].products.map(product => product.plu)).toContain('00123');
  });

  it('can use an explicitly configured company without mixing PLUs', () => {
    const companies = parseLabScaleCatalog(catalog);
    expect(selectLabScaleCompany(companies, 'visual')).toEqual(companies[0]);
  });

  it('rejects invalid or insufficient catalogs', () => {
    expect(() => parseLabScaleCatalog('[{"companyId":"x","companyName":"X","products":[{"plu":"bad","name":"X"}]}]'))
      .toThrow('INVALID_SCALE_CATALOG');
    expect(() => selectLabScaleCompany(parseLabScaleCatalog('[]')))
      .toThrow('NO_SCALE_PRODUCTS');
    expect(() => selectLabScaleCompany(parseLabScaleCatalog(catalog), 'absent'))
      .toThrow('NO_SCALE_PRODUCTS');
  });
});
