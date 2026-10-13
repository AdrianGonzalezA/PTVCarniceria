// Local development helper: attach to the POS Electron renderer launched with
// --remote-debugging-port=9224. Never run against a production register.
import { writeFile } from 'node:fs/promises';

const port = Number(process.env.CARNICERIAS_ELECTRON_CDP_PORT ?? 9224);
if (!Number.isInteger(port) || port < 1024 || port > 65535)
  throw new Error('Invalid local Electron CDP port.');
const targets = await fetch(`http://127.0.0.1:${port}/json/list`).then((response) => response.json());
const page = targets.find((target) => target.type === 'page' && target.url.startsWith('app://bundle/'));
if (!page) throw new Error(`No POS Electron page on local CDP port ${port}.`);

const socket = new WebSocket(page.webSocketDebuggerUrl);
await new Promise((resolve, reject) => {
  socket.addEventListener('open', resolve, { once: true });
  socket.addEventListener('error', reject, { once: true });
});
let nextId = 0;
function send(method, params = {}) {
  const id = ++nextId;
  return new Promise((resolve, reject) => {
    const onMessage = (event) => {
      const message = JSON.parse(event.data);
      if (message.id !== id) return;
      socket.removeEventListener('message', onMessage);
      if (message.error) reject(new Error(message.error.message));
      else resolve(message.result);
    };
    socket.addEventListener('message', onMessage);
    socket.send(JSON.stringify({ id, method, params }));
  });
}

try {
  if (process.argv[2] === '--accept-dialog') {
    await send('Page.handleJavaScriptDialog', { accept: true });
    console.log('Electron dialog accepted.');
  } else if (process.argv[2] === '--screenshot') {
    const targetPath = process.argv[3];
    if (!targetPath) throw new Error('Provide a screenshot output path.');
    const result = await send('Page.captureScreenshot', { format: 'png', captureBeyondViewport: false });
    await writeFile(targetPath, Buffer.from(result.data, 'base64'));
    console.log(`Electron screenshot saved: ${targetPath}`);
  } else {
    const expression = process.argv[2] ?? 'document.body.innerText.slice(0, 1500)';
    const result = await send('Runtime.evaluate', { expression, returnByValue: true, awaitPromise: true });
    if (result.exceptionDetails) throw new Error(result.exceptionDetails.text);
    console.log(JSON.stringify(result.result.value));
  }
} finally {
  socket.close();
}
