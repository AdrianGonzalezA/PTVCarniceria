# Diagnóstico de ARCA en homologación

Esta herramienta pide un ticket WSAA y consulta `FEParamGetPtosVenta` de WSFEv1. **No solicita CAE ni emite comprobantes.** Solo utiliza los endpoints de homologación y nunca imprime `token`, `sign`, el PFX ni el cuerpo SOAP. Carga la clave del PFX de modo efímero en memoria.

Configurar en la sesión local `ARCA_HOMO_PFX_PATH` con la ruta al PFX, `ARCA_HOMO_CUIT` con la CUIT representada autorizada en WSASS y, solo si corresponde, `ARCA_HOMO_PFX_PASSWORD`. Después ejecutar:

```powershell
dotnet run --project tools/Carnicerias.ArcaProbe/Carnicerias.ArcaProbe.csproj
```

No guardar estos valores ni tickets en Git. La herramienta aún no conserva el ticket entre ejecuciones: ARCA puede devolver `coe.alreadyAuthenticated` si se vuelve a pedir mientras el anterior sigue vigente (aproximadamente 12 horas). En ese caso no insistir; esperar su vencimiento. Para uso continuo se deberá agregar un proveedor de tickets con almacenamiento seguro y renovación controlada. [Manual WSAA, sección 10.6](https://www.arca.gob.ar/ws/WSAA/WSAAmanualDev.pdf), [WSFEv1, `FEParamGetPtosVenta`](https://www.arca.gob.ar/ws/documentacion/manuales/manual-desarrollador-ARCA-COMPG.pdf).
