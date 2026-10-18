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
  `denorm-queue` (`iplweb/bpp_denorm_queue`), `webserver`
  (`owasp/modsecurity-crs:nginx`). `authserver` (`iplweb/bpp_authserver`)
  **nie jest potrzebny**: `/__external_auth/` występuje tylko w
  `settings/auth_server.py` i `urls_auth_server.py`, a nginx używa go
  wyłącznie do `auth_request` przed grafaną/dozzle/flower/netdata
  (`_bpp-locations.conf:173-346`) — wszystko to nie-cele.
- bpp-deploy ma limity pamięci na usługach (`docker-compose.database.yml:79-83`,
  `application.yml:120-124`, `workers.yml:51-57`); autotune postgresa bez
  limitu cgroup liczy się od całej pamięci VM (`dbserver/autotune.sh:79-91`).
- Obraz `owasp/modsecurity-crs:nginx` działa jako uid 101 i wymaga
  kontenera `webserver-init` (root) do ustawienia uprawnień klucza
  (`docker-compose.infrastructure.yml:50-92`) oraz zmiennych WAF
  (`infrastructure.yml:124-132`).
- HSTS jest ustawiany w dwóch miejscach: Django (`production.py:169-170`)
  i nginx (`defaults/webserver/security-headers.conf:4`).
- Główny `bpp-deploy/docker-compose.yml` `include:`-uje wszystko bez
  profili → **dystrybucja ma własny, odchudzony compose**, nie reużywa
  głównego.
- `appserver` sam inicjalizuje bazę przy starcie (`baseline_load` +
  `migrate`; healthcheck zdefiniowany w compose, nie w obrazie —
  `application.yml:111-119`, grace 1800 s). Bez połączenia przez pgbouncer:
  `BPP_APPSERVER_DB_HOST=dbserver`, `BPP_APPSERVER_DB_PORT=5432`.
- Kreator pierwszego uruchomienia (`bpp_setup_wizard`): przy braku
  użytkowników każdy URL przekierowuje na `/setup/step/admin_user/`, potem
  `/setup/step/uczelnia/`.
- **Hosty:** `ALLOWED_HOSTS` = stałe `127.0.0.1`, `appserver`,
  `appserver:8000` + `DJANGO_BPP_HOSTNAME` (jeden) albo
  `DJANGO_BPP_HOSTNAMES` (CSV, minimum dwa) —
  `bpp/src/django_bpp/settings/base.py:652-702`. Brak wildcardu.
  `CSRF_TRUSTED_ORIGINS` budowane bez portu (`base.py:704`), dodatki przez
  `DJANGO_BPP_CSRF_EXTRA_ORIGINS`. `USE_X_FORWARDED_HOST` nie jest
  ustawione — liczy się nagłówek `Host`.
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
  - `docker-compose.yml` — 7 usług: `dbserver`, `redis`, `appserver`,
    `workerserver`, `celerybeat`, `denorm-queue`, `webserver`; bez
    pgbouncera, authservera i monitoringu. `webserver` publikuje **tylko**
    `8443:443` (bez 80, bez UDP/QUIC — decyzja Claude). Wszystkie usługi
    `restart: unless-stopped` (nie `always` — dockerd nie może podnieść
    starego stosu ze starą listą hostów, zanim `bpp-ctl start` ją
    zaktualizuje). Userland-proxy dockera zostaje włączony (domyślnie) —
    localhostForwarding WSL potrzebuje słuchającego gniazda.
  - **Limity pamięci (decyzja Claude, po recenzji):** `dbserver` 1 GB,
    `appserver` 1,5 GB, `workerserver` 1 GB z niskim
    `CELERY_WORKER_CONCURRENCY_PERCENT` (np. 1–2 procesy), `redis` 256 MB,
    `celerybeat`/`denorm-queue` po 256 MB, `webserver` 128 MB — razem
    ~4,5 GB, mieści się w domyślnych 50% RAM maszyny 8 GB z zapasem na
    system. Autotune postgresa liczy wtedy od limitu cgroup. Wartości do
    korekty po pomiarze.
  - `nginx/` — **własna** konfiguracja nginx (patrz 4.3).
  - `dbserver/` — skopiowane z bpp-deploy `docker-entrypoint-autotune.sh`
    i `autotune.sh` (wymagane przez obraz dbserver w compose).
  - `bin/bpp-ctl` — jedno narzędzie, które woła instalator/ikonka (4.2).
- Dane w nazwanych wolumenach dockera wewnątrz wirtualnego dysku
  dystrybucji (znikają razem z nią przy odinstalowaniu). `ext4.vhdx`
  nigdy się nie kurczy; po imporcie instalator próbuje
  `wsl --manage BPP --set-sparse true` (bez admina, nowsze WSL; błąd
  ignorowany).
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
| `start --hosts <csv>` | `hwclock -s` (zegar WSL2 rozjeżdża się po uśpieniu), aktualizuje listę hostów (jeśli się zmieniła: nowy certyfikat z SAN + nowe `DJANGO_BPP_HOSTNAMES`), `docker compose up -d`, czeka na zdrowie |
| `stop` | `docker compose stop` |
| `status` | JSON: stan kontenerów, zdrowie appservera |
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
- `DJANGO_BPP_HOSTNAMES=localhost,<IP…>`, a gdy brak adresów LAN —
  `DJANGO_BPP_HOSTNAME=localhost` (wariant jedno/wielohostowy wybiera
  `bpp-ctl`; `127.0.0.1` jest już w `ALLOWED_HOSTS` na stałe),
- certyfikat self-signed z SAN: `DNS:localhost`, `IP:127.0.0.1`, `IP:<IP…>`,
  ważny np. 10 lat (to instancja testowa),
- `DJANGO_BPP_CSRF_EXTRA_ORIGINS` z `https://<host>:8443` dla każdego hosta
  (wymagane — `CSRF_TRUSTED_ORIGINS` z BPP nie ma portu; dodatkowo nginx
  przekazuje `Host` z portem, więc `Origin` zgadza się też z
  `request.get_host()`).
Kontenery są przebudowywane tylko przy zmianie listy (porównanie z
zapisaną ostatnią listą) — zwykły start nie restartuje stosu bez potrzeby.

Adresy IP odczytuje **strona Windowsa** (wewnątrz WSL2 w trybie NAT ich
nie widać).

### 4.3 Własny nginx

**Decyzja Claude (po recenzji):** zwykły obraz `nginx:<przypięta wersja>`
(nie `owasp/modsecurity-crs` — ten wymaga kontenera init do uprawnień
klucza, konfiguracji WAF i ma pułapki ścieżek szablonów) i **własny,
krótki `nginx.conf` pisany od zera**, nie kopia `_bpp-locations.conf`
(ta ciągnie ukryte zależności: `map $connection_upgrade`, `limit_req_zone`,
`bpp-global-limits.inc` z `25-render-bpp-limits.sh`, `maintenance.html`,
`/__bpp_500__`). Zachowania:
- jeden serwer `listen 443 ssl default_server` z naszym certyfikatem —
  **bez** `ssl_reject_handshake`, przyjmuje dowolny Host/SNI,
- lokacje: `/static/` i `/media/` z wolumenów współdzielonych
  z appserverem (ścieżki jak w bpp-deploy), reszta → `appserver:8000`
  (z obsługą websocketów, jeśli appserver ich używa — budujący sprawdza),
- `proxy_set_header Host $http_host;` (z portem),
  `X-Forwarded-Proto https`, `X-Forwarded-For`,
- **bez HSTS:** brak `security-headers.conf`, a nagłówek z Django
  usuwany `proxy_hide_header Strict-Transport-Security;` — inaczej
  przeglądarka laika przez rok wymuszałaby HTTPS na każdym
  `http://localhost`,
- klucz certyfikatu czytelny dla procesu nginx (`bpp-ctl` ustawia
  uprawnienia; bez kontenera init),
- healthcheck kontenera: osobny `listen 80` (niepublikowany) z `/healthz`.

### 4.4 Aplikacja Windows (`installer/`)

.NET 10 LTS WinForms (decyzja Claude; pierwotnie 8, ale 8 traci wsparcie 2026-11 — najprostsze, wystarczy na okno z logo
i paskiem), publikowane jako single-file
(`-r win-x64 --self-contained -p:PublishSingleFile=true`).

**Pakowanie (decyzja Claude, po recenzji):** dwa programy z jednego
projektu/rozwiązania:
- `BPP.exe` — mały program ikonki/deinstalacji (bez rootfs),
- `BPP-Setup.exe` — instalator; rootfs (`bpp-rootfs.tar.gz`, kilkaset MB)
  i `BPP.exe` są **doklejone na końcu pliku** jako ładunek (stopka
  z offsetem i długością, odczyt przez `Environment.ProcessPath`), nie jako
  `EmbeddedResource` (kompilacja i pamięć) ani
  `IncludeAllContentForSelfExtract` (rozpakowanie do `%TEMP%` przy każdym
  starcie). Laik nadal pobiera jeden plik.

**Zasada uprawnień (decyzja użytkownika: ma działać bez admina, jeśli się
da).** Manifest `asInvoker` — instalator i ikonka działają jako **zwykły
użytkownik, który kliknął**. Dystrybucje WSL są per-użytkownik, więc import,
`bpp-ctl`, pliki, skróty i wpis odinstalowania należą do tego użytkownika.
Uprawnienia administratora są potrzebne tylko do dwóch rzeczy i wtedy
instalator uruchamia przez UAC (`runas`) **wyłącznie Microsoftowe
`dism.exe` / `netsh.exe` z System32** — nigdy własne exe, bo leżą w katalogu
zapisywalnym przez użytkownika i mogłyby zostać podmienione, zanim admin
zatwierdzi UAC. Nie ma trybu `--elevated` *(zmienione po przeglądzie bezpieczeństwa, R8)*. **Wszystkie ścieżki przekazywane są jako bezwzględne
argumenty** — proces z UAC może działać na innym koncie (informatyka),
więc nigdy nie wylicza `%LOCALAPPDATA%`/HKCU sam:

| Polecenie z UAC | Kiedy | Bez admina |
|---|---|---|
| `dism.exe` (enable-wsl) — włączenie funkcji Windows `Microsoft-Windows-Subsystem-Linux` i `VirtualMachinePlatform` (DISM) | tylko gdy ich brak | nie da się — komunikat „Poproś administratora o włączenie WSL” |
| `netsh.exe` (firewall, ścieżka bezwzględna BPP.exe) — reguła zapory przychodzącej dla programu, TCP 8443, profile Prywatny + Domena + **Publiczny** | raz, przy instalacji | BPP działa tylko na tym komputerze (`localhost`); w menu ikonki pozycja „Udostępnij w sieci lokalnej…” ponawia próbę |

Sam pakiet WSL (`wsl --install --no-distribution` / `wsl --update`)
instaluje **zwykły użytkownik** po restarcie — pakiet sklepowy WSL bywa
instalowany per-użytkownik, więc instalowanie go z konta admina mogłoby
zostawić zwykłemu użytkownikowi tylko zaślepkę. (Do weryfikacji, §7.)

Profil Publiczny włączony (decyzja Claude, po recenzji): Windows 11
domyślnie oznacza nowe sieci jako publiczne, więc bez niego dostęp z LAN
po cichu nie działałby na większości laptopów; reguła i tak dotyczy tylko
`BPP.exe` i portu 8443.

**Przekaźnik nie wiąże adresów LAN, dopóki reguła zapory nie istnieje** —
inaczej Windows pokaże zwykłemu użytkownikowi „Alert zabezpieczeń”, a jego
„Anuluj” tworzy trwałą regułę **blokującą** `BPP.exe`.

Jeśli UAC zostanie zatwierdzony hasłem innej osoby (informatyka), nic się
nie psuje: oba zadania są ogólnosystemowe, a wszystko per-użytkownik
zrobił wcześniej i dalej robi proces zwykłego użytkownika.

Pliki programu: instalator wypakowuje `BPP.exe` do
`%LOCALAPPDATA%\Programs\BPP\BPP.exe` (bez Program Files — nie wymaga
admina). Dysk dystrybucji: `%LOCALAPPDATA%\BPP\wsl\`. Wpis odinstalowania
w `HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\BPP`. Skrót
w Menu Start użytkownika.

Ten sam exe pełni role wybierane argumentem:

**Tryb instalatora (domyślny)**
1. **Ekran powitalny**: logo BPP, jedno zdanie co się stanie, przycisk
   „Zainstaluj”. Informacja o ~2 GB do pobrania i wymaganym internecie.
2. **Sprawdzenia wstępne**: CPU x64 (inaczej komunikat i koniec).
   **Wymagania sprzętowe (decyzja Claude, po recenzji):** < 8 GB RAM →
   **odmowa** z komunikatem (przy domyślnym limicie WSL 50% RAM mniej nie
   zmieści stosu); < 25 GB wolnego miejsca na dysku z `%LOCALAPPDATA%` →
   odmowa (kilka GB rozpakowanych obrazów + baza + niekurczący się
   `ext4.vhdx`). `.wslconfig` nietknięty; limity pamięci w compose (4.1).
   Port 8443 zajęty na Windowsie → komunikat z nazwą procesu, który go
   zajmuje, i koniec (port jest stały — decyzja użytkownika). Sprawdzenie
   **przed** pierwszym uruchomieniem dystrybucji (potem port trzyma
   przekaźnik localhost WSL).
3. **WSL**: jeśli `wsl.exe --status` zawodzi / brak WSL2 (uwaga: wyjście
   `wsl.exe` jest w UTF-16LE, chyba że `WSL_UTF8=1`) → zadanie `enable-wsl`
   z UAC; błąd wirtualizacji → zrozumiały komunikat. Po restarcie
   (i bez restartu, jeśli funkcje już były) — `wsl --install
   --no-distribution` jako zwykły użytkownik. Jeśli
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
- Z `systemd=true` dystrybucja nie gaśnie po zamknięciu ostatniej sesji
  `wsl.exe` — działa do `wsl --terminate`. Proces podtrzymujący prawdopodobnie
  zbędny; do weryfikacji (§7).
- Reaguje na zmianę adresów sieciowych (`NetworkChange.NetworkAddressChanged`):
  przepina przekaźnik i woła `bpp-ctl start --hosts …` z nową listą.
- Zamknięcie ikonki (Zatrzymaj, wylogowanie) → `bpp-ctl stop` przed
  `wsl --terminate BPP`.
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
i `%LOCALAPPDATA%\Programs\BPP` (działający `BPP.exe` usuwa się
z opóźnieniem, np. przez `cmd /c timeout & rmdir`), skrót, wpis odinstalowania, ewentualny
`RunOnce`. Jeśli reguła zapory istnieje → jedno zadanie z UAC
`remove-firewall`; odmowa → reguła zostaje (nieszkodliwa — dotyczy
nieistniejącego już exe) i komunikat o tym. **Nie** odinstalowuje samego
WSL.

### 4.5 Dostęp z sieci lokalnej — przekaźnik w ikonce

WSL2 domyślnie w NAT: `localhost:8443` działa automatycznie
(localhostForwarding WSL), ale z LAN nie. **Decyzja Claude:** ikonka sama
jest przekaźnikiem TCP (bez admina, bez zależności od zmiennego IP
dystrybucji). Nasłuchuje na `<IP-LAN>:8443` dla każdego adresu
interfejsu z listy hostów (nie na `0.0.0.0` i nie na `127.0.0.1`, żeby nie
kolidować z localhostForwarding WSL) i przekazuje surowy strumień TCP
(TLS kończy nginx w dystrybucji) do `127.0.0.1:8443`. Nasłuch na porcie
> 1024 nie wymaga admina; zaporę otwiera jednorazowo reguła programowa dla
`BPP.exe`. Przekaźnik żyje tyle, co ikonka — czyli tyle, co BPP.

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


- Czy dystrybucja z systemd i dockerem rzeczywiście nie gaśnie po
  zamknięciu ostatniej sesji `wsl.exe` (oczekiwane: nie gaśnie).
- Czy localhostForwarding WSL widzi port publikowany przez dockerd
  (userland-proxy) w dystrybucji.
- Czy po włączeniu funkcji przez admina zwykły użytkownik może sam
  wykonać `wsl --install --no-distribution` bez UAC.
- DNS w WSL pod VPN-em (zepsuty `resolv.conf`) — obecnie tylko komunikat
  o błędzie pobierania.
- Czy przekaźnik ikonki na `<IP-LAN>:8443` współistnieje z localhostForwarding
  WSL na `127.0.0.1:8443` (Windows a wiązanie konkretnych adresów vs
  wildcard).
- Czy reguła zapory programowa dla exe w `%LOCALAPPDATA%` wystarcza
  i czy Windows nie pokazuje mimo to własnego monitu zapory.
- Czy `wsl --install` na Windows 10 wymaga restartu zawsze, czy zależnie
  od stanu; zachowanie `RunOnce` przy podniesionych uprawnieniach.

## 8. Decyzje podjęte przez Claude (do przejrzenia)

- Debian + systemd + docker-ce w dystrybucji; użytkownik root domyślny.
- Port 8443, tylko HTTPS, bez publikowania 80 i QUIC.
- Obrazy BPP przypięte do tagu datowego podanego przy budowie.
- Własny nginx: zwykły obraz `nginx`, konfiguracja od zera, bez HSTS.
- 7 usług (bez authservera); limity pamięci ~4,5 GB łącznie;
  `restart: unless-stopped`.
- Dwa exe: mały `BPP.exe` + `BPP-Setup.exe` z doklejonym ładunkiem.
- Lista hostów i certyfikat odświeżane przy każdym starcie z IP odczytanych
  po stronie Windowsa; brak zmian w repo `bpp`.
- .NET 10 WinForms, jeden exe z osadzonym rootfs, trzy tryby
  (instalator / ikonka / deinstalacja) (`BPP.exe` odrzuca nieznane argumenty kodem 2; UAC tylko dla System32 `dism.exe`/`netsh.exe`). *(zmienione po przeglądzie bezpieczeństwa, R8)*
- Zapora dla profili Prywatny, Domena i Publiczny (tylko `BPP.exe`, TCP 8443).
- Instalacja per-użytkownik (`asInvoker`, `%LOCALAPPDATA%`, HKCU);
  UAC tylko dla System32 `dism.exe` (funkcje WSL) i `netsh.exe` (zapora);
  pakiet WSL instaluje zwykły użytkownik.
- Dostęp z LAN przez przekaźnik TCP wbudowany w ikonkę zamiast
  `netsh portproxy`.
- Certyfikat self-signed ważny 10 lat.
- „Pokaż logi” dodane do menu; bez „Aktualizuj”.
- Podpis kodu jako wyłączony krok builda.
- Minimalne wymagania: odmowa < 8 GB RAM i < 25 GB dysku.
- Brak obsługi proxy HTTP i trybu offline; czytelny błąd + „Spróbuj ponownie”.

## 9. Pozostawione budującemu

- Konkretna wersja Debiana, sposób liczenia postępu `pull`.
- Dokładny format zdarzeń JSON `bpp-ctl` (w ramach opisanego kontraktu).
- Wygląd okna (w granicach: logo BPP, pasek postępu, etykieta kroku,
  rozwijane szczegóły).
- Szczegóły `nginx.conf` (w granicach 4.3) i dokładne wartości limitów
  pamięci po pomiarze.
