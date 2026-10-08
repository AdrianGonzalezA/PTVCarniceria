interface EmulatorUiBridge {
  getState(): Promise<{ weightKg: number; stable: boolean;
    printed: readonly { saleId: string; total: number; path: string }[] }>;
  setWeight(weightKg: number, stable: boolean): Promise<{ weightKg: number; stable: boolean }>;
}

const bridge = (window as unknown as Window & { carniceriasEmulator: EmulatorUiBridge }).carniceriasEmulator;
const form = document.querySelector<HTMLFormElement>('#weight-form')!;
const weight = document.querySelector<HTMLInputElement>('#weight')!;
const stable = document.querySelector<HTMLInputElement>('#stable')!;
const statusElement = document.querySelector<HTMLElement>('#status')!;
const tickets = document.querySelector<HTMLOListElement>('#tickets')!;

async function refresh(): Promise<void> {
  const state = await bridge.getState();
  statusElement.textContent = state.stable
    ? `Balanza estable: ${state.weightKg.toLocaleString('es-AR', { minimumFractionDigits: 3, maximumFractionDigits: 3 })} kg`
    : 'Balanza sin lectura estable';
  tickets.replaceChildren();
  for (const ticket of state.printed) {
    const row = document.createElement('li');
    const title = document.createElement('strong');
    title.textContent = `${ticket.saleId} · ${ticket.total.toLocaleString('es-AR', { style: 'currency', currency: 'ARS' })}`;
    const location = document.createElement('small');
    location.textContent = ticket.path;
    row.append(title, location);
    tickets.append(row);
  }
}

form.addEventListener('submit', (event) => {
  event.preventDefault();
  const quantity = Number(weight.value.replace(',', '.'));
  void bridge.setWeight(quantity, stable.checked).then(() => refresh()).catch(() => {
    statusElement.textContent = 'Ingresá un peso entre 0 y 10.000 kg con hasta tres decimales.';
  });
});

void refresh();
setInterval(() => { void refresh().catch(() => { statusElement.textContent = 'Emulador sin conexión'; }); }, 1000);
