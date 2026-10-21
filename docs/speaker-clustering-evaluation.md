# Speaker clustering evaluation

This is a local, reproducible evaluation of the embedding and clustering stage.
It evaluates annotated speech intervals directly; it does not measure Whisper
transcript or timestamp accuracy. No meeting audio or derived embeddings are
stored in the repository.

## Dataset and method

- Dataset: meeting `ES2002a` from the [AMI Meeting Corpus](https://groups.inf.ed.ac.uk/ami/), whose current corpus license is [CC BY 4.0](https://groups.inf.ed.ac.uk/ami/download/). The corpus is credited to the AMI Consortium; the linked corpus and download pages carry its attribution and terms.
- Audio: the public 16 kHz mono `ES2002a.Mix-Headset.wav` meeting mix.
- Labels: the matching manual NXT `segments` annotations, one file per speaker.
- The four speaker records map to participant backgrounds in the official [AMI participant metadata](https://groups.inf.ed.ac.uk/ami/corpus/participantids.shtml): Northern Ireland, Scotland, Canada, and Singapore/Canada; two records also list Mandarin/Cantonese or French influence. This provides accent variation, but this one meeting is too small to compare accent groups separately.
- Evaluated 277 annotated turns. 107 are shorter than the diarizer's 1-second minimum and remain fallback-labeled. The annotations mark 230 turns as overlapping another speaker's interval.
- The remaining 170 turns were embedded with the pinned WeSpeaker model. Clustering was replayed at thresholds 0.70 through 0.98. Pairwise precision/recall compares whether two usable turns have the same reference speaker against whether the clusterer assigns them the same cluster. Pairs involving turns with no embedding are excluded.
- Noise conditions add deterministic white Gaussian noise at 10 dB and 0 dB signal-to-noise ratio to the public mix before embedding. These are controlled perturbations, not recordings of real rooms or microphones.

## Results

| Condition | Threshold | Precision | Recall | F1 | False merges | False splits |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Clean, current | 0.88 | 0.924 | 0.048 | 0.092 | 20 | 4,789 |
| Clean, best F1 in sweep | 0.71 | 0.683 | 0.265 | 0.382 | 618 | 3,699 |
| 10 dB noise, current | 0.88 | 0.721 | 0.173 | 0.279 | 337 | 4,161 |
| 10 dB noise, best F1 in sweep | 0.73 | 0.547 | 0.378 | 0.447 | 1,575 | 3,131 |
| 0 dB noise, current | 0.88 | 0.461 | 0.309 | 0.370 | 1,824 | 3,475 |
| 0 dB noise, best F1 in sweep | 0.75 | 0.354 | 0.519 | 0.421 | 4,763 | 2,422 |

The current threshold favors avoiding false merges: on clean audio it made 20
false-merge pairs compared with 618 at the F1-optimal threshold. It also split
many returning-speaker pairs. We retain 0.88 as a conservative default because
a false merge can put words from different people under one cluster label.
This is a risk choice, not evidence that 0.88 provides balanced diarization.

Overlap remains a known failure mode: the mono mix does not isolate concurrent
voices, and this evaluation shows no overlap-separation capability. Short
segments under one second retain the existing fallback label. The public sample
is one English meeting. Its participant metadata spans different regional
backgrounds, but the results are not broken out by accent group and do not
support a corpus-wide accuracy claim. More meetings and larger accent groups
are needed before generalizing or changing the threshold.

## Reproduce

Download the manual annotation archive and `ES2002a.Mix-Headset.wav` from the
[AMI download page](https://groups.inf.ed.ac.uk/ami/download/). Extract the
archive outside the repository and point the evaluator at its `segments`
directory. The speaker model must already be present in the local Biscotti
model cache.

```powershell
dotnet run -c Release --project tools/SpeakerEvaluation/SpeakerEvaluation.csproj -- `
  C:\path\to\ES2002a.Mix-Headset.wav `
  C:\path\to\ami\annotations\segments `
  "$env:LOCALAPPDATA\BiscottiWin\Models\wespeaker_en_voxceleb_resnet34.onnx" `
  > C:\path\to\evaluation.json
```

Optional fourth argument overrides the default synthetic noise conditions, for
example `10,0` for 10 dB and 0 dB. The evaluator reads only the WAV, matching
`<meeting>.<speaker>.segments.xml` files, and model path supplied on the
command line.
