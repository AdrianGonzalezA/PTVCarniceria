import net from 'node:net';

export const virtualDevicePipe = process.platform === 'win32'
  ? String.raw`\\.\pipe\carnicerias-pos-devices-v1`
  : '/tmp/carnicerias-pos-devices-v1.sock';

export interface ScaleReading {
  readonly weightKg: number;
  readonly stable: boolean;
  readonly observedAtUtc: string;
}

export type VirtualDeviceRequest = { readonly action: 'read-scale' };

export type VirtualDeviceResponse =
  | { readonly ok: true; readonly reading: ScaleReading }
  | { readonly ok: false; readonly error: string };

const maximumFrameBytes = 64 * 1024;
const timeoutMs = 5000;

export function createVirtualDeviceServer(
  handler: (request: VirtualDeviceRequest) => Promise<VirtualDeviceResponse>): net.Server {
  return net.createServer((socket) => {
    socket.setTimeout(timeoutMs, () => socket.destroy());
    let frame = Buffer.alloc(0);
    socket.on('data', (chunk: Buffer) => {
      frame = Buffer.concat([frame, chunk]);
      if (frame.length > maximumFrameBytes) {
        socket.destroy();
        return;
      }
      const end = frame.indexOf(10);
      if (end < 0) return;
      socket.pause();
      let request: VirtualDeviceRequest;
      try {
        const parsed: unknown = JSON.parse(frame.subarray(0, end).toString('utf8'));
        if (!parsed || typeof parsed !== 'object' ||
            !('action' in parsed) ||
            parsed.action !== 'read-scale')
          throw new Error('Invalid device action');
        request = parsed as VirtualDeviceRequest;
      } catch {
        socket.end(JSON.stringify({ ok: false, error: 'INVALID_REQUEST' }) + '\n');
        return;
      }
      void handler(request).then((response) => socket.end(JSON.stringify(response) + '\n'))
        .catch(() => socket.end(JSON.stringify({ ok: false, error: 'DEVICE_ERROR' }) + '\n'));
    });
  });
}

export function requestVirtualDevice(pipe: string,
  request: VirtualDeviceRequest): Promise<VirtualDeviceResponse> {
  return new Promise((resolve, reject) => {
    const socket = net.createConnection(pipe);
    let settled = false;
    let frame = Buffer.alloc(0);
    const fail = (message: string) => {
      if (settled) return;
      settled = true;
      socket.destroy();
      reject(new Error(message));
    };
    socket.setTimeout(timeoutMs, () => fail('VIRTUAL_DEVICE_TIMEOUT'));
    socket.once('connect', () => socket.write(JSON.stringify(request) + '\n'));
    socket.on('data', (chunk: Buffer) => {
      frame = Buffer.concat([frame, chunk]);
      if (frame.length > maximumFrameBytes) {
        fail('VIRTUAL_DEVICE_INVALID_RESPONSE');
        return;
      }
      const end = frame.indexOf(10);
      if (end < 0) return;
      try {
        const response: unknown = JSON.parse(frame.subarray(0, end).toString('utf8'));
        if (!response || typeof response !== 'object' || !('ok' in response) ||
            typeof response.ok !== 'boolean') throw new Error('Invalid response');
        settled = true;
        socket.end();
        resolve(response as VirtualDeviceResponse);
      } catch {
        fail('VIRTUAL_DEVICE_INVALID_RESPONSE');
      }
    });
    socket.once('error', () => fail('VIRTUAL_DEVICE_UNAVAILABLE'));
    socket.once('close', () => {
      if (!settled) fail('VIRTUAL_DEVICE_DISCONNECTED');
    });
  });
}
