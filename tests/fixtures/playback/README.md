# Playback fixtures

These are small files for `tests/Stx.Playback.Tests`. Every one was made with FFmpeg from a synthetic test pattern and tone, so there is no third-party content. Each is under 100 KB.

| File | What it is | Made with |
|---|---|---|
| `tiny.mp4` | 2 s, 320x180, 25 fps, H.264 video with mono AAC audio at 44.1 kHz | `ffmpeg -f lavfi -i testsrc2=size=320x180:rate=25 -f lavfi -i sine=frequency=440:sample_rate=44100 -t 2 -c:v libx264 -preset veryslow -crf 35 -pix_fmt yuv420p -c:a aac -b:a 32k -ac 1 tiny.mp4` |
| `tiny-audio.flac` | 2 s of 16-bit, 44.1 kHz mono FLAC with no picture | `ffmpeg -f lavfi -i sine=frequency=440:sample_rate=44100 -t 2 -c:a flac -sample_fmt s16 tiny-audio.flac` |
| `truncated.mp4` | the first 40% of `tiny.mp4`'s bytes, so the `moov` atom is missing (`ffprobe` reports "moov atom not found") | `python -c "d=open('tiny.mp4','rb').read(); open('truncated.mp4','wb').write(d[:int(len(d)*0.4)])"` |
| `Фильм тест.mp4` | a byte-for-byte copy of `tiny.mp4`, with a Cyrillic name and a space | `cp tiny.mp4 "Фильм тест.mp4"` |
| `surround-5.1.flac` | 1.5 s of 16-bit, 48 kHz, six-channel (5.1) FLAC, for the "surround is not downmixed" test | `ffmpeg -f lavfi -i sine=frequency=440:sample_rate=48000 -t 1.5 -ac 6 -c:a flac -sample_fmt s16 surround-5.1.flac` |
