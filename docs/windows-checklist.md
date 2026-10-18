# Lista kontrolna testu na Windowsie x64

Wykonuje człowiek na prawdziwym Windowsie (autor buduje na macOS i nie ma
maszyny z Windowsem). Kryteria ze specu §6 uruchamiać na czystym
Windows 11 x64 oraz osobno na Windows 10 22H2. Przy każdej pozycji wpisz
wynik, wersję Windowsa i uwagi.

Build: `BPP-Setup-<wersja>.exe` (wersja: ______, tag obrazów: ______),
Windows: ______ (11 / 10 22H2), konto: ______ (admin / zwykłe).

## Kryteria akceptacji (spec §6)

- [ ] **1.** Bez WSL: dwuklik → UAC (systemowe `dism.exe`) → instalacja WSL →
  restart → instalacja wznawia się sama (RunOnce) → kończy się przyciskiem
  „Otwórz BPP”. Uwaga: po wznowieniu pojawia się **drugie okno UAC**
  (systemowe `netsh.exe`, reguła zapory) — to oczekiwane, nie błąd; odmowa
  oznacza tylko BPP dostępne lokalnie.
  Wynik:
- [ ] **2.** Z WSL: od dwukliku do „Gotowe” bez interakcji poza „Zainstaluj”
  i jednym UAC (zapora).
  Wynik:
- [ ] **2a.** Konto zwykłego użytkownika z WSL już zainstalowanym: odmowa UAC →
  instalacja kończy się sukcesem, BPP działa na `localhost:8443`, ikonka
  oferuje „Udostępnij w sieci lokalnej…”. Dystrybucja widoczna w
  `wsl -l -v` tego użytkownika.
  Wynik:
- [ ] **2b.** Zwykły użytkownik, UAC zatwierdzony hasłem innego konta (admina):
  dystrybucja, skróty i ikonka nadal należą do zwykłego użytkownika.
  Wynik:
- [ ] **3.** Pasek postępu porusza się podczas pobierania obrazów (nie stoi na
  0% przez minuty).
  Wynik:
- [ ] **4.** `https://localhost:8443/` → (po zaakceptowaniu certyfikatu)
  przekierowanie na `/setup/step/admin_user/`; POST formularza admina
  przechodzi (brak błędu CSRF), potem krok uczelni, potem logowanie.
  Wynik:
- [ ] **5.** To samo z innego komputera w LAN przez
  `https://<IP-Windowsa>:8443/`, łącznie z POST-em formularza.
  Wynik:
- [ ] **6.** Przekierowania Django zachowują `:8443` (np. po zalogowaniu).
  Wynik:
- [ ] **7.** Restart Windowsa → skrót „BPP” → ikonka → BPP działa z tymi samymi
  danymi, LAN nadal działa (nowe IP WSL obsłużone).
  Wynik:
- [ ] **8.** Zmiana sieci (inne IP Wi-Fi) → ponowne uruchomienie ikonki → BPP
  dostępne pod nowym IP.
  Wynik:
- [ ] **9.** „Zatrzymaj” → `wsl -l -v` pokazuje BPP jako Stopped.
  Wynik:
- [ ] **10.** „Odinstaluj” → brak dystrybucji, reguły zapory
  `BPP (8443) <hex>` tego użytkownika, katalogów w
  `%LOCALAPPDATA%`, skrótu, wpisu w „Aplikacje i funkcje”. Inne dystrybucje
  WSL nietknięte.
  Wynik:
- [ ] **11.** „Pokaż logi” pokazuje logi kontenerów.
  Wynik:

- [ ] **12.** (0.1.1) Po DISM i restarcie, gdy Windows wymaga `wsl --update`:
  instalator sam uruchamia aktualizację w widocznym oknie konsoli (postęp
  pobierania widoczny), a potem importuje dystrybucję i kończy instalację.
  Wynik:
- [ ] **13.** (0.1.1) DISM uruchamia się w widocznym oknie konsoli z paskiem
  postępu; `netsh` (zapora) bez okna.
  Wynik:
- [ ] **14.** (0.1.1) „Anuluj” i krzyżyk działają na każdym kroku: potwierdzenie →
  okno się zamyka (podczas pobierania BPP najpierw zatrzymuje kontenery; drugie
  zamknięcie nie czeka). Ponowne uruchomienie instalatora dokańcza instalację.
  Wynik:
- [ ] **15.** (0.1.1) „Szczegóły ▸” wydłuża okno w dół (nie ściska strony),
  „Szczegóły ▾” przywraca wysokość; przy 150% DPI okno nie wychodzi poza ekran.
  Wynik:
- [ ] **16.** (0.1.2) Dwa paski: górny pokazuje bieżący obraz („Pobieranie obrazu
  … (3 z 7)”, od 0 do 100% dla każdego obrazu; przy innych krokach animacja),
  dolny „Cała instalacja: NN%” rośnie przez całą instalację i nigdy się nie cofa.
  Wynik:
- [ ] **17.** (0.1.3) Nagłówek: logo BPP po lewej, IPLweb po prawej, pod nimi link
  `https://bpp.iplweb.pl/`; kliknięcie każdego z trzech otwiera tę stronę w
  przeglądarce. Przy 125–150% DPI loga nie są ucięte ani rozciągnięte.
  Ekran powitalny mówi, że to instalacja do testów.
  Wynik:
- [ ] **18.** (0.1.4) Ekran powitalny pokazuje licencję MIT BPP; „Zainstaluj” jest
  nieaktywne do zaznaczenia zgody.
  Wynik:
- [ ] **19.** (0.1.4) Nieudany start usług: strona błędu ma „Pokaż logi” (duże okno,
  rozciągalne, maksymalizowalne) i „Wyślij diagnostykę do IPLweb…”: ZIP na
  Pulpicie (windows.txt, instalator-log.txt, bpp-diag.txt,
  bpp-diag-przed-zatrzymaniem.txt — bez haseł), Eksplorator z zaznaczonym
  plikiem, nowa wiadomość do support@iplweb.pl. To samo z menu ikonki i okna logów.
  Wynik:
- [ ] **20.** (0.1.4) „Spróbuj ponownie” po nieudanym starcie nie pobiera obrazów
  ponownie („Obraz … jest już pobrany”); polecenia z strony błędu
  (`wsl -d BPP bpp-ctl start`, `… compose ps`, `… compose logs workerserver`)
  działają w cmd i PowerShellu.
  Wynik:
- [ ] **21.** (0.1.5) workerserver dochodzi do healthy na laptopie z wieloma rdzeniami
  (wcześniej unhealthy blokował start). Wersja testowa: jeden proces roboczy
  w workerze (Celery), appserverze (gunicorn) i nginx; Postgres bez równoległości.
  Wynik:

## Do weryfikacji na prawdziwym Windowsie (spec §7)

- [ ] Dystrybucja z systemd i dockerem nie gaśnie po zamknięciu ostatniej
  sesji `wsl.exe` (oczekiwane: nie gaśnie).
  Wynik:
- [ ] `localhostForwarding` WSL widzi port publikowany przez dockerd
  (userland-proxy) w dystrybucji.
  Wynik:
- [ ] Po włączeniu funkcji przez admina zwykły użytkownik może sam wykonać
  `wsl --install --no-distribution` bez UAC.
  Wynik:
- [ ] DNS w WSL pod VPN-em (zepsuty `resolv.conf`) — czy komunikat o błędzie
  pobierania jest zrozumiały.
  Wynik:
- [ ] Przekaźnik ikonki na `<IP-LAN>:8443` współistnieje z
  `localhostForwarding` WSL na `127.0.0.1:8443`.
  Wynik:
- [ ] Reguła zapory programowa dla exe w `%LOCALAPPDATA%` wystarcza i Windows
  nie pokazuje mimo to własnego monitu zapory.
  Wynik:
- [ ] `wsl --install` na Windows 10: czy restart jest wymagany zawsze, czy
  zależnie od stanu; zachowanie `RunOnce` (kod 3010, wznowienie z `--resume`).
  Wynik:

## Dodatkowe punkty z przeglądów

- [ ] Ikonka w zasobniku renderuje się poprawnie (ikona PNG-w-ICO, ostra
  w 100%, 125%, 150% DPI; także ikona exe w Eksploratorze i skrót).
  Wynik:
- [ ] Samousuwanie programu po „Odinstaluj”: opóźnienie `ping` przed
  `rmdir` — katalog `%LOCALAPPDATA%\Programs\BPP` znika po chwili, nie
  zostaje zablokowany plik `BPP.exe`.
  Wynik:
- [ ] Wylogowanie / zamknięcie systemu przy włączonym BPP, w tym komunikat
  „aplikacje uniemożliwiają wylogowanie”: ikonka zatrzymuje stos
  (`stop` + terminate), nie blokuje wylogowania; po ponownym logowaniu BPP
  startuje poprawnie.
  Wynik:
- [ ] Okna UAC pokazują wydawcę Microsoft i `dism.exe` / `netsh.exe` (nie
  `BPP.exe` ani `BPP-Setup.exe`).
  Wynik:
- [ ] UAC może pojawić się za oknem instalatora — czy użytkownik to zauważa
  (fokus okna UAC, migający przycisk na pasku zadań).
  Wynik:
- [ ] Maszyna z Hyper-V, ale bez włączonej `VirtualMachinePlatform`:
  instalator włącza brakującą funkcję (jedno UAC), wymaga restartu i
  wznawia się.
  Wynik:
- [ ] Zajęty port 8443: komunikat po polsku wskazuje proces-właściciela
  także na polskojęzycznym Windowsie (parsowanie `netstat -ano`).
  Wynik:
- [ ] Okno instalatora przy skalowaniu 125% i 150%: przyciski widoczne,
  lista błędów się przewija.
  Wynik:
- [ ] Instalacja przerwana w połowie (np. zabicie procesu po imporcie
  dystrybucji) → wpis w „Aplikacje i funkcje” usuwa wszystko (dystrybucję,
  pliki, skrót, regułę zapory).
  Wynik:
- [ ] RunOnce po restarcie: po wymaganym restarcie instalacja wznawia się
  sama (`--resume`, bez strony powitalnej) i dochodzi do „Gotowe”.
  Wynik:
- [ ] Dwa konta Windows na jednym komputerze (A i B, kolejno — port 8443
  i przekaźnik są wspólne dla komputera, więc BPP konta A zatrzymaj przed
  uruchomieniem BPP konta B): każde konto ma własną regułę zapory
  `BPP (8443) <8 znaków hex>` (`netsh advfirewall firewall show rule
  name=all | findstr "BPP (8443)"` pokazuje dwie), BPP każdego konta jest
  dostępne z LAN, a odinstalowanie BPP z konta A nie usuwa reguły, dystrybucji
  ani skrótu konta B (i B nadal działa w LAN).
  Wynik:
- [ ] Instalator uruchomiony, gdy dystrybucja BPP działa bez ikonki (np.
  zabity `BPP.exe` w Menedżerze zadań przy działającym stosie): strona błędu
  wymagań wskazuje port 8443 zajęty przez `wslrelay` i oferuje „Zatrzymaj
  BPP” → po kliknięciu instalator zatrzymuje BPP, ponownie sprawdza wymagania
  i przechodzi dalej (pyta o ponowną instalację). Gdy port zajmuje inna
  dystrybucja WSL, po jednej próbie komunikat wskazuje inną dystrybucję i
  przycisk znika.
  Wynik:
- [ ] Pierwszy zimny start dystrybucji (`wsl --shutdown`, potem skrót „BPP”):
  ikonka dochodzi do „BPP — działa” bez błędu typu „Cannot connect to the
  Docker daemon” (bpp-ctl czeka do 60 s na `docker info`; w logach może być
  „Oczekiwanie na uruchomienie Dockera”).
  Wynik:
- [ ] Nieudane odinstalowanie dystrybucji (np. `wsl --unregister` zwraca
  błąd): komunikat „Nie udało się odinstalować BPP … Spróbuj ponownie
  odinstalować”, a program, skrót, wpis w „Aplikacje” i katalog
  `%LOCALAPPDATA%\Programs\BPP` zostają; ponowne „Odinstaluj” kończy
  dzieła.
  Wynik:
