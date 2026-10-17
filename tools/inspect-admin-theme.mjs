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
if (!/^\/admin(?:\/configuracion)?$/.test(route)) throw new Error('Only admin landing routes are supported.');
const outputDirectory = process.argv[3] ?? path.join(tmpdir(), 'carnicerias-admin-theme');
const chromePath = process.env.CARNICERIAS_TEST_CHROME_PATH ??
  'C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe';
const profileDirectory = await mkdtemp(path.join(tmpdir(), 'carnicerias-theme-'));
const chrome = spawn(chromePath, [
  '--headless=new', '--no-first-run', '--no-default-browser-check',
  '--disable-background-networking', '--remote-debugging-port=0',
  `--user-data-dir=${profileDirectory}`, '--window-size=1366,768', 'about:blank',
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
    width: 1366, height: 768, deviceScaleFactor: 1, mobile: false,
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
  await until(() => evaluate("Boolean(document.querySelector('app-admin-page .module-panel'))"));
  await mkdir(outputDirectory, { recursive: true });
  for (const theme of ['original', 'nocturno']) {
    const styles = await evaluate(`(() => {
      document.documentElement.dataset.visualTheme = ${JSON.stringify(theme)};
      const host = document.querySelector('app-admin-page');
      const header = host.querySelector('.app-header');
      const panel = host.querySelector('.module-panel');
      const brandMark = host.querySelector('.brand-mark');
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
      };
    })()`);
    if (styles.brandMarkWidth !== 72 || styles.brandMark !==
      (theme === 'nocturno' ? 'rgb(229, 178, 127)' : 'rgb(245, 245, 245)')) {
      throw new Error(`The ${theme} administrative brand mark is overridden by another component.`);
    }
    await evaluate('new Promise((resolve) => requestAnimationFrame(() => requestAnimationFrame(resolve)))');
    const screenshot = await send('Page.captureScreenshot', { format: 'png', captureBeyondViewport: false });
    const file = path.join(outputDirectory, `${route.slice(1).replaceAll('/', '-')}-${theme}.png`);
    await writeFile(file, Buffer.from(screenshot.data, 'base64'));
    console.log(JSON.stringify({ ...styles, screenshot: file }));
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
