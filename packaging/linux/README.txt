Lupe for Linux (X11 and Wayland)
================================

Run without installing:    ./Lupe
Set up for this user (application menu, command "lupe"):
                           ./install.sh
Remove again:              ~/.local/opt/lupe/install.sh --remove

Needs glibc 2.31 or later (Ubuntu 20.04, Debian 11 and newer) and, usually already installed:
libX11, libXtst, libXrandr, libXi, libICE, libSM, fontconfig and ICU. Without ICU Lupe does not start,
without fontconfig it cannot read image files.
  Debian/Ubuntu:  sudo apt install libx11-6 libxtst6 libxrandr2 libxi6 libice6 libsm6 libfontconfig1
                  plus ICU, e.g. libicu72 (apt-cache search --names-only '^libicu[0-9]+$' shows the name)
  Fedora:         sudo dnf install libX11 libXtst libXrandr libXi libICE libSM fontconfig libicu

Camera: the user must be in the group "video" (sudo usermod -aG video $USER, then sign in again).
Wayland: screen scan, typing and the shortcut go through the desktop portals; the desktop asks for
permission the first time. Details: https://github.com/ict-baden/lupe/blob/main/docs/platform-linux.md
If Lupe reports LUP-0001: https://github.com/ict-baden/lupe/blob/main/docs/error-codes.md
Licence: LICENSE.txt (MIT), third-party licences: THIRD-PARTY-NOTICES.txt
