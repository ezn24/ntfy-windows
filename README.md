# Vibe Coding by using Codex

# ntfy-windows

Windows 11 style ntfy desktop client (WinUI 3) with:
- authenticated subscriptions (Bearer + Basic)
- modern Fluent UI shell
- send message workflow with reusable templates

## Status
Scaffolded architecture + core services + starter UI pages.

## Planned stack
- WinUI 3 (Windows App SDK)
- .NET 8+
- Local encrypted credentials via DPAPI
- HTTP SSE/JSON stream for ntfy subscribe

## Core features
1. Accounts/Servers
   - Multiple ntfy servers
   - Auth mode per server (Bearer / Basic)
2. Inbox
   - Live message stream per topic
   - Filter/search/priority badges
3. Send
   - Topic, title, message, priority, tags, click URL
4. Templates
   - Save/update/delete templates
   - Token replacement (e.g. {{course}}, {{due_date}})

## Notes
This scaffold is implementation-first (services/models/UI shell). If your machine has WinUI templates installed, we can wire this into a full runnable app quickly.

## Release automation
- GitHub Actions workflow: `.github/workflows/release.yml`
- Manual build: run `Build Release` from Actions; all files are available as workflow artifacts
- Tagged release: push a tag such as `v1.0.0` to create a GitHub Release automatically
- Architectures: `x86`, `x64`, and `ARM64`
- Formats: self-contained portable ZIP and per-user Inno Setup installer for every architecture


- Tagged releases include `SHA256SUMS.txt` and GitHub build provenance attestations; no signing key is required

### Release integrity

Every tagged release contains:

- `SHA256SUMS.txt` for all portable ZIP and installer files
- GitHub Sigstore provenance for all portable ZIP and installer files

Verify a downloaded release:

```bash
sha256sum --check SHA256SUMS.txt
gh attestation verify ntfy-windows-win-x64-setup.exe --repo ezn24/ntfy-windows
```
