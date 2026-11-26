# Rastro AI

[English](README.md) · [Português](README.pt-BR.md) · **Español**

Sigue el rastro de una funcionalidad en tu código .NET, desde la entrada (controller, job, handler) hasta el final (o desde el clic en una pantalla Angular hasta el controller), con todas las bifurcaciones y la implementación real detrás de cada interfaz del DI. Muestra el flujo como un grafo editable y genera el prompt exacto para que la IA modifique ese punto.

Rastro se ejecuta solo en tu máquina: tu código nunca sale de ella.

## Instalar

Requiere el SDK de .NET 10. Node (20 o más reciente) es opcional: solo hace falta para rastrear flujos Angular.

```
dotnet tool install -g rastro-ai
```

## Usar

```
rastro
```

Rastro se abre en su propia ventana, como una aplicación, y la pantalla también queda disponible en http://127.0.0.1:7450 para el navegador (solo en esta máquina; cambia el puerto con `--porta`). Cerrar la ventana cierra Rastro; `rastro --sem-janela` (o `--no-window`) inicia solo la consola y la dirección. En la ventana, el campo de la carpeta del repositorio incluye el botón "Examinar…", que abre el cuadro "Elegir carpeta" del sistema.

La ventana usa el WebView del sistema: en Windows, el WebView2 Runtime (ya viene en Windows 11 y en las versiones actuales de Windows 10); en Linux, `libwebkit2gtk-4.1` (`sudo apt install libwebkit2gtk-4.1-0`) y una sesión con pantalla; en macOS, no hay nada que instalar. Sin eso, Rastro avisa en la consola y sigue en el navegador. La ventana viene de Photino.NET (Apache-2.0).

Si Rastro ya está abierto en ese puerto, el comando solo muestra la dirección y termina. Registra tus repositorios en Configuración (en el encabezado de la pantalla) o desde la terminal:

```
rastro repo add minha-api C:\codigo\minha-api
```

Para que la compilación y las pruebas entren en la validación de un cambio, edita los comandos en Configuración ("Editar compilación y pruebas") o indícalos al registrar desde la terminal:

```
rastro repo add minha-api C:\codigo\minha-api --build "dotnet build MinhaApi.slnx" --testes "dotnet test MinhaApi.slnx"
```

Los nombres de los comandos y de las opciones (`rastro repo add`, `--build`, `--testes`, `--sem-janela`, `--porta`) son los mismos en cualquier idioma. También valen los alias en inglés `--tests`, `--no-window` y `--port`, junto a los nombres en portugués. La ayuda (`rastro --help`) y los mensajes de la terminal salen en el idioma del `config.json`.

## Flujo entre sistemas

Cuando una API llama a otra por HTTP, el flujo continúa del otro lado. Al crear el rastreo, elige **Seguir llamadas HTTP hacia** y marca los otros repositorios (deben estar registrados en Rastro). Rastro empareja el verbo y la ruta de cada llamada con las rutas de esos repositorios y sigue el rastro en el método que la atiende, con las bifurcaciones de cada sistema en el mismo dibujo.

Lo que no se puede emparejar se convierte en un paso "no resuelto", con el motivo: ruta armada en tiempo de ejecución, sin destino o más de un destino. La IA (con una clave de API en Configuración) o tu agente completa ese paso, y tú confirmas en la pantalla.

La llamada puede salir por `HttpClient` o por una interfaz de Refit (`[Get]`, `[Post]` y los demás verbos); en ambos casos Rastro empareja el verbo y la ruta con el controller de destino.

Límites: la edición y la validación de un cambio solo valen para el primer sistema del rastreo, y Rastro mantiene una solution abierta a la vez.

## Flujo Angular (opcional)

El rastreo puede comenzar en el clic de una pantalla Angular, pasar por el service y por `HttpClient` y llegar al controller .NET, con las bifurcaciones de la pantalla (`@if`, `*ngIf`, `[disabled]`) al inicio del flujo.

Requisitos: Node en el PATH (sin él, la pantalla no ofrece Angular) y el `npm install` ya hecho en el repositorio Angular (en un monorepo, en la subcarpeta de la aplicación), porque Rastro usa el compilador de TypeScript del propio proyecto, que busca desde la carpeta del `tsconfig` hasta la raíz del repositorio.

Al crear el rastreo, elige el `tsconfig` de la aplicación en lugar de la solution (la pantalla sugiere el `tsconfig.app.json` de las carpetas con `angular.json`) e indica el punto de entrada como `Componente.metodo`, por ejemplo `DetalhePedidoComponent.cancelar`. Para atravesar hasta las APIs, marca los repositorios .NET en **Seguir llamadas HTTP hacia**; sin esa lista, el rastreo muestra solo el tramo Angular.

Si el método abre un modal con `MatDialog.open(Componente)` o con el `Dialog` del CDK, el flujo suma una rama "abre el modal Componente" con los métodos que la plantilla del modal enlaza a eventos (`(click)`, `(ngSubmit)`), y sigue desde allí hasta `HttpClient`. Ese vínculo viene de la lectura del HTML del modal (acción del usuario), por lo que los enlaces aparecen como inferidos. `afterClosed()` continúa después de la rama. Un servicio propio que delega en `MatDialog` no se reconoce.

Lo que no se puede leer (URL armada fuera del código, clase elegida solo en tiempo de ejecución, sin destino o más de un destino, profundidad superada) se convierte en un paso "no resuelto", con el motivo. El lector de plantillas es ligero: solo ve las condiciones escritas en el HTML del propio componente, sin el compilador de Angular. La edición y la validación de un cambio solo valen para flujos .NET.

## Vista consolidada

El flujo también tiene un **mapa**, al nivel de un diagrama C2: un carril por sistema, pocos bloques en cada uno, las decisiones que cambian el desenlace, las llamadas entre sistemas y los caminos de punta a punta. Al hacer clic en un bloque se muestra solo ese tramo. Las decisiones se convierten en **reglas** en frases, con la fuente en el código, y Rastro responde **preguntas** ya preparadas sobre el flujo; con el modo proveedor activado, también la pregunta libre, que usa el uso "Sugerencias" y envía al proveedor la pregunta, el mapa, las reglas y hasta 8 fragmentos de código.

Cada rastreo tiene una vista resumida en pocos pasos, en frases, como un diagrama Mermaid que editas junto a la vista previa. Haz clic en un paso para elegir su color. La versión editada se guarda en el rastreo. En el flujo, la vista también es uno de los dibujos del botón que cambia el dibujo: al hacer clic en un paso se abre el panel con su contexto, arrastrar mueve el dibujo y la rueda del mouse acerca. El menú Exportar del rastreo también descarga el grafo o el mapa por sistemas como archivo de draw.io (`.drawio`), con las posiciones de la pantalla y el `repositorio · archivo:línea` de cada paso.

## Esquema de la base de datos

Rastro lee el esquema de la base de datos del propio código del repositorio, sin conectarse a ninguna base de datos: el modelo de EF Core (`DbContext`, `DbSet`, atributos, `OnModelCreating`, `IEntityTypeConfiguration` y `UseSnakeCaseNamingConvention`), las migrations y los archivos `.sql` del proyecto de base de datos (SQL Server, con el lector oficial de T-SQL de Microsoft, y PostgreSQL, con SqlParserCS, ambos dentro del paquete y sin biblioteca nativa). El dialecto sale del repositorio: `UseNpgsql` o `UseSqlServer`, las anotaciones de las migrations y, sin EF, las marcas de los `.sql`. Salen tablas, columnas, claves, índices, vistas y procedimientos, cada elemento con el archivo y la línea de donde vino. Cuando el modelo y la migration discrepan (el tamaño de una columna, por ejemplo), aparecen las dos versiones y Rastro no elige. Nada de tu código se compila ni se ejecuta, y no se lee ningún dato de las tablas.

El esquema aparece en la pantalla **Mapa de la base de datos** de cada repositorio (enlace "Ver el mapa de la base de datos" en el rastreo nuevo): las tablas con PK y FK, las flechas con la cardinalidad, la búsqueda, el recorte en una tabla y sus vecinas, la exportación a PNG, SVG y Mermaid `erDiagram`, la marca "personal" en las columnas con nombre de dato personal (solo por el nombre) y el hallazgo de clave foránea creada sin índice por la migration o por el `.sql`. En el rastreo, el paso que usa EF Core dice qué tabla lee o escribe (consulta, inserción, actualización, eliminación) y qué procedimiento ejecuta, y el panel de la tabla muestra los flujos que la usan. Los hallazgos de base de datos sin IA (consulta dentro de un bucle, `ToList` antes del filtro, lectura sin `AsNoTracking`, procedimiento por SQL crudo, `ToList` solo para eliminar y `LIKE` con comodín al inicio) entran en las mejoras del flujo, también cuando la consulta está en un repositorio genérico (`IRepository<T>`), cuyo `T` queda cerrado por la llamada. Un rastreo hecho antes de esta versión debe rehacerse para tener esto. También sale por la API (`GET api/repositories/{nome}/schema` y `GET api/repositories/{nome}/schema/usage`) y por el MCP (`get_db_schema`, y el campo `db` de los pasos).

En la pestaña **Analizar consulta** de la misma pantalla, pegas una consulta en el dialecto del repositorio, SQL Server o PostgreSQL (o abres la de un paso del flujo que usa SQL crudo), y Rastro señala los cuellos de botella por gravedad, con el fragmento marcado y el motivo: `SELECT *`, función en la columna del filtro, `LIKE '%...'`, `OR` entre columnas, columna sin índice en el filtro o en el join (el índice compuesto cuenta cuando sus columnas iniciales están en el filtro con igualdad), lectura sin límite filtrada solo por igualdad, join sin condición, `DISTINCT` sobre un join, subconsulta correlacionada, `ORDER BY` sin límite, `NOT IN` con una columna que acepta nulos y `UPDATE` o `DELETE` sin `WHERE` (en PostgreSQL, también `ILIKE '%...'` y la conversión con `::` en la columna del filtro). La consulta no se ejecuta y nada va a ninguna base de datos. Con una base de datos en vivo, la estimación de filas del catálogo entra en la lectura sin límite: una tabla pequeña no genera hallazgo, y la lista grande de un padre vuelve a generarlo. También por la API (`POST api/repositories/{nome}/schema/analyze`) y por el MCP (`analyze_query`).

**Base de datos en vivo, opcional y de solo lectura:** con el botón "Conectar base de datos en vivo", registras la cadena de conexión de un SQL Server o de un PostgreSQL (cifrada en la base de datos local, nunca devuelta a la pantalla; el dialecto sale de la propia cadena o del repositorio). El catálogo entra como una fuente más del esquema, y la pestaña de consulta le pide a la base de datos el plan estimado, sin ejecutar la consulta: `SET SHOWPLAN_XML ON` en SQL Server (con el índice que la propia base dice que falta) y `EXPLAIN` sin `ANALYZE` en PostgreSQL, en una conexión y una transacción de solo lectura, con las operaciones más costosas y los escaneos. Los datos de las tablas nunca se leen. Usa un usuario de solo lectura. En SQL Server, `VIEW DEFINITION` para el catálogo y, para el plan, `SHOWPLAN` y `SELECT` en las tablas de la consulta (SQL Server solo muestra el plan a quien puede ejecutar la instrucción). En PostgreSQL, `CONNECT` en la base para el catálogo y, para el plan, `USAGE` en el esquema y `SELECT` en las tablas de la consulta; deja al usuario de solo lectura también en la base (`ALTER ROLE usuario SET default_transaction_read_only = on`).

**La IA sugiere la mejora (opcional):** con el modo proveedor activado y el uso "Sugerencias", el botón "Sugerir una mejora con la IA" pide la consulta reescrita y los índices, con el motivo de cada cambio. Rastro verifica antes de mostrar (la reescritura la lee el lector de SQL del dialecto, hace la misma operación, cita solo tablas y columnas que existen y devuelve las mismas columnas; el índice usa columnas que existen y no repite uno que ya existe) y rechaza con el motivo lo que no pase. El antes y el después aparecen lado a lado, con el costo estimado del plan de ambas cuando hay una base de datos en vivo, sin ejecutar nada. No se aplica nada: quien copia eres tú. Al proveedor se envían la consulta (con los literales), los hallazgos y solo las tablas que usa (nombres, columnas, tipos e índices). La cadena de conexión y los datos de las tablas nunca se envían.

## Con tu agente de IA (MCP)

Rastro funciona sin IA. Con un agente que habla MCP, como Claude Code, el agente completa lo que el compilador no resolvió y escribe las reglas de cada paso, usando su propia suscripción:

```
claude mcp add rastro -- rastro mcp
```

Las instrucciones del MCP y las descripciones de las herramientas están en inglés, porque el agente las lee una sola vez, al conectarse, y las entiende mejor así. Lo que devuelven las herramientas sale en el idioma que elegiste en Rastro (se lee de nuevo en cada llamada, así que cambiarlo en la pantalla vale para la respuesta siguiente), y el agente te responde en el idioma en que escribes.

Para el flujo entre sistemas, la herramienta `list_cross_edges` lista los enlaces HTTP y las llamadas que aún no tienen destino, y `propose_edge` acepta el parámetro `system`, con el repositorio de destino.

Para preguntas sobre el flujo, `get_flow_map` muestra el mapa por sistema (bloques, decisiones, llamadas y caminos) y `get_flow_rules` las reglas sacadas de las decisiones, para que el agente responda citando los pasos. Para una lectura de negocio, `get_business_view` da solo los pasos clave (inicio, decisiones que cambian el resultado, notificaciones, rechazos, llamadas entre sistemas y fin), cada uno con el texto propuesto por regla y su `repositorio · archivo:línea`; lo que cuenta como notificación o rechazo se configura en el `config.json`, en `Rastro:BusinessView`.

Para la base de datos, `get_db_schema` da el esquema leído del código (o solo una tabla), con la fuente de cada elemento y las discrepancias entre el modelo, las migrations y los `.sql`, y `analyze_query` da los hallazgos de una consulta SQL, sin ejecutarla, con el esquema solo de las tablas que usa. Con `propose_query_rewrite`, el agente propone la consulta reescrita y los índices; Rastro los verifica igual que en la pantalla y rechaza con el motivo lo que no pase. No se aplica nada: quien copia eres tú. Con una base de datos en vivo registrada en la pantalla, `get_query_plan` da el plan estimado y la reescritura viene con el costo de antes y después; la cadena de conexión nunca va al agente.

Todo lo que propone la IA llega marcado como inferido y solo entra en el flujo cuando tú confirmas.

## Licencia

Código abierto, licencia MIT: <https://github.com/felipemarcianodev/rastro-ai>.

## Idioma

Rastro habla inglés, portugués de Brasil y español, en la pantalla, la API, la CLI y el MCP. Elige en Configuración, en Apariencia: la elección se guarda en el `config.json` (`Rastro:Language`, con `en-US`, `pt-BR` o `es`). Sin ella, vale el idioma del sistema operativo, con el inglés como respaldo. La CLI (ayuda y mensajes) sigue ese mismo `config.json`; en el MCP, solo las instrucciones y las descripciones de las herramientas quedan en inglés fijo. La paleta y el tema quedan solo en el navegador. El inglés es el idioma principal de la documentación. El español se tradujo con ayuda de IA y no tiene revisión de un hablante nativo: las correcciones por pull request son bienvenidas.

## Tus datos

Quedan en `~/.rastro` (la base de datos SQLite y los repositorios registrados). Para usar otra carpeta, define la variable `RASTRO_HOME`.

## Actualizar y desinstalar

Detén `rastro` antes (en Windows, el ejecutable en uso no se puede reemplazar):

```
dotnet tool update -g rastro-ai
dotnet tool uninstall -g rastro-ai
```
