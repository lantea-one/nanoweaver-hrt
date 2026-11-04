# Copyright 2026 Nanoweaver HRT Project Contributors
# Distributed under the terms of the MIT License

EAPI=8

inherit dotnet-pkg

DESCRIPTION="Decentralized Synthetic Biology & DNA Origami Research Platform"
HOMEPAGE="https://git.lantea.one/lantea-one/nanoweaver-hrt"
EGIT_REPO_URI="https://git.lantea.one/lantea-one/nanoweaver-hrt.git"

LICENSE="MIT"
SLOT="0"
KEYWORDS="~amd64 ~arm64"

RDEPEND="
	dev-dotnet/dotnet-runtime-nugets:10.0
	x11-libs/libX11
	x11-libs/libICE
	x11-libs/libSM
	media-libs/fontconfig
	sys-apps/dbus
	x11-libs/libnotify
"
DEPEND="${RDEPEND}"
