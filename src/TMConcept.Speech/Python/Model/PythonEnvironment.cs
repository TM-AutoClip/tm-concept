namespace TMConcept.Speech.Python;

/// <summary>
/// One Python environment (a uv venv under <c>&lt;tools&gt;/speech-py/env-&lt;key&gt;</c>) with the pinned packages of one or more Python voices.
/// Its packages come from a lock file shipped with the app (<c>Python/Files/Locks/&lt;key&gt;-&lt;backend&gt;.txt</c>: every package at an exact
/// version, made with <c>uv pip compile --universal</c> for Windows and Linux): one for the CUDA build of PyTorch, one for the processor build.
/// </summary>
/// <param name="Key">Key (folder and lock file name).</param>
/// <param name="Name">What it is in the install window ("XTTS-v2").</param>
/// <param name="Packages">The packages asked for (the lock pins these and everything they need).</param>
/// <param name="CudaBytes">About how much the CUDA install downloads (PyTorch with CUDA alone is 3.5 GB on Windows).</param>
/// <param name="CpuBytes">About how much the processor install downloads.</param>
/// <param name="ProcessorOnly">Its voices always run on the processor (the small MMS): only the processor build is installed.</param>
/// <param name="CudaBackend">
/// Its PyTorch CUDA build (<see cref="PythonPins.CudaBackend"/>; 28 Nov 2026: MAGNeT's audiocraft needs PyTorch 2.1.0 with xformers, built for
/// CUDA 12.1, <see cref="PythonPins.Cuda121Backend"/>).
/// </param>
public sealed record PythonEnvironment(string Key, string Name, IReadOnlyList<string> Packages, long CudaBytes, long CpuBytes, bool ProcessorOnly = false,
    string CudaBackend = PythonPins.CudaBackend);
