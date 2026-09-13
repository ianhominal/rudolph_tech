# Rudolph Tech

Aplicación de bandeja (el iconito al lado del reloj) para la PC de la oficina de Rudolph
Electronics. Se encarga sola del relevamiento de la competencia por relevancia en Mercado Libre y
sube los resultados a la aplicación web, sin tareas programadas de Windows ni carpetas con archivos
.bat.

Reemplaza al zip que antes había que descomprimir a mano y "instalar" con doble clic.

## Por qué corre en la PC y no en la nube

Mercado Libre bloquea a los navegadores ocultos, así que el relevamiento necesita abrir una ventana
de Google Chrome de verdad. Una ventana de verdad necesita una sesión de Windows con escritorio, y
eso es justo lo que un servicio de Windows no tiene. Por eso Rudolph Tech vive en la bandeja del
sistema, dentro de la sesión de la persona que usa la PC.

## Qué hace falta

- Windows 10 o Windows 11 (64 bits).
- Google Chrome instalado. Es lo único que hay que tener aparte.
- La contraseña de la aplicación web.

No hace falta instalar Node ni nada más: el instalador ya trae todo adentro.

## Cómo se instala

1. Entrar a la sección **Releases** del repositorio y bajar el archivo
   `RudolphTech-<version>-setup.exe` de la última versión.
2. Ejecutarlo. Va a pedir la **contraseña del instalador** (la misma que se pasa por privado a quien
   instala; no está escrita en ningún lado del repositorio).
3. Aceptar la instalación. Si se quiere que arranque solo cada vez que se enciende la PC, dejar
   tildado "Iniciar Rudolph Tech al encender la PC".
4. Al abrirse por primera vez aparece la ventana de configuración pidiendo dos cosas:
   - la **dirección de la aplicación**, que ya viene escrita (`https://rudolph-mvp.vercel.app`),
   - la **contraseña de la aplicación web**.
5. Tocar **Iniciar sesión**. La aplicación descarga sola el agente de relevamiento y prepara todo.
   Cuando el renglón de estado dice que el agente quedó al día, ya está listo.

Desde ese momento el iconito queda al lado del reloj y no hay que hacer nada más.

## Qué hace y cuándo

- **Cada 15 minutos** revisa si alguien pidió un relevamiento desde la aplicación web (el botón
  "Actualizar"). Si no hay nada pendiente, no abre Chrome ni molesta a nadie.
- **Todos los días a las 06:45** releva todos los productos configurados. Antes de arrancar vuelve a
  descargar el agente, así los scripts siempre están sincronizados con la aplicación web.
- Nunca corre dos relevamientos al mismo tiempo.
- Cuando termina, avisa con un globito: cuántas publicaciones relevó, o que Mercado Libre pidió una
  verificación, o que hubo un error.

Los dos horarios se cambian desde la ventana de configuración.

## Mientras releva

Se abre una ventana de Google Chrome de verdad y visible. No hay que cerrarla: se ve el progreso
avanzando solo y se cierra sola al terminar. Si esa ventana molesta, en la configuración se puede
tildar **"Chrome fuera de la pantalla"**: la ventana sigue existiendo (nunca oculta, porque Mercado
Libre lo detecta) pero se abre fuera de la vista.

## El menú del iconito

Botón derecho sobre el iconito al lado del reloj:

- **Abrir configuración**: pide la contraseña de la aplicación web y abre la ventana.
- **Relevar ahora**: releva todos los productos en el momento.
- **Pausar** / **Reanudar**: mientras está en pausa no arranca ningún relevamiento automático.
- **Ver registro**: abre la carpeta con los archivos de registro.
- **Salir**: cierra la aplicación. Si hay un relevamiento en curso, pregunta antes.

## Dónde está cada cosa

- Configuración: `%LocalAppData%\RudolphTech\settings.json`
- Agente de relevamiento descargado: `%LocalAppData%\RudolphTech\agent`
- Registro, un archivo por día: `%LocalAppData%\RudolphTech\logs\agent-AAAA-MM-DD.log`

Los registros de más de 30 días se borran solos.

## Seguridad

- Ni el repositorio ni el instalador contienen la dirección de la aplicación como secreto, ni la
  contraseña, ni el token de subida de datos.
- La contraseña de la aplicación web nunca se guarda. Se pide cada vez que se abre la ventana de
  configuración y se verifica contra la aplicación misma. Sin conexión no se puede abrir la ventana.
- El token de subida de datos viene adentro del paquete que descarga la propia aplicación y se
  guarda cifrado con DPAPI, atado a esa cuenta de Windows y a esa PC. El archivo `.env` con el token
  se escribe solamente durante cada relevamiento y se borra apenas termina (y también al arrancar,
  por si una corrida quedó cortada por la mitad).
- Los relevamientos automáticos nunca piden contraseña.

## Cómo se desinstala

Panel de control, "Agregar o quitar programas", Rudolph Tech, Desinstalar. Se borra la aplicación, el
agente descargado y la configuración. Los registros quedan, por si hacen falta.

## Para desarrolladores

Requisitos: .NET SDK 10, PowerShell y Windows. Inno Setup solamente hace falta para armar el
instalador, y de eso se encarga GitHub Actions.

```powershell
dotnet build
dotnet test
powershell -ExecutionPolicy Bypass -File tools/get-node.ps1
dotnet publish src/RudolphTech/RudolphTech.csproj -c Release -r win-x64 --self-contained
```

Estructura:

- `src/RudolphTech.Core`: toda la lógica que se puede testear sin ventanas (decisiones de horario,
  configuración, lectura del archivo de estado del relevamiento, cliente HTTP contra la aplicación
  web, generación del `.env`).
- `src/RudolphTech`: la aplicación WinForms, el iconito de la bandeja y el lanzador de procesos.
- `tests/RudolphTech.Core.Tests`: los tests de xUnit del proyecto Core.
- `tools/get-node.ps1`: descarga el Node portable oficial (LTS, versión fija) y lo deja en
  `build/node`. Verifica el hash publicado por nodejs.org. `node.exe` no se versiona.
- `tools/make-icon.mjs`: genera el icono desde cero, sin usar arte de terceros.
- `installer/RudolphTech.iss`: el script de Inno Setup 6.
- `.github/workflows/build.yml`: compila, testea, publica, arma el instalador y lo sube como
  artefacto. En un tag `v*` además crea la Release con el instalador adjunto.

La contraseña del instalador se pasa como define de Inno Setup
(`iscc /DInstallerPassword=... installer\RudolphTech.iss`) y sale del secreto `INSTALLER_PASSWORD`
del repositorio. Si se compila sin ese define, el instalador queda sin contraseña; sirve para probar
localmente, no para repartir.

La aplicación habla con dos endpoints que ya existen en la aplicación web y que este repositorio no
modifica: `POST /api/ingresar` (igual que el formulario del navegador) y `GET /api/agente/descargar`
(el zip con `meli-survey.mjs`, `meli-lib.mjs`, `package.json` y el `.env` generado).

## Licencia

MIT. Ver [LICENSE](LICENSE).
