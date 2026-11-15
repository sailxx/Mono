# Security policy

## Reporting

Do not publish real OTP secrets, vault files, backups, or exploit details in public issues. Use the repository Security tab to report a vulnerability privately if private reporting is enabled. Otherwise, ask the maintainer for a private reporting channel without including sensitive details.

## Protection and limits

The local vault uses Windows DPAPI for the current Windows user. Portable backups use AES-256-GCM with PBKDF2-SHA256 (600,000 iterations). The optional master password locks the application interface; it does not provide a separate encryption boundary against software running as the same Windows user.

Protect your Windows account and backup password. Malware running as your user, administrator access, screenshots and clipboard history remain outside this protection boundary. Clipboard clearing cannot recall copies taken by other applications.

Legacy data is retained during migration. Remove old copies only after verifying the migrated vault and a restorable backup.

## Repository administration

Maintainers should enable private vulnerability reporting, secret scanning and push protection where available, protect the default branch with required review and CI checks, and restrict release-tag creation. These GitHub settings are not enabled by committing this file.

## Release verification

Check the build provenance of releases that include an attestation using `gh attestation verify <installer>.exe --repo sailxx/WinOTP`. Build provenance is not Windows code signing and does not guarantee absence of vulnerabilities.
