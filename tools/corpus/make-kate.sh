#!/bin/sh
# Makes tests/corpus/own/kate.ogv: a Theora + Vorbis clip with a Kate subtitle track, from our own kate-source.srt.
# FFmpeg can't write Kate, so this uses Xiph's kateenc and oggz-merge, fetched from Debian's package pool and
# unpacked into a temporary folder (nothing is installed). Run inside WSL or any Debian/Ubuntu shell, from the repo root,
# after: ffmpeg -f lavfi -i testsrc2=size=160x90:rate=15 -f lavfi -i sine=sample_rate=22050 -t 5 -c:v libtheora -q:v 2 -c:a libvorbis -q:a 0 -ac 1 tests/corpus/own/kate-base.ogv
set -e
REPO=$(pwd)
WORK=$(mktemp -d)
cd "$WORK"
POOL=https://deb.debian.org/debian/pool/main
for p in libk/libkate/libkate-tools_0.4.3-2 libk/libkate/libkate1_0.4.3-2 libk/libkate/liboggkate1_0.4.3-2 \
         libo/libogg/libogg0_1.3.6-2+b1 libp/libpng1.6/libpng16-16t64_1.6.59-1 \
         libo/liboggz/oggz-tools_1.1.3-3+b1 libo/liboggz/liboggz2_1.1.3-3+b1; do
  wget -q "$POOL/${p}_amd64.deb"
done
for f in *.deb; do dpkg -x "$f" root; done
export LD_LIBRARY_PATH=$(find root -name "*.so*" -exec dirname {} \; | sort -u | tr "\n" :)
root/usr/bin/kateenc -t srt -c SUB -l en -o kate-only.ogg "$REPO/tests/corpus/own/kate-source.srt"
root/usr/bin/oggz-merge -o "$REPO/tests/corpus/own/kate.ogv" "$REPO/tests/corpus/own/kate-base.ogv" kate-only.ogg
rm "$REPO/tests/corpus/own/kate-base.ogv"
cd "$REPO"; rm -rf "$WORK"
