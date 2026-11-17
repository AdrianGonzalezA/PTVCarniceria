interface QrPreview {
  readonly payload: string;
  readonly imageUrl: string;
  readonly companyName: string;
  readonly catalogCount: number;
  readonly items: readonly { plu: string; weightKg: number; productName: string }[];
}

interface EmulatorUiBridge {
  getState(): Promise<{ weightKg: number; stable: boolean }>;
  setWeight(weightKg: number, stable: boolean): Promise<{ weightKg: number; stable: boolean }>;
  makeRandomKretzQr(ticketNumber: number, scaleNumber: number): Promise<QrPreview>;
  simulateWeight(): Promise<{ weightKg: number; stable: boolean }>;
  copyKretzQr(payload: string): Promise<void>;
}

const bridge = (window as unknown as Window & { carniceriasEmulator: EmulatorUiBridge }).carniceriasEmulator;
const form = document.querySelector<HTMLFormElement>('#weight-form')!;
const weight = document.querySelector<HTMLInputElement>('#weight')!;
const stable = document.querySelector<HTMLInputElement>('#stable')!;
const weightAuto = document.querySelector<HTMLInputElement>('#weight-auto')!;
const statusElement = document.querySelector<HTMLElement>('#status')!;
const kretzItems = document.querySelector<HTMLOListElement>('#kretz-items')!;
const kretzGenerate = document.querySelector<HTMLButtonElement>('#kretz-generate')!;
const kretzCopy = document.querySelector<HTMLButtonElement>('#kretz-copy')!;
const kretzStatus = document.querySelector<HTMLElement>('#kretz-status')!;
const kretzCompany = document.querySelector<HTMLElement>('#kretz-company')!;
const kretzAuto = document.querySelector<HTMLInputElement>('#kretz-auto')!;
const kretzImage = document.querySelector<HTMLImageElement>('#kretz-image')!;
const kretzPayload = document.querySelector<HTMLTextAreaElement>('#kretz-payload')!;
let nextTicket = crypto.getRandomValues(new Uint16Array(1))[0]! % 10000;
let qrBusy = false;

function renderQr(preview: QrPreview, ticketNumber: number): void {
  kretzItems.replaceChildren(...preview.items.map(cut => {
    const item = document.createElement('li');
    item.textContent = `${cut.productName} · PLU ${cut.plu} · ` +
      `${cut.weightKg.toLocaleString('es-AR', { minimumFractionDigits: 3, maximumFractionDigits: 3 })} kg`;
    return item;
  }));
  kretzCompany.textContent = `${preview.companyName} · ${preview.catalogCount} artículos pesables con PLU`;
  kretzPayload.value = preview.payload;
  kretzPayload.hidden = false;
  kretzImage.src = preview.imageUrl;
  kretzImage.hidden = false;
  kretzCopy.disabled = false;
  kretzStatus.textContent = `Ticket ${ticketNumber}: QR generado con ${preview.items.length} productos. ` +
    'Escaneá la imagen o copiá su lectura.';
}

async function generateQr(): Promise<void> {
  if (qrBusy) return;
  qrBusy = true;
  kretzGenerate.disabled = true;
  try {
    const ticketNumber = nextTicket;
    const preview = await bridge.makeRandomKretzQr(ticketNumber, 1);
    renderQr(preview, ticketNumber);
    nextTicket = (nextTicket + 1) % 10000;
  } catch {
    kretzStatus.textContent = 'No se pudo leer el catálogo de la base de desarrollo. ' +
      'El simulador reintentará automáticamente.';
  } finally {
    qrBusy = false;
    kretzGenerate.disabled = false;
  }
}

kretzGenerate.addEventListener('click', () => { void generateQr(); });
kretzCopy.addEventListener('click', () => {
  void bridge.copyKretzQr(kretzPayload.value)
    .then(() => { kretzStatus.textContent = 'Lectura del QR copiada.'; })
    .catch(() => { kretzStatus.textContent = 'No se pudo copiar la lectura.'; });
});
kretzAuto.addEventListener('change', () => {
  if (kretzAuto.checked) void generateQr();
});

async function refresh(): Promise<void> {
  const state = await bridge.getState();
  statusElement.textContent = state.stable
    ? `Balanza estable: ${state.weightKg.toLocaleString('es-AR', { minimumFractionDigits: 3, maximumFractionDigits: 3 })} kg`
    : 'Balanza sin lectura estable';
}

async function simulateWeight(): Promise<void> {
  if (!weightAuto.checked) return;
  const state = await bridge.simulateWeight();
  weight.value = String(state.weightKg);
  stable.checked = true;
  await refresh();
}

weightAuto.addEventListener('change', () => {
  if (weightAuto.checked) void simulateWeight();
});
form.addEventListener('submit', (event) => {
  event.preventDefault();
  const quantity = Number(weight.value.replace(',', '.'));
  void bridge.setWeight(quantity, stable.checked).then(() => refresh()).catch(() => {
    statusElement.textContent = 'Ingresá un peso entre 0 y 10.000 kg con hasta tres decimales.';
  });
});

void refresh();
void generateQr();
void simulateWeight();
setInterval(() => { if (kretzAuto.checked) void generateQr(); }, 30_000);
setInterval(() => { void simulateWeight().catch(() => {
  statusElement.textContent = 'Emulador sin conexión';
}); }, 5_000);
setInterval(() => { void refresh().catch(() => {
  statusElement.textContent = 'Emulador sin conexión';
}); }, 1000);
