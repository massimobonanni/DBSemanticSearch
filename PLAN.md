# Piano: Db Semantic Search

## Problema e approccio

Realizzare da questo repository inizialmente vuoto un'applicazione .NET 10 per acquisire testi, generarne gli embedding tramite Microsoft Foundry, salvarli in PostgreSQL su Azure e ricercarli per similarità semantica. Il frontend sarà una SPA Blazor WebAssembly separata; chiamerà Azure Functions tramite una libreria condivisa di DTO e REST client. L'accesso a PostgreSQL e a Foundry sarà mediato da interfacce nel livello Core e implementazioni infrastrutturali dedicate. La soluzione seguirà la struttura AZD `src` + `infra`, con infrastruttura Bicep.

## Attività

1. Definire solution, progetti e contratti condivisi: Core, DTO/REST Client, API Azure Functions e frontend Blazor WebAssembly.
2. Implementare il modello di dominio, le interfacce Core, validazione e schema JSON per acquisizione singola e batch.
3. Implementare PostgreSQL con pgvector, migrazioni/inizializzazione schema, repository e ricerca top 5.
4. Implementare l'adapter Microsoft Foundry per embedding e configurazione runtime sicura.
5. Esporre endpoint Functions per ingestion e ricerca usando DTO e dipendenze iniettate.
6. Realizzare le pagine Blazor per inserimento/caricamento JSON e ricerca con visualizzazione risultati/errori.
7. Aggiungere test mirati, configurazione AZD e moduli Bicep per le risorse Azure necessarie.
8. Aggiornare README con avvio locale, formato JSON, configurazione/deploy AZD e verificare build e test.

## Decisioni e considerazioni

- Frontend: Blazor WebAssembly separato.
- File JSON: formato documentato e validato; il caricamento batch usa elaborazione parziale, salvando gli elementi validi e restituendo gli errori associati a quelli non validi.
- Formato JSON previsto: un oggetto con la proprietà `texts` contenente una lista di stringhe. Testi vuoti o composti solo da spazi sono rifiutati; i duplicati non vengono eliminati salvo vincoli motivati dal modello dati.
- API Azure Functions .NET isolated; il REST client condiviso evita duplicazioni del protocollo HTTP nel frontend.
- Dimensione dei vettori e nome del deployment del modello saranno configurabili, mantenendo allineati embedding e colonna pgvector senza legare il codice a un deployment specifico.
- Credenziali e segreti non saranno inclusi nel repository: configurazione locale tramite user-secrets/variabili d'ambiente e risorse Azure con identità gestita dove supportata.
- Nessuna autenticazione utente.
- I test copriranno logica Core, validazione/DTO e comportamento dei servizi; i test d'integrazione con Azure/PostgreSQL dipenderanno dalla disponibilità dei servizi locali o remoti.

## Struttura e componenti principali

- `DBSemanticSearch.sln` e `src/` con progetti Core, DTO/REST Client, Azure Functions e Blazor WebAssembly.
- `infra/` con moduli Bicep; `azure.yaml` e configurazione AZD standard.
- `tests/` con test unitari e, dove praticabile, d'integrazione.
- `README.md` con schema JSON, configurazione, esecuzione locale e provisioning.

## Stato di implementazione

La soluzione, le librerie Core/DTO/REST Client, i servizi PostgreSQL e Foundry, gli endpoint Functions, le pagine Blazor, i test, la documentazione e il provisioning Bicep/AZD sono implementati. Per validare effettivamente il provisioning e l'inoltro Static Web Apps occorre una subscription Azure con quote disponibili; il packaging del frontend con AZD richiede anche Node.js e Static Web Apps CLI.
