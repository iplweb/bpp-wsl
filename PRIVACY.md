# Polityka prywatności — BPP dla Windows

*English version below.*

Dotyczy instalatora `BPP-Setup-<wersja>.exe`, programu `BPP-<wersja>.exe`
(ikonka w zasobniku) i dystrybucji WSL `BPP` z tego repozytorium.

## Krótko

**Program nie zbiera i nie wysyła żadnych danych o Tobie ani o Twoim
komputerze.** Nie ma telemetrii, statystyk użycia, automatycznych raportów
błędów ani sprawdzania aktualizacji. Twoje dane w BPP zostają na Twoim
komputerze.

## Z czym program się łączy

Wyłącznie po to, żeby pobrać pliki potrzebne do instalacji i uruchomienia:

| Dokąd | Po co | Kiedy |
|---|---|---|
| GitHub Releases (`github.com/iplweb/bpp-wsl`) | pobranie `BPP-<wersja>.exe` i `bpp-rootfs-<wersja>.tar.gz` | podczas instalacji, chyba że pliki leżą obok instalatora |
| Docker Hub (`hub.docker.com`) | pobranie obrazów BPP (`iplweb/bpp_*`) oraz PostgreSQL, Redis i nginx | przy pierwszym uruchomieniu BPP |
| Microsoft (Windows Update, Microsoft Store) | włączenie lub aktualizacja WSL, robione przez sam Windows | tylko gdy WSL trzeba włączyć |

Te serwisy widzą standardowe dane każdego połączenia sieciowego (adres IP,
czas, pobierany plik, nagłówek User-Agent z wersją instalatora). Obowiązują
tam ich własne polityki prywatności:
[GitHub](https://docs.github.com/site-policy/privacy-policies/github-general-privacy-statement),
[Docker](https://www.docker.com/legal/docker-privacy-policy/),
[Microsoft](https://privacy.microsoft.com/).

## Diagnostyka dla IPLweb

**Wyślij diagnostykę do IPLweb…** działa tylko na Twoje kliknięcie i niczego
nie wysyła sam. Zapisuje na Pulpicie plik `BPP-diagnostyka-<data>.zip`
(logi programu i usług BPP, stan kontenerów, wersje Windowsa i WSL, lista
dystrybucji WSL, plik `.wslconfig`, ilość pamięci i wolnego miejsca, ścieżka
programu — zawiera nazwę konta Windows; **bez haseł**, które są maskowane) i otwiera
w programie pocztowym wiadomość do support@iplweb.pl. Plik możesz przejrzeć
przed wysłaniem. Wysyłasz go sam, ze swojej poczty, i decydujesz, czy w ogóle.
IPLweb używa go wyłącznie do rozwiązania zgłoszonego problemu.

## Sieć lokalna

Działające BPP jest dostępne pod `https://localhost:8443/` oraz — jeśli
zgodzisz się na regułę zapory — dla innych komputerów w sieci lokalnej.
Szczegóły w [README](README.md#uwagi-bezpieczeństwa).

## Kontakt

IPLweb, support@iplweb.pl, [bpp.iplweb.pl](https://bpp.iplweb.pl/).

---

# Privacy policy — BPP for Windows

Covers the installer `BPP-Setup-<version>.exe`, the program
`BPP-<version>.exe` (tray icon) and the `BPP` WSL distribution from this
repository.

**The software does not collect or send any data about you or your
computer.** There is no telemetry, no usage statistics, no automatic crash
reporting and no update check. Your BPP data stays on your computer.

**Network connections** are made only to download what installation and
operation need:

- **GitHub Releases** (`github.com/iplweb/bpp-wsl`): `BPP-<version>.exe` and
  `bpp-rootfs-<version>.tar.gz`, during installation, unless the files lie
  next to the installer;
- **Docker Hub**: BPP images (`iplweb/bpp_*`) plus PostgreSQL, Redis and
  nginx, on the first start;
- **Microsoft** (Windows Update / Microsoft Store): enabling or updating
  WSL, done by Windows itself, only when WSL has to be enabled.

These services see the usual data of any network request (IP address, time,
requested file, a User-Agent with the installer version) under their own
privacy policies ([GitHub](https://docs.github.com/site-policy/privacy-policies/github-general-privacy-statement),
[Docker](https://www.docker.com/legal/docker-privacy-policy/),
[Microsoft](https://privacy.microsoft.com/)).

**Diagnostics for IPLweb** run only when you click *Wyślij diagnostykę do
IPLweb…* and send nothing by themselves: a ZIP with logs, container state and
system information (Windows/WSL versions, WSL distribution list, `.wslconfig`,
RAM and free disk space, the program path including the Windows user name;
passwords masked) is saved to your Desktop and a draft
e-mail to support@iplweb.pl is opened. You review and send it yourself, from
your own mail account. IPLweb uses it only to solve the reported problem.

Contact: IPLweb, support@iplweb.pl, [bpp.iplweb.pl](https://bpp.iplweb.pl/).
