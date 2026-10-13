# 0001 — Estado OAuth de Clover en la base existente

**Estado:** aceptada para el desarrollo sandbox (13/10/2026). La migración se aplicó a `carnicerias_test_visual`; la autorización real del comercio sigue pendiente.

## Contexto

Clover exige OAuth v2 con access/refresh token que vencen. La administración local usa la API de `localhost:5197`, mientras Cloudflare Tunnel dirige el callback HTTPS a otra instancia de la API en `127.0.0.1:5198`. Un `state` guardado solo en memoria no sobreviviría al retorno entre procesos ni impediría su reutilización tras reiniciar.

## Decisión

- Guardar en `carnicerias_test_visual` un hash SHA-256 de cada `state` aleatorio de 256 bits, con empresa, administrador, vencimiento de diez minutos y consumo atómico. Nunca persistir el `state` en claro.
- Validar en el retorno el `state`, el comercio configurado y que el usuario asociado siga activo y tenga `organization.manage` en la empresa. Consumir el intento antes de intercambiar el código; un fallo requiere iniciar uno nuevo.
- Enviar código y App Secret solo a `https://apisandbox.dev.clover.com/oauth/v2/token`, sin seguir redirecciones HTTP. Guardar App Secret y tokens con ASP.NET Data Protection bajo un propósito exclusivo de Clover; entregar al frontend solo presencia y vencimiento.
- Restringir el proceso público a las cuatro rutas `/clover*` declaradas y limitar la frecuencia de launch/callback. El POS no usa todavía estos tokens ni permite un pago Clover.

## Alternativas y consecuencias

Un `state` solo en memoria era más simple, pero no funciona con dos procesos ni reinicios. Un `state` firmado sin persistencia evita una tabla, pero no da consumo único ni revocación. La tabla aditiva permite ambas garantías sin otra base de datos.

Las claves predeterminadas de Data Protection se comparten entre los dos procesos **solo si ejecutan bajo el mismo usuario/perfil de Windows**. El despliegue en servidor deberá fijar un anillo de claves persistente y compartido, con protección de acceso y respaldo. La rotación de refresh token de un solo uso y los cobros Cloud Pay Display son trabajos posteriores; “OAuth conectado” no equivale a “terminal lista para cobrar”.
