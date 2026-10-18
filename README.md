# BPP w WSL — instalator dla Windows

Jednoplikowy instalator `BPP-Setup.exe`, który stawia na zwykłym komputerze
z Windowsem działającą (testową) instancję BPP — Bibliografii Publikacji
Pracowników. Wszystko działa per-użytkownik, w osobnej dystrybucji WSL
o nazwie `BPP` (Debian + Docker + 7 kontenerów), dostępnej pod
`https://localhost:8443/`. Po instalacji w zasobniku systemowym działa
ikonka `BPP.exe` (otwórz, logi, udostępnienie w sieci lokalnej, zatrzymaj,
odinstaluj).

Szczegóły projektu: `docs/superpowers/specs/2026-10-18-bpp-wsl-design.md`.
Lista kontrolna do ręcznego testu na Windowsie: `docs/windows-checklist.md`.

## Wymagania (komputer użytkownika)

- Windows 10 22H2 lub Windows 11, **x64** (ARM nieobsługiwany),
- 8 GB RAM, 25 GB wolnego miejsca na dysku,
- dostęp do internetu (instalator pobiera ok. 2 GB obrazów kontenerów),
- **brak obsługi proxy HTTP** — sieć musi wpuszczać Dockera w WSL bezpośrednio,
- wolny port TCP 8443,
- uprawnienia administratora tylko jeśli trzeba włączyć funkcje Windows dla
  WSL albo dodać regułę zapory (UAC pojawia się wyłącznie dla systemowych
  `dism.exe` i `netsh.exe`).

## Jak zbudować

Budowanie działa na macOS i Linuksie (Windows nie jest potrzebny).

```bash
brew install dotnet            # .NET SDK 10
# Docker z możliwością budowania linux/amd64 (na Apple Silicon: emulacja)
build/build.sh <BPP_IMAGE_TAG> <wersja>
# np.
build/build.sh 202609.1402 0.1.0      # -> dist/BPP-Setup-0.1.0.exe
```

Wersja ma postać `N.N.N[.N]`, opcjonalnie z sufiksem (`0.1.0-rc1`); część
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

## Tekst na stronę pobierania (SmartScreen)

Dopóki plik nie jest podpisany, Windows SmartScreen pokaże ostrzeżenie
„System Windows ochronił ten komputer”. Do umieszczenia przy przycisku
pobierania:

> Po pobraniu Windows może wyświetlić niebieskie okno „System Windows
> ochronił ten komputer”, bo program nie jest jeszcze podpisany cyfrowo.
> Kliknij **Więcej informacji → Uruchom mimo to**.

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

`.github/workflows/build.yml` (runner `windows-latest`): `dotnet test`,
`dotnet build -c Release`, publish `BPP.exe` i smoke — `BPP.exe --bogus`
musi zakończyć się kodem 2 bez UI. **Nieprzetestowane**, dopóki repozytorium
nie ma zdalnego GitHuba.
