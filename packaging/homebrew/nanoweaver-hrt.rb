class NanoweaverHrt < Formula
  desc "Decentralized Synthetic Biology & DNA Origami Research Platform"
  homepage "https://git.lantea.one/lantea-one/nanoweaver-hrt"
  url "https://git.lantea.one/lantea-one/nanoweaver-hrt.git", branch: "main"
  version "2026.11.04.9"
  license "MIT"

  depends_on "dotnet" => :build

  def install
    system "dotnet", "publish", "src/NanoWeaverHrt.Desktop/NanoWeaverHrt.Desktop.csproj",
           "-c", "Release",
           "-o", libexec
    bin.install_symlink libexec/"NanoWeaverHrt.Desktop" => "nanoweaver-hrt-desktop"
  end

  test do
    system "#{bin}/nanoweaver-hrt-desktop", "--version"
  end
end
