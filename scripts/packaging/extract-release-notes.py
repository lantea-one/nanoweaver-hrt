#!/usr/bin/env python3
"""
NanoWeaver HRT: Automated Detailed Release Notes Extractor
----------------------------------------------------------
WHAT: Extracts the matching versioned changelog section from docs/release-notes.md
      and appends structured direct binary download and cryptographic verification links.
WHY:  Guarantees every Sovereign Forgejo Release and GitHub mirror Release carries
      complete milestone release notes automatically without manual editing.
HOW:  Parses level-2 version headings (## vYYYY.MM.DD.N) from docs/release-notes.md,
      falls back to the newest release entry for rolling 'nightly' releases, and writes
      the combined Markdown body to stdout or --output.
"""

from __future__ import annotations

import argparse
import re
import sys
from pathlib import Path


def extract_section(release_notes_path: Path, tag_name: str) -> str:
    """
    Extract the Markdown release notes section corresponding to tag_name.

    Args:
        release_notes_path: Path to docs/release-notes.md.
        tag_name: Target release tag (e.g. 'v2026.11.19.6' or 'nightly').

    Returns:
        The extracted Markdown section string, stripped of trailing horizontal rules.
    """
    # WHAT: Splits docs/release-notes.md by versioned '## v...' headings.
    # WHY:  Allows deterministic matching of the exact milestone release notes for any CalVer tag.
    # HOW:  Uses multiline lookahead regex split and normalizes trailing '---' separators.
    content = release_notes_path.read_text(encoding="utf-8")
    sections = re.split(r"(?m)^(?=## v[0-9]+\.[0-9]+\.[0-9]+(?:\.[0-9]+)?)", content)
    parsed: list[tuple[str, str]] = []
    for part in sections:
        match = re.match(r"^## (v[0-9]+\.[0-9]+\.[0-9]+(?:\.[0-9]+)?)", part)
        if not match:
            continue
        version_tag = match.group(1)
        body = part.strip()
        if body.endswith("---"):
            body = body[:-3].strip()
        parsed.append((version_tag, body))

    normalized = tag_name if tag_name.startswith("v") else f"v{tag_name}"
    for version_tag, body in parsed:
        if version_tag == normalized:
            return body

    if parsed:
        latest_tag, latest_body = parsed[0]
        if tag_name == "nightly":
            return (
                f"### Continuous Rolling Build (Latest Milestone Baseline: {latest_tag})\n\n"
                f"{latest_body}"
            )
        return latest_body

    return "### Sovereign Research & Clinical Therapeutics Release\n\nAutomated build of **NanoWeaver HRT**."


def build_download_catalog(registry: str, repository: str, tag_name: str) -> str:
    """
    Construct the standardized direct binary download and cryptographic verification section.

    Args:
        registry: Git host (e.g. 'git.lantea.one' or 'github.com').
        repository: Repository slug (e.g. 'lantea-one/nanoweaver-hrt').
        tag_name: Target release tag (e.g. 'v2026.11.19.6' or 'nightly').

    Returns:
        Formatted Markdown catalog of platform packages and trust anchors.
    """
    # WHAT: Builds direct clickable release asset download links for every supported OS and trust anchor.
    # WHY:  Gives clinicians and researchers immediate 1-click access to signed installers and checksums.
    # HOW:  Interpolates host, repository, and tag into canonical Forgejo/GitHub release download URLs.
    base = f"https://{registry}/{repository}/releases/download/{tag_name}"
    return f"""### Direct Binary Downloads & Cryptographic Verification

All packages are cryptographically signed and verifiable against the Sovereign Root CA and OpenPGP archive key.

#### Direct Windows Executables
- [nanoweaver-hrt-desktop-installer.exe]({base}/nanoweaver-hrt-desktop-installer.exe): Dual-scope Windows GUI installer (Standard user & Administrator scopes)
- [nanoweaver-hrt-desktop-portable-x64.exe]({base}/nanoweaver-hrt-desktop-portable-x64.exe): Standalone single-file portable Windows executable (Zero installation)
- [NanoWeaverHrt.exe]({base}/NanoWeaverHrt.exe): Direct portable executable
- [NanoWeaverHrt.msix]({base}/NanoWeaverHrt.msix): Windows Store application package

#### Linux Distribution Packages
- [nanoweaver-hrt-desktop-amd64.deb]({base}/nanoweaver-hrt-desktop-amd64.deb): Debian / Ubuntu x86_64
- [nanoweaver-hrt-desktop-arm64.deb]({base}/nanoweaver-hrt-desktop-arm64.deb): Debian / Ubuntu arm64
- [nanoweaver-hrt-desktop-amd64.rpm]({base}/nanoweaver-hrt-desktop-amd64.rpm): Fedora / RHEL x86_64
- [nanoweaver-hrt-desktop-arm64.rpm]({base}/nanoweaver-hrt-desktop-arm64.rpm): Fedora / RHEL aarch64
- [nanoweaver-hrt-desktop-x86_64.pkg.tar.zst]({base}/nanoweaver-hrt-desktop-x86_64.pkg.tar.zst): ArchLinux / CachyOS x86_64
- [nanoweaver-hrt-desktop-arm64.pkg.tar.zst]({base}/nanoweaver-hrt-desktop-arm64.pkg.tar.zst): ArchLinux aarch64

#### Apple macOS Disk Images
- [NanoWeaverHrt.dmg]({base}/NanoWeaverHrt.dmg): Universal Apple Silicon + Intel Disk Image
- [NanoWeaverHrt-macos-arm64.dmg]({base}/NanoWeaverHrt-macos-arm64.dmg): Apple Silicon Disk Image
- [NanoWeaverHrt-macos-amd64.dmg]({base}/NanoWeaverHrt-macos-amd64.dmg): Intel x86_64 Disk Image
- [NanoWeaverHrt-macos-universal.app.tar.gz]({base}/NanoWeaverHrt-macos-universal.app.tar.gz): Universal macOS Application Bundle Archive

#### Mobile Application Packages
- [nanoweaver-hrt-mobile-android.apk]({base}/nanoweaver-hrt-mobile-android.apk): Android application package
- [nanoweaver-hrt-mobile-ios.ipa]({base}/nanoweaver-hrt-mobile-ios.ipa): iOS application package
- [nanoweaver-hrt-mobile-ipados.ipa]({base}/nanoweaver-hrt-mobile-ipados.ipa): iPadOS application package

#### Verification & Trust Anchors
- [SHA256SUMS]({base}/SHA256SUMS): SHA-256 Release Catalog
- [SHA256SUMS.p7s]({base}/SHA256SUMS.p7s): Detached X.509 CMS Signature
- [SHA256SUMS.asc]({base}/SHA256SUMS.asc): Detached OpenPGP Armored Signature
- [nanoweaver-codesign-root-ca.crt]({base}/nanoweaver-codesign-root-ca.crt): Sovereign Root CA Trust Anchor
- [nanoweaver-archive-keyring.asc]({base}/nanoweaver-archive-keyring.asc): OpenPGP Public Keyring"""


def main() -> int:
    """
    CLI entry point composing detailed release notes for Forgejo or GitHub Releases.

    Returns:
        Exit status code (0 on success).
    """
    parser = argparse.ArgumentParser(description="Extract detailed NanoWeaver HRT release notes.")
    parser.add_argument("--tag", required=True, help="Target release tag (e.g. v2026.11.19.6 or nightly)")
    parser.add_argument("--registry", default="git.lantea.one", help="Git forge host for download links")
    parser.add_argument("--repo", default="lantea-one/nanoweaver-hrt", help="Owner/repository slug")
    parser.add_argument(
        "--notes-file",
        default="docs/release-notes.md",
        help="Path to docs/release-notes.md",
    )
    parser.add_argument("--output", default="", help="Optional output file path (writes to stdout if omitted)")
    args = parser.parse_args()

    section = extract_section(Path(args.notes_file), args.tag)
    catalog = build_download_catalog(args.registry, args.repo, args.tag)
    combined = f"{section}\n\n---\n\n{catalog}\n"

    if args.output:
        out_path = Path(args.output)
        out_path.parent.mkdir(parents=True, exist_ok=True)
        out_path.write_text(combined, encoding="utf-8")
    else:
        sys.stdout.write(combined)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
