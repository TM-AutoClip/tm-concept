namespace TMConcept.Speech.Python;

/// <summary>
/// How a Python worker is started and what it loads: the environment's interpreter with <see cref="Arguments"/> (isolated mode,
/// <c>worker.py</c>), its environment variables (after the secrets are removed), and the <c>load</c> line sent first. A running worker of the
/// same <see cref="Key"/> (voice and device) is kept; another one is ended first.
/// </summary>
/// <param name="Key">Voice and device ("xtts-v2|cuda").</param>
/// <param name="Program">The interpreter.</param>
/// <param name="Arguments">Its arguments.</param>
/// <param name="WorkingDirectory">Its working folder.</param>
/// <param name="Environment">Variables set for it.</param>
/// <param name="Load">The <c>load</c> line.</param>
public sealed record PythonWorkerStart(string Key, string Program, IReadOnlyList<string> Arguments, string WorkingDirectory,
    IReadOnlyDictionary<string, string> Environment, PythonWorkerRequest Load);
