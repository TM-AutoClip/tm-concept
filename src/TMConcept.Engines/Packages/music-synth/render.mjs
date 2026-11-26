// TM Concept: plays a MIDI file into a WAV file with a SoundFont (C#: TMConcept.Engines SpessaSynthRenderer; "Program composes").
//
//   node render.mjs --soundfont "<file.sf3>" --midi "<score.mid>" --out "<score.wav>" [--rate 44100] [--tail 2.5]
//
// spessasynth_core (Apache-2.0) renders offline on the processor: no browser, no network, no graphics card. The output is 16-bit stereo PCM,
// normalised, with the instruments' fading tail after the last note. Lines: "progress <0..1>" while it renders, "done <seconds>" at the end;
// a failure writes "error: <reason>" and ends with exit code 1.
import * as fs from "node:fs/promises";
import { audioToWav, BasicMIDI, SoundBankLoader, SpessaSynthProcessor, SpessaSynthSequencer } from "spessasynth_core";

const BUFFER = 128;
const PROGRESS_EVERY = 2000;

function option(name, fallback) {
  const index = process.argv.indexOf("--" + name);
  if (index < 0 || index + 1 >= process.argv.length) {
    if (fallback === undefined) {
      throw new Error("missing --" + name);
    }

    return fallback;
  }

  return process.argv[index + 1];
}

function exact(buffer) {
  // A Node Buffer may share a larger pool: hand over exactly its bytes.
  return buffer.buffer.slice(buffer.byteOffset, buffer.byteOffset + buffer.byteLength);
}

async function main() {
  const soundfont = option("soundfont");
  const midiFile = option("midi");
  const out = option("out");
  const rate = Number(option("rate", "44100"));
  const tail = Number(option("tail", "2.5"));
  if (!Number.isFinite(rate) || rate < 8000 || rate > 96000 || !Number.isFinite(tail) || tail < 0 || tail > 30) {
    throw new Error("bad --rate or --tail");
  }

  const midi = BasicMIDI.fromArrayBuffer(exact(await fs.readFile(midiFile)));
  const synth = new SpessaSynthProcessor(rate, { eventsEnabled: false });
  synth.soundBankManager.addSoundBank(SoundBankLoader.fromArrayBuffer(exact(await fs.readFile(soundfont))), "main");
  await synth.ready;
  synth.setSystemParameter("autoAllocateVoices", true);

  const sequencer = new SpessaSynthSequencer(synth);
  sequencer.loadNewSongList([midi]);
  sequencer.play();

  const total = Math.ceil(rate * (midi.duration + tail));
  const left = new Float32Array(total);
  const right = new Float32Array(total);
  let filled = 0;
  let blocks = 0;
  while (filled < total) {
    sequencer.processTick();
    const size = Math.min(BUFFER, total - filled);
    synth.process(left, right, filled, size);
    filled += size;
    if (++blocks % PROGRESS_EVERY === 0) {
      console.log("progress " + (filled / total).toFixed(3));
    }
  }

  await fs.writeFile(out, new Uint8Array(audioToWav([left, right], rate, { normalizeAudio: true })));
  console.log("done " + (total / rate).toFixed(2));
}

main().catch((error) => {
  console.error("error: " + (error && error.message ? error.message : String(error)));
  process.exit(1);
});
