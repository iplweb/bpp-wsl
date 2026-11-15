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
- [ ] **2.** Z WSL: od dwukliku do „Gotowe” bez interakcji poza „Zainstaluj”,
  zapowiedzią zapory („OK”) i jednym UAC (zapora).
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
- [ ] **4.** `https://localhost:8443/` → (bez ostrzeżenia o certyfikacie, jeśli
  w instalatorze zaakceptowano „BPP localhost CA”; inaczej po zaakceptowaniu
  certyfikatu w przeglądarce)
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
- [ ] **15.** Strona postępu startuje ze schowanym terminalem (okno zwykłej wysokości).
  „Pokaż terminal” rozwija go (czarne tło, białe litery, ok. 80 kolumn,
  do 25 wierszy — mniej na niskim ekranie, np. 1366×768; okno nie wychodzi poza ekran,
  także przy 150% DPI) razem ze wszystkim, co wypisano, zanim go pokazano, przewinięty
  do końca, pod wierszem przycisków („Pokaż/Ukryj terminal”, „Zapisz log do pliku…”
  i po prawej „Anuluj” — w jednej linii, nie przesuwają się); „Ukryj terminal” przywraca zwykłą wysokość. Widać w nim kroki instalatora,
  a przy pierwszym starcie usług logi bazy i aplikacji (migracje). „Zapisz log do
  pliku…” działa także przy schowanym terminalu. Inne strony mają zwykłą wysokość okna.
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

- [ ] **22.** Żadne okno Windows nie pojawia się bez zapowiedzi: przed UAC dla DISM,
  przed konsolą `wsl --update` i przed UAC zapory instalator pokazuje komunikat
  „Teraz: …” / „Ostatni krok: …” z przyciskiem OK i dopiero po nim uruchamia krok.
  Gdy reguła zapory już istnieje (ponowna instalacja), zapowiedzi zapory nie ma.
  Po restarcie RunOnce otwiera stronę „Witaj w kolejnym etapie instalacji BPP”
  i nic nie rusza przed „OK” („Później” zamyka instalator).
  Wynik:

- [ ] **23.** Strona „Instalowanie BPP”: pod tytułem, nad paskami, lista pięciu etapów
  (piąty: „Pierwszy start BPP…” — zaczyna się po pobraniu obrazów, pasek kroku „kręci się”
  do końca migracji i startu usług);
  bieżący pogrubiony ze znakiem ▸, ukończone szare z ✓ (z WSL już zainstalowanym etapy
  1–2 od razu dostają ✓). Lista mieści się w oknie przy 100–150% DPI, przyciski widoczne.
  Wynik:
- [ ] **24.** „Zapisz log do pliku…” (strona postępu, strona błędu, okno „Pokaż logi”,
  okno logów ikonki): okno zapisu z domyślną nazwą `BPP-…-log-<data>.txt` na Pulpicie,
  plik w UTF-8, bez haseł.
  Wynik:

- [ ] **25.** Bez WSL: dystrybucja BPP pobiera się w tle już podczas DISM / `wsl --update`
  (log: „Pobieranie dystrybucji BPP w tle.”); krok „Pobieranie dystrybucji BPP” po
  aktualizacji WSL jest krótki albo natychmiastowy. Restart w trakcie pobierania:
  po restarcie pobieranie wznawia się od miejsca przerwania.
  Wynik:
- [ ] **26.** Górny pasek przy pobieraniu obrazu rośnie stopniowo (nie skacze z 0 na 100%
  po pierwszej małej warstwie); ostatnie ~10% to rozpakowywanie warstw.
  Wynik:

- [ ] **27.** „Gotowe” pojawia się dopiero, gdy działają wszystkie usługi (log: ewentualne
  „Uruchamianie usług w tle: …”; po 3 min bez workera instalacja i tak się kończy).
  Adres na stronie „Gotowe” jest klikalny i otwiera BPP; ikonka w zasobniku jest już
  widoczna, zanim zamkniesz instalator.
  Wynik:
- [ ] **28.** Menu ikonki → „Terminal”: otwiera się okno konsoli z powłoką root w
  dystrybucji BPP, w katalogu `/opt/bpp`; `bpp-ctl compose ps` działa.
  Wynik:

- [ ] **29.** „Odinstaluj” pyta dwa razy (oba pytania domyślnie „Nie”); przed oknem
  administratora „Network Command Shell” (usunięcie reguły zapory) jest komunikat z OK;
  bez reguły zapory nie ma ani komunikatu, ani UAC. To samo przed „Udostępnij w sieci
  lokalnej…” z menu ikonki (OK / Anuluj).
  Wynik:
- [ ] **30.** „Wyślij diagnostykę” z okna logów ikonki, okno logów zamknięte w trakcie
  zbierania: brak wyjątku „Cannot access a disposed object”, komunikaty pokazują się
  bez okna-właściciela.
  Wynik:

- [ ] **31.** Dane przykładowe: strona powitalna ma pole „Wypełnij BPP przykładowymi danymi”
  — domyślnie zaznaczone — i listę trzech motywów, z wybranym losowo „Światem Stanisława
  Lema” albo „Światem Wiedźmina” (przy kolejnych uruchomieniach instalatora trafiają się oba;
  lista nieaktywna po odznaczeniu pola; licencja nadal czytelna).
  Po instalacji z motywem BPP ma uczelnię, autorów i publikacje z opisami bibliograficznymi,
  a kreator pyta tylko o konto administratora. Bez zaznaczenia baza jest pusta. Wybór
  przetrwa restart po włączeniu WSL (RunOnce z `--demo <motyw>`). „Spróbuj ponownie” nie
  generuje danych drugi raz.
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
  także na polskojęzycznym Windowsie (nazwa procesu z `GetExtendedTcpTable`, bez parsowania tekstu).
  Wynik:
- [ ] Okno instalatora przy skalowaniu 125% i 150%: przyciski widoczne,
  lista błędów się przewija.
  Wynik:
- [ ] Instalacja przerwana w połowie (np. zabicie procesu po imporcie
  dystrybucji) → wpis w „Aplikacje i funkcje” usuwa wszystko (dystrybucję,
  pliki, skrót, regułę zapory).
  Wynik:
- [ ] RunOnce po restarcie: po wymaganym restarcie instalacja wznawia się
  sama (`--resume`, strona „Witaj w kolejnym etapie…” zamiast licencji) i po „OK” dochodzi do „Gotowe”.
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
  dzieło.
  Wynik:

## Mini instalator i pobieranie (wydanie z trzech plików)

- [ ] **M1.** Sam `BPP-Setup-<wersja>.exe` (kilkadziesiąt KB) w pustym folderze:
  okienko „Pobieranie instalatora BPP… X z Y MB” → właściwy instalator
  z logo. **Bez okna UAC** przy starcie mini instalatora (manifest `asInvoker`).
  Wynik:
- [ ] **M2.** Krok „Pobieranie dystrybucji BPP… X z Y MB”: pasek kroku
  i pasek całości rosną. Po instalacji w `%LOCALAPPDATA%\BPP\download` nie
  zostaje rootfs ani pobrany `BPP-<wersja>.exe`.
  Wynik:
- [ ] **M3.** Odłączenie sieci w trakcie pobierania rootfs: po ok. minucie
  strona „Nie udało się pobrać dystrybucji BPP” ze „Spróbuj ponownie”. Po
  przywróceniu sieci ponowienie **wznawia** pobieranie (log: „Wznawianie
  pobierania od … B”), nie zaczyna od zera.
  Wynik:
- [ ] **M4.** Offline: trzy pliki wydania w jednym folderze, sieć odłączona →
  instalacja dochodzi do pobierania obrazów (te wymagają sieci) bez
  pobierania z GitHuba (log: „Dystrybucja z pliku: …”).
  Wynik:
- [ ] **M5.** Podmieniony / uszkodzony `BPP-<wersja>.exe` obok mini
  instalatora: mini instalator go nie uruchamia (pobiera właściwy z GitHuba).
  Wynik:
- [ ] **M6.** Ścieżka z restartem (bez WSL): po restarcie RunOnce uruchamia
  `%LOCALAPPDATA%\Programs\BPP\BPP.exe --setup --resume` i instalacja kończy
  się sukcesem, choć pobrana kopia `BPP-<wersja>.exe` została usunięta.
  Wynik:
- [ ] **M7.** Komputer z mniej niż 8 GB RAM (albo dysk z mniej niż 8 GB
  wolnego): strona wymagań z przyciskiem **„Zainstaluj mimo to”** →
  ostrzeżenie Tak/Nie → instalacja idzie dalej. Przy zajętym porcie 8443 lub
  procesorze ARM tego przycisku **nie ma**. Po „mimo to” i restarcie (WSL)
  wznowiona instalacja nie pyta drugi raz.
  Wynik:
- [ ] **M8.** Windows Defender / SmartScreen nie blokuje mini instalatora ani
  pobranego `BPP-<wersja>.exe` (pliki niepodpisane — odnotuj ostrzeżenia).
  Wynik:

## Lokalne CA i zaufany certyfikat

- [ ] **C1.** Po `bpp-ctl init` instalator pokazuje zapowiedź „Certyfikat BPP
  dla przeglądarki”, a po OK systemowe „Ostrzeżenie o zabezpieczeniach”
  (bez UAC) z nazwą „BPP localhost CA”. „Tak” → Edge i Chrome otwierają
  `https://localhost:8443/` z kłódką, bez ostrzeżenia (po ponownym
  uruchomieniu przeglądarki, jeśli była otwarta). Odnotuj Firefoksa.
  Wynik:
- [ ] **C2.** „Nie” w oknie Windows → instalacja kończy się poprawnie, strona
  „Gotowe” wspomina o ostrzeżeniu przeglądarki, a menu ikonki ma pozycję
  „Dodaj certyfikat BPP…”; po jej użyciu i „Tak” pozycja znika.
  Wynik:
- [ ] **C3.** `certmgr.msc` → Zaufane główne urzędy certyfikacji: jeden wpis
  „BPP localhost CA”, z ograniczeniem nazw (DNS: localhost). W magazynie
  komputera (`certlm.msc`) wpisu nie ma.
  Wynik:
- [ ] **C4.** Zmiana sieci (nowy adres IP w liście hostów) → certyfikat
  serwera jest wymieniany, a przeglądarka dalej nie ostrzega i Windows o nic
  nie pyta. Wejście po adresie LAN z tego samego komputera: też bez
  ostrzeżenia.
  Wynik:
- [ ] **C5.** Ponowna instalacja (nowa dystrybucja = nowe CA): Windows pyta
  o dodanie nowego certyfikatu i o usunięcie starego; w magazynie zostaje
  jeden wpis.
  Wynik:
- [ ] **C6.** „Odinstaluj” → Windows pyta o usunięcie „BPP localhost CA”;
  po „Tak” wpisu nie ma. Po „Nie” podsumowanie odinstalowania mówi, że
  certyfikat został i jak go usunąć.
  Wynik:

## Inne dystrybucje WSL obok BPP

- [ ] **W1.** Z zainstalowanym Ubuntu (lub inną dystrybucją) jako domyślną:
  instalacja BPP kończy się sukcesem, `wsl -l -v` pokazuje obie, domyślna się
  nie zmienia, pliki w Ubuntu są na miejscu.
  Wynik:
- [ ] **W2.** „Odinstaluj” przy działającym Ubuntu: znika tylko `BPP`
  i `%LOCALAPPDATA%\BPP`; Ubuntu dalej działa (nie zostało zatrzymane) i ma
  swoje pliki.
  Wynik:
- [ ] **W3.** Obca dystrybucja o nazwie `BPP` w innym katalogu
  (`wsl --import BPP C:\wsl\obca <dowolny rootfs.tar>`): instalator kończy stroną
  „Nazwa dystrybucji WSL „BPP” jest już zajęta” z jej katalogiem, bez pytania
  o ponowną instalację; dystrybucja jest nietknięta.
  Wynik:
- [ ] **W4.** Jak W3, ale BPP.exe jest już w folderze programu (zostaje po
  W3): „Odinstaluj” usuwa program i wpis w „Aplikacje”, a obca dystrybucja
  `BPP` zostaje zarejestrowana i uruchamia się.
  Wynik:

## Dystrybucja ma działać, dopóki działa ikonka

WSL zatrzymuje dystrybucję kilkanaście sekund po odłączeniu ostatniego procesu
`wsl.exe`, razem z Dockerem i usługami BPP. Ikonka i instalator trzymają więc
jeden bezczynny proces `wsl.exe -d BPP -u root -- cat`.

- [ ] **K1.** Po instalacji i po starcie z Menu Start: po 5 minutach bez
  dotykania komputera `wsl -l -v` nadal pokazuje `BPP Running`,
  a `https://localhost:8443/` odpowiada.
  Wynik:
- [ ] **K2.** Między końcem instalacji (okna certyfikatu i zapory, strona
  „Gotowe”) a startem ikonki BPP nie zatrzymuje się: usługi mają ciągły czas
  działania (`wsl -d BPP bpp-ctl compose ps`).
  Wynik:
- [ ] **K3.** „Zakończ” w menu ikonki: BPP.exe kończy się bez błędu aplikacji
  w Podglądzie zdarzeń, a po kilkunastu sekundach `BPP` jest `Stopped`.
  Wynik:
- [ ] **K4.** Zabicie BPP.exe w Menedżerze zadań: proces `wsl.exe … cat`
  znika sam, a dystrybucja po kilkunastu sekundach się zatrzymuje.
  Wynik:

## Stan w ikonce

Ikonka co 20 sekund pyta: czy dystrybucja działa (`wsl -l --running`, bez
uruchamiania jej), co mówi `bpp-ctl status` o Dockerze i usługach, i czy
`https://localhost:8443/` odpowiada z Windowsa.

- [ ] **S1.** Po starcie: zielona kropka na ikonce, dymek „BPP — działa”,
  w menu cztery zielone linie, „Usługi: działa 7 z 7”.
  Wynik:
- [ ] **S2.** W trakcie startu: żółta kropka i „BPP — uruchamianie…”, linie w
  menu zmieniają się na bieżąco, „Uruchom ponownie” jest wyszarzone.
  Wynik:
- [ ] **S3.** `wsl -d BPP bpp-ctl compose stop workerserver`: w ciągu minuty
  czerwona kropka, „Usługi: działa 6 z 7; nie działa: workerserver”, jeden
  dymek „BPP — problem”. „Uruchom ponownie” przywraca zielony stan.
  Wynik:
- [ ] **S4.** `wsl -d BPP systemctl stop docker docker.socket`: „Docker: nie
  odpowiada”, „Usługi: —”, „Strona: nie odpowiada”.
  Wynik:
- [ ] **S5.** „Zatrzymaj” w menu: po wyjściu ikonki dystrybucja zostaje
  `Stopped` — sprawdzanie stanu nie uruchamia jej ponownie.
  Wynik:
- [ ] **S6.** Kropka i linie menu są czytelne przy skalowaniu 100% i 150%.
  Wynik:

## Autostart i ponowna instalacja bez pobierania

- [ ] **A1.** Pole „Uruchamiaj BPP po zalogowaniu do Windows” jest domyślnie
  zaznaczone, pod nim uwaga o pamięci RAM (cała strona mieści się w oknie przy
  100% i 150%). Po instalacji, restarcie i zalogowaniu ikonka pojawia się sama
  i BPP wstaje. Po odznaczeniu pola — nie pojawia się.
  Wynik:
- [ ] **A2.** Menu ikonki → „Uruchamiaj po zalogowaniu do Windows”: ptaszek
  zgadza się z wpisem `BPP` w `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`,
  kliknięcie go przełącza.
  Wynik:
- [ ] **A3.** Wybór autostartu przetrwa restart w trakcie instalacji (włączanie
  WSL): po wznowieniu i zakończeniu wpis `Run` jest taki, jak zaznaczono.
  Wynik:
- [ ] **A4.** Odinstalowanie usuwa wpis `Run`.
  Wynik:
- [ ] **A5.** Druga instalacja tej samej wersji na istniejącą („Zainstaluj
  ponownie”): krok pobierania dystrybucji kończy się od razu (plik
  `%LOCALAPPDATA%\BPP\download\bpp-rootfs-….tar.gz` został po pierwszej), w logu
  nie ma pobierania 170 MB.
  Wynik:

## Pierwsza strona instalatora i strona wymagań

- [ ] **P1.** Okno instalatora otwiera się na środku ekranu (także w pionie),
  przy 100% i 150%.
  Wynik:
- [ ] **P2.** Licencja jest na białym tle, tekst da się zaznaczyć i skopiować;
  rok w nocie to 2017-2026.
  Wynik:
- [ ] **P3.** „Zapisz licencję…” zapisuje plik tekstowy; „Wydrukuj licencję…”
  otwiera okno drukowania Windows, wydruk (np. do PDF) ma cały tekst, zawinięty
  w marginesach.
  Wynik:
- [ ] **P4.** Bez zaznaczonej akceptacji: najechanie myszą na wyszarzone
  „Zainstaluj” albo kliknięcie go — pole akceptacji miga trzy razy na żółto.
  Po zaznaczeniu nie miga.
  Wynik:
- [ ] **P5.** Mało RAM albo miejsca: tytuł „Ten komputer ma mniej zasobów, niż
  zaleca BPP”, bez linków do logów i diagnostyki. Dysk z 16 GB wolnego nie
  wywołuje ostrzeżenia.
  Wynik:
