# FiveM SimRig FFB Bridge (v1.0.0)

Täysiverinen DirectInput- ja Force Feedback -silta **FiveM**-palvelimille. Mahdollistaa Direct Drive -rattien (esim. Moza SW20 V3), erillisten polkimien, analogisen käsijarrun sekä H-kaavio- ja sekventiaalivaihteistojen käytön pelissä ilman pelin omia ohjainrajoituksia.

## Ominaisuudet
- **Aito Force Feedback (DirectInput):** Keskialueen jousivastus, kaarre-G-voimat, tienpinnan töyssyt, luistovärinä ja säädettävä Softlock-päätyrajoitin.
- **Täysi monilaitetuki:** Ratti, kaasu, jarru, kytkin ja analoginen käsijarru eri USB-laitteilta samanaikaisesti.
- **Vaihteistotilat:** H-kaavio (1–6 + R + vapaa kytkinpolkimella), sekventiaalinen (SQ) ja automaatti.
- **Pelin sisäinen `F10`-valikko & HUD:** Säädä FFB-voimat, kääntösäde ja vaihteistotila lennosta pelissä; alakulman HUD näyttää vaihteen ja polkimien asennot reaaliajassa.

## Asennus

### 1. FiveM-palvelin (`fivem-ffb-bridge`)
1. Lataa `fivem-ffb-bridge.zip` [Releases](../../releases)-sivulta ja pura kansio palvelimen `resources/[standalone]/`-hakemistoon.
2. Lisää palvelimen `server.cfg`-tiedostoon:
   ```cfg
   ensure fivem-ffb-bridge
   ```

### 2. Windows-asiakas (`SimRigBridge.exe`)
1. Lataa `SimRigBridge.exe` [Releases](../../releases)-sivulta omaan kansioonsa.
2. **Suositus (HidHide):** Piilota ratti, polkimet ja käsijarru FiveM:ltä HidHide-sovelluksella ja lisää `SimRigBridge.exe` sallittujen sovellusten listalle, jotta FiveM ei kaappaa FFB-laitetta.
3. Käynnistä `SimRigBridge.exe`, valitse FFB-laitteeksi rattisi (esim. `SW20 V3`), määritä akselit ja napit, ja jätä **Ohjauksen käyrä (Gamma %)** arvoon **`100%`**.
4. Käynnistä FiveM ja avaa pelin sisäiset säädöt **`F10`**-näppäimellä.
