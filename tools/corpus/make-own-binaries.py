"""Writes the corpus files no encoder makes, byte by byte, so each is entirely our own: MIDI (arpeggio.mid, and the
same tune as karaoke.kar and arpeggio.rmi), tracker modules (sine.mod, .s3m, .xm, .it, .669, .mtm) and game music
(arpeggio.vgm, tone.gbs). Every module plays one looping sine sample as C, E, G and A; the game-music files drive the
console's own sound chip. The same bytes on every run.
Run from the repo root: python tools/corpus/make-own-binaries.py"""
import math, struct

OWN = 'tests/corpus/own/'

# Standard MIDI file, format 0: a C major arpeggio of four quarter notes, then end of track.
events = bytearray()
events += b'\x00\xff\x51\x03\x07\xa1\x20'          # tempo: 500,000 microseconds per quarter note
events += b'\x00\xc0\x00'                          # program change: piano
for note in (60, 64, 67, 72):
    events += b'\x00\x90' + bytes([note, 100])     # note on
    events += b'\x83\x60\x80' + bytes([note, 0])   # 480 ticks later, note off
events += b'\x00\xff\x2f\x00'                      # end of track
midi = b'MThd' + struct.pack('>IHHH', 6, 0, 1, 480) + b'MTrk' + struct.pack('>I', len(events)) + bytes(events)
open(OWN + 'arpeggio.mid', 'wb').write(midi)

# ProTracker module (M.K., four channels): one looping 8-bit sine sample, one pattern playing four notes.
length = 2048
sample = bytes(int(round(100 * math.sin(2 * math.pi * i / 32))) & 0xff for i in range(length))
header = b'STX corpus'.ljust(20, b'\0')
for i in range(31):
    if i == 0:
        header += b'sine'.ljust(22, b'\0') + struct.pack('>HBBHH', length // 2, 0, 64, 0, length // 2)
    else:
        header += bytes(22) + struct.pack('>HBBHH', 0, 0, 0, 0, 1)
header += bytes([1, 127]) + bytes(128) + b'M.K.'
pattern = bytearray()
for row in range(64):
    for channel in range(4):
        if channel == 0 and row % 16 == 0:
            period = (428, 381, 339, 320)[row // 16]   # C, E, G, A
            pattern += bytes([(period >> 8) & 0x0f, period & 0xff, 0x10, 0])   # sample 1
        else:
            pattern += bytes(4)
open(OWN + 'sine.mod', 'wb').write(header + bytes(pattern) + sample)

# Karaoke MIDI (.kar): the same arpeggio as a format 0 file with the text events karaoke players read (Tune 1000's
# "@KMIDI KARAOKE FILE" mark, a title, then one lyric syllable before each note).
def text(delta, words):
    return bytes([delta]) + b'\xff\x01' + bytes([len(words)]) + words
karaoke = bytearray()
karaoke += text(0, b'@KMIDI KARAOKE FILE') + text(0, b'@TSTX corpus')
karaoke += b'\x00\xff\x51\x03\x07\xa1\x20' + b'\x00\xc0\x00'
for note, syllable in zip((60, 64, 67, 72), (b'Do', b'Mi', b'Sol', b'Do')):
    karaoke += text(0, syllable)
    karaoke += b'\x00\x90' + bytes([note, 100]) + b'\x83\x60\x80' + bytes([note, 0])
karaoke += b'\x00\xff\x2f\x00'
open(OWN + 'karaoke.kar', 'wb').write(b'MThd' + struct.pack('>IHHH', 6, 0, 1, 480) + b'MTrk' + struct.pack('>I', len(karaoke)) + bytes(karaoke))

# RIFF MIDI (.rmi): arpeggio.mid wrapped in Microsoft's RIFF "RMID" chunk.
data = b'data' + struct.pack('<I', len(midi)) + midi + (b'\0' if len(midi) % 2 else b'')
open(OWN + 'arpeggio.rmi', 'wb').write(b'RIFF' + struct.pack('<I', 4 + len(data)) + b'RMID' + data)

# The tracker modules below share sine.mod's sample (signed 8-bit, 32 samples a cycle, looped over its whole length)
# and its tune: C, E, G and A on the first channel, one every 16 rows, at speed 6 and 125 BPM.
unsigned = bytes((b + 128) & 0xff for b in sample)
TUNE = (0, 4, 7, 9)   # semitones above C
order = bytes([0, 255])   # pattern 0, then the end mark (S3M and IT)
def pad16(data):
    return data + bytes(-len(data) % 16)

# Scream Tracker 3 (.s3m): four channels, one sample, one pattern; unsigned samples (file format 2).
s3m = (b'STX corpus'.ljust(28, b'\0') + bytes([0x1a, 16, 0, 0])
       + struct.pack('<HHHHHH', len(order), 1, 1, 0, 0x1320, 2) + b'SCRM'
       + bytes([64, 6, 125, 0xb0, 16, 0]) + bytes(8) + struct.pack('<H', 0) + bytes([0, 8, 1, 9] + [255] * 28))
s3m += order
instrument_at = 0x70
pattern_at = instrument_at + 0x50
s3m += struct.pack('<HH', instrument_at >> 4, pattern_at >> 4)
s3m = s3m.ljust(instrument_at, b'\0')
rows = bytearray()
for row in range(64):
    if row % 16 == 0:
        rows += bytes([0x60, 0x40 + TUNE[row // 16], 1, 64])   # channel 1: note (octave 4), instrument 1, volume 64
    rows += b'\0'
packed = pad16(struct.pack('<H', len(rows) + 2) + bytes(rows))
sample_at = pattern_at + len(packed)
s3m += (bytes([1]) + b'sine.raw'.ljust(12, b'\0') + bytes([(sample_at >> 4) >> 16]) + struct.pack('<H', (sample_at >> 4) & 0xffff)
        + struct.pack('<III', length, 0, length) + bytes([64, 0, 0, 1]) + struct.pack('<I', 8363) + bytes(12)
        + b'sine'.ljust(28, b'\0') + b'SCRS')
s3m += packed + unsigned
open(OWN + 'sine.s3m', 'wb').write(s3m)

# FastTracker 2 (.xm): four channels, one instrument with one sample, one pattern; sample data delta-coded.
xm = (b'Extended Module: ' + b'STX corpus'.ljust(20, b'\0') + b'\x1a' + b'STX corpus'.ljust(20, b'\0')
      + struct.pack('<HI', 0x0104, 276) + struct.pack('<HHHHHHHH', 1, 0, 4, 1, 1, 1, 6, 125) + bytes(256))
cells = bytearray()
for row in range(64):
    for channel in range(4):
        if channel == 0 and row % 16 == 0:
            cells += bytes([0x87, 49 + TUNE[row // 16], 1, 0x50])   # note (C-4 is 49), instrument 1, volume 64
        else:
            cells += b'\x80'
xm += struct.pack('<IBHH', 9, 0, 64, len(cells)) + bytes(cells)
instrument = struct.pack('<I', 263) + b'sine'.ljust(22, b'\0') + struct.pack('<BHI', 0, 1, 40) + bytes(96 + 48 + 48 + 14 + 2 + 22)
assert len(instrument) == 263
delta, previous = bytearray(), 0
for b in sample:
    delta.append((b - previous) & 0xff)
    previous = b
xm += instrument + struct.pack('<IIIBbBBbB', length, 0, length, 64, 0, 1, 128, 0, 0) + b'sine'.ljust(22, b'\0') + bytes(delta)
open(OWN + 'sine.xm', 'wb').write(xm)

# Impulse Tracker (.it): sample mode, one sample, one pattern.
header = (b'IMPM' + b'STX corpus'.ljust(26, b'\0') + struct.pack('<HHHHHHHHH', 0x1004, len(order), 0, 1, 1, 0x0214, 0x0200, 0x0009, 0)
          + bytes([128, 48, 6, 125, 128, 0]) + struct.pack('<HII', 0, 0, 0) + bytes([32] * 4 + [160] * 60) + bytes([64] * 64))
assert len(header) == 0xc0
sample_header_at = len(header) + len(order) + 8
pattern_at = sample_header_at + 80
rows = bytearray()
for row in range(64):
    if row % 16 == 0:
        rows += bytes([0x81, 0x07, 60 + TUNE[row // 16], 1, 64])   # channel 1: note (C-5 is 60), sample 1, volume 64
    rows += b'\0'
pattern = struct.pack('<HH', len(rows), 64) + bytes(4) + bytes(rows)
sample_at = pattern_at + len(pattern)
it = header + order + struct.pack('<II', sample_header_at, pattern_at)
it += (b'IMPS' + b'sine.raw'.ljust(12, b'\0') + bytes([0, 64, 0x11, 64]) + b'sine'.ljust(26, b'\0') + bytes([1, 32])
       + struct.pack('<IIIIIII', length, 0, length, 8363, 0, 0, sample_at) + bytes(4))
it += pattern + sample
open(OWN + 'sine.it', 'wb').write(it)

# Composer 669 (.669): eight channels, one sample, one pattern; unsigned samples.
c669 = (b'if' + b'STX corpus'.ljust(108, b'\0') + bytes([1, 1, 0]) + bytes([0] + [255] * 127) + bytes([6] * 128) + bytes([63] * 128)
        + b'sine.raw'.ljust(13, b'\0') + struct.pack('<III', length, 0, length))
for row in range(64):
    for channel in range(8):
        if channel == 0 and row % 16 == 0:
            c669 += bytes([(24 + TUNE[row // 16]) << 2, 0x0f, 0xff])   # note, instrument 1 (stored as 0), volume 15
        else:
            c669 += bytes([0xff, 0, 0xff])
c669 += unsigned
open(OWN + 'sine.669', 'wb').write(c669)

# MultiTracker (.mtm): four channels, one sample, one track played by two patterns (the format wants at least two).
track = bytearray()
for row in range(64):
    track += bytes([(24 + TUNE[row // 16]) << 2, 0x10, 0]) if row % 16 == 0 else bytes(3)   # note, instrument 1
mtm = (b'MTM\x10' + b'STX corpus'.ljust(20, b'\0') + struct.pack('<HBBHBBBB', 1, 1, 1, 0, 1, 0, 64, 4) + bytes([3, 12, 3, 12] + [0] * 28)
       + b'sine'.ljust(22, b'\0') + struct.pack('<IIIbBB', length, 0, length, 0, 64, 0)
       + bytes([0, 1] + [0] * 126) + bytes(track) + (struct.pack('<H', 1) + bytes(62)) * 2 + unsigned)
open(OWN + 'sine.mtm', 'wb').write(mtm)

# Video Game Music (.vgm, version 1.50): the arpeggio on a Sega Master System's SN76489, one second a note.
CLOCK = 3579545
commands = bytearray(b'\x50\xbf\x50\xdf\x50\xff\x50\x90')   # channels 2, 3 and noise silent, channel 1 loudest
for frequency in (261.63, 329.63, 392.00, 523.25):
    period = round(CLOCK / (32 * frequency))
    commands += bytes([0x50, 0x80 | (period & 0x0f), 0x50, period >> 4]) + b'\x61' + struct.pack('<H', 44100)
commands += b'\x50\x9f\x66'                                 # silence, end of data
vgm = b'Vgm ' + struct.pack('<IIIIIIIIIHBBIIIII', 0x40 + len(commands) - 4, 0x150, CLOCK, 0, 0, 4 * 44100, 0, 0, 60,
                            0x0009, 16, 0, 0, 0, 0x0c, 0, 0)
assert len(vgm) == 0x40
vgm += bytes(commands)
open(OWN + 'arpeggio.vgm', 'wb').write(vgm)

# Game Boy Sound System (.gbs): an init routine that turns the APU on and holds a 440 Hz square wave on channel 1;
# the play routine returns at once.
code = bytes([0x3e, 0x80, 0xe0, 0x26,    # NR52: sound on
              0x3e, 0x77, 0xe0, 0x24,    # NR50: both sides loudest
              0x3e, 0x11, 0xe0, 0x25,    # NR51: channel 1 to both sides
              0x3e, 0x00, 0xe0, 0x10,    # NR10: no sweep
              0x3e, 0x80, 0xe0, 0x11,    # NR11: 50% duty
              0x3e, 0xf0, 0xe0, 0x12,    # NR12: volume 15, held
              0x3e, 0xd6, 0xe0, 0x13,    # NR13: low bits of 1750; 131072 / (2048 - 1750) = 440 Hz
              0x3e, 0x86, 0xe0, 0x14,    # NR14: high bits, trigger
              0xc9]).ljust(0x20, b'\0') + b'\xc9'   # ret; the play routine at 0x0420 is a ret too
gbs = (b'GBS' + bytes([1, 1, 1]) + struct.pack('<HHHH', 0x400, 0x400, 0x420, 0xfffe) + bytes([0, 0])
       + b'STX corpus'.ljust(32, b'\0') + b'STX'.ljust(32, b'\0') + b'STX corpus'.ljust(32, b'\0') + code)
assert gbs[0x70:0x71] == b'\x3e'
open(OWN + 'tone.gbs', 'wb').write(gbs)
