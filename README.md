# BPP na Windowsie — instalator testowy

**BPP (Bibliografia Publikacji Pracowników) na zwykłym komputerze z Windowsem,
jednym kliknięciem.** Instalator sam włącza WSL, stawia w nim Dockera, pobiera
obrazy BPP i uruchamia czystą instancję z kreatorem pierwszej konfiguracji.
Nie trzeba znać Linuksa, Dockera ani wiersza poleceń.

> ⚠️ **Wersja testowa i wczesna.** Służy do wypróbowania BPP, nie do pracy
> produkcyjnej. Instalator nie jest jeszcze podpisany cyfrowo i nie przeszedł
> pełnych testów na Windowsie — zgłaszaj problemy w
> [Issues](https://github.com/iplweb/bpp-wsl/issues).

## Pobieranie

**[⬇ Pobierz najnowszy `BPP-Setup.exe` z zakładki Releases](https://github.com/iplweb/bpp-wsl/releases/latest)**

## Wymagania

- Windows 10 22H2 lub Windows 11, **64-bitowy (x64)** — komputery ARM nie są obsługiwane,
- **8 GB RAM** i **25 GB wolnego miejsca** na dysku,
- internet (instalator pobiera ok. 2 GB) — bez serwera proxy,
- wolny port **8443**,
- hasło administratora **tylko wtedy**, gdy trzeba włączyć WSL albo otworzyć
  BPP dla innych komputerów w sieci. Resztę robi zwykły użytkownik.

## Instalacja krok po kroku

1. Pobierz `BPP-Setup-<wersja>.exe` i kliknij go dwukrotnie.
2. Jeśli Windows pokaże niebieskie okno **„System Windows ochronił ten
   komputer”**, kliknij **Więcej informacji → Uruchom mimo to**. (Pojawia się,
   bo program nie jest jeszcze podpisany.)
3. Kliknij **Zainstaluj**. Instalator sprawdzi komputer i pokaże postęp.
4. **Jeśli WSL nie jest włączony**, Windows poprosi o zgodę administratora
   (okno UAC z wydawcą *Microsoft Windows*). Postęp włączania widać w czarnym
   oknie konsoli. Potem instalator poprosi o **restart komputera**; po ponownym
   zalogowaniu instalacja wznowi się sama.
5. **Jeśli WSL wymaga aktualizacji** (Windows mówi wtedy „wsl --update”),
   instalator zrobi ją sam — w oknie konsoli widać pobieranie, może też
   pojawić się okno administratora. Nie zamykaj tego okna konsoli.
6. Pobieranie obrazów trwa od kilku do kilkunastu minut, zależnie od łącza.
7. Na koniec Windows może zapytać o zgodę na **regułę zapory** — zgódź się,
   jeśli BPP ma być widoczne z innych komputerów w sieci. Odmowa nie psuje
   instalacji: BPP będzie wtedy dostępne tylko na tym komputerze.
8. Kliknij **Otwórz BPP**.

Instalację można w każdej chwili przerwać przyciskiem **Anuluj** lub
krzyżykiem i dokończyć później, uruchamiając instalator ponownie.

## Pierwsze uruchomienie

Przeglądarka otworzy `https://localhost:8443/`. Zobaczysz ostrzeżenie
o certyfikacie — to normalne, bo certyfikat jest wygenerowany lokalnie.
Kliknij **Zaawansowane → Przejdź do localhost**.

Pojawi się **kreator konfiguracji BPP**: załóż konto administratora i wpisz
dane uczelni. Zrób to od razu — dopóki konta nie ma, kreator może otworzyć
każdy w sieci lokalnej.

Z innych komputerów w sieci BPP jest dostępne pod `https://<adres-IP-tego-komputera>:8443/`.

## Codzienne używanie

BPP **nie startuje samo** z Windowsem. Uruchamiasz je skrótem **BPP** w Menu
Start. Wtedy w zasobniku (obok zegara) pojawia się ikonka z menu:

| Pozycja | Co robi |
|---|---|
| **Otwórz BPP** | otwiera BPP w przeglądarce |
| **Pokaż logi** | pokazuje dziennik — przydatny przy zgłaszaniu problemów |
| **Udostępnij w sieci lokalnej…** | dodaje regułę zapory, gdy wcześniej jej odmówiono |
| **Zatrzymaj** | wyłącza BPP i zwalnia pamięć |
| **Odinstaluj** | usuwa BPP razem z danymi |

## Odinstalowanie

Menu ikonki → **Odinstaluj** albo **Ustawienia → Aplikacje → BPP**.
**Wszystkie dane testowe zostaną usunięte bezpowrotnie.** Instalacja nowszej
wersji również zaczyna od czystej bazy.

## Gdy coś nie działa

- **„Port 8443 jest zajęty”** — instalator podpowie, co go używa; jeśli to
  stare BPP, kliknij **Zatrzymaj BPP**.
- **Za mało pamięci/miejsca** — instalator odmówi i powie, czego brakuje.
- **Inne błędy** — menu ikonki → **Pokaż logi** → skopiuj i dołącz do
  zgłoszenia w [Issues](https://github.com/iplweb/bpp-wsl/issues).

## Jak to działa w środku

W WSL powstaje osobna dystrybucja `BPP` (Debian + Docker CE) tylko dla Twojego
konta. Działa w niej 7 kontenerów BPP (aplikacja, baza PostgreSQL, Redis,
zadania w tle, nginx z lokalnym certyfikatem). Program `BPP.exe` w zasobniku
uruchamia i zatrzymuje dystrybucję oraz przekazuje ruch z sieci lokalnej na
port 8443. Szczegóły:
[specyfikacja](docs/superpowers/specs/2026-10-18-bpp-wsl-design.md).

---

# Dla programistów

## Jak zbudować

Budowanie działa na macOS i Linuksie (Windows nie jest potrzebny).

```bash
brew install dotnet            # .NET SDK 10
# Docker z możliwością budowania linux/amd64 (na Apple Silicon: emulacja)
build/build.sh <BPP_IMAGE_TAG> <wersja>
# np.
build/build.sh 202609.1402 0.1.0      # -> dist/BPP-Setup-0.1.0.exe
```

Wersja ma postać `N.N.N`, opcjonalnie z sufiksem (`0.1.0-rc1`); część
numeryczna trafia do `-p:Version` obu programów, więc instalator i wpis
w „Aplikacje” pokazują właściwą wersję.

Skrypt: (1) buduje rootfs dystrybucji, (2) publikuje `BPP.exe` i
`BPP-Setup.exe` (`win-x64`, samodzielne, jeden plik, kompresja),
(3) dokleja do `BPP-Setup.exe` ładunek (`rootfs`, `BPP.exe`) narzędziem
`BPP.Pack`, (4) opcjonalnie podpisuje. Wypisuje ścieżkę i rozmiar wyniku.
Zawartość ładunku sprawdzisz:

```bash
dotnet run --project installer/src/BPP.Pack -- --list dist/BPP-Setup-0.1.0.exe
```

Jeśli `DOTNET_ROOT` nie jest ustawione, a istnieje
`/opt/homebrew/opt/dotnet/libexec`, skrypt użyje go automatycznie.

### Jak wybrać tag obrazów BPP

Tag jest wymagany i przypinany w instalatorze (brak domyślnego `latest`).
Najnowsze tagi z Docker Hub:

```bash
curl -s "https://hub.docker.com/v2/repositories/iplweb/bpp_appserver/tags?page_size=20&ordering=last_updated" \
  | jq -r '.results[].name'
```

Wybierz tag w formacie `RRRRMM.NNNN` (np. `202609.1402`), nie `latest`.

### Testy

```bash
build/test-distro.sh                         # testy bats dystrybucji
bash distro/tests/rootfs-smoke.sh            # smoke obrazu rootfs
export DOTNET_ROOT=/opt/homebrew/opt/dotnet/libexec
dotnet test installer
```

Pełny test end-to-end instalatora wymaga prawdziwego Windowsa x64 —
patrz `docs/windows-checklist.md`.

## Podpisywanie kodu (opcjonalne)

Domyślnie wyłączone. Włączenie podpisu `dist/BPP-Setup-<wersja>.exe` przez
[jsign](https://github.com/ebourg/jsign) (uruchamiany w kontenerze, jar
przypięty po wersji i SHA-256) — podpis następuje **po** doklejeniu ładunku:

```bash
export BPP_SIGN=1
export BPP_SIGN_KEYSTORE=/ścieżka/do/cert.pfx   # lub URL PKCS#11
export BPP_SIGN_STOREPASS=...
export BPP_SIGN_ALIAS=...
export BPP_SIGN_STORETYPE=PKCS12                # domyślnie PKCS12
build/build.sh 202609.1402 0.1.0
```

Azure Trusted Signing zamiast keystore: `BPP_SIGN_AZURE_ENDPOINT`,
`BPP_SIGN_AZURE_ACCOUNT`, `BPP_SIGN_AZURE_PROFILE`, `BPP_SIGN_AZURE_TOKEN`.
Przy `BPP_SIGN=1` bez wymaganych zmiennych build kończy się błędem.
Podpisać można też gotowy plik ręcznie: `build/sign.sh dist/BPP-Setup-0.1.0.exe`.
(Podpisywanie nie zostało jeszcze przetestowane — brak certyfikatu.)

## Publikacja wydania

Wydania buduje GitHub Actions (`.github/workflows/release.yml`, runner
`ubuntu-latest`): testy, `build/build.sh`, potem `BPP-Setup-<wersja>.exe`
i `SHA256SUMS.txt` trafiają do Releases jako *pre-release*.

```bash
git tag v0.1.1 && git push origin v0.1.1      # tag obrazów ze zmiennej repo BPP_IMAGE_TAG
gh variable set BPP_IMAGE_TAG --body 202609.1402   # zmiana tagu obrazów BPP
```

Albo ręcznie: **Actions → release → Run workflow** (wersja, opcjonalnie tag
obrazów, opcja „szkic” do testów).

## Uwagi bezpieczeństwa

- **BPP nasłuchuje na wszystkich interfejsach, także w sieciach publicznych**,
  dopóki działa (reguła zapory obejmuje profile Prywatny, Domena i Publiczny).
  To świadomy wybór (R9), nie przeoczenie. Bez reguły zapory (odmowa UAC)
  BPP jest dostępne tylko lokalnie.
- Reguła zapory jest **osobna dla każdego konta Windows**: nazywa się
  `BPP (8443) <8 znaków hex>`, gdzie sufiks to pierwsze 8 cyfr SHA-256
  z SID-u użytkownika, który instaluje (liczony w procesie bez uprawnień
  administratora). Odinstalowanie BPP z jednego konta nie usuwa reguły
  drugiego konta na tym samym komputerze.
- Kreator `/setup` jest osiągalny z sieci lokalnej, **dopóki nie powstanie
  konto administratora** — pierwszy, kto go otworzy, ustawia hasło admina.
  Załóż konto od razu po instalacji.
- Certyfikat TLS jest samopodpisany — przeglądarka ostrzeże (Zaawansowane →
  Przejdź do localhost). Ostrzeżenie pojawi się też u innych komputerów
  w LAN.
- HSTS jest wyłączony celowo (nginx nie wysyła nagłówka i usuwa ten
  z aplikacji), żeby samopodpisany certyfikat nie blokował przeglądarki
  „na stałe”.
- To wersja testowa: dane żyją w dysku dystrybucji `BPP`; „Odinstaluj”
  usuwa je bezpowrotnie.

## CI

`.github/workflows/build.yml` (runner `windows-latest`, przy każdym pushu
i PR): `dotnet test`, `dotnet build -c Release`, publish `BPP.exe` i smoke —
`BPP.exe --bogus` musi zakończyć się kodem 2 bez UI. Dystrybucja Linuksa nie
jest budowana w tym workflow — robi to `release.yml` (wyżej).

Lista kontrolna ręcznego testu na Windowsie: [docs/windows-checklist.md](docs/windows-checklist.md).
