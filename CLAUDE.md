# CLAUDE.md

## Releasing a new version

A release is made by pushing a tag `v<version>` (see `.github/workflows/release.yml`). The
workflow builds the three files, publishes them in GitHub Releases and moves the download
link in this repository's `README.md` by itself.

One place is **not** updated automatically and has to be done by hand after every regular
release (not for drafts or pre-releases): the installation page of the BPP website, in the
repository [iplweb/web-page-bpp](https://github.com/iplweb/web-page-bpp), file
`components/installation-tabs.tsx` (the Windows tab). Update there:

- the download link `https://github.com/iplweb/bpp-wsl/releases/download/v<version>/BPP-Setup-<version>.exe`,
- the file name shown as the link text, `BPP-Setup-<version>.exe`,
- the file size shown next to it, e.g. `(43 KB)` — take the real size of the published
  `BPP-Setup-<version>.exe` (`gh release view v<version> --json assets`),
- the link to the release page, `https://github.com/iplweb/bpp-wsl/releases/tag/v<version>`,
  and its link text, `BPP dla Windows <version>`.

Afterwards no mention of the previous version may be left in that file.

Do it only once the release workflow has finished and the files are public; a link to a
release that does not exist yet breaks the download on the website.
