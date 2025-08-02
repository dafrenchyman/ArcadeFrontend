#!/bin/sh
echo -ne '\033c\033]0;ArcadeFrontend\a'
base_path="$(dirname "$(realpath "$0")")"
"$base_path/ArcadeFrontend.x86_64" "$@"
