{ lib
, buildDotnetModule
, fetchgit
, dotnetCorePackages
, libX11
, libICE
, libSM
, fontconfig
, dbus
, libnotify
}:

buildDotnetModule rec {
  pname = "nanoweaver-hrt-desktop";
  version = "2026.11.04.9";

  src = fetchgit {
    url = "https://git.lantea.one/lantea-one/nanoweaver-hrt.git";
    rev = "refs/tags/v${version}";
    hash = "sha256-AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=";
  };

  projectFile = "src/NanoWeaverHrt.Desktop/NanoWeaverHrt.Desktop.csproj";
  nugetDeps = ./deps.nix;
  dotnet-sdk = dotnetCorePackages.sdk_10_0;
  dotnet-runtime = dotnetCorePackages.runtime_10_0;

  runtimeDeps = [
    libX11
    libICE
    libSM
    fontconfig
    dbus
    libnotify
  ];

  meta = with lib; {
    description = "Decentralized Synthetic Biology & DNA Origami Research Platform";
    homepage = "https://git.lantea.one/lantea-one/nanoweaver-hrt";
    license = licenses.mit;
    platforms = platforms.linux;
  };
}
