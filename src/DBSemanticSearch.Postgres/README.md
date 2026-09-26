# DBSemanticSearch.Postgres

Adapter PostgreSQL della soluzione DB Semantic Search. Il progetto implementa il contratto di persistenza definito in `DBSemanticSearch.Core` e usa `pgvector` per salvare gli embedding e ordinare i risultati per distanza coseno.

## Responsabilità

- persistere testi ed embedding in PostgreSQL;
- eseguire ricerche vettoriali esatte con l'operatore `pgvector` `<=>`;
- creare e verificare lo schema alla prima operazione;
- validare dimensione e valori degli embedding prima di inviarli al database.

Il progetto non legge direttamente variabili d'ambiente e non sceglie il metodo di autenticazione. Riceve tramite dependency injection un `NpgsqlDataSource` già configurato dall'API, mantenendo separati accesso ai dati e composizione dell'applicazione.

## Dipendenze

- [`DBSemanticSearch.Core`](../DBSemanticSearch.Core) per `ITextRepository`, `TextDocument` e `SearchHit`;
- [`Npgsql`](https://www.npgsql.org/) per connessioni, comandi e pooling PostgreSQL;
- [`pgvector`](https://github.com/pgvector/pgvector) come estensione installata sul server.

## Classi

| Classe | Descrizione |
| --- | --- |
| [`PostgresTextRepository`](PostgresTextRepository.cs) | Implementazione sealed di `ITextRepository`. Riceve un `NpgsqlDataSource` e il numero atteso di dimensioni degli embedding. |

### `PostgresTextRepository`

`InsertAsync` valida il vettore, inizializza lo schema se necessario e inserisce un documento con un nuovo UUID. Il timestamp viene generato dal database e restituito come `DateTimeOffset` UTC.

`SearchAsync` calcola la distanza coseno tra il vettore richiesto e ogni documento, ordina per distanza crescente e applica il limite indicato. In caso di parità usa l'UUID come ordinamento deterministico.

`EnsureSchemaAsync` viene eseguito una sola volta per istanza. Un `SemaphoreSlim` impedisce inizializzazioni concorrenti; dopo la creazione controlla che il tipo della colonna sia esattamente `vector(<dimensions>)`.

`FormatVector` rifiuta vettori con dimensione diversa da quella configurata o valori non finiti e usa la cultura invariant per la serializzazione numerica.

## Schema

Alla prima operazione il repository verifica l'estensione e la tabella seguenti:

```sql
CREATE EXTENSION IF NOT EXISTS vector;

CREATE TABLE IF NOT EXISTS documents (
    id uuid PRIMARY KEY,
    text text NOT NULL,
    embedding vector(<dimensions>) NOT NULL,
    created_at timestamptz NOT NULL DEFAULT now()
);
```

La dimensione è un vincolo persistente dello schema. Cambiare modello di embedding richiede una migrazione della colonna oppure la ricreazione della tabella; modificare soltanto `EMBEDDING_DIMENSIONS` causa intenzionalmente un errore all'avvio della prima operazione.

## Configurazione

La configurazione del data source avviene in [`DBSemanticSearch.Api/Program.cs`](../DBSemanticSearch.Api/Program.cs). `DB_CONNECTION_STRING`, quando presente, ha precedenza sulle singole variabili.

| Variabile | Obbligatoria | Descrizione |
| --- | --- | --- |
| `DB_CONNECTION_STRING` | No | Connection string Npgsql completa, usata principalmente in sviluppo locale. |
| `DB_HOST` | Senza connection string | Host PostgreSQL. |
| `DB_NAME` | No | Nome del database; valore predefinito `semantic_search`. |
| `DB_USER` | Senza connection string | Ruolo PostgreSQL o nome del principal Microsoft Entra. |
| `DB_AUTH_MODE` | No | `Password` (predefinito), `DefaultAzureCredential` o `ManagedIdentity`. |
| `DB_PASSWORD` | Con modalità `Password` | Password PostgreSQL. Non viene configurata nel deployment Azure. |
| `DB_MANAGED_IDENTITY_CLIENT_ID` | No | Client ID di una user-assigned managed identity; se assente viene usata quella system-assigned. |
| `EMBEDDING_DIMENSIONS` | Sì per l'API | Dimensione passata al repository e attesa dalla colonna `embedding`. |

Tutte le configurazioni costruite da variabili separate usano `SslMode=VerifyFull`.

### Autenticazione

- **`Password`** usa `DB_PASSWORD` ed è adatta a un PostgreSQL locale.
- **`DefaultAzureCredential`** usa la catena di credenziali Azure ed è utile in sviluppo con `az login`.
- **`ManagedIdentity`** usa esclusivamente l'identità gestita dell'host. Npgsql richiede un token per lo scope `https://ossrdbms-aad.database.windows.net/.default` e lo rinnova periodicamente senza inserirlo nella connection string.

Il deployment in [`infra`](../../infra) configura PostgreSQL in modalità Microsoft Entra-only e imposta `DB_AUTH_MODE=ManagedIdentity` sulla Function App. L'identità deve essere registrata una volta come ruolo PostgreSQL tramite `pgaadauth_create_principal_with_oid`; la procedura completa è nel [README principale](../../README.md#distribuzione-azure).

## Permessi database

Con l'inizializzazione automatica corrente, il ruolo applicativo deve poter:

- connettersi al database `semantic_search`;
- usare e creare oggetti nello schema `public`;
- leggere e inserire righe nella tabella `documents` che crea.

L'estensione `vector` deve essere consentita nella configurazione del Flexible Server e creata da un amministratore durante il bootstrap. In un ambiente con migrazioni amministrative è possibile precreare tabella ed estensione e ridurre ulteriormente i privilegi dell'identità applicativa.

## Registrazione

L'API registra il data source e il repository come singleton:

```csharp
builder.Services.AddSingleton(CreatePostgresDataSource());
builder.Services.AddSingleton<ITextRepository>(serviceProvider =>
    new PostgresTextRepository(
        serviceProvider.GetRequiredService<NpgsqlDataSource>(),
        settings.Dimensions));
```

`NpgsqlDataSource` gestisce il pool delle connessioni ed è progettato per essere condiviso per tutta la durata del processo.

## Verifica

Per compilare il progetto:

```powershell
dotnet build src\DBSemanticSearch.Postgres\DBSemanticSearch.Postgres.csproj
```

I test della soluzione possono essere eseguiti con:

```powershell
dotnet test DBSemanticSearch.sln
```