# DB Semantic Search

Applicazione .NET 10 con frontend Blazor interattivo Server e backend Azure Functions isolated per salvare testi ed embedding in PostgreSQL con l'estensione `vector`, e cercare i cinque testi più vicini tramite distanza coseno. Gli embedding sono generati dal deployment `text-embedding-3-large` su Azure OpenAI (Microsoft Foundry). Non è richiesta l'autenticazione dell'utente.

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

Sono necessari .NET SDK 10, Azure Functions Core Tools v4, PostgreSQL con pgvector, Azurite per lo storage locale, Azure CLI e un deployment di `text-embedding-3-large` in Microsoft Foundry. L'utente locale di PostgreSQL deve poter eseguire `CREATE EXTENSION vector` e creare tabelle; crea prima il database `semantic_search`. Accedi ad Azure con `az login` e assegna al tuo utente il ruolo **Cognitive Services OpenAI User** sulla risorsa del modello. Non inserire segreti nei file versionati.

1. Copia `src/DBSemanticSearch.Api/local.settings.example.json` in `src/DBSemanticSearch.Api/local.settings.json` e configura la stringa PostgreSQL, l'endpoint HTTPS OpenAI e il nome del deployment. `EMBEDDING_DIMENSIONS` deve corrispondere alla dimensione reale degli embedding (3072 per il deployment predefinito) e rimane fisso per la tabella esistente. In alternativa alla stringa, l'API accetta `DB_HOST`, `DB_NAME`, `DB_USER`, `DB_AUTH_MODE` e, soltanto in modalità `Password`, `DB_PASSWORD`. `DefaultAzureCredential` permette di usare l'identità di `az login` in locale; tutte le connessioni separate richiedono TLS con verifica dell'host.
2. Avvia PostgreSQL e Azurite; avvia l'API dalla sua cartella con `func start`. Le chiamate all'API partono dal server Blazor, quindi non serve configurare CORS per il browser.
3. Da un'altra shell esegui `dotnet run --project src\DBSemanticSearch.Web --launch-profile http`, poi apri `http://localhost:5207`. Il file `src/DBSemanticSearch.Web/appsettings.Development.json`, nella radice del progetto e non in `wwwroot`, configura `ApiBaseUrl` su `http://localhost:7071/`. Azure Functions Core Tools non applica localmente i livelli di autorizzazione HTTP, quindi non serve una function key locale. Per altre API locali imposta `ApiBaseUrl` nella configurazione server; non inserire `FunctionKey` nei file versionati.
4. Per eseguire i test: `dotnet test DBSemanticSearch.sln`. L'API crea l'estensione e la tabella alla prima richiesta, verificando che la dimensione della colonna corrisponda al modello configurato.

## Distribuzione Azure

Servono Azure Developer CLI (`azd`), Azure CLI autenticato (`az login`) e .NET SDK 10. Occorrono una subscription con permessi di creazione risorse e di assegnazione ruoli, e una regione che supporti Windows App Service, Flex Consumption, PostgreSQL Flexible Server e la quota del modello OpenAI. `infra/main.bicep` orchestra moduli Bicep separati per frontend, backend, database, Foundry e monitoraggio. Il frontend Blazor Server gira come applicazione ASP.NET Core su Windows App Service, con WebSockets abilitati per i circuiti interattivi; il browser comunica con il sito, non direttamente con le Functions. Il Bicep configura `ApiBaseUrl` e `FunctionKey` nelle app settings dell'App Service: il server le legge a runtime e invia la key nelle richieste alle Functions, senza copiarla negli asset pubblici. In Azure entrambe le impostazioni sono obbligatorie; in locale la key può essere omessa. Gli endpoint HTTP Functions richiedono una function key in Azure. Per più istanze App Service occorre pianificare l'affinità delle sessioni e la gestione dei circuiti Blazor.

Prima del deploy, esegui `dotnet test DBSemanticSearch.sln` e `az bicep build --file infra/main.bicep`; con un ambiente AZD selezionato puoi eseguire `azd package` senza modificare le risorse Azure. Nel pacchetto web controlla che `wwwroot` non contenga `appsettings*.json`, function key o bundle WebAssembly: la configurazione ASP.NET Core nella radice del pacchetto non viene servita come asset statico. Dopo il deploy verifica le pagine `/`, `/texts` e `/search`, la riconnessione del circuito WebSocket, il caricamento JSON e una ricerca; nelle richieste di rete del browser non devono apparire chiamate dirette alla Function App né la key.

Il modulo Foundry crea una risorsa Microsoft Foundry (`AIServices`) con identità gestita system-assigned, un progetto dedicato `DB Semantic Search` e un deployment `GlobalStandard` (capacità predefinita 10K TPM) di `text-embedding-3-large`, disponibile nel progetto. L'identità sull'account è necessaria per la creazione dei progetti. I deployment sono risorse figlie dell'account Foundry nella gerarchia ARM, mentre il progetto è una risorsa figlia separata dello stesso account. La Function App ha un'identità gestita system-assigned con il ruolo **Cognitive Services OpenAI User** sulla risorsa Foundry e riceve `EMBEDDING_ENDPOINT`, `EMBEDDING_DEPLOYMENT`, `EMBEDDING_DIMENSIONS=3072` e `EMBEDDING_AUTH_MODE=ManagedIdentity`. Gli output `AZURE_FOUNDRY_PROJECT_ID` e `AZURE_FOUNDRY_PROJECT_NAME` identificano il progetto creato.

Application Insights è una singola istanza workspace-based (`appi-api-*`) condivisa tramite `AZURE_APPINSIGHTS_CONNECTION_STRING`; la Function App la usa per inviare telemetria. Il frontend può usare la stessa connection string se viene aggiunta l'instrumentazione browser. Foundry e PostgreSQL non inviano automaticamente telemetria ad Application Insights: i loro log di piattaforma richiedono Diagnostic Settings verso il workspace Log Analytics.

```powershell
az login
azd auth login
azd env new dev
azd env set AZURE_LOCATION swedencentral
$account = az ad signed-in-user show | ConvertFrom-Json
azd env set AZURE_POSTGRES_ENTRA_ADMIN_OBJECT_ID $account.id
azd env set AZURE_POSTGRES_ENTRA_ADMIN_NAME $account.userPrincipalName
azd env set AZURE_POSTGRES_ENTRA_ADMIN_PRINCIPAL_TYPE User
azd up
```

Il server usa esclusivamente Microsoft Entra ID: non viene creata alcuna password PostgreSQL. L'hook `postprovision` attende che il server sia pronto e configura l'amministratore Entra con un deployment Bicep separato e ripetibile. Richiede Azure CLI autenticato (`az login`) e il permesso di deployment sul resource group. Al termine del primo `azd up`, registra una sola volta la managed identity della Function come principal PostgreSQL. Sono necessari `psql` e una sessione `az login` dell'amministratore configurato sopra:

```powershell
$postgresHost = azd env get-value AZURE_POSTGRES_HOST
$functionName = azd env get-value AZURE_FUNCTION_NAME
$functionPrincipalId = azd env get-value AZURE_FUNCTION_PRINCIPAL_ID
$entraAdmin = azd env get-value AZURE_POSTGRES_ENTRA_ADMIN_NAME
$env:PGPASSWORD = az account get-access-token --resource-type oss-rdbms --query accessToken -o tsv

psql "host=$postgresHost dbname=postgres user=$entraAdmin sslmode=require" -c "SELECT pg_catalog.pgaadauth_create_principal_with_oid('$functionName', '$functionPrincipalId', 'service', false, false) WHERE NOT EXISTS (SELECT FROM pg_roles WHERE rolname = '$functionName');"
psql "host=$postgresHost dbname=semantic_search user=$entraAdmin sslmode=require" -c ('CREATE EXTENSION IF NOT EXISTS vector; GRANT USAGE, CREATE ON SCHEMA public TO "' + $functionName + '";')
Remove-Item Env:PGPASSWORD
```

Scegli una regione compatibile con il modello. I valori AZD sono memorizzati localmente in `.azure/` (esclusa da Git). Se il deploy viene eseguito da un service principal o vuoi usare un gruppo come amministratore, imposta i tre valori `AZURE_POSTGRES_ENTRA_ADMIN_*` con nome, object ID e tipo corrispondenti. La prima distribuzione può richiedere alcuni minuti per propagare identità e assegnazioni. La configurazione pubblica del server PostgreSQL consente connessioni da servizi Azure (`0.0.0.0`): per ambienti esposti o regolamentati è necessario sostituirla con un'architettura di rete privata. **La function key rimane sul server, ma non autentica gli utenti né rende privata l'API Functions.** Se la precedente versione WebAssembly ha pubblicato la key, ruotala dopo la migrazione coordinando eventuali altri consumer; la host key configurata qui autorizza tutte le HTTP Functions dell'app. Per controllare l'accesso degli utenti o limitare l'esposizione dell'API servono misure dedicate, per esempio Microsoft Entra/App Service Authentication o Azure API Management.

Non vengono configurate credenziali PostgreSQL o Foundry nell'app: l'API usa `DB_AUTH_MODE=ManagedIdentity`, `EMBEDDING_AUTH_MODE=ManagedIdentity` e la propria identità gestita system-assigned. I secret locali restano fuori dal repository.

### Autenticazione verso Foundry

`EMBEDDING_AUTH_MODE` seleziona la credenziale usata dal servizio di embedding:

| Valore | Comportamento |
| --- | --- |
| `DefaultAzureCredential` (predefinito) | Catena di credenziali Azure: identità gestita in Azure, `az login`/Visual Studio in locale |
| `ManagedIdentity` | Solo identità gestita; system-assigned, oppure user-assigned se è impostato `EMBEDDING_MANAGED_IDENTITY_CLIENT_ID` |
| `ApiKey` | Chiave della risorsa, letta da `EMBEDDING_API_KEY` (obbligatoria in questa modalità) |

Con `ManagedIdentity` e `DefaultAzureCredential` l'identità deve avere il ruolo **Cognitive Services OpenAI User** sulla risorsa. La chiave API non va mai inserita in file versionati: in Azure conservala in Key Vault e referenziala dalle impostazioni della Function.
