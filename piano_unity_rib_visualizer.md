# Piano di lavoro — Rib Fracture 3D Visualizer (Unity)

Riferimento: layout "Vertebral Lesion 3D Visualizer" del corso, adattato a costole con risultati di classificazione automatica invece di selezione manuale.

Bar minimo richiesto dal corso: pipeline deve coprire image processing, estrazione feature, gestione dati 3D, classificazione, visualizzazione Unity. Questo documento riguarda solo l'ultimo blocco.

---

## 0. Decisioni di design già prese (non rinegoziare in corso d'opera)

- Toggle unificato a 3 stati invece di due toggle separati: `Nessuna lesione` / `Lesione binaria` / `Lesione per tipo`
- Niente "Suspected lesion selector" manuale: il pannello mostra i risultati già calcolati da XGBoost, non un input dello studente
- Colore per classe: 3 colori se il modello finale resta a 3 classi (Displaced, Non-displaced, Buckle), eventualmente un 4° se si reintroduce Segmental
- Confidenza esposta al click (dato già disponibile da `predict_proba`), non solo classe predetta
- Selezione struttura singola: click diretto sul modello 3D sulla mesh della frattura, non lista di toggle
- **Visualizzazione: CT intera come volume rendering 3D (sfondo anatomico) + mesh delle fratture sovrapposte e colorate per classe**
- Non si dispone della segmentazione per costola: le uniche mesh disponibili sono quelle delle singole fratture. Non esiste una mesh della costola intera né della gabbia toracica da segmentazione.

Se uno di questi punti viene messo in discussione durante il lavoro, fermarsi e deciderlo esplicitamente prima di continuare — non procedere per inerzia sul vecchio layout vertebre.

**Rischio principale del modulo Unity**: il volume rendering della CT in Unity richiede uno shader dedicato. È la singola dipendenza tecnica più rischiosa dell'intero piano — se si blocca qui, tutto il resto del modulo si blocca. Affrontarlo per primo, non per ultimo.

---

## 1. Input dati necessari (da preparare PRIMA di aprire Unity)

Lo step 1 è quasi tutto fuori da Unity. Non si comincia a costruire la scena finché questi file non esistono.

1.1. **CT intera del paziente** in formato NIfTI (`.nii` o `.nii.gz`), già disponibile dal dataset RibFrac. Deve essere convertita in un formato importabile in Unity per il volume rendering — vedere punto 1.4.

1.2. **Mesh delle fratture per paziente**, già disponibili come OBJ dal pipeline, una mesh per frattura (identificata da `label_id`). Non si dispone di mesh per costola sana né per la gabbia toracica intera — la CT volumetrica fornisce il contesto anatomico al posto di queste mesh mancanti.

1.3. **File di mapping frattura → predizione**, da generare dal notebook Python. Formato JSON:
   - `public_id` (paziente)
   - `label_id` (identificatore frattura, consistente con il naming delle mesh OBJ)
   - `predicted_class` (Displaced / Non-displaced / Buckle)
   - `confidence` (probabilità della classe predetta, da `predict_proba`)
   - facoltativo: `prob_displaced`, `prob_nondisplaced`, `prob_buckle` per tooltip dettagliato

   Questo file va generato una volta dal notebook (aggiungere cella di export se non esiste) e importato in Unity. Non va calcolato dentro Unity.

1.4. **Conversione CT per volume rendering Unity**: il NIfTI non è importabile direttamente come volume 3D in Unity. Il percorso più praticabile è:
   - Esportare la CT come stack di slice PNG/JPEG (una per slice assiale) oppure come texture 3D in formato `.asset` Unity tramite script Python (nibabel → array numpy → file binario letto da Unity)
   - Alternativa: usare un package Unity per volume rendering che accetti direttamente array 3D a runtime (es. `UnityVolumeRendering` open source su GitHub — valutare compatibilità con Unity 6)
   - Questa conversione va testata su un paziente prima di impegnarsi su tutti: è il punto tecnico più rischioso dell'intero modulo

1.5. **Pannello Case INFO**: nessun metadato clinico disponibile oltre al `public_id`. Il pannello mostra:
   - `public_id`
   - conteggio fratture per classe (es. "3 fratture — 2 Displaced, 1 Buckle"), calcolato a runtime dal file di predizioni

   Non serve un file metadati separato: il JSON del punto 1.3 è la singola fonte dati per colorazione, tooltip e Case INFO.

**Checkpoint 1**: per almeno 1 paziente di test verificare che esistano: CT convertita in formato Unity-compatibile + mesh fratture OBJ + file predizioni JSON. Testare il caricamento della CT come volume 3D in Unity prima di procedere — se questo non funziona, tutto il piano A si blocca.

---

## 2. Setup struttura progetto Unity

2.1. Creare/riusare repository Git LFS già configurato per il team.

2.2. Struttura cartelle:
```
Assets/
  Volumes/<public_id>/ct_volume/...    (CT convertita per volume rendering)
  Models/Fractures/<public_id>/...     (mesh OBJ per frattura, es. fracture_01.obj)
  Data/Predictions/<public_id>.json    (unica fonte dati: predizioni + base per Case INFO)
  Scripts/
  UI/
  Materials/
  Shaders/                             (shader volume rendering, se custom)
```

2.3. Importare e testare lo shader/package di volume rendering su una CT di test. Verificare:
   - La CT si vede come volume semitrasparente con dettaglio osseo leggibile
   - La scala e l'orientamento sono coerenti con le mesh OBJ delle fratture (errore comune: NIfTI e OBJ possono avere sistemi di coordinate diversi — LPS vs RAS vs Unity LHS)
   - Le mesh OBJ delle fratture si sovrappongono correttamente al volume CT senza offset

**Checkpoint 2**: volume CT + almeno una mesh frattura visibili insieme in scena, posizionalmente coerenti, nessun asse invertito. Questo checkpoint è bloccante: non procedere ai blocchi successivi finché non è superato.

---

## 3. Script di caricamento dati (logica, non UI)

3.1. Script che legge il JSON delle predizioni e popola una struttura dati interna (dizionario `label_id → {classe, confidenza}`).

3.2. Script che calcola il riepilogo per il pannello Case INFO dalla struttura dati del punto 3.1 (conteggio fratture per classe). Non esiste un file metadati separato: tutto viene dal JSON predizioni.

3.3. Script "Load DATA": all'avvio non carica nulla. Al click carica CT + mesh fratture + predizioni per il paziente selezionato. La CT come volume e le mesh come GameObject separati.

3.4. Gestione pazienti non nel test split: se per un `public_id` non esiste il file `Predictions/<public_id>.json`, mostrare nel pannello Case INFO "Paziente non nel test set — predizioni non disponibili" invece di un errore. Le mesh e la CT possono comunque essere mostrate senza colorazione (tutte grigie).

**Checkpoint 3**: pannello Case INFO si popola (public_id + conteggi) al click di Load DATA; caso "non nel test set" gestito senza crash.

---

## 4. Logica di colorazione (il cuore del modulo)

Le mesh delle fratture sono gli unici oggetti colorabili — non si colora la costola intera (non disponibile) ma la mesh del frammento fratturato.

4.1. Script che assegna colore al materiale di ciascuna mesh-frattura in base allo stato del toggle a 3 vie:
   - Stato "Nessuna lesione": tutte le mesh fratture colore neutro (grigio, coerente con il volume CT sullo sfondo)
   - Stato "Lesione binaria": rosso per tutte le mesh-frattura (qualunque classe predetta)
   - Stato "Lesione per tipo": colore per classe — Displaced / Non-displaced / Buckle con 3 colori distinguibili

4.2. Il volume CT rimane sempre grigio semitrasparente indipendentemente dallo stato del toggle — non viene colorato, serve solo come riferimento anatomico.

4.3. Palette colori: scegliere 3 colori distinguibili anche per chi ha deficit di visione dei colori (evitare rosso/verde puro). Proposta: arancio (#E87722) per Displaced, blu (#4A90D9) per Non-displaced, viola (#9B59B6) per Buckle. La legenda nel pannello destro deve riprodurre esattamente questi colori.

4.4. Il cambio di stato del toggle aggiorna i colori in tempo reale senza ricaricare mesh o volume.

**Checkpoint 4**: passando tra i 3 stati del toggle su un paziente con fratture di tipo diverso, i colori sulle mesh cambiano correttamente; il volume CT rimane grigio.

---

## 5. Interazione: click su mesh-frattura → dettaglio

5.1. Collider su ogni mesh-frattura (MeshCollider sul singolo GameObject). Il volume CT non deve avere collider o deve essere su un layer separato escluso dal raycast, altrimenti intercetta tutti i click prima delle mesh.

5.2. Script di raycast su click sinistro: al click su una mesh-frattura apre un tooltip/pannello fisso in un angolo con:
   - `label_id` (es. "Fracture 3")
   - classe predetta
   - confidenza (es. "87%")
   - opzionale: le 3 probabilità grezze

5.3. Tooltip come pannello fisso in un angolo dello schermo (più semplice e più stabile del world-space label — consigliato per restare nei tempi). Il pannello rimane visibile fino al prossimo click su una mesh diversa o su area vuota.

5.4. Click su area vuota (nessuna mesh colpita dal raycast): chiude il tooltip.

**Checkpoint 5**: click su mesh-frattura mostra classe e confidenza corrette; click su volume CT o area vuota chiude il tooltip senza crash.

---

## 6. UI complessiva e controlli rimanenti

6.1. Layout finale:
   - Sinistra alto: pannello **Case INFO** (public_id + conteggio fratture per classe)
   - Sinistra basso: pannello **Functionalities** con Load DATA / toggle 3 stati / Reset View / Take screenshot / Help
   - Destra: **legenda colori** (3 voci: Displaced / Non-displaced / Buckle)
   - Centro: volume CT 3D + mesh fratture sovrapposte
   - Angolo (es. basso destra): tooltip click-to-detail del punto 5

6.2. Reimplementare Reset View e Take screenshot — verificare prima se il template del corso li fornisce già come script riusabili.

6.3. Help: testo statico controlli — Right Click rotate, Scroll zoom, Middle Click pan, Left Click show fracture detail.

**Checkpoint 6**: tutti i controlli funzionano insieme su almeno 2 pazienti diversi senza bug di stato (cambiare paziente mentre il toggle è su "per tipo" resetta correttamente colori e tooltip, non lascia residui del paziente precedente).

---

## 7. Estensione multi-paziente

7.1. Selettore paziente (dropdown) integrato nel bottone Load DATA o come elemento separato — verificare come funziona nel template originale prima di costruirne uno nuovo.

7.2. Generalizzare gli script per caricare dinamicamente CT + mesh + predizioni di qualsiasi paziente nella struttura cartelle definita al punto 2.2.

**Checkpoint 7**: testare almeno 3-4 pazienti diversi, inclusi: paziente con 0 fratture nel test set (solo CT, nessuna mesh), paziente con fratture multiple di tipo diverso, paziente non nel test set (messaggio "non nel test set").

---

## 8. Rifinitura finale (solo se c'è tempo residuo)

- Indicazione visiva di confidenza bassa: mesh-frattura con colore più sbiadito/semitrasparente se confidenza < 0.5
- Controllo opacità del volume CT (slider) per migliorare la leggibilità delle mesh sovrapposte
- Polish estetico (font, icone, transizioni)

Non iniziare il punto 8 se i punti 1-7 non sono già completi e testati.

---

## Sequenza di lavoro consigliata

Il blocco 1.4 (conversione CT + test volume rendering) è la dipendenza critica: va fatto per primo, da chi ha più esperienza con Unity, prima di qualsiasi altra cosa. Se fallisce, tutto il piano A cade e si deve rivalutare.

Una volta superato il checkpoint 2, i blocchi 3 (caricamento dati) e 4-5 (colorazione + raycast) possono procedere in parallelo su branch separati: il blocco 3 non dipende dalle mesh in scena per essere scritto e testato con dati mock, mentre i blocchi 4 e 5 dipendono dal checkpoint 2 ma sono indipendenti tra loro. Il blocco 6 assembla i pezzi già testati: ha senso solo dopo che 4 e 5 funzionano. Il blocco 7 generalizza una logica già validata: va fatto per ultimo.

---

## Checkpoint riassuntivi

Checkpoint 1: CT convertita in formato Unity-compatibile + mesh fratture + JSON predizioni pronti per almeno 1 paziente.
Checkpoint 2 (BLOCCANTE): volume CT e mesh frattura visibili insieme in scena, posizionalmente coerenti, nessun asse invertito.
Checkpoint 3: pannello Case INFO popolato al click di Load DATA; caso "non nel test set" gestito senza crash.
Checkpoint 4: i 3 stati del toggle cambiano colore sulle mesh in modo coerente con le predizioni; CT rimane grigia.
Checkpoint 5: click su mesh-frattura mostra dettaglio; click su CT o area vuota chiude tooltip senza crash.
Checkpoint 6: tutti i controlli coerenti insieme su almeno 2 pazienti, nessun residuo di stato tra cambi paziente.
Checkpoint 7: testati casi limite (0 fratture, fratture multiple, paziente fuori test set).
