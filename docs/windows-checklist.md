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
  „Otwórz BPP”.
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
