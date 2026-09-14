# Milestone 1 — Local Identity

## Scope

- ECDSA P-256 identity generation and persistence.
- Deterministic Peer ID derivation.
- Windows DPAPI (CurrentUser) encrypted key storage.
- No networking, signaling, or WebRTC.

## Design decisions

- **Key type:** ECDSA P-256 from `System.Security.Cryptography` (mature, built
  into .NET, no third-party crypto). Ed25519 is unavailable in .NET 9 built-ins;
  this will be re-evaluated during the Milestone 8 security review.
- **Peer ID:** `Base32(SHA-256(Qx || Qy)[0..16])` — 128 bits, unpadded RFC 4648
  Base32, 26 uppercase characters. Coordinates are zero-padded to fixed 32-byte
  width before hashing so the derivation is stable across key serializations.
- **Key storage:** DPAPI `CurrentUser` scope with static app-specific optional
  entropy (`"EphemeralChat-Identity-v1"`, not a secret, domain separation only).
  The plaintext PKCS#8 key never touches disk; the payload on disk is
  `DPAPI("ECID" | version | keyLen | PKCS#8)`.
- **Storage location (app):** `%LOCALAPPDATA%\EphemeralChat\identity.dat`.
  Tests inject a unique temp directory via the store constructor.

## Dependencies

| Package | Purpose | License | Source |
| --- | --- | --- | --- |
| `System.Security.Cryptography.ProtectedData` 9.0.0 | Exposes Windows DPAPI (`ProtectedData`) on modern .NET | MIT | Microsoft first-party |

## Privacy invariants

- The private key is generated locally and never transmitted; this milestone
  contains no networking code at all.
- The identity file holds only ciphertext (verified by a unit test that scans
  the file for the plaintext PKCS#8 bytes).
- Plaintext key buffers are zeroed with `CryptographicOperations.ZeroMemory`
  after protection (best-effort in managed .NET; deeper memory cleanup review
  happens in Milestone 8).
- Tampering with the identity file causes DPAPI unprotect to throw rather than
  load a corrupted identity (pinned by a unit test).

## Tests

- RFC 4648 Base32 test vectors.
- Peer ID determinism (same key, and across PKCS#8 re-import), distinctness,
  and format.
- Store round trip, plaintext-absence scan, tamper detection, delete semantics.
- Service create/persist/load-across-instances behavior.
- MainViewModel shows the Peer ID and milestone-0 send flow still works.

## Identity recovery (closeout fix)

When the identity file exists but cannot be loaded (DPAPI integrity failure,
wrong Windows user, or format corruption), startup no longer crashes:

- The user is prompted with an explicit warning; the default button is Cancel.
- Cancel keeps the unreadable file untouched and starts without an identity.
- Confirming rebuild deletes the file and creates a NEW identity with a NEW
  Peer ID. Peer ID replacement is never silent.
- The decision flow lives in `IdentityStartupService` (Security layer,
  UI-agnostic via a delegate), so the full matrix is unit tested: healthy
  load, first launch, corruption prompt, cancel preserving bytes, rebuild
  replacing the file, and post-rebuild Peer ID stability.
