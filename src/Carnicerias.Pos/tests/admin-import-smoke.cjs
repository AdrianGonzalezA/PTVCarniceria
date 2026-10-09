// Development-only smoke helper: authenticates through the local API, never prints credentials or cookies.
const port = Number(process.argv.find((value) => value.startsWith('--port='))?.slice(7) ?? 9224);
const target = process.argv.find((value) => value.startsWith('--navigate='))?.slice(11)
  ?? 'app://bundle/admin/categories';

async function connect() {
  const pages = await fetch(`http://localhost:${port}/json/list`).then((response) => response.json());
  const page = pages.find((item) => item.type === 'page' && item.url.startsWith('app://bundle/'));
  if (!page) throw new Error('No se encontró la ventana Electron de desarrollo.');
  const socket = new WebSocket(page.webSocketDebuggerUrl);
  await new Promise((resolve, reject) => {
    socket.addEventListener('open', resolve, { once: true });
    socket.addEventListener('error', reject, { once: true });
  });
  let nextId = 0;
  const send = (method, params = {}) => new Promise((resolve, reject) => {
    const id = ++nextId;
    const onMessage = (event) => {
      const result = JSON.parse(event.data);
      if (result.id !== id) return;
      socket.removeEventListener('message', onMessage);
      if (result.error) reject(new Error(result.error.message));
      else resolve(result.result);
    };
    socket.addEventListener('message', onMessage);
    socket.send(JSON.stringify({ id, method, params }));
  });
  return { socket, send };
}

async function main() {
  const credential = process.env.CARNICERIAS_TEST_USER;
  const password = process.env.CARNICERIAS_TEST_PASSWORD;
  if (!credential || !password) throw new Error('Configurá CARNICERIAS_TEST_USER y CARNICERIAS_TEST_PASSWORD.');
  const login = await fetch('http://127.0.0.1:5197/api/sessions', {
    method: 'POST', headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ credential, password }),
  });
  if (!login.ok) throw new Error(`No se pudo iniciar la sesión de prueba (HTTP ${login.status}).`);
  const cookie = login.headers.get('set-cookie')?.match(/^carnicerias\.session=([^;]+)/)?.[1];
  if (!cookie) throw new Error('La API no devolvió la cookie de sesión.');
  const cookieHeader = { Cookie: `carnicerias.session=${cookie}` };
  const contexts = await fetch('http://127.0.0.1:5197/api/operational-contexts',
    { headers: cookieHeader }).then((response) => response.json());
  if (contexts.length !== 1 || contexts[0].branches.length !== 1)
    throw new Error('Se esperaba un único contexto de prueba; elegí la sucursal manualmente.');
  const selected = await fetch('http://127.0.0.1:5197/api/sessions/current/context', {
    method: 'PUT', headers: { ...cookieHeader, 'Content-Type': 'application/json' },
    body: JSON.stringify({ companyId: contexts[0].companyId, branchId: contexts[0].branches[0].branchId }),
  });
  if (!selected.ok) throw new Error(`No se pudo fijar el contexto (HTTP ${selected.status}).`);

  const { socket, send } = await connect();
  try {
    const set = await send('Network.setCookie', {
      name: 'carnicerias.session', value: cookie, url: 'http://localhost:5197/api',
      path: '/api', secure: true, httpOnly: true, sameSite: 'Strict',
    });
    if (!set.success) throw new Error('Electron rechazó la cookie de sesión.');
    await send('Page.navigate', { url: target });
    await new Promise((resolve) => setTimeout(resolve, 1200));
    const state = await send('Runtime.evaluate', {
      expression: `({ location: location.href, headings: [...document.querySelectorAll('h1,h2')]
        .map(item => item.textContent?.trim()), text: document.body.innerText.slice(0, 500) })`,
      returnByValue: true,
    });
    console.log(JSON.stringify(state.result.value, null, 2));
  } finally { socket.close(); }
}

main().catch((error) => { console.error(error.message); process.exitCode = 1; });
