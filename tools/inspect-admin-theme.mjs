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
if (!/^\/admin(?:\/(?:configuracion|categories))?$/.test(route)) {
  throw new Error('Only the initial administration routes are supported.');
}
const viewport = /^--viewport=(\d{3,4})x(\d{3,4})$/.exec(
  process.argv.find((argument) => argument.startsWith('--viewport=')) ?? '--viewport=1366x768',
);
if (!viewport) throw new Error('Use --viewport=WIDTHxHEIGHT.');
const width = Number(viewport[1]);
const height = Number(viewport[2]);
const expectedTab = route === '/admin/categories' ? '/admin/configuracion' : route;
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

  await send('Page.navigate', { url: `http://localhost:4200${route}` });
  await until(() => evaluate(`Boolean(document.querySelector(
    ${JSON.stringify(route === '/admin/categories' ? 'app-categories-page .category-panel tbody tr' : 'app-admin-page .module-panel')}
  ))`));
  await mkdir(outputDirectory, { recursive: true });
  for (const theme of ['original', 'nocturno']) {
    const styles = await evaluate(`(() => {
      document.documentElement.dataset.visualTheme = ${JSON.stringify(theme)};
      const host = document.querySelector('app-admin-page, app-categories-page');
      const header = host.querySelector('.app-header');
      const panel = host.querySelector('.module-panel, .category-panel');
      const brandMark = host.querySelector('.brand-mark');
      const activeTab = host.querySelector('.area-tabs a[aria-current="page"]');
      const tableHead = host.querySelector('thead th');
      const search = host.querySelector('#category-search');
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
        tableRows: host.querySelectorAll('tbody tr').length,
        documentScrollWidth: document.documentElement.scrollWidth,
        viewportWidth: window.innerWidth,
      };
    })()`);
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
    if (keyboardFocus.href !== expectedTab || !keyboardFocus.visible || keyboardFocus.outline !==
      (theme === 'nocturno' ? 'rgb(229, 178, 127)' : 'rgb(84, 194, 189)')) {
      throw new Error(`The ${theme} active administration tab has no keyboard-visible focus.`);
    }
    styles.keyboardFocus = keyboardFocus;
    if (route === '/admin/categories' && (styles.tableHead !==
      (theme === 'nocturno' ? 'rgb(72, 57, 43)' : 'rgb(0, 139, 139)') ||
      styles.search !== (theme === 'nocturno' ? 'rgb(24, 27, 32)' : 'rgb(255, 255, 255)'))) {
      throw new Error(`The ${theme} shared ABM controls did not receive their theme.`);
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
