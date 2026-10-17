# BPP dla Windows (WSL) — instalator testowej instancji

Data: 2026-10-18
Status: projekt do akceptacji

## 1. Po co to jest

Laik (bibliotekarz, osoba z uczelni rozważającej BPP) chce **wypróbować BPP
na własnym komputerze z Windowsem**, nie znając Dockera, Linuksa ani WSL.
Pobiera jeden plik `BPP-Setup.exe`, klika, widzi ładne okno z logo BPP
i paskiem postępu, a na końcu przeglądarka otwiera mu czyste BPP z kreatorem
pierwszego uruchomienia (`/setup/`), gdzie sam zakłada konto administratora
i uczelnię.

Sukces = osoba bez wiedzy technicznej dochodzi od pobrania pliku do
formularza kreatora BPP bez otwierania terminala i bez czytania instrukcji
(poza ewentualnym ostrzeżeniem SmartScreen i restartem po instalacji WSL).

## 2. Cele, nie-cele, anty-cele

**Cele**
- Jeden plik `BPP-Setup.exe` robi wszystko od kliknięcia, łącznie z
  instalacją WSL, jeśli go nie ma.
- Własna dystrybucja WSL (Debian) z **Docker CE** w środku (nie Docker
  Desktop).
- Przy pierwszej instalacji: ekran powitalny, pobranie obrazów BPP,
  wygenerowanie fałszywego (self-signed) certyfikatu, uruchomienie stosu.
- BPP dostępne pod `https://localhost:8443/` **oraz** pod
  `https://<każdy-adres-IP-komputera>:8443/` z sieci lokalnej — bez żadnych
  checkboxów, domyślnie wszystkie interfejsy.
- Czysta instancja z kreatorem (bez danych demo).
- Ikonka w zasobniku systemowym z menu: **Otwórz BPP / Pokaż logi /
  Zatrzymaj / Odinstaluj**.

**Nie-cele**
- Środowisko produkcyjne, backupy, monitoring (grafana, netdata, loki,
  alloy, dozzle, flower, ofelia, autoheal, pgbouncer, certbot, rclone).
- Aktualizacje w miejscu — nowa wersja = nowy instalator.
- Windows na ARM (obrazy `iplweb/bpp_*` są tylko `linux/amd64`).
- Dane demonstracyjne.
- Autostart BPP z Windowsem.
- Sieci z obowiązkowym proxy HTTP / filtrowaniem Docker Hub oraz instalacja
  offline — przy nieudanym `pull` czytelny komunikat („Nie udało się pobrać
  obrazów BPP — sprawdź połączenie z internetem”) i przycisk „Spróbuj
  ponownie” (decyzja Claude).

**Anty-cele (porażka, nawet jeśli „działa”)**
- Laik musi wpisać jakąkolwiek komendę w terminalu.
- Instalator zmienia globalne ustawienia WSL użytkownika (`.wslconfig`,
  `networkingMode=mirrored`) albo psuje jego inne dystrybucje.
- Po odinstalowaniu zostają śmieci: reguły zapory, skróty, wpisy
  w rejestrze, wirtualny dysk WSL.
- Instalator wymaga uprawnień administratora do czegokolwiek poza
  instalacją samego WSL i otwarciem zapory dla sieci lokalnej.
- Zmiany w repo `bpp` / obrazach BPP wymagane do działania — instalator
  korzysta z opublikowanych obrazów takimi, jakie są.

## 3. Ustalenia z rekonesansu (fakty)

Źródło: `/Volumes/SSD/Programowanie/bpp-deploy` i `/Volumes/SSD/Programowanie/bpp`.

- **Minimalny zestaw usług** (z `bpp-deploy/docker-compose.*.yml`):
  `dbserver` (`postgres:16.13`, ICU pl-PL, własny entrypoint autotune),
  `redis` (`redis:8.6.2`), `appserver` (`iplweb/bpp_appserver`, port 8000,
  `/health/`), `workerserver` (`iplweb/bpp_workerserver`,
  `CELERY_QUEUE=celery,denorm`), `celerybeat` (`iplweb/bpp_beatserver`),
  `denorm-queue` (`iplweb/bpp_denorm_queue`), `authserver`
  (`iplweb/bpp_authserver`, port 8001), `webserver`
  (`owasp/modsecurity-crs:nginx`).
- Główny `bpp-deploy/docker-compose.yml` `include:`-uje wszystko bez
  profili → **dystrybucja ma własny, odchudzony compose**, nie reużywa
  głównego.
- `appserver` sam inicjalizuje bazę przy starcie (`baseline_load` +
  `migrate`, grace healthchecka 1800 s). Bez połączenia przez pgbouncer:
  `BPP_APPSERVER_DB_HOST=dbserver`, `BPP_APPSERVER_DB_PORT=5432`.
- Kreator pierwszego uruchomienia (`bpp_setup_wizard`): przy braku
  użytkowników każdy URL przekierowuje na `/setup/step/admin_user/`, potem
  `/setup/step/uczelnia/`.
- **Hosty:** `ALLOWED_HOSTS` budowane wyłącznie z `DJANGO_BPP_HOSTNAME`
  (jeden) albo `DJANGO_BPP_HOSTNAMES` (CSV, minimum dwa) —
  `bpp/src/django_bpp/settings/base.py:652+`. Brak wildcardu.
- **nginx z bpp-deploy nie nadaje się bez zmian:** domyślny serwer 443 ma
  `ssl_reject_handshake on` (wejście po surowym IP = brak SNI = zerwany
  handshake), a `_bpp-locations.conf` wysyła `Host $host` (bez portu) →
  przekierowania Django gubiłyby `:8443`.
- `production.py`: `SECURE_SSL_REDIRECT=True`, HSTS 1 rok,
  `SECURE_PROXY_SSL_HEADER=("HTTP_X_FORWARDED_PROTO","https")`.
- Zmienne `.env` (wzór w `bpp-deploy/scripts/init-configs.sh` ok. l. 308):
  `DJANGO_BPP_DB_*`, `DJANGO_BPP_REDIS_*`, `DJANGO_BPP_SECRET_KEY`,
  `DJANGO_BPP_HOSTNAME(S)`, `DJANGO_BPP_CSRF_EXTRA_ORIGINS`,
  `DOCKER_VERSION`, `STATIC_ROOT=/staticroot/`,
  `DJANGO_BPP_MEDIA_ROOT=/mediaroot`,
  `DJANGO_SETTINGS_MODULE=django_bpp.settings.production`,
  `DJANGO_BPP_SSL_MODE=manual`, `DJANGO_BPP_HOST_BACKUP_DIR` (mount w
  dbserver — musi istnieć).
- Obrazy BPP: ~300 MB skompresowane każdy, tylko amd64, tagi `latest`,
  `staging`, datowe (`202609.1402`).
- Logo: `bpp/src/bpp/static/bpp/images/logo_bpp.png` (3248×1108 RGBA),
  `logo.png` (260×311). Brak SVG.

## 4. Architektura

Trzy części w jednym repo `bpp-wsl`:

```
bpp-wsl/
  distro/        # budowanie rootfs dystrybucji WSL (Debian + docker-ce + nasze pliki)
  installer/     # aplikacja .NET (instalator + ikonka w zasobniku, jeden .exe)
  build/         # skrypty budujące całość, opcjonalny krok podpisu
  docs/
```

### 4.1 Dystrybucja WSL (`distro/`)

- Baza: Debian stable (bookworm/trixie — wybór budującego), `linux/amd64`.
- Zawiera: `systemd` (włączony przez `/etc/wsl.conf` `[boot] systemd=true`),
  `docker-ce`, `docker-compose-plugin`, `openssl`, `curl`, `jq`.
- `/etc/wsl.conf`: `systemd=true`, `[user] default=root` (laik nigdy nie
  wchodzi do powłoki; brak OOBE z pytaniem o użytkownika),
  `[interop] appendWindowsPath=false`, `[automount]` może zostać domyślny.
- Pliki BPP w `/opt/bpp/`:
  - `docker-compose.yml` — 8 usług z sekcji 3, bez pgbouncera, bez
    monitoringu; `webserver` publikuje **tylko** `8443:443` (bez 80, bez
    UDP/QUIC — moja decyzja, upraszcza przekierowania).
  - `nginx/` — **własna** konfiguracja nginx (patrz 4.3).
  - `dbserver/` — skopiowane z bpp-deploy `docker-entrypoint-autotune.sh`
    i `autotune.sh` (wymagane przez obraz dbserver w compose).
  - `bin/bpp-ctl` — jedno narzędzie, które woła instalator/ikonka (4.2).
- Dane w nazwanych wolumenach dockera wewnątrz wirtualnego dysku
  dystrybucji (znikają razem z nią przy odinstalowaniu).
- Wersja obrazów BPP **przypięta** do konkretnego tagu datowego w momencie
  budowy instalatora (np. `202609.1402`), nie `latest` — moja decyzja:
  instalator ma być powtarzalny; tag jest parametrem buildu.
- **Obrazy nie są wpiekane** w dystrybucję — pobierane przy instalacji
  (`docker compose pull`). Wymaga internetu; instalator ma dzięki temu
  rozmiar rzędu kilkuset MB (rootfs) zamiast kilku GB.

### 4.2 `bpp-ctl` — kontrakt między Windowsem a dystrybucją

Wywoływane z Windowsa jako `wsl.exe -d BPP -u root -- /opt/bpp/bin/bpp-ctl <polecenie> [arg]`.
Wyjście maszynowe: **jedna linia JSON na zdarzenie** na stdout (do
paska postępu), logi ludzkie na stderr.

| Polecenie | Co robi |
|---|---|
| `init --hosts <csv>` | jednorazowo: losuje hasło DB i `SECRET_KEY` → `/opt/bpp/.env`; generuje certyfikat; `docker compose pull` z postępem; `up -d`; czeka na zdrowie appservera |
| `start --hosts <csv>` | aktualizuje listę hostów (jeśli się zmieniła: nowy certyfikat z SAN + nowe `DJANGO_BPP_HOSTNAMES`), `docker compose up -d`, czeka na zdrowie |
| `stop` | `docker compose stop` |
| `status` | JSON: stan kontenerów, zdrowie, adres IP dystrybucji (`hostname -I`) |
| `logs [usługa]` | ostatnie N linii `docker compose logs` |

Format zdarzeń postępu (propozycja, do dopracowania przez budującego):
`{"step":"pull","progress":0.42,"message":"Pobieranie iplweb/bpp_appserver…"}`.
Kroki: `config`, `cert`, `pull`, `start`, `migrate` (czekanie na
appserver — najdłuższe przy pierwszym starcie), `ready`.
Postęp `pull` liczony z bajtów warstw (np. `docker compose pull --progress json`
albo Docker Engine API przez gniazdo) — szczegół dla budującego.

**Hosty:** `--hosts` zawsze zawiera `localhost` + wszystkie adresy IPv4
interfejsów Windowsa w stanie „Up” (bez loopbacku, APIPA i wirtualnych
interfejsów WSL/Hyper-V). Z tego:
- `DJANGO_BPP_HOSTNAMES=localhost,127.0.0.1,<IP…>` (zawsze ≥ 2, więc
  zawsze wariant CSV),
- certyfikat self-signed z SAN: `DNS:localhost`, `IP:127.0.0.1`, `IP:<IP…>`,
  ważny np. 10 lat (to instancja testowa),
- `DJANGO_BPP_CSRF_EXTRA_ORIGINS` z `https://<host>:8443` dla każdego hosta
  (asekuracyjnie, choć przy poprawnym `Host` z portem Django i tak
  porównuje `Origin` z `request.get_host()`).
Kontenery są przebudowywane tylko przy zmianie listy (porównanie z
zapisaną ostatnią listą) — zwykły start nie restartuje stosu bez potrzeby.

Adresy IP odczytuje **strona Windowsa** (wewnątrz WSL2 w trybie NAT ich
nie widać).

### 4.3 Własny nginx

Ten sam obraz `owasp/modsecurity-crs:nginx` (spójność z produkcją) albo
zwykły `nginx` — wybór budującego; ważne są zachowania:
- jeden serwer `listen 443 ssl default_server` z naszym certyfikatem —
  **bez** `ssl_reject_handshake`, przyjmuje dowolny Host/SNI,
- `proxy_set_header Host $http_host;` (z portem),
  `X-Forwarded-Proto https`, `X-Forwarded-Host $http_host`,
- lokacje statyczne/media/upstreamy jak w `bpp-deploy/defaults/webserver/_bpp-locations.conf`
  (appserver, authserver dla `/__external_auth/` i `auth_request`),
  skopiowane i dostosowane — budujący sprawdza, które lokacje są potrzebne,
- healthcheck kontenera po HTTP wewnątrz kontenera (np. osobny
  `listen 80` niepublikowany, `/healthz`).

### 4.4 Aplikacja Windows (`installer/`)

.NET 8 WinForms (decyzja Claude — najprostsze, wystarczy na okno z logo
i paskiem), publikowane jako **jeden plik** `BPP-Setup.exe`
(`-r win-x64 --self-contained -p:PublishSingleFile=true`), z **osadzonym**
rootfs dystrybucji (`bpp-rootfs.tar.gz`) — laik ma jeden plik.

**Zasada uprawnień (decyzja użytkownika: ma działać bez admina, jeśli się
da).** Manifest `asInvoker` — instalator i ikonka działają jako **zwykły
użytkownik, który kliknął**. Dystrybucje WSL są per-użytkownik, więc import,
`bpp-ctl`, pliki, skróty i wpis odinstalowania należą do tego użytkownika.
Uprawnienia administratora są potrzebne tylko do dwóch rzeczy i wtedy
instalator uruchamia **osobny, krótki proces potomny z UAC**
(`BPP.exe --elevated <zadanie>`), który robi tylko to jedno i kończy się:

| Zadanie z UAC | Kiedy | Bez admina |
|---|---|---|
| `install-wsl` — `wsl --install --no-distribution` | tylko gdy WSL nie ma | nie da się — komunikat „Poproś administratora o zainstalowanie WSL” |
| `firewall` — reguła zapory przychodzącej dla `BPP.exe`, TCP 8443, profile Prywatny + Domena | raz, przy instalacji | BPP działa tylko na tym komputerze (`localhost`); w menu ikonki pozycja „Udostępnij w sieci lokalnej…” ponawia próbę |

Jeśli UAC zostanie zatwierdzony hasłem innej osoby (informatyka), nic się
nie psuje: oba zadania są ogólnosystemowe, a wszystko per-użytkownik
zrobił wcześniej i dalej robi proces zwykłego użytkownika.

Pliki programu: `BPP-Setup.exe` kopiuje się do
`%LOCALAPPDATA%\Programs\BPP\BPP.exe` (bez Program Files — nie wymaga
admina). Dysk dystrybucji: `%LOCALAPPDATA%\BPP\wsl\`. Wpis odinstalowania
w `HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\BPP`. Skrót
w Menu Start użytkownika.

Ten sam exe pełni role wybierane argumentem:

**Tryb instalatora (domyślny)**
1. **Ekran powitalny**: logo BPP, jedno zdanie co się stanie, przycisk
   „Zainstaluj”. Informacja o ~2 GB do pobrania i wymaganym internecie.
2. **Sprawdzenia wstępne**: CPU x64 (inaczej komunikat i koniec).
   **Wymagania sprzętowe (decyzja Claude):** < 8 GB RAM → ostrzeżenie
   „BPP może działać wolno” z możliwością kontynuacji; < 25 GB wolnego
   miejsca na dysku z `%LOCALAPPDATA%` → odmowa z komunikatem.
   `.wslconfig` nietknięty; compose bez dodatkowych limitów pamięci
   poza tym, co ustawia autotune postgresa.
   Port 8443 zajęty na Windowsie → komunikat z nazwą procesu, który go
   zajmuje, i koniec (port jest stały — decyzja użytkownika).
3. **WSL**: jeśli `wsl.exe --status` zawodzi / brak WSL2 → zadanie
   `install-wsl` z UAC; błąd wirtualizacji → zrozumiały komunikat. Jeśli
   wymagany restart → „Uruchom ponownie komputer” + wpis
   `HKCU\…\RunOnce` (rejestrowany przez proces zwykłego użytkownika, więc
   po restarcie wznawia się jako ten użytkownik, w tym samym kroku).
4. **Import dystrybucji**: `wsl --import BPP %LOCALAPPDATA%\BPP\wsl <rootfs.tar.gz> --version 2`.
   Jeśli `BPP` już istnieje → pytanie „Zainstalować ponownie? Dane testowe
   zostaną usunięte.” (kasowanie danych przy nowej wersji jest akceptowane).
5. **Konfiguracja i pobieranie**: `bpp-ctl init --hosts …`, czytanie
   strumienia JSON → pasek postępu + etykieta bieżącego kroku + rozwijane
   „Szczegóły” z logiem.
6. **Sieć lokalna**: zadanie `firewall` z UAC. Odmowa / brak hasła admina
   → instalacja idzie dalej, końcowy ekran mówi, że BPP jest dostępne tylko
   na tym komputerze.
7. **Skróty**: Menu Start „BPP” (tryb ikonki), wpis odinstalowania.
8. **Koniec**: „Gotowe” + przycisk „Otwórz BPP” → `https://localhost:8443/`
   + krótka informacja, że przeglądarka ostrzeże o certyfikacie i jak je
   ominąć; uruchamia tryb ikonki.

**Tryb ikonki w zasobniku (`--tray`, ze skrótu „BPP”)**
- Zwykły użytkownik, bez UAC. Pojedyncza instancja — drugie kliknięcie
  skrótu tylko otwiera przeglądarkę.
- Przy starcie: odczytuje IP Windowsa, woła `bpp-ctl start --hosts …`,
  uruchamia przekaźnik sieciowy (4.5), ikonka pokazuje stan
  (uruchamianie / działa / błąd), dymek „BPP działa”.
- Utrzymuje proces `wsl.exe -d BPP` przy życiu, dopóki BPP ma działać
  (WSL wyłącza dystrybucję bez aktywnej sesji — do weryfikacji, patrz 7).
- Reaguje na zmianę adresów sieciowych (`NetworkChange.NetworkAddressChanged`):
  przepina przekaźnik i woła `bpp-ctl start --hosts …` z nową listą.
- Menu:
  - **Otwórz BPP** → przeglądarka na `https://localhost:8443/`,
  - **Pokaż logi** → okno z `bpp-ctl logs` (przewijane, przycisk „Kopiuj
    do schowka” do zgłoszeń),
  - **Udostępnij w sieci lokalnej…** → tylko gdy reguły zapory brak,
  - **Zatrzymaj** → `bpp-ctl stop`, `wsl --terminate BPP`, zamknięcie
    ikonki,
  - **Odinstaluj** → potwierdzenie, potem tryb deinstalacji.

**Tryb deinstalacji (`--uninstall`)**, jako zwykły użytkownik:
`wsl --unregister BPP` (dane i dysk), katalogi `%LOCALAPPDATA%\BPP`
i `%LOCALAPPDATA%\Programs\BPP` (exe usuwa się z opóźnieniem, np. przez
`cmd /c timeout & rmdir`), skrót, wpis odinstalowania, ewentualny
`RunOnce`. Jeśli reguła zapory istnieje → jedno zadanie z UAC
`remove-firewall`; odmowa → reguła zostaje (nieszkodliwa — dotyczy
nieistniejącego już exe) i komunikat o tym. **Nie** odinstalowuje samego
WSL.

### 4.5 Dostęp z sieci lokalnej — przekaźnik w ikonce

WSL2 domyślnie w NAT: `localhost:8443` działa automatycznie
(localhostForwarding WSL), ale z LAN nie. Zamiast `netsh interface
portproxy` (wymaga admina przy każdej zmianie IP dystrybucji, która
następuje przy każdym starcie WSL) — **decyzja Claude:** ikonka sama jest
przekaźnikiem TCP. Nasłuchuje na `<IP-LAN>:8443` dla każdego adresu
interfejsu z listy hostów (nie na `0.0.0.0` i nie na `127.0.0.1`, żeby nie
kolidować z localhostForwarding WSL) i przekazuje surowy strumień TCP
(TLS kończy nginx w dystrybucji) do `127.0.0.1:8443`. Nasłuch na porcie
> 1024 nie wymaga admina; zaporę otwiera jednorazowo reguła programowa dla
`BPP.exe`. Przekaźnik żyje tyle, co ikonka — czyli tyle, co BPP.

Skutki: brak zadania harmonogramu, brak portproxy, nic do odświeżania
z uprawnieniami admina.

Uwaga: adres klienta widziany przez nginx/Django to adres lokalny
(przekaźnik), nie prawdziwy adres w LAN — dla instancji testowej bez
znaczenia.

## 5. Budowanie (`build/`)

- Rootfs: `docker buildx build --platform linux/amd64` z `distro/Dockerfile`
  → `docker export` → `bpp-rootfs.tar.gz`. Działa na tym Macu (emulacja
  amd64) i na Linuxie/CI.
- Exe: `dotnet publish installer -r win-x64 -c Release
  -p:PublishSingleFile=true -p:SelfContained=true
  -p:EnableWindowsTargeting=true` — też buduje się z macOS/Linuxa.
- Ikona `.ico` i grafika okna generowane z `logo_bpp.png` / `logo.png`
  (np. ImageMagick) w kroku budowania.
- Parametry: tag obrazów BPP (`BPP_IMAGE_TAG`), wersja instalatora.
- **Podpis kodu: krok opcjonalny, domyślnie wyłączony.** Gdy pojawi się
  certyfikat (Azure Trusted Signing albo klasyczny), krok podpisuje
  `BPP-Setup.exe` (`signtool`/`jsign`). Do tego czasu SmartScreen pokazuje
  ostrzeżenie — opisane w tekście strony pobierania (README).
- Wynik: `dist/BPP-Setup-<wersja>.exe`.

## 6. Kryteria akceptacji

Na czystym Windows 11 x64 (i osobno Windows 10 22H2):
1. Bez WSL: dwuklik → UAC → instalacja WSL → restart → instalacja wznawia
   się sama → kończy się przyciskiem „Otwórz BPP”.
2. Z WSL: od dwukliku do „Gotowe” bez żadnej interakcji poza „Zainstaluj”
   i jednym UAC (zapora).
2a. Konto **zwykłego użytkownika** z WSL już zainstalowanym: odmowa UAC
   → instalacja kończy się sukcesem, BPP działa na `localhost:8443`,
   ikonka oferuje „Udostępnij w sieci lokalnej…”. Dystrybucja widoczna
   w `wsl -l -v` **tego** użytkownika.
2b. Zwykły użytkownik, UAC zatwierdzony hasłem innego konta (admina):
   dystrybucja, skróty i ikonka nadal należą do zwykłego użytkownika.
3. Pasek postępu porusza się podczas pobierania obrazów (nie stoi na 0%
   przez minuty).
4. `https://localhost:8443/` → (po zaakceptowaniu certyfikatu) przekierowanie
   na `/setup/step/admin_user/`; **POST formularza admina przechodzi**
   (brak błędu CSRF), następnie krok uczelni, potem logowanie.
5. To samo z innego komputera w LAN przez `https://<IP-Windowsa>:8443/`,
   łącznie z POST-em formularza.
6. Przekierowania Django zachowują `:8443` (np. po zalogowaniu).
7. Restart Windowsa → skrót „BPP” → ikonka → BPP działa z tymi samymi
   danymi, LAN nadal działa (nowe IP WSL obsłużone).
8. Zmiana sieci (inne IP Wi-Fi) → ponowne uruchomienie ikonki → BPP
   dostępne pod nowym IP.
9. „Zatrzymaj” → `wsl -l -v` pokazuje BPP jako Stopped.
10. „Odinstaluj” → brak dystrybucji, reguły zapory, katalogów
    w `%LOCALAPPDATA%`, skrótu, wpisu w „Aplikacje i funkcje”. Inne dystrybucje WSL nietknięte.
11. „Pokaż logi” pokazuje logi kontenerów.

## 7. Do weryfikacji na prawdziwym Windowsie (nie zakładać)

**Środowisko:** autor pracuje wyłącznie na macOS (Apple Silicon), bez
maszyny z Windowsem x64. Budowanie (rootfs, exe) odbywa się na Macu.
Weryfikacja na Macu obejmuje: build rootfs, uruchomienie stosu z rootfs
w Dockerze (emulacja amd64) z `bpp-ctl init/start/stop/status/logs`,
testy kontraktu JSON `bpp-ctl`, kompilację exe i testy jednostkowe logiki
.NET niezależnej od Windowsa (parsowanie zdarzeń, lista hostów, przekaźnik
TCP). Do tego CI GitHub Actions `windows-latest` (decyzja Claude) — build,
testy jednostkowe, uruchomienie exe w trybach nie wymagających WSL.
Pełne kryteria z sekcji 6 wykonuje człowiek na Windowsie x64 wg listy;
budujący przygotowuje ją jako checklistę w `docs/`.


- Czy dystrybucja z systemd i dockerem jest wyłączana przez WSL po
  zamknięciu ostatniej sesji `wsl.exe` (i po jakim czasie) — czy ikonka
  musi trzymać proces podtrzymujący.
- Czy przekaźnik ikonki na `<IP-LAN>:8443` współistnieje z localhostForwarding
  WSL na `127.0.0.1:8443` (Windows a wiązanie konkretnych adresów vs
  wildcard).
- Czy reguła zapory programowa dla exe w `%LOCALAPPDATA%` wystarcza
  i czy Windows nie pokazuje mimo to własnego monitu zapory.
- Czy `wsl --install` uruchomione z konta admina (UAC innej osoby)
  instaluje WSL tak, że zwykły użytkownik może go od razu używać.
- Czy `wsl --install` na Windows 10 wymaga restartu zawsze, czy zależnie
  od stanu; zachowanie `RunOnce` przy podniesionych uprawnieniach.
- HSTS (1 rok) z `production.py` zostaje w przeglądarce dla `localhost` —
  może przeszkadzać w innych lokalnych projektach użytkownika na
  `http://localhost`. Do oceny; ewentualnie nadpisać nagłówek w naszym
  nginx (`Strict-Transport-Security` usunięty/`max-age=0`).

## 8. Decyzje podjęte przez Claude (do przejrzenia)

- Debian + systemd + docker-ce w dystrybucji; użytkownik root domyślny.
- Port 8443, tylko HTTPS, bez publikowania 80 i QUIC.
- Obrazy BPP przypięte do tagu datowego podanego przy budowie.
- Własny nginx zamiast reużycia konfiguracji bpp-deploy.
- Lista hostów i certyfikat odświeżane przy każdym starcie z IP odczytanych
  po stronie Windowsa; brak zmian w repo `bpp`.
- .NET 8 WinForms, jeden exe z osadzonym rootfs, trzy tryby
  (instalator / ikonka / deinstalacja) + krótkie tryby `--elevated`.
- Zapora tylko dla profili Prywatny i Domena.
- Instalacja per-użytkownik (`asInvoker`, `%LOCALAPPDATA%`, HKCU);
  UAC tylko w osobnych procesach potomnych: instalacja WSL, zapora.
- Dostęp z LAN przez przekaźnik TCP wbudowany w ikonkę zamiast
  `netsh portproxy` i zadania harmonogramu.
- Certyfikat self-signed ważny 10 lat.
- „Pokaż logi” dodane do menu; bez „Aktualizuj”.
- Podpis kodu jako wyłączony krok builda.
- Minimalne wymagania: ostrzeżenie < 8 GB RAM, odmowa < 25 GB dysku.
- Brak obsługi proxy HTTP i trybu offline; czytelny błąd + „Spróbuj ponownie”.

## 9. Pozostawione budującemu

- Konkretna wersja Debiana, sposób liczenia postępu `pull`.
- Dokładny format zdarzeń JSON `bpp-ctl` (w ramach opisanego kontraktu).
- Wygląd okna (w granicach: logo BPP, pasek postępu, etykieta kroku,
  rozwijane szczegóły).
- Które lokacje z `_bpp-locations.conf` są potrzebne w uproszczonym nginx.
