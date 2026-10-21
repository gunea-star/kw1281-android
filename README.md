# KW1281 Android

Aplicatie Android care ruleaza **kw1281test v1.0.4** (Greg Menounos, licenta MIT,
https://github.com/gmenounos/kw1281test) direct pe telefon, cu un cablu KKL FTDI conectat prin OTG.

Toate comenzile din versiunea de PC sunt incluse: ReadIdent, ReadFaultCodes, GetSKC, DumpEeprom,
LoadEeprom, GroupRead, Adaptation*, DumpEdc15/16Flash, LoadEdc15/16Flash, EDC15 EEPROM, boot mode etc.

## Ce contine arhiva

```
kw1281test/                codul original v1.0.4, neatins in afara de Program.cs (modificari marcate [Android])
KW1281Android/
  KW1281Android.csproj     proiectul Android (.NET 10)
  MainActivity.cs          interfata: cablu, baud, adresa, comanda, argumente, log, import/export
  AndroidFtdiInterface.cs  driver USB pentru cipul FTDI (inlocuieste portul COM)
  AndroidManifest.xml
  Resources/xml/device_filter.xml
.github/workflows/build-apk.yml   compilare automata a APK-ului pe GitHub
```

## Varianta A - compilare pe GitHub (fara sa instalezi nimic)

1. Fa-ti cont pe github.com si creeaza un repository nou (poate fi privat).
2. Urca tot continutul arhivei (inclusiv folderul `.github`). Cel mai simplu: "Add file > Upload files".
3. Mergi la tab-ul **Actions** - compilarea porneste singura (~5-10 minute).
4. Cand apare bifa verde, deschizi rularea si descarci artefactul **KW1281Android-apk** (un .zip cu APK-ul).

## Varianta B - compilare pe PC-ul tau (Windows)

1. Instaleaza **Visual Studio 2026** (sau 2022 actualizat) cu workload-ul **".NET Multi-platform App UI"**
   (acesta aduce Android SDK si Java). Alternativ, doar .NET 10 SDK + `dotnet workload install android`.
2. Deschide `KW1281Android\KW1281Android.csproj`.
3. Release + `dotnet publish -c Release -f net10.0-android`, sau din Visual Studio: Build > Publish.
4. APK-ul semnat apare ca `ro.kw1281test.android-Signed.apk` in `bin\Release\net10.0-android\publish\`.

## Unde intra APK Toolkit

APK Toolkit nu poate *crea* o aplicatie din cod sursa - el decompileaza, recompileaza, semneaza si instaleaza
APK-uri existente. Dupa ce ai APK-ul de mai sus il poti folosi pentru:
- **Install Apk** - il instaleaza pe telefonul conectat prin USB (cu USB debugging activ);
- **Sign** - daca vrei sa-l semnezi cu cheia ta;
- **Decompile** - sa vezi/modifici resursele (nume, iconita).

## Folosire

1. Conecteaza cablul KKL la telefon printr-un adaptor OTG. Android intreaba daca deschide KW1281 Android - accepta.
2. Baud: `10400` (unele module vechi: `9600`). Adresa este in hex ca in VCDS: `01` motor, `17` bord, `15` airbag, `56` radio.
3. Alegi comanda, completezi argumentele (sub lista apare ce asteapta fiecare comanda), apesi **Ruleaza**.
4. Dump-urile si `KW1281Test.log` se salveaza in `Android/data/ro.kw1281test.android/files/`.
   - **Exporta** - copiezi un fisier unde vrei (Download, Drive etc.).
   - **Importa** - aduci un .bin in folderul aplicatiei (numele se adauga automat la argumente) pentru comenzile Load.

## Limitari (de stiut)

- **Doar cabluri cu cip FTDI** (VID 0403: FT232RL, FT232BM, FT230X). Cablurile cu CH340 sau PL2303 nu sunt suportate.
- Pe Android nu exista tastatura de consola: comenzile care asteapta o tasta (ex. ActuatorTest, meniurile
  interactive KWP2000, confirmarea corectiei de checksum la LoadEdc16Flash) pot sa se opreasca la acel pas.
  Unde programul accepta text, foloseste campul "Raspuns" + Trimite.
- Temporizarea pe USB OTG e mai putin precisa decat pe PC. Citirile merg de obicei bine;
  **pentru scrieri de flash (LoadEdc15Flash / LoadEdc16Flash) recomand totusi PC-ul**.
- Inainte de orice comanda Load/Write: fa dump/backup, tine bateria pe redresor, nu bloca telefonul
  (aplicatia tine ecranul aprins) si nu deconecta cablul.

## Licenta

Codul kw1281test este MIT (vezi `kw1281test/LICENSE.txt`). Partea Android se distribuie in aceleasi conditii.
