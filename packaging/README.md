# Firaw h4K 1.0.0 packaging

Run commands from the project root in Windows PowerShell 5.1 or PowerShell 7.
No build runs when these files are added. Build scripts publish the current source;
wait for application editing to finish before running them.

## Signed website / Firaw CENTER release

```powershell
.\tools\build-release.ps1
# Optional alternate NSIS location:
.\tools\build-release.ps1 -MakeNsis 'C:\Program Files (x86)\NSIS\makensis.exe'
```

Requires .NET 10 SDK, NSIS 3 (including System and nsExec plugins), Windows SDK
SignTool, and the existing CurrentUser\My Firawynix certificate
`668BEF56480FCA8EB1B0BC3B102A6A9CEC554CC4`. The signing script reuses the
firaw-work-assistant pattern and signs with the certificate store, without
reading/exporting key material. Signing and an RFC3161 timestamp are mandatory
for app EXEs, embedded uninstallers, and all three installers. Timestamping needs
network access. Nothing installs certificates or changes trust settings.

Outputs are copied to both `release/` and `site/public/downloads/` after successful
packaging. These are the same bytes for the site, online installer, and CENTER:

| Artifact | Canonical URL |
| --- | --- |
| Universal online / CENTER download_url | https://h4k.firawynix.com.br/downloads/Firaw-h4K-Instalador-Online.exe |
| Offline x64 | https://h4k.firawynix.com.br/downloads/Firaw-h4K-1.0.0-x64-Setup.exe |
| Offline x86 | https://h4k.firawynix.com.br/downloads/Firaw-h4K-1.0.0-x86-Setup.exe |
| Manifest | https://h4k.firawynix.com.br/downloads/release.json |
| Checksums | https://h4k.firawynix.com.br/downloads/SHA256SUMS.txt |

The manifest is `{version, architectures:{x64:{url,size,sha256,signerThumbprint},
x86:{url,size,sha256,signerThumbprint}}}`. Sizes/hashes are measured after signing.
Checksums cover all installers and the manifest. Build staging directories are
unique and retained for diagnosis; old files are not reused as payloads. Scripts
do not upload, deploy, modify the Center catalog, or create a Selector bundle.
Center architecture metadata can reference the same canonical offline URLs.

The online installer chooses x64 on a 64-bit OS and x86 otherwise. It verifies
the canonical URL, size, SHA-256, pinned signer, timestamp, and signed version
before executing the offline package. Following the reference certificate policy,
`Valid` and `NotTrusted` signatures are accepted only with the pinned certificate;
hash mismatches and missing signatures are rejected. `NotTrusted` permits this
private certificate without changing machine trust; Windows may still show an
unrecognized-publisher/SmartScreen prompt.

## Installation and updater contract

- Fixed per-user path: `%LOCALAPPDATA%\Programs\Firaw h4K`.
- Executable: `Firaw-h4K.exe`; uninstall key: HKCU
  `Software\Microsoft\Windows\CurrentVersion\Uninstall\FirawH4K` (32-bit view).
- Interactive installers require the consent checkbox. No automatic application
  launch, startup entry, scheduled task, or effect activation is installed.
- Silent install requires `/S /ACCEPTCONSENT`, where the calling UI must first
  show `packaging/consent.txt` and obtain acceptance. For example:
  `Firaw-h4K-Instalador-Online.exe /S /ACCEPTCONSENT`.
- `/D=` is intentionally overridden; CENTER must use the fixed per-user path.
- Exit `0`: success; `1602`: silent consent missing; `1618`: application/file in
  use or another installation; `1603`: verification, download, or installation
  failure; `1633`: x64 package on 32-bit Windows.
- Windows Restart Manager checks the exact installed EXE, including watchdog
  processes using that file, before install/uninstall and immediately before file
  changes. An inspection error blocks the operation. No process is killed or
  asked to shut down, and there is no forced reboot or delayed replacement.
- An updater must wait for voluntary app exit and treat `1618` as deferred. Ask
  users to press Ctrl+Alt+F12, then close h4K. Never retry by terminating it or by
  suppressing the guard. Ctrl+Alt+Del remains available.

The app currently provides no shared update mutex/effects-state protocol. The
guard therefore blocks any use of the installed EXE, regardless of effect state.
A new launch between the final check and file replacement can still cause a
sharing error; that fails installation rather than forcing a replacement. An
app/updater cooperation protocol would require changes outside this scope.
MSIX servicing is controlled by Windows/Store, not the NSIS guard; do not add a
custom forced Store-update path while effects are active.

## Store bundle

Create `packaging/store-identity.json` from `store-identity.example.json`, populate
the exact confirmed Partner Center `Name`, `Publisher`, `PublisherDisplayName`,
reserved `DisplayName`, chosen application `ApplicationId`, and `StoreId`, then
set boolean `confirmed` to `true`. Blank or placeholder identities are rejected
before any publishing. No product identity has been invented or copied from the
other app.

```powershell
.\tools\build-store.ps1
# Optional explicit confirmed identity file:
.\tools\build-store.ps1 -IdentityPath 'C:\approved\store-identity.json'
```

Builds fresh self-contained x64 and x86 apps, reuses the PNG for aspect-preserving
Store logos and the existing ICO embedded by the project, and uses official
Windows SDK MakeAppx with validation enabled. Output:
`release/Firaw-h4K-1.0.0.msixbundle` and
`release/store-packages-1.0.0/Firaw-h4K-1.0.0-{x64,x86}.msix`.
The app EXEs use the pinned signature. Packages/bundle are unsigned for Store
submission because the confirmed Store Publisher may differ from `CN=Firawynix`;
Store applies its own package signature. Sideloading needs a certificate matching
that identity and is not implemented. No startup extension is declared.

## Verification before release

The implementation can be statically parsed without building. Before publishing
real artifacts, test fresh install, same-version reinstall, upgrade, uninstall,
silent consent rejection, and running-app deferral on x64 and x86 Windows. Check
both the main process and cursor watchdog, and verify unrelated processes are
left alone. Test the online downloader against real canonical artifacts, including
bad hash/signer/size and failed downloads. Inspect all installer/uninstaller
signatures and run Store validation with the confirmed identity. No runtime
installation or Store certification is implied by static checks.

References: [NSIS signed uninstallers](https://nsis.sourceforge.io/Signing_an_Uninstaller_externally),
[Restart Manager file resources](https://learn.microsoft.com/en-us/windows/win32/api/restartmanager/nf-restartmanager-rmregisterresources),
[official MakeAppx packaging](https://learn.microsoft.com/en-us/windows/msix/package/create-app-package-with-makeappx-tool).
