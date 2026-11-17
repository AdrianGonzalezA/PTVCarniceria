import { execFile } from 'node:child_process';
import { promisify } from 'node:util';

export interface LabScaleProduct {
  readonly plu: string;
  readonly name: string;
}

export interface LabScaleCompany {
  readonly companyId: string;
  readonly companyName: string;
  readonly products: readonly LabScaleProduct[];
}

const execFileAsync = promisify(execFile);
const query = `SELECT COALESCE(json_agg(json_build_object(
  'companyId', company."Id", 'companyName', company."Name", 'products',
  (SELECT COALESCE(json_agg(json_build_object('plu', product."ScalePlu", 'name', product."Name")
    ORDER BY product."Name", product."Id"), '[]'::json)
   FROM catalog_pricing.products product
   WHERE product."CompanyId" = company."Id" AND product."IsActive" AND product."SaleMode" = 0
     AND product."ScalePlu" ~ '^[0-9]{5}$')) ORDER BY company."Name"), '[]'::json)
FROM platform_access.companies company WHERE company."IsActive"`;

/** This helper runs only in the Electron device lab, against the single named development container. */
export async function loadLabScaleCatalog(): Promise<LabScaleCompany[]> {
  const { stdout } = await execFileAsync('docker', [
    'exec', 'carnicerias-pos-test-20261007', 'psql', '-U', 'postgres',
    '-d', 'carnicerias_test_visual', '-Atc', query,
  ], { timeout: 5000, windowsHide: true, maxBuffer: 1024 * 1024 });
  return parseLabScaleCatalog(stdout);
}

export function parseLabScaleCatalog(raw: string): LabScaleCompany[] {
  let value: unknown;
  try { value = JSON.parse(raw.trim()); }
  catch { throw new Error('INVALID_SCALE_CATALOG'); }
  if (!Array.isArray(value) || value.some(company =>
    !company || typeof company !== 'object' ||
    typeof company.companyId !== 'string' || !company.companyId ||
    typeof company.companyName !== 'string' || !company.companyName ||
    !Array.isArray(company.products) || company.products.some((product: unknown) =>
      !product || typeof product !== 'object' ||
      !('plu' in product) || typeof product.plu !== 'string' || !/^\d{5}$/.test(product.plu) ||
      !('name' in product) || typeof product.name !== 'string' || !product.name)))
    throw new Error('INVALID_SCALE_CATALOG');
  return value as LabScaleCompany[];
}

export function selectLabScaleCompany(companies: readonly LabScaleCompany[],
  requestedId?: string): LabScaleCompany {
  const available = companies.filter(company => company.products.length >= 3 &&
    new Set(company.products.map(product => product.plu)).size === company.products.length);
  const selected = requestedId
    ? available.find(company => company.companyId === requestedId)
    : [...available].sort((left, right) => right.products.length - left.products.length ||
        left.companyName.localeCompare(right.companyName, 'es'))[0];
  if (!selected) throw new Error('NO_SCALE_PRODUCTS');
  return selected;
}
