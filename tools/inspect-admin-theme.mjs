// Read-only visual smoke check for the local administrator. Credentials are supplied
// through the process environment and never written to the repository or output.
import { spawn } from 'node:child_process';
import { mkdtemp, mkdir, readFile, rm, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import path from 'node:path';

const companyCode = process.env.CARNICERIAS_TEST_COMPANY_CODE;
const username = process.env.CARNICERIAS_TEST_USERNAME;
const password = process.env.CARNICERIAS_TEST_PASSWORD;
if (!companyCode || !username || !password) {
  throw new Error('Set CARNICERIAS_TEST_COMPANY_CODE, CARNICERIAS_TEST_USERNAME and CARNICERIAS_TEST_PASSWORD.');
}

const route = process.argv[2] ?? '/admin';
if (!/^\/admin(?:\/(?:configuracion|categories|taxes|products|price-lists|customers|arca|payment-gateways|organization|central-connection|barcode-layouts|stock|pieces|history|accounts))?$/.test(route)) {
  throw new Error('Only the initial administration routes are supported.');
}
const viewport = /^--viewport=(\d{3,4})x(\d{3,4})$/.exec(
  process.argv.find((argument) => argument.startsWith('--viewport=')) ?? '--viewport=1366x768',
);
if (!viewport) throw new Error('Use --viewport=WIDTHxHEIGHT.');
const width = Number(viewport[1]);
const height = Number(viewport[2]);
const expectedTab = ['/admin/categories', '/admin/taxes', '/admin/products', '/admin/price-lists',
  '/admin/customers', '/admin/arca', '/admin/payment-gateways', '/admin/organization',
  '/admin/central-connection', '/admin/barcode-layouts']
  .includes(route) ? '/admin/configuracion' : ['/admin/stock', '/admin/pieces', '/admin/history', '/admin/accounts'].includes(route) ? '/admin' : route;
const inspectDialog = process.argv.includes('--dialog');
const inspectImportDialog = process.argv.includes('--import-dialog');
const inspectTaxDialog = process.argv.includes('--tax-dialog');
const inspectProductDialog = process.argv.includes('--product-dialog');
const inspectPricesDialog = process.argv.includes('--prices-dialog');
const inspectCustomerDialog = process.argv.includes('--customer-dialog');
const inspectGatewayDialog = process.argv.includes('--gateway-dialog');
const inspectOrganizationDialog = process.argv.includes('--organization-dialog');
const mockCentral = process.argv.includes('--mock-central');
const inspectTaxEditor = process.argv.includes('--tax-editor');
const inspectBarcodePreview = process.argv.includes('--barcode-preview');
const inspectStockDialog = process.argv.includes('--stock-dialog');
const inspectHistoryDialog = process.argv.includes('--history-dialog');
const inspectAccountsDialog = process.argv.includes('--accounts-dialog');
if ((inspectDialog || inspectImportDialog) && route !== '/admin/categories') {
  throw new Error('Dialog checks require /admin/categories.');
}
if (inspectTaxDialog && route !== '/admin/taxes') throw new Error('--tax-dialog requires /admin/taxes.');
if (inspectProductDialog && route !== '/admin/products') throw new Error('--product-dialog requires /admin/products.');
if (inspectPricesDialog && route !== '/admin/price-lists') throw new Error('--prices-dialog requires /admin/price-lists.');
if (inspectCustomerDialog && route !== '/admin/customers') throw new Error('--customer-dialog requires /admin/customers.');
if (inspectGatewayDialog && route !== '/admin/payment-gateways') {
  throw new Error('--gateway-dialog requires /admin/payment-gateways.');
}
if (inspectOrganizationDialog && route !== '/admin/organization') {
  throw new Error('--organization-dialog requires /admin/organization.');
}
if (mockCentral && route !== '/admin/central-connection') {
  throw new Error('--mock-central requires /admin/central-connection.');
}
if (inspectTaxEditor && route !== '/admin/taxes') throw new Error('--tax-editor requires /admin/taxes.');
if (inspectBarcodePreview && route !== '/admin/barcode-layouts') {
  throw new Error('--barcode-preview requires /admin/barcode-layouts.');
}
if (inspectStockDialog && route !== '/admin/stock') throw new Error('--stock-dialog requires /admin/stock.');
if (inspectHistoryDialog && route !== '/admin/history') throw new Error('--history-dialog requires /admin/history.');
if (inspectAccountsDialog && route !== '/admin/accounts') throw new Error('--accounts-dialog requires /admin/accounts.');
if (inspectDialog && inspectImportDialog) throw new Error('Choose one dialog check at a time.');
const outputDirectory = process.argv.find((argument) => argument.startsWith('--output='))?.slice(9) ??
  path.join(tmpdir(), 'carnicerias-admin-theme');
const chromePath = process.env.CARNICERIAS_TEST_CHROME_PATH ??
  'C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe';
const profileDirectory = await mkdtemp(path.join(tmpdir(), 'carnicerias-theme-'));
const chrome = spawn(chromePath, [
  '--headless=new', '--no-first-run', '--no-default-browser-check',
  '--disable-background-networking', '--remote-debugging-port=0',
  `--user-data-dir=${profileDirectory}`, `--window-size=${width},${height}`, 'about:blank',
], { stdio: 'ignore', windowsHide: true });

function pause(milliseconds) { return new Promise((resolve) => setTimeout(resolve, milliseconds)); }
async function until(action, timeoutMilliseconds = 15000) {
  const started = Date.now();
  while (Date.now() - started < timeoutMilliseconds) {
    const result = await action();
    if (result) return result;
    await pause(100);
  }
  throw new Error('Timed out waiting for the administrator UI.');
}

let socket;
let send;
try {
  const activePort = await until(async () => {
    try {
      return Number((await readFile(path.join(profileDirectory, 'DevToolsActivePort'), 'utf8')).split('\n')[0]);
    } catch { return null; }
  });
  const targets = await fetch(`http://127.0.0.1:${activePort}/json/list`).then((response) => response.json());
  const target = targets.find((candidate) => candidate.type === 'page');
  if (!target) throw new Error('Chrome did not expose a test page.');
  socket = new WebSocket(target.webSocketDebuggerUrl);
  await new Promise((resolve, reject) => {
    socket.addEventListener('open', resolve, { once: true });
    socket.addEventListener('error', reject, { once: true });
  });
  let nextId = 0;
  const pending = new Map();
  socket.addEventListener('message', (event) => {
    const message = JSON.parse(event.data);
    if (message.method === 'Fetch.requestPaused' && mockCentral) {
      const request = message.params;
      if (request.request.method !== 'GET') {
        void send('Fetch.failRequest', { requestId: request.requestId, errorReason: 'BlockedByClient' });
        return;
      }
      const sample = {
        companyId: '00000000-0000-4000-8000-000000000001', companyName: 'Empresa de prueba',
        branchId: '00000000-0000-4000-8000-000000000002', branchName: 'Sucursal de prueba',
        installationId: '00000000-0000-4000-8000-000000000003',
        destinationUrl: 'http://127.0.0.1:5197/', isActive: false, hasCredential: false,
        hasPreviousCredential: false, previousCredentialExpiresAtUtc: null, revision: 1,
      };
      void send('Fetch.fulfillRequest', {
        requestId: request.requestId, responseCode: 200,
        responseHeaders: [{ name: 'Content-Type', value: 'application/json; charset=utf-8' }],
        body: Buffer.from(JSON.stringify(sample)).toString('base64'),
      });
      return;
    }
    const completion = pending.get(message.id);
    if (!completion) return;
    pending.delete(message.id);
    if (message.error) completion.reject(new Error(message.error.message));
    else completion.resolve(message.result);
  });
  send = (method, params = {}) => new Promise((resolve, reject) => {
    const id = ++nextId;
    pending.set(id, { resolve, reject });
    socket.send(JSON.stringify({ id, method, params }));
  });
  async function evaluate(expression) {
    const result = await send('Runtime.evaluate', { expression, returnByValue: true, awaitPromise: true });
    if (result.exceptionDetails) throw new Error(result.exceptionDetails.text);
    return result.result.value;
  }
  await send('Page.enable');
  await send('Emulation.setDeviceMetricsOverride', {
    width, height, deviceScaleFactor: 1, mobile: false,
  });
  await send('Page.navigate', { url: 'http://localhost:4200/' });
  await until(() => evaluate("Boolean(document.querySelector('.login-form'))"));
  const login = `(() => {
    for (const [selector, value] of ${JSON.stringify([
      ['#company-code', companyCode], ['#credential', username], ['#password', password],
    ])}) {
      const field = document.querySelector(selector);
      field.value = value;
      field.dispatchEvent(new Event('input', { bubbles: true }));
    }
    document.querySelector('.login-form').requestSubmit();
    return true;
  })()`;
  await evaluate(login);
  const loginResult = await until(() => evaluate(`(() => {
    if (document.querySelector('.context-form select#branch')) return 'branch';
    if (document.querySelector('a[href="/admin"]')) return 'ready';
    if (document.querySelector('.form-error')) return 'error';
    return null;
  })()`));
  if (loginResult === 'error') throw new Error(`Local login failed: ${await evaluate("document.querySelector('.form-error').textContent.trim()")}`);
  if (loginResult === 'branch') {
    await until(() => evaluate("Boolean(document.querySelector('#branch option:not([value=\"\"])'))"));
    await evaluate(`(() => {
      const field = document.querySelector('#branch');
      field.value = Array.from(field.options).find((option) => option.value).value;
      field.dispatchEvent(new Event('change', { bubbles: true }));
      return true;
    })()`);
    await until(() => evaluate("!document.querySelector('.context-form button[type=submit]').disabled"));
    await evaluate("document.querySelector('.context-form').requestSubmit(); true");
    await until(() => evaluate("Boolean(document.querySelector('a[href=\"/admin\"]'))"));
  }

  if (mockCentral) {
    await send('Fetch.enable', {
      patterns: [{ urlPattern: '*api/admin/central-connection', requestStage: 'Request' }],
    });
  }

  await send('Page.navigate', { url: `http://localhost:4200${route}` });
  await until(() => evaluate(`Boolean(document.querySelector(
    ${JSON.stringify(route === '/admin/categories' ? 'app-categories-page .category-panel tbody tr' :
      route === '/admin/taxes' ? 'app-product-tax-page app-tax-catalog-panel .tax-catalog-form' :
      route === '/admin/products' ? 'app-products-page .category-panel tbody tr' :
      route === '/admin/price-lists' ? 'app-price-lists-page .category-panel tbody tr' :
      route === '/admin/customers' ? 'app-customers-page .category-panel tbody tr' :
      route === '/admin/arca' ? 'app-arca-settings-page .category-panel #issuer-title' :
      route === '/admin/payment-gateways' ? 'app-payment-gateways-page .category-panel #mp-title' :
      route === '/admin/organization' ? 'app-organization-page .company-row' :
      route === '/admin/central-connection' ? 'app-central-connection-page #identity-title, app-central-connection-page .central-error' :
      route === '/admin/barcode-layouts' ? 'app-barcode-layout-page .barcode-form' :
      route === '/admin/stock' ? 'app-stock-page .stock-row' :
      route === '/admin/pieces' ? 'app-piece-receipt-page .piece-form' :
      route === '/admin/history' ? 'app-history-page .history-tabs' :
      route === '/admin/accounts' ? 'app-accounts-page .account-search' :
      'app-admin-page .module-panel')}
  ))`));
  if (route === '/admin/taxes') {
    await until(() => evaluate(`(() => {
      const catalog = document.querySelector('app-tax-catalog-panel .category-panel');
      const assignment = document.querySelector('app-tax-assignment-panel .category-panel');
      return catalog?.getAttribute('aria-busy') === 'false' && assignment &&
        !assignment.querySelector('.state-message[role="status"]');
    })()`));
  }
  if (route === '/admin/barcode-layouts') {
    await until(() => evaluate("Boolean(document.querySelector('app-barcode-layout-page .saved-profile'))"));
  }
  if (route === '/admin/pieces') {
    await until(() => evaluate("Boolean(document.querySelector('app-piece-receipt-page .table-wrap tbody tr'))"));
  }
  if (route === '/admin/history') {
    await until(() => evaluate("document.querySelector('app-history-page .history-panel')?.getAttribute('aria-busy') === 'false'"));
  }
  if (route === '/admin/accounts') {
    await until(() => evaluate("document.querySelector('app-accounts-page .history-panel')?.getAttribute('aria-busy') === 'false'"));
  }
  await mkdir(outputDirectory, { recursive: true });
  for (const theme of ['original', 'nocturno']) {
    const styles = await evaluate(`(() => {
      document.documentElement.dataset.visualTheme = ${JSON.stringify(theme)};
      const host = document.querySelector(
        'app-admin-page, app-categories-page, app-product-tax-page, app-products-page, app-price-lists-page, app-customers-page, app-arca-settings-page, app-payment-gateways-page, app-organization-page, app-central-connection-page, app-barcode-layout-page, app-stock-page, app-piece-receipt-page, app-history-page, app-accounts-page');
      const header = host.querySelector('.app-header');
      const panel = host.querySelector('.module-panel, .category-panel, .central-error');
      const brandMark = host.querySelector('.brand-mark');
      const activeTab = host.querySelector('.area-tabs a[aria-current="page"]');
      const tableHead = host.querySelector('thead th');
      const search = host.querySelector('#category-search');
      const taxCatalog = host.querySelector('app-tax-catalog-panel');
      const taxAssignment = host.querySelector('app-tax-assignment-panel');
      const productFilter = host.querySelector('.product-filters select');
      const arcaControl = host.querySelector('.arca-form input');
      const certificateState = host.querySelector('.certificate-state');
      const gatewayCard = host.querySelector('.gateway-register');
      const centralControl = host.querySelector('#central-destination-url');
      const savedProfile = host.querySelector('.saved-profile');
      const pieceProfile = host.querySelector('#piece-profile');
      const stockCode = host.querySelector('.stock-row td small');
      const historyFilter = host.querySelector('#history-branch');
      const historyTab = host.querySelector('.history-tabs button[aria-current="page"]');
      const historyDate = host.querySelector('#history-from');
      const accountSearch = host.querySelector('#account-search-input');
      return {
        theme: document.documentElement.dataset.visualTheme,
        route: location.pathname,
        title: host.querySelector('h1').textContent.trim(),
        stageToken: getComputedStyle(host).getPropertyValue('--admin-stage').trim(),
        stage: getComputedStyle(host).backgroundColor,
        header: getComputedStyle(header).backgroundColor,
        panel: getComputedStyle(panel).backgroundColor,
        brandMark: getComputedStyle(brandMark).backgroundColor,
        brandMarkWidth: brandMark.getBoundingClientRect().width,
        activeTab: activeTab?.getAttribute('href'),
        activeTabBorder: activeTab && getComputedStyle(activeTab).borderBottomColor,
        tableHead: tableHead && getComputedStyle(tableHead).backgroundColor,
        search: search && getComputedStyle(search).backgroundColor,
        taxCatalogSelect: taxCatalog && getComputedStyle(taxCatalog.querySelector('#new-tax-kind')).backgroundColor,
        taxCatalogRows: taxCatalog?.querySelectorAll('tbody tr').length,
        taxAssignmentSelect: taxAssignment && getComputedStyle(taxAssignment.querySelector('#assignment-tax')).backgroundColor,
        taxAssignmentRows: taxAssignment?.querySelectorAll('tbody tr').length,
        productFilter: productFilter && getComputedStyle(productFilter).backgroundColor,
        arcaControl: arcaControl && getComputedStyle(arcaControl).backgroundColor,
        certificateState: certificateState && getComputedStyle(certificateState).backgroundColor,
        gatewayCard: gatewayCard && getComputedStyle(gatewayCard).backgroundColor,
        gatewayCards: host.querySelectorAll('.gateway-register').length,
        centralControl: centralControl && getComputedStyle(centralControl).backgroundColor,
        centralAvailable: Boolean(centralControl),
        savedProfile: savedProfile && getComputedStyle(savedProfile).backgroundColor,
        savedProfiles: host.querySelectorAll('.saved-profile').length,
        pieceProfile: pieceProfile && getComputedStyle(pieceProfile).backgroundColor,
        pieceRows: host.querySelectorAll('#recent-pieces-title ~ .table-wrap tbody tr').length,
        stockCode: stockCode && getComputedStyle(stockCode).color,
        historyFilter: historyFilter && getComputedStyle(historyFilter).backgroundColor,
        historyTab: historyTab && getComputedStyle(historyTab).backgroundColor,
        historyDateWidth: historyDate?.getBoundingClientRect().width,
        accountSearch: accountSearch && getComputedStyle(accountSearch).backgroundColor,
        tableRows: host.querySelectorAll('tbody tr').length,
        documentScrollWidth: document.documentElement.scrollWidth,
        viewportWidth: window.innerWidth,
      };
    })()`);
    if (mockCentral) styles.simulated = true;
    if (styles.brandMarkWidth !== 72 || styles.brandMark !==
      (theme === 'nocturno' ? 'rgb(229, 178, 127)' : 'rgb(245, 245, 245)')) {
      throw new Error(`The ${theme} administrative brand mark is overridden by another component.`);
    }
    if (styles.activeTab !== expectedTab || styles.activeTabBorder !==
      (theme === 'nocturno' ? 'rgb(229, 178, 127)' : 'rgb(84, 194, 189)')) {
      throw new Error(`The ${theme} active administration tab lost its route or contrast.`);
    }
    const keyboardFocusExpression = `(() => {
      const focused = document.activeElement;
      return {
        href: focused?.getAttribute('href'),
        visible: focused?.matches(':focus-visible'),
        outline: getComputedStyle(focused).outlineColor,
      };
    })()`;
    let keyboardFocus = await evaluate(keyboardFocusExpression);
    if (theme === 'original') {
      for (let tabCount = 0; tabCount < 12 && keyboardFocus.href !== expectedTab; tabCount++) {
        await send('Input.dispatchKeyEvent', {
          type: 'keyDown', key: 'Tab', code: 'Tab', windowsVirtualKeyCode: 9,
        });
        await send('Input.dispatchKeyEvent', {
          type: 'keyUp', key: 'Tab', code: 'Tab', windowsVirtualKeyCode: 9,
        });
        keyboardFocus = await evaluate(keyboardFocusExpression);
      }
    }
    const allowedFocus = theme === 'nocturno' ? ['rgb(229, 178, 127)'] :
      ['rgb(84, 194, 189)', 'rgb(113, 184, 218)'];
    if (keyboardFocus.href !== expectedTab || !keyboardFocus.visible ||
      !allowedFocus.includes(keyboardFocus.outline)) {
      throw new Error(`The ${theme} active administration tab has no keyboard-visible focus: ${JSON.stringify(keyboardFocus)}`);
    }
    styles.keyboardFocus = keyboardFocus;
    if (route === '/admin/categories' && (styles.tableHead !==
      (theme === 'nocturno' ? 'rgb(72, 57, 43)' : 'rgb(0, 139, 139)') ||
      styles.search !== (theme === 'nocturno' ? 'rgb(24, 27, 32)' : 'rgb(255, 255, 255)'))) {
      throw new Error(`The ${theme} shared ABM controls did not receive their theme.`);
    }
    if (route === '/admin/taxes' && (styles.taxCatalogSelect !==
      (theme === 'nocturno' ? 'rgb(24, 27, 32)' : 'rgb(255, 255, 255)') ||
      styles.taxAssignmentSelect !==
      (theme === 'nocturno' ? 'rgb(24, 27, 32)' : 'rgb(255, 255, 255)'))) {
      throw new Error(`The ${theme} tax catalog and assignment controls did not receive their theme.`);
    }
    if (route === '/admin/products' && styles.productFilter !==
      (theme === 'nocturno' ? 'rgb(24, 27, 32)' : 'rgb(255, 255, 255)')) {
      throw new Error(`The ${theme} article filters did not receive their theme.`);
    }
    if (['/admin/arca', '/admin/payment-gateways'].includes(route) && styles.arcaControl !==
      (theme === 'nocturno' ? 'rgb(24, 27, 32)' : 'rgb(255, 255, 255)')) {
      throw new Error(`The ${theme} integration form controls did not receive their theme.`);
    }
    if (route === '/admin/arca' && styles.certificateState && styles.certificateState !==
      (theme === 'nocturno' ? 'rgb(72, 57, 43)' : 'rgb(198, 232, 227)')) {
      throw new Error(`The ${theme} certificate status lost its palette.`);
    }
    if (route === '/admin/payment-gateways' && styles.gatewayCard && styles.gatewayCard !==
      (theme === 'nocturno' ? 'rgb(45, 50, 57)' : 'rgb(255, 255, 255)')) {
      throw new Error(`The ${theme} gateway register card lost its palette.`);
    }
    if (route === '/admin/central-connection' && styles.centralAvailable && styles.centralControl !==
      (theme === 'nocturno' ? 'rgb(24, 27, 32)' : 'rgb(255, 255, 255)')) {
      throw new Error(`The ${theme} central connection controls did not receive their theme.`);
    }
    if (route === '/admin/barcode-layouts' && styles.savedProfile && styles.savedProfile !==
      (theme === 'nocturno' ? 'rgb(45, 50, 57)' : 'rgb(255, 255, 255)')) {
      throw new Error(`The ${theme} saved barcode profiles did not receive their theme.`);
    }
    if (route === '/admin/pieces' && styles.pieceProfile !==
      (theme === 'nocturno' ? 'rgb(24, 27, 32)' : 'rgb(255, 255, 255)')) {
      throw new Error(`The ${theme} piece receipt selectors did not receive their theme.`);
    }
    if (route === '/admin/stock' && styles.stockCode !==
      (theme === 'nocturno' ? 'rgb(187, 184, 179)' : 'rgb(101, 93, 82)')) {
      throw new Error(`The ${theme} stock item metadata lost its contrast.`);
    }
    if (route === '/admin/history' && (styles.historyFilter !==
      (theme === 'nocturno' ? 'rgb(24, 27, 32)' : 'rgb(255, 255, 255)') ||
      styles.historyTab !== (theme === 'nocturno' ? 'rgb(229, 178, 127)' : 'rgb(0, 139, 139)'))) {
      throw new Error(`The ${theme} history filters or selected tab lost their theme.`);
    }
    if (route === '/admin/history' && width <= 560 && styles.historyDateWidth < 200) {
      throw new Error(`The ${theme} history date filter is too narrow to use on mobile.`);
    }
    if (route === '/admin/accounts' && styles.accountSearch !==
      (theme === 'nocturno' ? 'rgb(24, 27, 32)' : 'rgb(255, 255, 255)')) {
      throw new Error(`The ${theme} accounts search lost its theme.`);
    }
    if (route === '/admin/categories' && width <= 560) {
      styles.tableScroll = await evaluate(`(() => {
        const wrap = document.querySelector('app-categories-page .table-wrap');
        const before = wrap.scrollLeft;
        wrap.scrollLeft = wrap.scrollWidth;
        const lastAction = wrap.querySelector('tbody tr .row-actions button:last-child').getBoundingClientRect();
        const bounds = wrap.getBoundingClientRect();
        const reachable = lastAction.left >= bounds.left && lastAction.right <= bounds.right + 1;
        wrap.scrollLeft = before;
        return { viewport: wrap.clientWidth, content: wrap.scrollWidth, reachable };
      })()`);
      if (styles.tableScroll.content <= styles.tableScroll.viewport || !styles.tableScroll.reachable) {
        throw new Error(`The ${theme} row actions are not reachable inside the mobile table.`);
      }
    }
    if (['/admin/products', '/admin/price-lists', '/admin/customers'].includes(route) && width <= 560) {
      styles.tableScroll = await evaluate(`(() => {
        const wrap = document.querySelector('.category-panel > .table-wrap');
        const before = wrap.scrollLeft;
        wrap.scrollLeft = wrap.scrollWidth;
        const lastAction = wrap.querySelector('tbody tr .row-actions button:last-child').getBoundingClientRect();
        const bounds = wrap.getBoundingClientRect();
        const reachable = lastAction.left >= bounds.left && lastAction.right <= bounds.right + 1;
        wrap.scrollLeft = before;
        return { viewport: wrap.clientWidth, content: wrap.scrollWidth, reachable };
      })()`);
      if (!styles.tableScroll.reachable) {
        throw new Error(`The ${theme} last row action is not reachable inside the mobile table.`);
      }
    }
    if (styles.documentScrollWidth > styles.viewportWidth) {
      throw new Error(`The ${theme} admin route overflows the ${styles.viewportWidth}px viewport.`);
    }
    await evaluate('new Promise((resolve) => requestAnimationFrame(() => requestAnimationFrame(resolve)))');
    const screenshot = await send('Page.captureScreenshot', { format: 'png', captureBeyondViewport: false });
    const file = path.join(outputDirectory, `${route.slice(1).replaceAll('/', '-')}-${theme}-${width}x${height}.png`);
    await writeFile(file, Buffer.from(screenshot.data, 'base64'));
    console.log(JSON.stringify({ ...styles, screenshot: file }));
    if (route === '/admin/categories' && width <= 560) {
      const fullPage = await send('Page.captureScreenshot', { format: 'png', captureBeyondViewport: true });
      await writeFile(file.replace(/\.png$/, '-full.png'), Buffer.from(fullPage.data, 'base64'));
    }
    if (route === '/admin/taxes') {
      const assignment = await evaluate(`(() => {
        const panel = document.querySelector('app-tax-assignment-panel');
        panel.scrollIntoView({ block: 'start' });
        const wrap = panel.querySelector('.table-wrap');
        const before = wrap?.scrollLeft ?? 0;
        if (wrap) wrap.scrollLeft = wrap.scrollWidth;
        const action = wrap?.querySelector('tbody tr button')?.getBoundingClientRect();
        const bounds = wrap?.getBoundingClientRect();
        const actionReachable = !action || !bounds ||
          (action.left >= bounds.left && action.right <= bounds.right + 1);
        if (wrap) wrap.scrollLeft = before;
        return {
          title: panel.querySelector('h2')?.textContent.trim(),
          note: getComputedStyle(panel.querySelector('.assignment-note')).color,
          selection: getComputedStyle(panel.querySelector('#assignment-selected')).accentColor,
          tableWidth: wrap?.scrollWidth ?? 0,
          tableViewport: wrap?.clientWidth ?? 0,
          actionReachable,
        };
      })()`);
      if (assignment.note !== (theme === 'nocturno' ? 'rgb(229, 178, 127)' : 'rgb(0, 95, 96)') ||
        !assignment.actionReachable) {
        throw new Error(`The ${theme} tax assignment panel lost contrast or table access.`);
      }
      await evaluate('new Promise((resolve) => requestAnimationFrame(() => requestAnimationFrame(resolve)))');
      const detail = await send('Page.captureScreenshot', { format: 'png', captureBeyondViewport: false });
      const detailFile = file.replace(/\.png$/, '-assignment.png');
      await writeFile(detailFile, Buffer.from(detail.data, 'base64'));
      console.log(JSON.stringify({ theme, ...assignment, screenshot: detailFile }));
      await evaluate('window.scrollTo(0, 0); true');
    }
    if (route === '/admin/arca' || route === '/admin/payment-gateways') {
      const target = route === '/admin/arca' ? '#certificate-title' : '#clover-title';
      await evaluate(`document.querySelector(${JSON.stringify(target)}).scrollIntoView({ block: 'start' }); true`);
      await evaluate('new Promise((resolve) => requestAnimationFrame(() => requestAnimationFrame(resolve)))');
      const detail = await send('Page.captureScreenshot', { format: 'png', captureBeyondViewport: false });
      const detailFile = file.replace(/\.png$/, '-details.png');
      await writeFile(detailFile, Buffer.from(detail.data, 'base64'));
      console.log(JSON.stringify({ theme, route, screenshot: detailFile }));
      await evaluate('window.scrollTo(0, 0); true');
    }
    if (route === '/admin/central-connection' && styles.centralAvailable) {
      await evaluate("document.querySelector('#package-title').scrollIntoView({ block: 'start' }); true");
      await evaluate('new Promise((resolve) => requestAnimationFrame(() => requestAnimationFrame(resolve)))');
      const detail = await send('Page.captureScreenshot', { format: 'png', captureBeyondViewport: false });
      const detailFile = file.replace(/\.png$/, '-package.png');
      await writeFile(detailFile, Buffer.from(detail.data, 'base64'));
      console.log(JSON.stringify({ theme, route, simulated: mockCentral, screenshot: detailFile }));
      await evaluate('window.scrollTo(0, 0); true');
    }
    if (route === '/admin/pieces') {
      await evaluate("document.querySelector('#recent-pieces-title').scrollIntoView({ block: 'start' }); true");
      await evaluate('new Promise((resolve) => requestAnimationFrame(() => requestAnimationFrame(resolve)))');
      const detail = await send('Page.captureScreenshot', { format: 'png', captureBeyondViewport: false });
      const detailFile = file.replace(/\.png$/, '-history.png');
      await writeFile(detailFile, Buffer.from(detail.data, 'base64'));
      console.log(JSON.stringify({ theme, route, screenshot: detailFile }));
      await evaluate('window.scrollTo(0, 0); true');
    }
  }
  if (inspectDialog) {
    await evaluate(`(() => {
      document.documentElement.dataset.visualTheme = 'original';
      document.querySelector('.create-button').click();
      return true;
    })()`);
    await until(() => evaluate("Boolean(document.querySelector('.admin-detail-dialog[open] #category-name'))"));
    await evaluate(`(() => {
      const field = document.querySelector('#category-name');
      field.value = 'Cortes de prueba sin guardar';
      field.dispatchEvent(new Event('input', { bubbles: true }));
      return true;
    })()`);
    for (const theme of ['original', 'nocturno']) {
      const dialogStyles = await evaluate(`(() => {
        document.documentElement.dataset.visualTheme = ${JSON.stringify(theme)};
        const dialog = document.querySelector('.admin-detail-dialog[open]');
        return {
          theme: document.documentElement.dataset.visualTheme,
          open: dialog?.open,
          title: document.getElementById(dialog?.getAttribute('aria-labelledby'))?.textContent.trim(),
          value: dialog?.querySelector('#category-name')?.value,
          surface: getComputedStyle(dialog).backgroundColor,
          ink: getComputedStyle(dialog).color,
        };
      })()`);
      if (!dialogStyles.open || dialogStyles.title !== 'Nueva categoría' ||
        dialogStyles.value !== 'Cortes de prueba sin guardar' || dialogStyles.surface !==
        (theme === 'nocturno' ? 'rgb(36, 40, 46)' : 'rgb(237, 237, 237)')) {
        throw new Error(`The ${theme} category editor did not preserve its open state and colors.`);
      }
      await evaluate('new Promise((resolve) => requestAnimationFrame(() => requestAnimationFrame(resolve)))');
      const screenshot = await send('Page.captureScreenshot', { format: 'png', captureBeyondViewport: false });
      const file = path.join(outputDirectory, `admin-categories-modal-${theme}-${width}x${height}.png`);
      await writeFile(file, Buffer.from(screenshot.data, 'base64'));
      console.log(JSON.stringify({ ...dialogStyles, screenshot: file }));
    }
  }
  if (inspectImportDialog) {
    await evaluate(`(() => {
      document.documentElement.dataset.visualTheme = 'original';
      document.querySelector('.import-open').click();
      return true;
    })()`);
    await until(() => evaluate("Boolean(document.querySelector('.admin-detail-dialog[open] .import-file input'))"));
    for (const theme of ['original', 'nocturno']) {
      const importStyles = await evaluate(`(() => {
        document.documentElement.dataset.visualTheme = ${JSON.stringify(theme)};
        const dialog = document.querySelector('.admin-detail-dialog[open]');
        const picker = dialog.querySelector('.import-file input');
        return {
          theme: document.documentElement.dataset.visualTheme,
          open: dialog.open,
          title: dialog.querySelector('h2')?.textContent.trim(),
          templateInk: getComputedStyle(document.querySelector('.import-template')).color,
          openButton: getComputedStyle(document.querySelector('.import-open')).backgroundColor,
          dialogInk: getComputedStyle(dialog.querySelector('.import-dialog')).color,
          picker: getComputedStyle(picker).backgroundColor,
          pickerFocused: picker.matches(':focus-visible'),
          pickerOutline: getComputedStyle(picker).outlineColor,
          selectedFiles: picker.files.length,
          confirmDisabled: dialog.querySelector('.import-footer button:last-child').disabled,
        };
      })()`);
      if (!importStyles.open || importStyles.title !== 'Importar desde Excel' ||
        importStyles.selectedFiles !== 0 || !importStyles.confirmDisabled ||
        importStyles.templateInk !== (theme === 'nocturno' ? 'rgb(229, 178, 127)' : 'rgb(37, 75, 70)') ||
        importStyles.dialogInk !== (theme === 'nocturno' ? 'rgb(243, 239, 233)' : 'rgb(39, 56, 61)') ||
        (theme === 'nocturno' && importStyles.pickerFocused && importStyles.pickerOutline !== 'rgb(229, 178, 127)')) {
        throw new Error(`The ${theme} Excel import dialog lost its visual or disabled state.`);
      }
      await evaluate('new Promise((resolve) => requestAnimationFrame(() => requestAnimationFrame(resolve)))');
      const screenshot = await send('Page.captureScreenshot', { format: 'png', captureBeyondViewport: false });
      const file = path.join(outputDirectory, `admin-categories-import-${theme}-${width}x${height}.png`);
      await writeFile(file, Buffer.from(screenshot.data, 'base64'));
      console.log(JSON.stringify({ ...importStyles, screenshot: file }));
    }
  }
  if (inspectTaxDialog) {
    await evaluate(`(() => {
      document.documentElement.dataset.visualTheme = 'original';
      document.querySelector('app-tax-catalog-panel tbody button').click();
      return true;
    })()`);
    await until(() => evaluate("Boolean(document.querySelector('.admin-detail-dialog[open] #tax-action-title'))"));
    for (const theme of ['original', 'nocturno']) {
      const dialogStyles = await evaluate(`(() => {
        document.documentElement.dataset.visualTheme = ${JSON.stringify(theme)};
        const dialog = document.querySelector('.admin-detail-dialog[open]');
        return {
          theme: document.documentElement.dataset.visualTheme,
          open: dialog.open,
          title: dialog.querySelector('#tax-action-title')?.textContent.trim(),
          surface: getComputedStyle(dialog).backgroundColor,
          ink: getComputedStyle(dialog).color,
          confirmDisabled: dialog.querySelector('.confirm-tax-action').disabled,
        };
      })()`);
      if (!dialogStyles.open || dialogStyles.title !== 'Inactivar impuesto o gravamen' ||
        dialogStyles.surface !== (theme === 'nocturno' ? 'rgb(36, 40, 46)' : 'rgb(237, 237, 237)')) {
        throw new Error(`The ${theme} tax action dialog lost its state or surface.`);
      }
      await evaluate('new Promise((resolve) => requestAnimationFrame(() => requestAnimationFrame(resolve)))');
      const screenshot = await send('Page.captureScreenshot', { format: 'png', captureBeyondViewport: false });
      const file = path.join(outputDirectory, `admin-taxes-dialog-${theme}-${width}x${height}.png`);
      await writeFile(file, Buffer.from(screenshot.data, 'base64'));
      console.log(JSON.stringify({ ...dialogStyles, screenshot: file }));
    }
  }
  if (inspectProductDialog) {
    await evaluate(`(() => {
      document.documentElement.dataset.visualTheme = 'original';
      document.querySelector('app-products-page tbody .edit-button').click();
      return true;
    })()`);
    await until(() => evaluate("Boolean(document.querySelector('.admin-detail-dialog[open] #product-name'))"));
    for (const theme of ['original', 'nocturno']) {
      const editorStyles = await evaluate(`(() => {
        document.documentElement.dataset.visualTheme = ${JSON.stringify(theme)};
        const dialog = document.querySelector('.admin-detail-dialog[open]');
        const editor = dialog.querySelector('.product-editor');
        return {
          theme: document.documentElement.dataset.visualTheme,
          open: dialog.open,
          title: editor.querySelector('h3')?.textContent.trim(),
          name: editor.querySelector('#product-name').value,
          editor: getComputedStyle(editor).backgroundColor,
          select: getComputedStyle(editor.querySelector('#product-category')).backgroundColor,
          alternates: getComputedStyle(editor.querySelector('.alternate-codes')).backgroundColor,
        };
      })()`);
      if (!editorStyles.open || !editorStyles.name || editorStyles.editor !==
        (theme === 'nocturno' ? 'rgb(45, 50, 57)' : 'rgb(197, 197, 197)') ||
        editorStyles.alternates !== (theme === 'nocturno' ? 'rgb(36, 40, 46)' : 'rgb(224, 224, 224)')) {
        throw new Error(`The ${theme} article editor lost its data or theme.`);
      }
      await evaluate('new Promise((resolve) => requestAnimationFrame(() => requestAnimationFrame(resolve)))');
      const screenshot = await send('Page.captureScreenshot', { format: 'png', captureBeyondViewport: false });
      const file = path.join(outputDirectory, `admin-products-dialog-${theme}-${width}x${height}.png`);
      await writeFile(file, Buffer.from(screenshot.data, 'base64'));
      console.log(JSON.stringify({ ...editorStyles, screenshot: file }));
    }
    await evaluate("document.querySelector('.product-editor button.secondary-button').click(); true");
    await until(() => evaluate(`(() => {
      const dialog = document.querySelector('#cost-history-title')?.closest('.admin-detail-dialog[open]');
      return dialog && !dialog.querySelector('[role="status"]');
    })()`));
    for (const theme of ['original', 'nocturno']) {
      const history = await evaluate(`(() => {
        document.documentElement.dataset.visualTheme = ${JSON.stringify(theme)};
        const title = document.querySelector('#cost-history-title');
        const dialog = title.closest('.admin-detail-dialog');
        return {
          theme: document.documentElement.dataset.visualTheme,
          title: title.textContent.trim(),
          open: dialog.open,
          surface: getComputedStyle(dialog).backgroundColor,
          rows: dialog.querySelectorAll('li').length,
        };
      })()`);
      if (!history.open || !history.title || history.surface !==
        (theme === 'nocturno' ? 'rgb(36, 40, 46)' : 'rgb(237, 237, 237)')) {
        throw new Error(`The ${theme} article cost history lost its state or surface.`);
      }
      await evaluate('new Promise((resolve) => requestAnimationFrame(() => requestAnimationFrame(resolve)))');
      const screenshot = await send('Page.captureScreenshot', { format: 'png', captureBeyondViewport: false });
      const file = path.join(outputDirectory, `admin-product-cost-history-${theme}-${width}x${height}.png`);
      await writeFile(file, Buffer.from(screenshot.data, 'base64'));
      console.log(JSON.stringify({ ...history, screenshot: file }));
    }
  }
  if (inspectPricesDialog) {
    await evaluate(`(() => {
      document.documentElement.dataset.visualTheme = 'original';
      document.querySelector('app-price-lists-page tbody .prices-button').click();
      return true;
    })()`);
    await until(() => evaluate(`(() => {
      const panel = document.querySelector('.admin-detail-dialog[open] .detail-panel');
      return panel && !panel.querySelector('[role="status"]') &&
        (panel.querySelector('.price-row') || panel.textContent.includes('No se encontraron'));
    })()`));
    for (const theme of ['original', 'nocturno']) {
      const details = await evaluate(`(() => {
        document.documentElement.dataset.visualTheme = ${JSON.stringify(theme)};
        const dialog = document.querySelector('.admin-detail-dialog[open]');
        const panel = dialog.querySelector('.detail-panel');
        const wrap = panel.querySelector('.table-wrap');
        const scrollBefore = wrap?.scrollLeft ?? 0;
        if (wrap) wrap.scrollLeft = wrap.scrollWidth;
        const action = wrap?.querySelector('.price-row .row-actions button:last-child')?.getBoundingClientRect();
        const bounds = wrap?.getBoundingClientRect();
        const actionsReachable = !action || !bounds ||
          (action.left >= bounds.left && action.right <= bounds.right + 1);
        if (wrap) wrap.scrollLeft = scrollBefore;
        return {
          theme: document.documentElement.dataset.visualTheme,
          open: dialog.open,
          title: panel.querySelector('h2')?.textContent.trim(),
          surface: getComputedStyle(panel).backgroundColor,
          ink: getComputedStyle(panel).color,
          rows: panel.querySelectorAll('.price-row').length,
          firstPrice: panel.querySelector('.price-row td:nth-child(3)')?.textContent.trim(),
          tableWidth: wrap?.scrollWidth ?? 0,
          tableViewport: wrap?.clientWidth ?? 0,
          actionsReachable,
        };
      })()`);
      if (!details.open || !details.title || !details.actionsReachable || details.surface !==
        (theme === 'nocturno' ? 'rgb(36, 40, 46)' : 'rgb(237, 237, 237)')) {
        throw new Error(`The ${theme} price list details lost their state or surface.`);
      }
      await evaluate('new Promise((resolve) => requestAnimationFrame(() => requestAnimationFrame(resolve)))');
      const screenshot = await send('Page.captureScreenshot', { format: 'png', captureBeyondViewport: false });
      const file = path.join(outputDirectory, `admin-price-lists-dialog-${theme}-${width}x${height}.png`);
      await writeFile(file, Buffer.from(screenshot.data, 'base64'));
      console.log(JSON.stringify({ ...details, screenshot: file }));
    }
    await evaluate("document.querySelector('.detail-panel .price-row .set-price-button').click(); true");
    await until(() => evaluate("Boolean(document.querySelector('.admin-detail-dialog[open] .price-form #new-price'))"));
    for (const theme of ['original', 'nocturno']) {
      const editor = await evaluate(`(() => {
        document.documentElement.dataset.visualTheme = ${JSON.stringify(theme)};
        const form = document.querySelector('.admin-detail-dialog[open] .price-form');
        return {
          theme: document.documentElement.dataset.visualTheme,
          title: form.querySelector('h3')?.textContent.trim(),
          amount: form.querySelector('#new-price').value,
          surface: getComputedStyle(form).backgroundColor,
        };
      })()`);
      if (!editor.title || editor.surface !==
        (theme === 'nocturno' ? 'rgb(45, 50, 57)' : 'rgb(220, 220, 220)')) {
        throw new Error(`The ${theme} price editor lost its surface or article.`);
      }
      await evaluate('new Promise((resolve) => requestAnimationFrame(() => requestAnimationFrame(resolve)))');
      const screenshot = await send('Page.captureScreenshot', { format: 'png', captureBeyondViewport: false });
      const file = path.join(outputDirectory, `admin-price-editor-${theme}-${width}x${height}.png`);
      await writeFile(file, Buffer.from(screenshot.data, 'base64'));
      console.log(JSON.stringify({ ...editor, screenshot: file }));
    }
    await evaluate("document.querySelector('.price-form .secondary-button').click(); true");
    await until(() => evaluate("Boolean(document.querySelector('.detail-panel .price-row .history-button'))"));
    await evaluate("document.querySelector('.detail-panel .price-row .history-button').click(); true");
    await until(() => evaluate(`(() => {
      const history = document.querySelector('.admin-detail-dialog[open] .price-history');
      return history && !history.querySelector('[role="status"]');
    })()`));
    for (const theme of ['original', 'nocturno']) {
      const history = await evaluate(`(() => {
        document.documentElement.dataset.visualTheme = ${JSON.stringify(theme)};
        const section = document.querySelector('.admin-detail-dialog[open] .price-history');
        return {
          theme: document.documentElement.dataset.visualTheme,
          title: section.querySelector('h3')?.textContent.trim(),
          surface: getComputedStyle(section).backgroundColor,
          rows: section.querySelectorAll('tbody tr').length,
        };
      })()`);
      if (!history.title || history.surface !==
        (theme === 'nocturno' ? 'rgb(45, 50, 57)' : 'rgb(220, 220, 220)')) {
        throw new Error(`The ${theme} price history lost its surface or article.`);
      }
      await evaluate('new Promise((resolve) => requestAnimationFrame(() => requestAnimationFrame(resolve)))');
      const screenshot = await send('Page.captureScreenshot', { format: 'png', captureBeyondViewport: false });
      const file = path.join(outputDirectory, `admin-price-history-${theme}-${width}x${height}.png`);
      await writeFile(file, Buffer.from(screenshot.data, 'base64'));
      console.log(JSON.stringify({ ...history, screenshot: file }));
    }
  }
  if (inspectCustomerDialog) {
    await evaluate(`(() => {
      document.documentElement.dataset.visualTheme = 'original';
      document.querySelector('app-customers-page tbody .row-actions button').click();
      return true;
    })()`);
    await until(() => evaluate("Boolean(document.querySelector('.admin-detail-dialog[open] #customer-name'))"));
    for (const theme of ['original', 'nocturno']) {
      const editor = await evaluate(`(() => {
        document.documentElement.dataset.visualTheme = ${JSON.stringify(theme)};
        const dialog = document.querySelector('.admin-detail-dialog[open]');
        const name = dialog.querySelector('#customer-name');
        return {
          theme: document.documentElement.dataset.visualTheme,
          open: dialog.open,
          title: dialog.querySelector('h3')?.textContent.trim(),
          name: name.value,
          surface: getComputedStyle(dialog).backgroundColor,
          control: getComputedStyle(name).backgroundColor,
        };
      })()`);
      if (!editor.open || !editor.name || editor.surface !==
        (theme === 'nocturno' ? 'rgb(36, 40, 46)' : 'rgb(237, 237, 237)') ||
        editor.control !== (theme === 'nocturno' ? 'rgb(24, 27, 32)' : 'rgb(255, 255, 255)')) {
        throw new Error(`The ${theme} customer editor lost its data or theme.`);
      }
      await evaluate('new Promise((resolve) => requestAnimationFrame(() => requestAnimationFrame(resolve)))');
      const screenshot = await send('Page.captureScreenshot', { format: 'png', captureBeyondViewport: false });
      const file = path.join(outputDirectory, `admin-customers-dialog-${theme}-${width}x${height}.png`);
      await writeFile(file, Buffer.from(screenshot.data, 'base64'));
      console.log(JSON.stringify({ ...editor, screenshot: file }));
    }
  }
  if (inspectGatewayDialog) {
    const hasRegister = await evaluate("Boolean(document.querySelector('.gateway-register button'))");
    if (!hasRegister) throw new Error('The visual test company has no gateway register to inspect.');
    await evaluate(`(() => {
      document.documentElement.dataset.visualTheme = 'original';
      document.querySelector('.gateway-register button').click();
      return true;
    })()`);
    await until(() => evaluate("Boolean(document.querySelector('.gateway-dialog input'))"));
    for (const theme of ['original', 'nocturno']) {
      const editor = await evaluate(`(() => {
        document.documentElement.dataset.visualTheme = ${JSON.stringify(theme)};
        const dialog = document.querySelector('.gateway-dialog');
        return {
          theme: document.documentElement.dataset.visualTheme,
          title: dialog.querySelector('h2')?.textContent.trim(),
          surface: getComputedStyle(dialog).backgroundColor,
          ink: getComputedStyle(dialog).color,
          control: getComputedStyle(dialog.querySelector('input')).backgroundColor,
          fields: dialog.querySelectorAll('input').length,
        };
      })()`);
      if (!editor.title || editor.fields !== 2 || editor.surface !==
        (theme === 'nocturno' ? 'rgb(36, 40, 46)' : 'rgb(255, 255, 255)') ||
        editor.control !== (theme === 'nocturno' ? 'rgb(24, 27, 32)' : 'rgb(255, 255, 255)')) {
        throw new Error(`The ${theme} gateway register editor lost its theme.`);
      }
      await evaluate('new Promise((resolve) => requestAnimationFrame(() => requestAnimationFrame(resolve)))');
      const screenshot = await send('Page.captureScreenshot', { format: 'png', captureBeyondViewport: false });
      const file = path.join(outputDirectory, `admin-gateway-dialog-${theme}-${width}x${height}.png`);
      await writeFile(file, Buffer.from(screenshot.data, 'base64'));
      console.log(JSON.stringify({ ...editor, screenshot: file }));
    }
  }
  if (inspectOrganizationDialog) {
    await evaluate(`(() => {
      document.documentElement.dataset.visualTheme = 'original';
      document.querySelector('app-organization-page .company-row button').click();
      return true;
    })()`);
    await until(() => evaluate(`(() => {
      const panel = document.querySelector('.admin-detail-dialog[open] .branches-panel');
      return panel && panel.getAttribute('aria-busy') === 'false';
    })()`));
    for (const theme of ['original', 'nocturno']) {
      const branches = await evaluate(`(() => {
        document.documentElement.dataset.visualTheme = ${JSON.stringify(theme)};
        const panel = document.querySelector('.admin-detail-dialog[open] .branches-panel');
        const selected = document.querySelector('.company-row.selected-row');
        return {
          theme: document.documentElement.dataset.visualTheme,
          title: panel.querySelector('h2')?.textContent.trim(),
          surface: getComputedStyle(panel).backgroundColor,
          selected: selected && getComputedStyle(selected).backgroundColor,
          rows: panel.querySelectorAll('.branch-row').length,
        };
      })()`);
      if (branches.title !== 'Sucursales' || branches.selected !==
        (theme === 'nocturno' ? 'rgb(72, 57, 43)' : 'rgb(220, 238, 237)')) {
        throw new Error(`The ${theme} branch detail or selection lost its theme.`);
      }
      await evaluate('new Promise((resolve) => requestAnimationFrame(() => requestAnimationFrame(resolve)))');
      const screenshot = await send('Page.captureScreenshot', { format: 'png', captureBeyondViewport: false });
      const file = path.join(outputDirectory, `admin-organization-dialog-${theme}-${width}x${height}.png`);
      await writeFile(file, Buffer.from(screenshot.data, 'base64'));
      console.log(JSON.stringify({ ...branches, screenshot: file }));
    }
  }
  if (inspectTaxEditor) {
    await until(() => evaluate("Boolean(document.querySelector('.history-panel tbody tr button'))"));
    await evaluate(`(() => {
      document.documentElement.dataset.visualTheme = 'original';
      document.querySelector('.history-panel tbody tr button').click();
      return true;
    })()`);
    await until(() => evaluate("Boolean(document.querySelector('.admin-detail-dialog[open] #tax-treatment'))"));
    for (const theme of ['original', 'nocturno']) {
      const editor = await evaluate(`(() => {
        document.documentElement.dataset.visualTheme = ${JSON.stringify(theme)};
        const section = document.querySelector('.admin-detail-dialog[open] .product-tax-editor');
        return {
          theme: document.documentElement.dataset.visualTheme,
          title: section.querySelector('h2')?.textContent.trim(),
          surface: getComputedStyle(section).backgroundColor,
          control: getComputedStyle(section.querySelector('#tax-treatment')).backgroundColor,
          treatment: section.querySelector('#tax-treatment').value,
        };
      })()`);
      if (!editor.title || editor.control !==
        (theme === 'nocturno' ? 'rgb(24, 27, 32)' : 'rgb(255, 255, 255)')) {
        throw new Error(`The ${theme} tax editor lost its treatment or control theme.`);
      }
      await evaluate('new Promise((resolve) => requestAnimationFrame(() => requestAnimationFrame(resolve)))');
      const screenshot = await send('Page.captureScreenshot', { format: 'png', captureBeyondViewport: false });
      const file = path.join(outputDirectory, `admin-tax-editor-${theme}-${width}x${height}.png`);
      await writeFile(file, Buffer.from(screenshot.data, 'base64'));
      console.log(JSON.stringify({ ...editor, screenshot: file }));
    }
  }
  if (inspectBarcodePreview) {
    await evaluate("document.querySelector('.barcode-form').requestSubmit(); true");
    await until(() => evaluate("Boolean(document.querySelector('.barcode-result h3'))"));
    for (const theme of ['original', 'nocturno']) {
      const result = await evaluate(`(() => {
        document.documentElement.dataset.visualTheme = ${JSON.stringify(theme)};
        const output = document.querySelector('.barcode-result:has(h3)');
        return {
          theme: document.documentElement.dataset.visualTheme,
          weight: output.querySelector('strong')?.textContent.trim(),
          surface: getComputedStyle(output).backgroundColor,
          fields: output.querySelectorAll('dl div').length,
          savedProfiles: document.querySelectorAll('.saved-profile').length,
        };
      })()`);
      if (!result.weight || result.fields < 3 || result.surface !==
        (theme === 'nocturno' ? 'rgb(45, 50, 57)' : 'rgb(246, 248, 247)')) {
        throw new Error(`The ${theme} barcode preview lost its data or result theme.`);
      }
      await evaluate("document.querySelector('.barcode-result:has(h3)').scrollIntoView({ block: 'center' }); true");
      await evaluate('new Promise((resolve) => requestAnimationFrame(() => requestAnimationFrame(resolve)))');
      const screenshot = await send('Page.captureScreenshot', { format: 'png', captureBeyondViewport: false });
      const file = path.join(outputDirectory, `admin-barcode-preview-${theme}-${width}x${height}.png`);
      await writeFile(file, Buffer.from(screenshot.data, 'base64'));
      console.log(JSON.stringify({ ...result, screenshot: file }));
    }
  }
  if (inspectStockDialog) {
    await evaluate(`(() => {
      document.documentElement.dataset.visualTheme = 'original';
      document.querySelector('.stock-row button').click();
      return true;
    })()`);
    await until(() => evaluate("Boolean(document.querySelector('.admin-detail-dialog[open] #stock-delta'))"));
    for (const theme of ['original', 'nocturno']) {
      const adjustment = await evaluate(`(() => {
        document.documentElement.dataset.visualTheme = ${JSON.stringify(theme)};
        const dialog = document.querySelector('.admin-detail-dialog[open]');
        return {
          theme: document.documentElement.dataset.visualTheme,
          title: dialog.querySelector('h2')?.textContent.trim(),
          surface: getComputedStyle(dialog).backgroundColor,
          amount: dialog.querySelector('#stock-delta').value,
          optionalReason: !dialog.querySelector('#stock-reason').required,
        };
      })()`);
      if (!adjustment.title || !adjustment.optionalReason || adjustment.surface !==
        (theme === 'nocturno' ? 'rgb(36, 40, 46)' : 'rgb(237, 237, 237)')) {
        throw new Error(`The ${theme} stock adjustment dialog lost its state or theme.`);
      }
      await evaluate('new Promise((resolve) => requestAnimationFrame(() => requestAnimationFrame(resolve)))');
      const screenshot = await send('Page.captureScreenshot', { format: 'png', captureBeyondViewport: false });
      const file = path.join(outputDirectory, `admin-stock-dialog-${theme}-${width}x${height}.png`);
      await writeFile(file, Buffer.from(screenshot.data, 'base64'));
      console.log(JSON.stringify({ ...adjustment, screenshot: file }));
    }
  }
  if (inspectHistoryDialog) {
    await until(() => evaluate("Boolean(document.querySelector('app-history-page tbody tr button'))"));
    await evaluate("document.querySelector('app-history-page tbody tr button').click(); true");
    await until(() => evaluate("Boolean(document.querySelector('app-history-page .ticket-detail ul li'))"));
    for (const theme of ['original', 'nocturno']) {
      const ticket = await evaluate(`(() => {
        document.documentElement.dataset.visualTheme = ${JSON.stringify(theme)};
        const dialog = document.querySelector('app-history-page .admin-detail-dialog[open]');
        const detail = dialog.querySelector('.ticket-detail');
        return {
          theme: document.documentElement.dataset.visualTheme,
          title: detail.querySelector('h2')?.textContent.trim(),
          summary: detail.querySelector('p')?.textContent.trim().slice(0, 80),
          surface: getComputedStyle(detail).backgroundColor,
          lines: detail.querySelectorAll('li').length,
        };
      })()`);
      if (!ticket.title || ticket.lines < 1 || ticket.surface !==
        (theme === 'nocturno' ? 'rgb(36, 40, 46)' : 'rgb(255, 255, 255)')) {
        throw new Error(`The ${theme} read-only ticket detail lost its state or surface.`);
      }
      await evaluate('new Promise((resolve) => requestAnimationFrame(() => requestAnimationFrame(resolve)))');
      const screenshot = await send('Page.captureScreenshot', { format: 'png', captureBeyondViewport: false });
      const file = path.join(outputDirectory, `admin-history-dialog-${theme}-${width}x${height}.png`);
      await writeFile(file, Buffer.from(screenshot.data, 'base64'));
      console.log(JSON.stringify({ ...ticket, screenshot: file }));
    }
    await evaluate("document.querySelector('app-history-page .ticket-detail button').click(); true");
    for (const [tab, label] of [
      ['shifts', 'Turnos'], ['cash', 'Caja y pagos'], ['stock', 'Movimientos de stock'],
    ]) {
      await evaluate(`(() => {
        const button = Array.from(document.querySelectorAll('app-history-page .history-tabs button'))
          .find((candidate) => candidate.textContent.trim() === ${JSON.stringify(label)});
        button.click();
        return true;
      })()`);
      await until(() => evaluate(`(() => {
        const panel = document.querySelector('app-history-page .history-panel');
        return panel.getAttribute('aria-busy') === 'false' &&
          Array.from(panel.querySelectorAll('.history-tabs button'))
            .find((button) => button.textContent.trim() === ${JSON.stringify(label)})
            ?.getAttribute('aria-current') === 'page';
      })()`));
      await evaluate("document.querySelector('app-history-page .history-tabs').scrollIntoView({ block: 'start' }); true");
      for (const theme of ['original', 'nocturno']) {
        const movements = await evaluate(`(() => {
          document.documentElement.dataset.visualTheme = ${JSON.stringify(theme)};
          const panel = document.querySelector('app-history-page .history-panel');
          return {
            theme: document.documentElement.dataset.visualTheme,
            tab: ${JSON.stringify(tab)},
            rows: panel.querySelectorAll('tbody tr').length,
            firstRow: panel.querySelector('tbody tr')?.textContent.trim().slice(0, 160),
            empty: Boolean(panel.querySelector('.state-message')),
            scrollWidth: document.documentElement.scrollWidth,
          };
        })()`);
        if (movements.scrollWidth > width) throw new Error(`The ${theme} ${tab} history overflows.`);
        await evaluate('new Promise((resolve) => requestAnimationFrame(() => requestAnimationFrame(resolve)))');
        const screenshot = await send('Page.captureScreenshot', { format: 'png', captureBeyondViewport: false });
        const file = path.join(outputDirectory, `admin-history-${tab}-${theme}-${width}x${height}.png`);
        await writeFile(file, Buffer.from(screenshot.data, 'base64'));
        console.log(JSON.stringify({ ...movements, screenshot: file }));
      }
    }
  }
  if (inspectAccountsDialog) {
    await until(() => evaluate("Boolean(document.querySelector('app-accounts-page tbody tr button'))"));
    await evaluate("document.querySelector('app-accounts-page tbody tr button').click(); true");
    await until(() => evaluate("Boolean(document.querySelector('app-accounts-page .account-totals'))"));
    for (const theme of ['original', 'nocturno']) {
      const account = await evaluate(`(() => {
        document.documentElement.dataset.visualTheme = ${JSON.stringify(theme)};
        const dialog = document.querySelector('app-accounts-page .admin-detail-dialog[open]');
        const detail = dialog.querySelector('.account-detail-panel');
        return {
          theme: document.documentElement.dataset.visualTheme,
          title: detail.querySelector('h2')?.textContent.trim(),
          totals: Array.from(detail.querySelectorAll('.account-totals strong')).map((item) => item.textContent.trim()),
          surface: getComputedStyle(detail.querySelector('.account-totals div')).backgroundColor,
          correctionAvailable: Boolean(detail.querySelector('button')?.textContent.includes('Corregir')),
        };
      })()`);
      if (!account.title || account.totals.length !== 3 || account.surface !==
        (theme === 'nocturno' ? 'rgb(45, 50, 57)' : 'rgb(238, 241, 236)')) {
        throw new Error(`The ${theme} account statement lost its totals or theme.`);
      }
      await evaluate('new Promise((resolve) => requestAnimationFrame(() => requestAnimationFrame(resolve)))');
      const screenshot = await send('Page.captureScreenshot', { format: 'png', captureBeyondViewport: false });
      const file = path.join(outputDirectory, `admin-accounts-dialog-${theme}-${width}x${height}.png`);
      await writeFile(file, Buffer.from(screenshot.data, 'base64'));
      console.log(JSON.stringify({ ...account, screenshot: file }));
    }
  }
} finally {
  try { if (send && socket?.readyState === WebSocket.OPEN) await send('Browser.close'); } catch { /* closing */ }
  socket?.close();
  chrome.kill();
  try { await until(() => chrome.exitCode !== null || chrome.signalCode !== null, 3000); } catch { /* exit in progress */ }
  if (path.dirname(profileDirectory) === tmpdir() && path.basename(profileDirectory).startsWith('carnicerias-theme-')) {
    await until(async () => {
      try { await rm(profileDirectory, { recursive: true, force: true }); return true; }
      catch { return null; }
    }, 5000);
  }
}
