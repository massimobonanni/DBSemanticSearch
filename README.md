# DB Semantic Search

Applicazione .NET 10 con frontend Blazor WebAssembly e backend Azure Functions isolated per salvare testi ed embedding in PostgreSQL con l'estensione `vector`, e cercare i cinque testi più vicini tramite distanza coseno. Gli embedding sono generati dal deployment `text-embedding-3-small` su Azure OpenAI (Microsoft Foundry). Non è richiesta l'autenticazione dell'utente.

## Struttura

| Percorso | Responsabilità |
| --- | --- |
| `src/DBSemanticSearch.Core` | Modello, interfacce per database/modello, validazione e servizio applicativo |
| `src/DBSemanticSearch.Contracts` | DTO REST e [schema JSON dei batch](src/DBSemanticSearch.Contracts/schemas/text-batch.schema.json) |
| `src/DBSemanticSearch.Client` | REST client utilizzato da Blazor |
| `src/DBSemanticSearch.Foundry` | Configurazione e adapter Azure OpenAI (Microsoft Foundry) |
| `src/DBSemanticSearch.Postgres` | Repository PostgreSQL/pgvector |
| `src/DBSemanticSearch.Api` | Endpoint HTTP Azure Functions |
| `src/DBSemanticSearch.Web` | Pagine Blazor per inserimento, caricamento JSON e ricerca |
| `infra` | Risorse Azure Bicep e parametri AZD |
| `tests` | Test unitari per servizi, batch e client |

## Formato del file e API

Il file JSON deve essere un oggetto con un array `texts` di 1–100 elementi. I testi non possono essere vuoti o composti soltanto da spazi e non possono superare i 10.000 caratteri; dimensione massima del file/richiesta: 1 MiB.

```json
{
  "texts": ["Il primo testo da indicizzare", "Un altro testo da indicizzare"]
}
```

Il batch elabora ciascun elemento nell'ordine fornito. Un elemento non valido o un errore del modello/database non interrompe i successivi; `inserted` indica il numero salvato e ogni voce di `items` riporta `index` (base zero), `document` o `error`. Un documento valido è persistito subito: ripetere un batch dopo un errore può creare duplicati.

| Metodo | Percorso | Corpo | Risposta |
| --- | --- | --- | --- |
| POST | `/api/texts` | `{"text":"..."}` | `201` con documento `{id,text,createdAt}` |
| POST | `/api/texts/batch` | `{"texts":["...", "..."]}` | `200` con `{inserted,items:[{index,document,error}]}` |
| POST | `/api/search` | `{"text":"..."}` | `200` con `{results:[{document,distance}]}` (massimo 5) |

I problemi di formato/validazione restituiscono `400` e `{ "error": "..." }`; richieste oltre 1 MiB restituiscono `413`. La distanza coseno minore indica maggiore vicinanza. Per raccolte piccole la query effettua una ricerca esatta, senza indice approssimato.

## Esecuzione locale

Sono necessari .NET SDK 10, Azure Functions Core Tools v4, PostgreSQL con pgvector, Azurite per lo storage locale, Azure CLI e un deployment di `text-embedding-3-small` in Microsoft Foundry. L'utente locale di PostgreSQL deve poter eseguire `CREATE EXTENSION vector` e creare tabelle; crea prima il database `semantic_search`. Accedi ad Azure con `az login` e assegna al tuo utente il ruolo **Cognitive Services OpenAI User** sulla risorsa del modello. Non inserire segreti nei file versionati.

1. Copia `src/DBSemanticSearch.Api/local.settings.example.json` in `src/DBSemanticSearch.Api/local.settings.json` e configura la stringa PostgreSQL, l'endpoint HTTPS OpenAI e il nome del deployment. `EMBEDDING_DIMENSIONS` deve corrispondere alla dimensione reale degli embedding (1536 per il deployment predefinito) e rimane fisso per la tabella esistente. In alternativa alla stringa, l'API accetta `DB_HOST`, `DB_NAME`, `DB_USER`, `DB_PASSWORD` da variabili d'ambiente e richiede TLS con verifica dell'host.
2. Avvia PostgreSQL e Azurite; avvia l'API dalla sua cartella con `func start --cors http://localhost:5207`.
3. Da un'altra shell esegui `dotnet run --project src\DBSemanticSearch.Web --launch-profile http`, poi apri `http://localhost:5207`. La configurazione di sviluppo punta a `http://localhost:7071`; in produzione il frontend usa `/api` sullo stesso host.
4. Per eseguire i test: `dotnet test DBSemanticSearch.sln`. L'API crea l'estensione e la tabella alla prima richiesta, verificando che la dimensione della colonna corrisponda al modello configurato.

## Distribuzione Azure

Servono Azure Developer CLI (`azd`), Azure CLI e, per il packaging di Static Web Apps, **Node.js e Static Web Apps CLI** (`npm install -g @azure/static-web-apps-cli`). Occorrono una subscription con permessi di creazione risorse e di assegnazione ruoli, e una regione che supporti Flex Consumption, PostgreSQL Flexible Server e la quota del modello OpenAI. `infra/main.bicep` orchestra moduli Bicep separati per frontend, backend, database, Foundry, workspace Log Analytics e i componenti Application Insights di frontend, backend e Foundry. Static Web Apps inoltra `/api/*` alle Functions. Il frontend è pubblicato come asset statici; la connection string di Application Insights del backend viene configurata nell'API, mentre quelle per frontend e Foundry sono esposte come output del deployment per la strumentazione dei rispettivi componenti.

Il modulo Foundry crea due deployment `GlobalStandard` (capacità predefinita 10K TPM ciascuno): `text-embedding-3-small`, usato dall'API, e `text-embedding-3-large`. La Function App ha un'identità gestita system-assigned con il ruolo **Cognitive Services OpenAI User** sulla risorsa Foundry e riceve `EMBEDDING_ENDPOINT`, `EMBEDDING_DEPLOYMENT`, `EMBEDDING_DIMENSIONS` e `EMBEDDING_AUTH_MODE=ManagedIdentity`. Il deployment `text-embedding-3-large` produce di default vettori a 3072 dimensioni, oltre il limite di 2000 accettato dall'applicazione e dalla colonna `vector` indicizzabile: per usarlo nell'API occorre prima supportare dimensioni ridotte.

```powershell
azd auth login
azd env new dev
azd env set AZURE_LOCATION swedencentral
azd env set AZURE_POSTGRES_PASSWORD "UNA_PASSWORD_FORTE_GENERATA_DA_TE"
azd up
```

Scegli una regione compatibile con il modello e una password robusta; i valori AZD sono memorizzati localmente in `.azure/` (esclusa da Git). La password del database è un parametro Bicep sicuro e viene usata nelle impostazioni della Function, non negli output di provisioning. La prima distribuzione può richiedere alcuni minuti per propagare le assegnazioni di ruolo. La configurazione pubblica del server PostgreSQL consente connessioni da servizi Azure (`0.0.0.0`): per ambienti esposti o regolamentati è necessario sostituirla con un'architettura di rete privata. **L'API è anonima e può generare costi Foundry a ogni richiesta**: usare questo esempio soltanto in un ambiente in cui accesso e consumi siano controllati a livello di rete/deployment.

Non vengono configurate credenziali Foundry nell'app: l'API usa `EMBEDDING_AUTH_MODE=ManagedIdentity` e la propria identità gestita system-assigned. I secret locali restano fuori dal repository.

### Autenticazione verso Foundry

`EMBEDDING_AUTH_MODE` seleziona la credenziale usata dal servizio di embedding:

| Valore | Comportamento |
| --- | --- |
| `DefaultAzureCredential` (predefinito) | Catena di credenziali Azure: identità gestita in Azure, `az login`/Visual Studio in locale |
| `ManagedIdentity` | Solo identità gestita; system-assigned, oppure user-assigned se è impostato `EMBEDDING_MANAGED_IDENTITY_CLIENT_ID` |
| `ApiKey` | Chiave della risorsa, letta da `EMBEDDING_API_KEY` (obbligatoria in questa modalità) |

Con `ManagedIdentity` e `DefaultAzureCredential` l'identità deve avere il ruolo **Cognitive Services OpenAI User** sulla risorsa. La chiave API non va mai inserita in file versionati: in Azure conservala in Key Vault e referenziala dalle impostazioni della Function.