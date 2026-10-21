# Third-party notices

The release ZIP contains `LICENSES/DEPENDENCIES.md`, version-matched .NET
runtime and WPF license/third-party notice files, the Microsoft .NET Library
License terms for Windows, and the license/notice files available in the
resolved NuGet packages. Microsoft Windows App SDK license terms are included
because its runtime is published self-contained with the app. Review those
terms when redistributing the ZIP.

## Bundled application dependencies

| Dependency | Version | License / notice |
| --- | --- | --- |
| .NET runtime and Windows Desktop runtime | 10.0.x | MIT and the runtime's included `ThirdPartyNotices.txt` |
| Microsoft Windows App SDK runtime | 1.8.260804001 | Microsoft Software License Terms; full package license is included in `LICENSES/` |
| Whisper.net and CPU runtime | 1.9.0 | MIT |
| sherpa-onnx .NET/runtime | 1.13.5 | Apache-2.0 |
| NAudio | 3.2.0 | MIT |
| Ical.Net | 5.2.3 | MIT |
| NodaTime | 3.2.2 | Apache-2.0 |
| Microsoft WebView2 SDK | 1.0.3179.45 | MIT |

The release includes the resolved runtime package inventory and each NuGet
license identifier in `LICENSES/DEPENDENCIES.md`. Package-provided license and
notice files are copied into `LICENSES/NuGet/`.

## Models downloaded on demand

The Whisper `ggml-large-v3-turbo.bin` and `ggml-small.bin` model files are
downloaded from the [whisper.cpp Hugging Face repository](https://huggingface.co/ggerganov/whisper.cpp),
which identifies the model files as MIT licensed. Models are not bundled in
the release ZIP.

Biscotti downloads `wespeaker_en_voxceleb_resnet34.onnx` from the
[sherpa-onnx speaker model release](https://github.com/k2-fsa/sherpa-onnx/releases/tag/speaker-recongition-models)
on demand. The model is provided by the WeSpeaker project and is licensed under
the [Creative Commons Attribution 4.0 International license](https://creativecommons.org/licenses/by/4.0/).

Attribution: WeSpeaker contributors, “WeSpeaker pretrained VoxCeleb ResNet34
model,” [WeSpeaker](https://github.com/wenet-e2e/wespeaker), trained on the
VoxCeleb dataset.

## Optional CUDA runtime downloaded on demand

The CUDA tier is not bundled. When a user opts in, Biscotti downloads
`whisper.net.runtime.cuda.windows` 1.9.0 and NVIDIA CUDA runtime 13.1.80 and
cuBLAS 13.1.1.3 packages into `%LOCALAPPDATA%\BiscottiWin\runtimes`. The
Whisper.net package is MIT licensed; NVIDIA redistributables are subject to
the [NVIDIA CUDA Toolkit EULA](https://docs.nvidia.com/cuda/eula/index.html).
