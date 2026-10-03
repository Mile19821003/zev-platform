# Dependency/security status — 27.09.2026.

| Komponenta | Komanda | Rezultat | Odluka |
|---|---|---|---|
| Web | `npm audit --json` | 0 prijavljenih ranjivosti (119 dependencies u audit metapodacima) | PASS za zaključani web lockfile; ponoviti u CI. |
| Mobile | `npm audit --json` | 23 moderate, 0 high/critical; uglavnom Expo/CLI transitive lanac, uključujući `uuid <11.1.1` (`GHSA-w5hq-g745-h8pq`) | **Otvoreno**. Provjeriti Expo-kompatibilno ažuriranje i ponovno skenirati; ne raditi slijepi major upgrade. |
| .NET | `dotnet list src/Zev.Api/Zev.Api.csproj package --vulnerable --include-transitive` | **NOT VERIFIED**: NuGet feed `https://api.nuget.org/v3/index.json` nedostupan; `NU1900` je warning-as-error. | Ponoviti sken u CI/okruženju s ispravnim TLS/feed pristupom i popisati konkretne pakete/verzije. |

`npm ci --cache ../../work/npm-cache --no-audit --no-fund` i build/typecheck su naknadno uspjeli. Prvi offline `npm ci` nije uspio zbog nedostajućih artefakata u cache-u; nije označen kao uspješna provjera. Za mobilnih 23 nalaza `npm audit` daje `fixAvailable=false` za neke Expo podpakete; produkcijska odluka zahtijeva trijažu stvarnog runtime dosega, dostupne zakrpe i test na uređaju. Ovaj sken nije penetration test ni dokaz odsustva IDOR-a.

## Mobile trijaža na zaključanom lockfile-u

Ponovljeni `npm audit --json --prefix mobile` daje 23 moderate, 0 high/critical. `fixAvailable` je npm-ova procjena, **ne** dokazana Expo/RN kompatibilnost. Za posredne Expo stavke audit nije objavio pojedinačnu sigurnu fiksnu verziju; „provjeriti“ nije odobrenje za slijepi upgrade. Direktni Expo paketi su međusobno vezani za SDK 57/RN 0.86, pa ih ne treba razdvajati bez Expo compatibility i uređajskih testova.

| Paket | Veza | Severity | Audit fixAvailable | Potvrđena fiksna verzija / odluka |
|---|---|---|---|---|
| `@expo/cli` | transitive | moderate | yes | Nije zasebno navedena; provjeriti SDK-kompatibilan update |
| `@expo/config` | transitive | moderate | yes | Nije zasebno navedena; provjeriti |
| `@expo/config-plugins` | transitive | moderate | no | Nema kompatibilnog fixa u auditu |
| `@expo/dom-webview` | transitive | moderate | yes | Nije zasebno navedena; provjeriti |
| `@expo/inline-modules` | transitive | moderate | yes | Nije zasebno navedena; provjeriti |
| `@expo/local-build-cache-provider` | transitive | moderate | yes | Nije zasebno navedena; provjeriti |
| `@expo/log-box` | transitive | moderate | yes | Nije zasebno navedena; provjeriti |
| `@expo/metro-config` | transitive | moderate | yes | Nije zasebno navedena; provjeriti |
| `@expo/prebuild-config` | transitive | moderate | yes | Nije zasebno navedena; provjeriti |
| `@expo/router-server` | transitive | moderate | yes | Nije zasebno navedena; provjeriti |
| `babel-preset-expo` | transitive | moderate | yes | Nije zasebno navedena; provjeriti |
| `expo` | direct | moderate | no | Trenutna SDK linija nema audit fix |
| `expo-asset` | transitive | moderate | yes | Nije zasebno navedena; provjeriti |
| `expo-constants` | transitive | moderate | yes | Nije zasebno navedena; provjeriti |
| `expo-file-system` | direct | moderate | no | Nema audit fix; SDK usklađivanje obavezno |
| `expo-font` | transitive | moderate | yes | Nije zasebno navedena; provjeriti |
| `expo-image-loader` | transitive | moderate | yes | Nije zasebno navedena; provjeriti |
| `expo-image-picker` | direct | moderate | no | Nema audit fix; SDK usklađivanje obavezno |
| `expo-keep-awake` | transitive | moderate | yes | Nije zasebno navedena; provjeriti |
| `expo-secure-store` | direct | moderate | no | Nema audit fix; SDK usklađivanje obavezno |
| `expo-sharing` | direct | moderate | no | Nema audit fix; SDK usklađivanje obavezno |
| `uuid` | transitive | moderate | no | Advisory `GHSA-w5hq-g745-h8pq`: fiksno `>=11.1.1`; ulančano kroz `xcode`, bez kompatibilnog fixa |
| `xcode` | transitive | moderate | no | Ulančano preko `uuid`; nema audit fix |

Nije primijenjen `npm audit fix --force` ni major upgrade bez uređajskog UAT-a. Ponovljeni NuGet scan i dalje je **NOT VERIFIED**: `NU1900` pri pristupu `https://api.nuget.org/v3/index.json`. Lokalni build uz `NuGetAudit=false` nije zamjena za vulnerability scan.
