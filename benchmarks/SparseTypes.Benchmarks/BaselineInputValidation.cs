internal static class BaselineInputValidation
{
    internal static void Run()
    {
        var wide = new GeneratorWideModelBenchmarks { PropertyCount = 8 };
        ValidateStationary(wide.Setup, wide.IncrementalUnrelatedEditBytes);
        var depth = new GeneratorNestedDepthBenchmarks { Depth = 3 };
        ValidateStationary(depth.Setup, depth.IncrementalUnrelatedEditBytes);
        var fanOut = new GeneratorFanOutBenchmarks { FanOut = 2 };
        ValidateStationary(fanOut.Setup, fanOut.IncrementalUnrelatedEditBytes);
        var keyed = new GeneratorKeyedCollectionBenchmarks { KeyedMembers = 1 };
        ValidateStationary(keyed.Setup, keyed.IncrementalUnrelatedEditBytes);
        var dictionary = new GeneratorDictionaryMemberBenchmarks { DictionaryMembers = 1 };
        ValidateStationary(dictionary.Setup, dictionary.IncrementalUnrelatedEditBytes);
        var promoted = new GeneratorPromotedCountBenchmarks { PromotedCount = 2 };
        ValidateStationary(promoted.Setup, promoted.IncrementalUnrelatedEditBytes);
        ValidateStationary(promoted.Setup, promoted.IncrementalSharedEditBytes);
        var certification = new ThreeLayerGeneratorBenchmarks { ModelCount = 1 };
        ValidateStationary(certification.Setup, certification.IncrementalUnrelatedEditBytes);
        foreach (var rules in new[] { false, true })
        {
            var invalidation = new GeneratorInvalidationBenchmarks
            {
                RootCount = 10,
                WithComparisonRules = rules,
            };
            ValidateStationary(invalidation.Setup, invalidation.IncrementalUnrelatedEdit);
            ValidateStationary(invalidation.Setup, invalidation.IncrementalSharedEdit);
        }
        foreach (var shape in Enum.GetValues<ChangeSetJsonShape>())
            new ChangeSetJsonBenchmarks { Size = 16, Shape = shape }.Setup();
        Console.WriteLine("Bounded incremental inputs and payload round trips validated.");
    }

    private static void ValidateStationary(Action setup, Func<int> operation)
    {
        setup();
        var expected = operation();
        for (var repeat = 0; repeat < 8; repeat++)
            if (operation() != expected)
                throw new InvalidOperationException(
                    "Incremental workload size must remain constant across invocations."
                );
    }
}
