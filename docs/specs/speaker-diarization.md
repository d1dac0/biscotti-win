# Loopback speaker labels use offline diarization

**Status:** Accepted

## Decision

Use the `org.k2fsa.sherpa.onnx` .NET package for speaker embedding extraction
and cluster embeddings computed from Whisper's timestamped loopback segments.
Whisper timestamps select audio directly for each segment, so the pipeline
does not need a second segmentation model. Lazy-download the English WeSpeaker embedding
model into the app's existing model directory; verify its pinned SHA-256 before
use. Preserve its CC-BY-4.0 attribution in project notices. Never bundle model
weights in source control or send meeting audio to a service.

Assign each Whisper transcript line to the cluster of its audio segment.
Segments that are too short or have unusable embeddings keep the existing
gap-based fallback label. Renumber cluster IDs by first appearance to keep
transcript labels stable within a meeting. Keep `You` from the separately
captured microphone channel unchanged.

Resolve a cluster to a real name only with direct evidence: an explicit
self-introduction in that speaker's transcript or an attendee name matched to
that cluster with such evidence. Calendar invitees are candidate names and
must not be assigned to voices merely because they are on the invite. When no
match is supported, retain `Speaker N`.

## Alternatives

- **Keep the current gap-length labeling** — inexpensive, but it treats turns
  as identities and cannot distinguish a speaker who returns after a pause.
- **Add a Python/pyannote sidecar** — the main diarization pipeline is capable,
  but adds a separately installed runtime and process to a .NET desktop app.
- **Write an embedding and clustering stack in Biscotti** — avoids another
  runtime, but would require independently maintaining segmentation, embedding
  extraction, and clustering models and algorithms.

## Why

The sherpa-onnx project supplies a C# speaker embedding API and Windows native
NuGet runtime. Whisper already identifies speech regions and timestamps, so
clustering those segments avoids introducing a second speech-segmentation
model. This meets the issue's explicit embedding-plus-clustering requirement
while keeping inference on-device. The repo stores encrypted 16 kHz mono PCM
loopback audio, so processing can reuse the existing authenticated audio reader
and WAV format.

The 0.88 similarity threshold was first checked against ten short public
Sherpa-ONNX speaker samples. A later labeled evaluation used 277 annotated
turns from one public AMI meeting. On the 170 turns long enough to embed, the
threshold produced pairwise precision 0.924, recall 0.048, and F1 0.092. The
F1-optimal tested threshold was 0.71 (precision 0.683, recall 0.265); its 618
false-merge pairs were substantially more than the 20 at 0.88. Keep 0.88 as a
conservative default until broader evidence supports accepting that merge risk.
This result exposes substantial false splitting, so cluster labels remain
best effort and overlap is not separated. See
[speaker-clustering evaluation](../speaker-clustering-evaluation.md) for the
method, noise experiments, and scope limits.

The selected model is not redistributed in the app; it is downloaded after
recording when needed. Its source training model is licensed CC-BY-4.0, which
requires attribution. See `docs/THIRD-PARTY-NOTICES.md`.

## References

- [sherpa-onnx C# diarization example](https://github.com/k2-fsa/sherpa-onnx/blob/master/dotnet-examples/offline-speaker-diarization/Program.cs)
- [sherpa-onnx C# speaker embedding API](https://github.com/k2-fsa/sherpa-onnx/blob/master/scripts/dotnet/SpeakerEmbeddingExtractor.cs)
- [sherpa-onnx offline diarization API](https://k2-fsa.github.io/sherpa/onnx/c-api/html/speaker_diarization.html)
- [sherpa-onnx NuGet package](https://www.nuget.org/packages/org.k2fsa.sherpa.onnx/)
- [WeSpeaker pretrained model license note](https://github.com/wenet-e2e/wespeaker/blob/master/docs/pretrained.md#model-license)
