let
  pkgs = import <nixpkgs> {};
in pkgs.buildFHSEnv {
  name = "godot-env";
  targetPkgs = pkgs: with pkgs; [
    bash
    glibc
    gcc
    alsa-lib
    freetype
    zlib
    mesa
    vulkan-loader

    icu
    dbus
    libpulseaudio
    systemd  # for libudev
    libxkbcommon
    fontconfig

    xorg.libX11
    xorg.libXcursor
    xorg.libXrandr
    xorg.libXinerama
    xorg.libXi
    xorg.libXext
  ];
  runScript = "./ArcadeFrontend.x86_64";
}
