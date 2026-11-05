"""Writes tests/corpus/own/arpeggio.mid and sine.mod byte by byte, so the corpus has a MIDI file and a tracker
module that are entirely our own. Run from the repo root: python tools/corpus/make-own-binaries.py"""
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
