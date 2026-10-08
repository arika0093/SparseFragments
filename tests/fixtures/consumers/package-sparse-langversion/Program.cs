using System;
using LangVersionProbe;

// Entry point for the runnable TFMs (net48, net8.0). The netstandard2.0 leg
// excludes this file and is build-only: compilation success is its gate.
Console.WriteLine(LangVersionCheck.Run());
